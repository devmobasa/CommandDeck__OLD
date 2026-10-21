
namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AgentReplacesAFaultedSessionBeforeTheNextTurnAsync( )
    {
        ScriptedAgentBackend backend = new( );
        backend.Script = ( input, host, token ) => host.OnTextAsync( "ok", token );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
                await using var client = await ConnectWithRetryAsync( socket );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true, BackendId = AgentBackendIds.Codex }
                }, default );
                await CollectUntilDoneAsync( client, 1 );

                await client.SendAsync( new ProtocolRequest { RequestId = 2, Operation = ProtocolOperations.AgentTurnStart, Query = "one" }, default );
                var first = await CollectUntilDoneAsync( client, 2 );
                True( first.Any( message => message.AgentEvent?.Done?.Outcome == AgentTurnOutcomes.Completed ), "the first turn completes" );
                Equal( 1, backend.Sessions );

                // A second turn reuses the live session; no new one is started.
                await client.SendAsync( new ProtocolRequest { RequestId = 3, Operation = ProtocolOperations.AgentTurnStart, Query = "two" }, default );
                await CollectUntilDoneAsync( client, 3 );
                Equal( 1, backend.Sessions );

                // The sandboxed process exits under the conversation.
                backend.FaultActiveSession( );
                await client.SendAsync( new ProtocolRequest { RequestId = 4, Operation = ProtocolOperations.AgentTurnStart, Query = "three" }, default );
                var third = await CollectUntilDoneAsync( client, 4 );
                True( third.Any( message => message.AgentEvent?.Done?.Outcome == AgentTurnOutcomes.Completed ),
                    "a turn after the process exits recovers instead of failing" );
                Equal( 2, backend.Sessions );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentStartsTheNextTurnAsSoonAsTheLastIsDoneAsync( )
    {
        const int turns = 20;
        ScriptedAgentBackend backend = new( );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
                await using var client = await ConnectWithRetryAsync( socket );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true, BackendId = AgentBackendIds.Codex }
                }, default );
                await CollectUntilDoneAsync( client, 1 );

                // Each prompt goes out the moment the previous turn's done arrives,
                // as it does when the user sends the next prompt without a pause.
                for ( var turn = 1; turn <= turns; turn++ )
                {
                    var requestId = turn + 1;
                    await client.SendAsync( new ProtocolRequest
                    {
                        RequestId = requestId,
                        Operation = ProtocolOperations.AgentTurnStart,
                        Query = $"turn {turn}"
                    }, default );
                    var messages = await CollectUntilDoneAsync( client, requestId );

                    Equal<string?>( null, messages.FirstOrDefault( message => message.Type == ProtocolMessageTypes.Error )?.Error );
                    True( messages.Any( message => message.AgentEvent?.Done?.Outcome == AgentTurnOutcomes.Completed ),
                        "a turn started on the previous turn's done completes" );

                    var idle = runtime.SubscribeAgentEvents( completeWhenIdle: true, out var subscriptionId );
                    try
                    {
                        await idle.Completion.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                    }
                    finally
                    {
                        runtime.UnsubscribeAgentEvents( subscriptionId );
                    }
                }

                Equal( 1, backend.Sessions );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }
}
