using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionRevocationWaitsForEffectAndFencesActionsAsync( )
    {
        SessionViewCommand command = new( ) { Actions = [ViewAction.CopyText( "copy", "Copy", "Admitted effect" )] };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, [ExtensionPermissionIds.ClipboardWrite] );
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource revoked = new( TaskCreationOptions.RunContinuationsAsynchronously );
        harness.Registry.RegisterRevocation( ExtensionRevocationStage.Search, ( _, _ ) =>
        {
            revoked.TrySetResult( );
            return ValueTask.CompletedTask;
        } );
        harness.Effects.BeforeExecute = async _ =>
        {
            entered.TrySetResult( );
            await release.Task;
        };
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 30 ) );
        await harness.OpenAsync( cancellationToken: deadline.Token );
        var effect = harness.ActAsync( "Copy", deadline.Token ).AsTask( );
        await entered.Task.WaitAsync( deadline.Token );
        var disable = harness.Registry.SetEnabledAsync( harness.Installation.Key, false, harness.Registry.Document.Revision, deadline.Token ).AsTask( );
        try
        {
            True( !revoked.Task.IsCompleted );
            True( !disable.IsCompleted );
            True( harness.Registry.Find( harness.Installation.Key ).Enabled );
        }
        finally
        {
            release.TrySetResult( );
            try
            {
                await effect.WaitAsync( deadline.Token );
            }
            catch ( OperationCanceledException ) { }

            await disable.WaitAsync( deadline.Token );
        }

        True( revoked.Task.IsCompleted );
        Equal( "Admitted effect", harness.Effects.Actions.Single( ).Arguments["text"] );
        await ThrowsAsync<InvalidOperationException>( async ( ) => await harness.ActAsync( "Copy", deadline.Token ) );
        Equal( 1, harness.Effects.Actions.Count );
    }

    private static async Task ExtensionRevocationCancelsQueuedDataWithoutDeadlockAsync( )
    {
        TaskCompletionSource handlerEntered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource requestNow = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource requested = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource<Exception> cancelled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        SessionViewCommand command = new( )
        {
            Handler = async ( context, _, token ) =>
            {
                handlerEntered.TrySetResult( );
                await requestNow.Task.WaitAsync( token );
                requested.TrySetResult( );
                try
                {
                    await context.Data.WriteAsync( "queued", JsonElement.Parse( "\"must not save\"" ), 0, token );
                }
                catch ( Exception error )
                {
                    cancelled.TrySetResult( error );
                    throw;
                }
            }
        };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, [ExtensionPermissionIds.StorageWrite] );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 30 ) );
        await harness.OpenAsync( cancellationToken: deadline.Token );
        var action = harness.ActAsync( "Update", deadline.Token ).AsTask( );
        await handlerEntered.Task.WaitAsync( deadline.Token );
        var held = harness.Registry.WithExecutionAsync( harness.Installation.Key, async _ =>
        {
            entered.TrySetResult( );
            await release.Task;
            return true;
        }, deadline.Token ).AsTask( );
        await entered.Task.WaitAsync( deadline.Token );
        var disable = harness.Registry.SetEnabledAsync( harness.Installation.Key, false, harness.Registry.Document.Revision, deadline.Token ).AsTask( );
        requestNow.TrySetResult( );
        try
        {
            await requested.Task.WaitAsync( deadline.Token );
            True( !action.IsCompleted );
            True( !disable.IsCompleted );
        }
        finally
        {
            release.TrySetResult( );
            await held.WaitAsync( deadline.Token );
        }

        await disable.WaitAsync( deadline.Token );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await action.WaitAsync( deadline.Token ) );
        var cancellation = await cancelled.Task.WaitAsync( deadline.Token );
        True( cancellation is OperationCanceledException or IOException { InnerException: OperationCanceledException }
            or ExtensionRpcException { Code: -32800 },
            $"Queued data may receive either transport cancellation or the host's cancelled error reply; got {cancellation}." );
        Equal( 0L, harness.Registry.DataStore.Read( harness.Installation.Key ).Revision );
        Equal( 0, harness.Registry.DataStore.Read( harness.Installation.Key ).Values.Count );
    }

    private static async Task ExtensionEffectsDoNotBlockOtherPackagesAsync( )
    {
        SessionViewCommand command = new( ) { Actions = [ViewAction.CopyText( "copy", "Copy", "Host-bound payload" )] };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, [ExtensionPermissionIds.ClipboardWrite] );
        await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( harness.Root, PackageFixture( ) with
        {
            Id = "org.example.other"
        } ), harness.Registry.ArtifactRoot, CancellationToken.None );
        var other = await harness.Registry.InstallAsync( staged, "local", harness.Registry.Document.Revision, true, [], true, CancellationToken.None );
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        harness.Effects.BeforeExecute = async token =>
        {
            entered.TrySetResult( );
            await release.Task.WaitAsync( token );
        };
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 30 ) );
        await harness.OpenAsync( cancellationToken: deadline.Token );
        var effect = harness.ActAsync( "Copy", deadline.Token ).AsTask( );
        await entered.Task.WaitAsync( deadline.Token );
        try
        {
            using CancellationTokenSource otherDeadline = new( TimeSpan.FromSeconds( 30 ) );
            var data = await harness.Registry.WithExecutionAsync( other.Key, current => harness.Registry.AccessDataAsync( current,
                new( "s", 1, 1, "i", "write", "draft", 0, JsonElement.Parse( "\"Independent\"" ) ), otherDeadline.Token ), otherDeadline.Token );
            Equal( 1L, data.Revision );
            True( !effect.IsCompleted );
        }
        finally
        {
            release.TrySetResult( );
            await effect;
        }
        Equal( "Host-bound payload", harness.Effects.Actions.Single( ).Arguments["text"] );
    }
}
