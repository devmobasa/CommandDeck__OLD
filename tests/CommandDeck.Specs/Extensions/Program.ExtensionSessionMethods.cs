using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionSessionOpensAndInvokesAsync( )
    {
        SessionViewCommand command = new( );
        await using var harness = await ExtensionSessionHarness.CreateAsync( command );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        await harness.OpenAsync( cancellationToken: deadline.Token );
        var initial = harness.LatestView;
        Equal( "Initial", initial.Content!.Value.GetProperty( "title" ).GetString( ) );

        await harness.ActAsync( "Update", deadline.Token );

        var invoked = ( ActionInvoked ) command.Events.Single( );
        Equal( "update", invoked.ActionId );
        True( harness.LatestView.Revision > initial.Revision );
        Equal( "Updated", harness.LatestView.Content!.Value.GetProperty( "title" ).GetString( ) );
        Equal( ExtensionSessionStatuses.Ready, harness.Latest.Status );
    }

    private static async Task ExtensionSessionRevocationCancelsActionAsync( )
    {
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource cancelled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        SessionViewCommand command = new( )
        {
            Handler = async ( _, _, token ) =>
            {
                entered.TrySetResult( );
                try
                {
                    await Task.Delay( Timeout.Infinite, token );
                }
                catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
                {
                    cancelled.TrySetResult( );
                    throw;
                }
            }
        };
        using StringWriter diagnostics = new( );
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, diagnostics: diagnostics );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        await harness.OpenAsync( cancellationToken: deadline.Token );
        var action = harness.ActAsync( "Update", deadline.Token ).AsTask( );
        await entered.Task.WaitAsync( deadline.Token );

        await harness.Registry.SetEnabledAsync( harness.Installation.Key, false, harness.Registry.Document.Revision, deadline.Token );

        await cancelled.Task.WaitAsync( deadline.Token );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await action.WaitAsync( deadline.Token ) );
        Equal( ExtensionSessionStatuses.Closed, harness.Latest.Status );
        Equal( "", diagnostics.ToString( ) );
        True( !harness.Registry.Find( harness.Installation.Key ).Enabled );
    }

    private static async Task ExtensionSessionRecoversHandlerErrorAsync( )
    {
        var attempts = 0;
        SessionViewCommand command = new( )
        {
            Handler = ( _, _, _ ) =>
            {
                if ( ++attempts == 1 )
                {
                    throw new InvalidOperationException( "Private author details" );
                }
                return ValueTask.CompletedTask;
            }
        };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command );
        await harness.OpenAsync( );
        var initial = harness.LatestView;
        try
        {
            await harness.ActAsync( "Update" );
        }
        catch ( IOException ) { } // The old host closes a healthy peer for an application error.

        Equal( ExtensionSessionStatuses.Ready, harness.Latest.Status );
        Equal( "The extension couldn't complete that action.", harness.Latest.Error );
        Equal( initial.Revision, harness.LatestView.Revision );
        True( harness.States.All( state => state.Error?.Contains( "Private author details", StringComparison.Ordinal ) != true ) );
        await harness.ActAsync( "Update" );
        Equal( 2, attempts );
        True( harness.Latest.Error is null );
    }
}
