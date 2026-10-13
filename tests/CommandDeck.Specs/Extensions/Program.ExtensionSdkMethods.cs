using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionSdkRoundTripAndRetiredContextAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 5 ) );
        List<ViewUpdate> updates = [];
        await using ExtensionPeer host = new( pipes.Host, pipes.Host, true, ["views.replace", "views.push"], ( frame, _ ) =>
        {
            var update = frame.Params!.Value.Deserialize( ExtensionViewJsonContext.Default.ViewUpdate )!;
            ViewValidation.Validate( update.Content );
            updates.Add( update );
            return ValueTask.FromResult( JsonSerializer.SerializeToElement( new ViewAccepted( update.ViewId, update.ExpectedRevision + 1 ), ExtensionViewJsonContext.Default.ViewAccepted ) );
        } );
        host.Start( );
        SdkFixtureCommand command = new( );
        var sdk = ExtensionApp.RunAsync( pipes.Child, pipes.Child, [command], lifetime.Token );
        try
        {
            var initialize = new InitializeRequest( new( ), "session-one", 7, "uuids", "interactive", ["commands@1", "views.list@1", "actions.copy-text@1"], new( ) );
            var response = await host.CallAsync( "initialize", JsonSerializer.SerializeToElement( initialize, ExtensionJsonContext.Default.InitializeRequest ), lifetime.Token );
            Equal( "uuids", response.Deserialize( ExtensionJsonContext.Default.InitializeResult )!.Commands.Single( ) );
            host.Notify( "initialized" );
            var opened = await host.CallAsync( "command.open", JsonSerializer.SerializeToElement( new CommandOpenRequest( 1, "open-one", [] ), ExtensionViewJsonContext.Default.CommandOpenRequest ), lifetime.Token );
            Equal( "opened-view", opened.Deserialize( ExtensionViewJsonContext.Default.CommandOutcome )!.Outcome );
            Equal( 1, updates.Count );
            Equal( 7L, updates[0].ExecutionEpoch );
            Equal( 0L, updates[0].ExpectedRevision );
            var retained = command.RetainedContext!;
            await ThrowsAsync<InvalidOperationException>( async ( ) => await retained.Views.ReplaceAsync( command.View, lifetime.Token ) );
            Equal( 1, updates.Count );
            var @event = new CommandEventRequest( 2, "event-two", "uuids", 1, new ActionInvoked( "regenerate" ) );
            await host.CallAsync( "command.event", JsonSerializer.SerializeToElement( @event, ExtensionViewJsonContext.Default.CommandEventRequest ), lifetime.Token );
            Equal( 2, updates.Count );
            Equal( 1L, updates[1].ExpectedRevision );
            Equal( 2L, updates[1].Generation );
            Equal( "Generated 2", (( ListView ) updates[1].Content).Items[0].Title );
            await ThrowsAsync<ExtensionRpcException>( async ( ) => await host.CallAsync( "command.event", JsonSerializer.SerializeToElement( @event, ExtensionViewJsonContext.Default.CommandEventRequest ), lifetime.Token ) );
            Equal( 2, updates.Count );
        }
        finally
        {
            await lifetime.CancelAsync( );
            try
            {
                await sdk;
            }
            catch ( OperationCanceledException ) { }
        }
    }

    private static async Task ExtensionSdkRejectsUnsupportedRendererAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 5 ) );
        var writes = 0;
        await using ExtensionPeer host = new( pipes.Host, pipes.Host, true, ["views.replace"], ( _, _ ) =>
        {
            writes++;
            return ValueTask.FromResult( JsonElement.Parse( "{}" ) );
        } );
        host.Start( );
        var sdk = ExtensionApp.RunAsync( pipes.Child, pipes.Child, [new SdkFixtureCommand( )], lifetime.Token );
        try
        {
            var initialize = new InitializeRequest( new( ), "session-one", 1, "uuids", "interactive", ["commands@1"], new( ) );
            await host.CallAsync( "initialize", JsonSerializer.SerializeToElement( initialize, ExtensionJsonContext.Default.InitializeRequest ), lifetime.Token );
            host.Notify( "initialized" );
            await ThrowsAsync<ExtensionRpcException>( async ( ) => await host.CallAsync( "command.open", JsonSerializer.SerializeToElement( new CommandOpenRequest( 1, "open", [] ), ExtensionViewJsonContext.Default.CommandOpenRequest ), lifetime.Token ) );
            Equal( 0, writes );
        }
        finally
        {
            await lifetime.CancelAsync( );
            try
            {
                await sdk;
            }
            catch ( OperationCanceledException ) { }
        }
    }

    private static async Task ExtensionSdkCancellationFencesContextAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 5 ) );
        using CancellationTokenSource invocation = CancellationTokenSource.CreateLinkedTokenSource( lifetime.Token );
        await using ExtensionPeer host = NoRequestsPeer( pipes.Host );
        HeldSdkCommand command = new( );
        var sdk = ExtensionApp.RunAsync( pipes.Child, pipes.Child, [command], lifetime.Token );
        try
        {
            var initialize = new InitializeRequest( new( ), "session", 1, "held", "interactive", ["commands@1", "views.detail@1"], new( ) );
            await host.CallAsync( "initialize", JsonSerializer.SerializeToElement( initialize, ExtensionJsonContext.Default.InitializeRequest ), lifetime.Token );
            host.Notify( "initialized" );
            var open = host.CallAsync( "command.open", JsonSerializer.SerializeToElement( new CommandOpenRequest( 1, "open", [] ), ExtensionViewJsonContext.Default.CommandOpenRequest ), invocation.Token ).AsTask( );
            var context = await command.Started.Task.WaitAsync( lifetime.Token );
            await invocation.CancelAsync( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await open );
            await command.Cancelled.Task.WaitAsync( lifetime.Token );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await context.Views.ReplaceAsync( new DetailView { Id = "late", Title = "Late" }, lifetime.Token ) );
        }
        finally
        {
            await lifetime.CancelAsync( );
            try
            {
                await sdk;
            }
            catch ( OperationCanceledException ) { }
        }
    }

    private sealed class HeldSdkCommand : IExtensionCommand
    {
        public string Id => "held";
        public TaskCompletionSource<CommandContext> Started { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Cancelled { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            Started.SetResult( context );
            try
            {
                await Task.Delay( Timeout.Infinite, cancellationToken );
            }
            finally
            {
                Cancelled.SetResult( );
            }
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    }

    private sealed class SdkFixtureCommand : IExtensionCommand
    {
        private int _count;
        public string Id => "uuids";
        public CommandContext? RetainedContext { get; private set; }
        public ListView View => new( ) { Id = "uuids", Title = "UUIDs", Items = [new ListItem { Id = "one", Title = "Generated " + _count, Actions = [ViewAction.CopyText( "copy", "Copy", "value" )] }] };
        public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            RetainedContext = context;
            _count++;
            return context.Views.ReplaceAsync( View, cancellationToken );
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) =>
            commandEvent is ActionInvoked { ActionId: "regenerate" } ? OpenAsync( context, cancellationToken ) : ValueTask.CompletedTask;
    }
}
