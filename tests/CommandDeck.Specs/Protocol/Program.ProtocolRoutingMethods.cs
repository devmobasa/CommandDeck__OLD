namespace CommandDeck.Specs;

public static partial class Program
{
    // An operation without a route gets an error and done and runs nothing: the
    // sign-out, grant, and provider-test lookalikes leave the backend signed in
    // and the connection usable.
    private static async Task UnknownOperationsRunNoNeighbourAsync( )
    {
        ScriptedAgentBackend backend = new( );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) = await StartAgentRuntimeAsync(
            backend,
            serverCancellation.Token,
            new RuntimeConstructionOptions { PackageHelperFactory = static ( ) => null } );
        try
        {
            await using ( runtime )
            {
                await using var client = await ConnectWithRetryAsync( SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" ) );
                string[] lookalikes = ["agent.logout.all", "agent.grants.list", "ai.providers.retest", "files.toggle", "index.restart"];
                long requestId = 0;
                foreach ( var operation in lookalikes )
                {
                    await client.SendAsync( new ProtocolRequest { RequestId = ++requestId, Operation = operation }, default );
                    var messages = await CollectUntilDoneAsync( client, requestId );

                    Equal( 2, messages.Count );
                    Equal( ProtocolMessageTypes.Error, messages[0].Type );
                    Equal( $"Unknown operation '{operation}'.", messages[0].Error );
                    Equal( ProtocolMessageTypes.Done, messages[1].Type );
                }

                True( backend.Authenticated, "no lookalike signed the backend out" );
                await client.SendAsync( new ProtocolRequest { RequestId = ++requestId, Operation = ProtocolOperations.Ping }, default );
                var pong = await CollectUntilDoneAsync( client, requestId );
                Equal( ProtocolMessageTypes.Pong, pong[0].Type );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    // Each routed operation, sent empty, ends with done and is never answered as
    // unknown: the family handler it routes to serves it by name. Operations whose
    // empty form fails validation stop earlier with their own error.
    private static async Task EveryRoutedOperationReachesItsHandlerAsync( )
    {
        ScriptedAgentBackend backend = new( );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) = await StartAgentRuntimeAsync(
            backend,
            serverCancellation.Token,
            new RuntimeConstructionOptions { PackageHelperFactory = static ( ) => null } );
        try
        {
            await using ( runtime )
            {
                await using var client = await ConnectWithRetryAsync( SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" ) );
                long requestId = 0;
                List<string> unknown = [];
                foreach ( var operation in ProtocolRoutes.All.Keys.Order( StringComparer.Ordinal ) )
                {
                    await client.SendAsync( new ProtocolRequest { RequestId = ++requestId, Operation = operation }, default );
                    var messages = await CollectUntilDoneAsync( client, requestId );

                    True( messages.Count > 0 && messages[^1].Type == ProtocolMessageTypes.Done, $"{operation} ends with done" );
                    if ( messages.Any( message => message.Error?.StartsWith( "Unknown operation", StringComparison.Ordinal ) == true ) )
                    {
                        unknown.Add( operation );
                    }
                }

                Equal( string.Empty, string.Join( ',', unknown ) );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }
}
