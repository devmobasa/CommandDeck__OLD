using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task<(CommandDeckRuntime Runtime, UnixProtocolServer Server, string Root, ScriptedAgentBackend Backend)>
        StartAgentRuntimeAsync( ScriptedAgentBackend backend, CancellationToken serverToken,
            RuntimeConstructionOptions? constructionOptions = null, AgentOptions? agentOptions = null )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-socket-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        var config = Path.Combine( root, "config.json" );
        CommandDeckOptions options = new( )
        {
            Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
            Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
            Privacy = new PrivacyOptions { StoreUsageHistory = false, NeverIndex = [] },
            Ai = new AiOptions { Enabled = false, Providers = [] },
            Agent = agentOptions ?? new AgentOptions { RuntimeDirectory = Path.Combine( root, "runtime" ), StateDirectory = Path.Combine( root, "state" ) }
        };
        await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ), serverToken );
        var runtime = await CommandDeckRuntime.CreateAsync( config,
            new RuntimeConstructionOptions
            {
                AgentBackendFactory = ( _, _ ) => backend,
                CliAgentBackendFactory = constructionOptions?.CliAgentBackendFactory,
                PackageCatalogFactory = constructionOptions?.PackageCatalogFactory,
                AgentClock = constructionOptions?.AgentClock,
                PackageHelperFactory = constructionOptions?.PackageHelperFactory
            }, CancellationToken.None );
        UnixProtocolServer server = new( runtime, SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" ) );
        _ = server.RunAsync( serverToken );
        return (runtime, server, root, backend);
    }

    private static async Task<List<ProtocolMessage>> CollectUntilDoneAsync( UnixProtocolClient client, long requestId )
    {
        List<ProtocolMessage> messages = [];
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 20 ) );
        await foreach ( var line in client.ReadRawMessagesAsync( timeout.Token ) )
        {
            var message = DeserializeMessage( line );
            if ( message is null || message.RequestId != requestId )
            {
                continue;
            }
            messages.Add( message );
            if ( message.Type == ProtocolMessageTypes.Done )
            {
                break;
            }
        }
        return messages;
    }

    private static async Task<List<ProtocolMessage>> ReadUntilRequestEventAsync( UnixProtocolClient client, long requestId )
    {
        List<ProtocolMessage> events = [];
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 20 ) );
        await foreach ( var line in client.ReadRawMessagesAsync( timeout.Token ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId != requestId )
            {
                continue;
            }
            events.Add( message );
            if ( message.AgentEvent?.Kind == AgentEventKinds.Request )
            {
                break;
            }
        }
        return events;
    }

    private static async Task<List<ProtocolMessage>> ReadUntilSnapshotAsync( UnixProtocolClient client, long requestId )
    {
        List<ProtocolMessage> messages = [];
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 20 ) );
        await foreach ( var line in client.ReadRawMessagesAsync( timeout.Token ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId != requestId )
            {
                continue;
            }
            messages.Add( message );
            if ( message.Type == ProtocolMessageTypes.AgentConversationResult )
            {
                break;
            }
        }
        return messages;
    }

    private static async Task AgentSocketStreamsDaemonOwnedTurnsAndParksQuestionsAsync( )
    {
        ScriptedAgentBackend backend = new( );
        TaskCompletionSource questionAsked = new( TaskCreationOptions.RunContinuationsAsynchronously );
        backend.Script = async ( input, host, token ) =>
        {
            await host.OnStatusAsync( "Thinking", token );
            await host.OnTextAsync( "Hello ", token );
            var answer = await host.OnUserInputAsync( new AgentUserInputRequest
            {
                Questions = [new AgentQuestion { Id = "q1", Prompt = "Which?", Options = ["A", "B"] }]
            }, token );
            questionAsked.TrySetResult( );
            await host.OnTextAsync( "you chose " + (answer.Declined ? "nothing" : answer.Answers["q1"]), token );
        };
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
                await using var client = await ConnectWithRetryAsync( socket );
                await client.SendAsync( new ProtocolRequest { RequestId = 1, Operation = ProtocolOperations.AgentTurnStart, Query = "hi" }, default );
                var disabled = await CollectUntilDoneAsync( client, 1 );
                True( disabled.Any( message => message.Type == ProtocolMessageTypes.Error && message.Error!.Contains( "disabled", StringComparison.OrdinalIgnoreCase ) ) );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 2,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true, BackendId = AgentBackendIds.Codex }
                }, default );
                var enabled = await CollectUntilDoneAsync( client, 2 );
                var enabledPreferences = enabled[0].AgentPreferences;
                True( enabledPreferences is { Enabled: true, TrustLevel: AgentTrustLevels.Default } );

                await client.SendAsync( new ProtocolRequest { RequestId = 3, Operation = ProtocolOperations.AgentTurnStart, Query = "hi" }, default );
                var firstEvents = await ReadUntilRequestEventAsync( client, 3 );
                var pending = firstEvents[^1].AgentEvent!.Request!;
                Equal( AgentPendingRequestKinds.Question, pending.Kind );
                True( firstEvents.Any( message => message.AgentEvent?.Kind == AgentEventKinds.Text && message.AgentEvent.Text == "Hello " ) );
                True( firstEvents.Select( message => message.AgentEvent!.Sequence ).SequenceEqual( firstEvents.Select( message => message.AgentEvent!.Sequence ).Order( ) ) );
                var options = backend.LastOptions!;
                True( options.Tools.Any( tool => tool.Name == AgentToolNames.ReadFile ) && !options.Tools.Any( tool => tool.Name == AgentToolNames.RunCommand ) );

                await using var second = await ConnectWithRetryAsync( socket );
                await second.SendAsync( new ProtocolRequest { RequestId = 10, Operation = ProtocolOperations.AgentConversationGet }, default );
                var reopened = await ReadUntilSnapshotAsync( second, 10 );
                var snapshot = reopened[0].AgentConversation!;
                Equal( AgentConversationStates.Waiting, snapshot.Summary?.State );
                Equal( pending.Id, snapshot.Pending?.Id );
                Equal( "Hello ", snapshot.ActiveText );
                True( snapshot.ActiveTurnId > 0 );
                var home = await runtime.SearchAsync( new SearchRequest { Query = string.Empty, Limit = 20 }, default );
                True( home.Any( result => result.Id == "agent:conversation" && result.Title.Contains( "waiting", StringComparison.OrdinalIgnoreCase ) ) );
                var aiRows = await runtime.SearchAsync( new SearchRequest { Query = "ai: what is 5+2", Limit = 20 }, default );
                True( aiRows.Any( result => result.Id == "agent:codex" ), "the Ask Codex row survives source toggles that never list the assistant" );

                await second.SendAsync( new ProtocolRequest
                {
                    RequestId = 11,
                    Operation = ProtocolOperations.AgentTurnInput,
                    PendingRequestId = pending.Id,
                    Answers = new Dictionary<string, string>( StringComparer.Ordinal ) { ["q1"] = "B" }
                }, default );
                await questionAsked.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
                var rest = await CollectUntilDoneAsync( second, 10 );
                True( rest.Any( message => message.AgentEvent?.Kind == AgentEventKinds.Done && message.AgentEvent.Done?.Outcome == AgentTurnOutcomes.Completed ) );
                var final = runtime.GetAgentConversation( );
                Equal( 2, final.Entries.Count );
                Equal( "Hello you chose B", final.Entries[1].Text );
                True( final.OutputGeneration > 0 && final.OutputActions.Count == 3 );
                var savePath = Path.Combine( root, "answer.md" );
                var saved = await runtime.ExecuteAgentOutputAsync( final.OutputGeneration, "save", savePath, null, default );
                True( saved.Succeeded );
                Equal( "Hello you chose B", await File.ReadAllTextAsync( savePath ) );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentSocketTrustChallengesAndDisableResetAuthorityAsync( )
    {
        ScriptedAgentBackend backend = new( );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                await using var client = await ConnectWithRetryAsync( SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" ) );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true, TrustLevel = AgentTrustLevels.Unrestricted }
                }, default );
                var forged = await CollectUntilDoneAsync( client, 1 );
                True( forged[0].Type == ProtocolMessageTypes.Error );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 2,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true }
                }, default );
                await CollectUntilDoneAsync( client, 2 );
                await client.SendAsync( new ProtocolRequest { RequestId = 3, Operation = ProtocolOperations.AgentTrustChallenge, TrustLevel = AgentTrustLevels.ConfirmedCommands }, default );
                var challenged = await CollectUntilDoneAsync( client, 3 );
                var challenge = challenged[0].AgentTrustChallenge!;
                True( !challenge.RequiresPhrase );
                await client.SendAsync( new ProtocolRequest { RequestId = 4, Operation = ProtocolOperations.AgentTrustUpdate, ChallengeId = challenge.Id, TrustLevel = AgentTrustLevels.ConfirmedCommands }, default );
                var applied = await CollectUntilDoneAsync( client, 4 );
                Equal( AgentTrustLevels.ConfirmedCommands, applied[0].AgentTrust?.Level );
                await client.SendAsync( new ProtocolRequest { RequestId = 5, Operation = ProtocolOperations.AgentTrustUpdate, ChallengeId = challenge.Id, TrustLevel = AgentTrustLevels.ConfirmedCommands }, default );
                var replayed = await CollectUntilDoneAsync( client, 5 );
                True( replayed[0].Type == ProtocolMessageTypes.Error );

                await client.SendAsync( new ProtocolRequest { RequestId = 6, Operation = ProtocolOperations.AgentGrantsAdd, GrantKind = AgentGrantKinds.Folder, GrantScope = root }, default );
                var grantsAfterAdd = await CollectUntilDoneAsync( client, 6 );
                True( grantsAfterAdd[0].Type == ProtocolMessageTypes.Error || grantsAfterAdd[0].AgentGrants is not null );

                await client.SendAsync( new ProtocolRequest { RequestId = 7, Operation = ProtocolOperations.AgentStatusGet }, default );
                var status = await CollectUntilDoneAsync( client, 7 );
                Equal( AgentTrustLevels.ConfirmedCommands, status[0].AgentStatus?.Trust.Level );
                True( status[0].AgentStatus?.Helper.Installed == false );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 8,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = false, TrustLevel = AgentTrustLevels.ConfirmedCommands }
                }, default );
                var disabled = await CollectUntilDoneAsync( client, 8 );
                Equal( AgentTrustLevels.Default, disabled[0].AgentPreferences?.TrustLevel );
                True( backend.Stopped );
                await client.SendAsync( new ProtocolRequest { RequestId = 9, Operation = ProtocolOperations.AgentGrantsGet }, default );
                var grants = await CollectUntilDoneAsync( client, 9 );
                Equal( 0, grants[0].AgentGrants?.Grants.Count );
                await client.SendAsync( new ProtocolRequest { RequestId = 12, Operation = ProtocolOperations.AgentTurnStart, Query = "x", Version = 2 }, default );
                var oldVersion = await CollectUntilDoneAsync( client, 12 );
                True( oldVersion.Any( message => message.Type == ProtocolMessageTypes.Error && message.Error!.Contains( "version", StringComparison.OrdinalIgnoreCase ) ) );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentLoginPageOpensThroughCachedActionAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        ScriptedAgentBackend backend = new( )
        {
            Authenticated = false
        };
        using CancellationTokenSource serverCancellation = new( );
        var (runtime, _, root, _) = await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            await using ( runtime )
            {
                await using var client = await ConnectWithRetryAsync( SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" ) );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true }
                }, default );
                _ = await CollectUntilDoneAsync( client, 1 );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 2,
                    Operation = ProtocolOperations.AgentLoginStart,
                    LoginMethod = AgentLoginMethods.DeviceCode
                }, default );
                var pending = (await CollectUntilDoneAsync( client, 2 ))
                    .Single( static message => message.AgentLogin is not null ).AgentLogin!;
                Equal( AgentAuthenticationStates.Pending, pending.State );

                var capturedArguments = Path.Combine( root, "browser-arguments" );
                var launcher = Path.Combine( root, "uwsm-app" );
                await File.WriteAllTextAsync( launcher, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{capturedArguments}'\n" );
                File.SetUnixFileMode( launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
                Environment.SetEnvironmentVariable( "PATH", root );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 3,
                    Operation = ProtocolOperations.Invoke,
                    ResultId = "agent:login",
                    InvocationReference = pending.InvocationReference,
                    ActionId = AgentLoginActionIds.OpenUrl
                }, default );
                var action = (await CollectUntilDoneAsync( client, 3 ))
                    .Single( static message => message.ActionResult is not null ).ActionResult!;
                True( action.Succeeded, "the login page action starts the desktop browser" );
                var arguments = await File.ReadAllLinesAsync( capturedArguments );
                Equal( "-t|service|-S|both|--|xdg-open", string.Join( '|', arguments.Take( 6 ) ) );
                Equal( pending.VerificationUrl, arguments[6] );
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

}
