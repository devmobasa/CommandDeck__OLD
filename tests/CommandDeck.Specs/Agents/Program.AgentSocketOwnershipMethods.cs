using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AgentSensitiveDecisionsStayBoundToTheRequestingClientAsync( )
    {
        var contextRoot = SpecTemporaryFiles.CreateHomeDirectory( "commanddeck-client-owner-" ).FullName;
        var contextPath = Path.Combine( contextRoot, "note.txt" );
        await File.WriteAllTextAsync( contextPath, "owner-only context" );
        ScriptedAgentBackend backend = new( );
        backend.Script = async ( input, host, token ) =>
        {
            if ( input.Text == "run it" )
            {
                var command = await host.OnToolCallAsync( new AgentToolCall
                {
                    CallId = "command-1",
                    Name = AgentToolNames.RunCommand,
                    ArgumentsJson = "{\"argv\":[\"/usr/bin/printf\",\"bound\"]}"
                }, token );
                await host.OnTextAsync( command.Success ? "command allowed" : "command denied", token );
                return;
            }
            var result = await host.OnToolCallAsync( new AgentToolCall
            {
                CallId = "context-1",
                Name = AgentToolNames.ReadFile,
                ArgumentsJson = JsonSerializer.Serialize( new { path = contextPath } )
            }, token );
            await host.OnTextAsync( result.Success ? "allowed" : "denied", token );
        };
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
                await using var owner = await ConnectWithRetryAsync( socket );
                await owner.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true }
                }, default );
                await CollectUntilDoneAsync( owner, 1 );
                await owner.SendAsync( new ProtocolRequest
                {
                    RequestId = 2,
                    Operation = ProtocolOperations.AgentTurnStart,
                    Query = "read it"
                }, default );
                var ownerEvents = await ReadUntilRequestEventAsync( owner, 2 );
                var pending = ownerEvents[^1].AgentEvent!.Request!;
                Equal( AgentPendingRequestKinds.Context, pending.Kind );

                await using var other = await ConnectWithRetryAsync( socket );
                await other.SendAsync( new ProtocolRequest
                {
                    RequestId = 10,
                    Operation = ProtocolOperations.AgentConversationGet
                }, default );
                var hidden = await ReadUntilSnapshotAsync( other, 10 );
                True( hidden[0].AgentConversation!.Pending is null );
                True( hidden[0].AgentConversation!.Summary?.PendingRequestId is null );
                await other.SendAsync( new ProtocolRequest
                {
                    RequestId = 11,
                    Operation = ProtocolOperations.AgentContextResolve,
                    PendingRequestId = pending.Id,
                    Decision = AgentRequestDecisions.Allow
                }, default );
                var rejected = await CollectUntilDoneAsync( other, 11 );
                True( rejected.Any( message => message.Type == ProtocolMessageTypes.Error ) );

                await owner.SendAsync( new ProtocolRequest
                {
                    RequestId = 3,
                    Operation = ProtocolOperations.AgentContextResolve,
                    PendingRequestId = pending.Id,
                    Decision = AgentRequestDecisions.Allow
                }, default );
                var resolved = await CollectUntilDoneAsync( owner, 3 );
                True( resolved.Any( message => message.Type == ProtocolMessageTypes.AgentConversationResult ) );
                var completed = await CollectUntilDoneAsync( owner, 2 );
                True( completed.Any( message => message.AgentEvent?.Done?.Outcome == AgentTurnOutcomes.Completed ) );
                Equal( "allowed", runtime.GetAgentConversation( ).Entries[^1].Text );

                await owner.SendAsync( new ProtocolRequest
                {
                    RequestId = 4,
                    Operation = ProtocolOperations.AgentTrustChallenge,
                    TrustLevel = AgentTrustLevels.ConfirmedCommands
                }, default );
                var challenge = (await CollectUntilDoneAsync( owner, 4 ))[0].AgentTrustChallenge!;
                await owner.SendAsync( new ProtocolRequest
                {
                    RequestId = 5,
                    Operation = ProtocolOperations.AgentTrustUpdate,
                    ChallengeId = challenge.Id,
                    TrustLevel = AgentTrustLevels.ConfirmedCommands
                }, default );
                await CollectUntilDoneAsync( owner, 5 );
                await owner.SendAsync( new ProtocolRequest
                {
                    RequestId = 6,
                    Operation = ProtocolOperations.AgentTurnStart,
                    Query = "run it"
                }, default );
                var commandPending = (await ReadUntilRequestEventAsync( owner, 6 ))[^1].AgentEvent!.Request!;
                Equal( AgentPendingRequestKinds.Command, commandPending.Kind );
                await other.SendAsync( new ProtocolRequest
                {
                    RequestId = 12,
                    Operation = ProtocolOperations.AgentConversationGet
                }, default );
                True( (await ReadUntilSnapshotAsync( other, 12 ))[0].AgentConversation!.Pending is null );
                await other.SendAsync( new ProtocolRequest
                {
                    RequestId = 13,
                    Operation = ProtocolOperations.AgentContextResolve,
                    PendingRequestId = commandPending.Id,
                    Decision = AgentRequestDecisions.Allow
                }, default );
                True( (await CollectUntilDoneAsync( other, 13 )).Any( message => message.Type == ProtocolMessageTypes.Error ) );
                await owner.SendAsync( new ProtocolRequest
                {
                    RequestId = 7,
                    Operation = ProtocolOperations.AgentContextResolve,
                    PendingRequestId = commandPending.Id,
                    Decision = AgentRequestDecisions.Allow
                }, default );
                await CollectUntilDoneAsync( owner, 7 );
                True( (await CollectUntilDoneAsync( owner, 6 )).Any(
                    message => message.AgentEvent?.Done?.Outcome == AgentTurnOutcomes.Completed ) );
                Equal( "command allowed", runtime.GetAgentConversation( ).Entries[^1].Text );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
            Directory.Delete( contextRoot, recursive: true );
        }
    }

    // A client that disconnects mid-turn loses its connection state, not the
    // assistant's turn: the daemon-owned turn finishes and another client reads it.
    private static async Task AgentTurnOutlivesTheClientThatStartedItAsync( )
    {
        ScriptedAgentBackend backend = new( );
        TaskCompletionSource started = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        backend.Script = async ( _, host, token ) =>
        {
            await host.OnTextAsync( "before ", token );
            started.TrySetResult( );
            await release.Task.WaitAsync( token );
            await host.OnTextAsync( "after", token );
        };
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var server, var root, var _) = await StartAgentRuntimeAsync(
            backend,
            serverCancellation.Token,
            new RuntimeConstructionOptions { PackageHelperFactory = static ( ) => null } );
        try
        {
            await using ( runtime )
            {
                var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                var starter = await ConnectWithRetryAsync( socket );
                await starter.SendAsync( new ProtocolRequest { RequestId = 1, Operation = ProtocolOperations.AgentTurnStart, Query = "hi" }, default );
                await started.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );

                await starter.DisposeAsync( );
                True( await WaitUntilAsync( ( ) => Task.FromResult( server.ConnectedClients == 0 ), TimeSpan.FromSeconds( 5 ) ),
                    "the server released the disconnected client" );
                True( runtime.GetAgentConversation( "spec-observer" ).ActiveTurnId > 0, "the turn is still running" );
                release.TrySetResult( );

                await using var reader = await ConnectWithRetryAsync( socket );
                True( await WaitUntilAsync(
                    ( ) => Task.FromResult( runtime.GetAgentConversation( "spec-observer" ).Summary?.State == AgentConversationStates.Completed ),
                    TimeSpan.FromSeconds( 10 ) ) );
                await reader.SendAsync( new ProtocolRequest { RequestId = 2, Operation = ProtocolOperations.AgentConversationGet }, default );
                var snapshot = (await ReadUntilSnapshotAsync( reader, 2 ))[^1].AgentConversation!;
                Equal( "before after", snapshot.Entries[^1].Text );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }
}
