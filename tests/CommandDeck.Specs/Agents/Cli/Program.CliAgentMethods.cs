namespace CommandDeck.Specs;

public static partial class Program
{
    private static void CliAgentProfilesBuildClosedArgvAndParseOutputs( )
    {
        Equal( 3, CliAgentProfile.All.Count );
        True( CliAgentProfile.All.Select( static profile => profile.BackendId ).SequenceEqual(
            [AgentBackendIds.ClaudeCode, AgentBackendIds.Cursor, AgentBackendIds.Grok] ) );
        True( CliAgentProfile.All.All( static profile => AgentBackendIds.IsReserved( profile.BackendId ) ) );

        ClaudeCodeProfile claude = new( );
        var claudeArgv = claude.BuildTurnArguments( new CliAgentTurnRequest
        {
            Prompt = "hello",
            ModelId = "sonnet",
            ReasoningEffort = "high",
            ContinuationId = "session-1",
            SystemPrompt = "answer only"
        } );
        True( HasArgumentPair( claudeArgv, "--tools", "" ) && claudeArgv.Contains( "--restricted" ) );
        True( HasArgumentPair( claudeArgv, "--permission-mode", "dontAsk" ) && HasArgumentPair( claudeArgv, "--permission-prompts", "none" ) );
        True( HasArgumentPair( claudeArgv, "--resume", "session-1" ) && HasArgumentPair( claudeArgv, "--model", "sonnet" ) );
        True( HasArgumentPair( claudeArgv, "--append-system-prompt", "answer only" ) && HasArgumentPair( claudeArgv, "--max-turns", "1" ) );
        True( !claudeArgv.Contains( "--bare" ), "bare mode would disable the subscription sign-in" );
        True( !claudeArgv.Contains( "hello" ), "the prompt travels on stdin, never in argv" );
        True( claude.PromptOnStandardInput && claude.RequiresPastedCode && claude.LoginMethod == AgentLoginMethods.Browser );
        Equal( (true, "claude.ai"), claude.ParseStatus( "{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"email\":\"x\"}" ) );
        Equal( (false, null), claude.ParseStatus( "{\"loggedIn\":false}" ) );
        Equal( null, claude.ParseStatus( "not json" ) );
        Equal( 4, claude.ParseModels( string.Empty ).Count );
        Equal( "sonnet", claude.ParseModels( string.Empty ).Single( static model => model.IsDefault ).Id );

        CursorProfile cursor = new( );
        var cursorArgv = cursor.BuildTurnArguments( new CliAgentTurnRequest { Prompt = "review", SystemPrompt = "rules", ContinuationId = "chat-9" } );
        True( HasArgumentPair( cursorArgv, "--mode", "ask" ) && HasArgumentPair( cursorArgv, "--sandbox", "enabled" ) );
        True( HasArgumentPair( cursorArgv, "--resume", "chat-9" ) && cursorArgv[^2] == "--" && cursorArgv[^1] == "rules\n\nreview" );
        Equal( (true, null), cursor.ParseStatus( "Cannot open directory /x to load OpenSSL certificates.\n{\"status\":\"authenticated\",\"isAuthenticated\":true}" ) );
        var cursorModels = cursor.ParseModels( "Available models\n\nauto - Auto (default)\ngpt-5.2 - GPT-5.2\nbad line\n" );
        Equal( 2, cursorModels.Count );
        True( cursorModels[0].Id == "auto" && cursorModels[0].IsDefault && cursorModels[0].DisplayName == "Auto" );
        True( cursorModels[1].Id == "gpt-5.2" && !cursorModels[1].IsDefault );

        GrokProfile grok = new( );
        Throws<InvalidOperationException>( ( ) => grok.BuildTurnArguments( new CliAgentTurnRequest { Prompt = "x" } ) );
        var grokArgv = grok.BuildTurnArguments( new CliAgentTurnRequest { Prompt = "x", PromptFilePath = "/run/scratch/prompt.txt", ModelId = "grok-4.6" } );
        True( HasArgumentPair( grokArgv, "--prompt-file", "/run/scratch/prompt.txt" ) && HasArgumentPair( grokArgv, "--tools", "" ) );
        True( grokArgv.Contains( "--disable-web-search" ) && grokArgv.Contains( "--no-subagents" ) && HasArgumentPair( grokArgv, "--permission-mode", "dontAsk" ) );
        True( HasArgumentPair( grokArgv, "--output-format", "streaming-messages-json" ) );
        var grokModels = grok.ParseModels( "You are logged in with grok.com.\n\nDefault model: grok-4.6\n\nAvailable models:\n  * grok-4.6 (default)\n  - grok-4.5\n" );
        Equal( 2, grokModels.Count );
        True( grokModels[0].Id == "grok-4.6" && grokModels[0].IsDefault && grokModels[1].Id == "grok-4.5" );
        True( grok.LoginMethod == AgentLoginMethods.DeviceCode && !grok.RequiresPastedCode && grok.StatusArguments is null );

        Throws<InvalidOperationException>( ( ) => CliAgentProfile.AssertSafe( ["--print", "--yolo"] ) );
        Throws<InvalidOperationException>( ( ) => CliAgentProfile.AssertSafe( ["--permission-mode", "bypassPermissions"] ) );
    }

    private static void ClaudeModelsExposeEffortsAndForwardCliArguments( )
    {
        ClaudeCodeProfile claude = new( );
        string[] expectedEfforts = ["low", "medium", "high", "xhigh", "max"];
        var models = claude.ParseModels( string.Empty );
        Equal( 4, models.Count );
        foreach ( var model in models )
        {
            True( model.ReasoningEfforts.Select( static effort => effort.Id ).SequenceEqual( expectedEfforts ),
                $"Claude model {model.Id} must expose every CLI effort to the picker." );
            foreach ( var effort in expectedEfforts )
            {
                var argv = claude.BuildTurnArguments( new CliAgentTurnRequest
                {
                    Prompt = "hello",
                    ModelId = model.Id,
                    ReasoningEffort = effort
                } );
                True( HasArgumentPair( argv, "--model", model.Id ) && HasArgumentPair( argv, "--effort", effort ) );
                Equal( 1, argv.Count( static argument => argument == "--effort" ) );
            }
        }
        foreach ( var effort in new string?[] { null, string.Empty } )
        {
            var argv = claude.BuildTurnArguments( new CliAgentTurnRequest { Prompt = "hello", ReasoningEffort = effort } );
            True( !argv.Contains( "--effort" ), "An unset effort leaves the CLI default unchanged." );
        }
    }

    private static void CliAgentSandboxBindsOnlyTheOwnedHomeAndInstallTree( )
    {
        CliAgentLaunchRequest request = new( )
        {
            BinaryPath = "/home/user/.local/share/claude/versions/2.1.263",
            InstallPath = "/home/user/.local/share/claude/versions",
            HomePath = "/home/user/.local/state/commanddeck/claude/home",
            WorkingDirectory = "/run/user/1000/commanddeck/claude/gen",
            Arguments = ["auth", "login"],
            Environment = new Dictionary<string, string>( StringComparer.Ordinal ) { ["NO_OPEN_BROWSER"] = "1" }
        };
        var sandbox = string.Join( ' ', BubblewrapCliAgentLauncher.BuildArguments( request ) );
        True( sandbox.Contains( "--unshare-all --share-net --die-with-parent", StringComparison.Ordinal ) );
        True( sandbox.Contains( "--tmpfs /home", StringComparison.Ordinal ) && sandbox.Contains( "--bind /home/user/.local/state/commanddeck/claude/home /home/user/.local/state/commanddeck/claude/home", StringComparison.Ordinal ) );
        True( sandbox.Contains( "--ro-bind /home/user/.local/share/claude/versions /home/user/.local/share/claude/versions", StringComparison.Ordinal ) );
        True( sandbox.Contains( "--clearenv --setenv HOME /home/user/.local/state/commanddeck/claude/home", StringComparison.Ordinal ) );
        True( sandbox.Contains( "--setenv NO_OPEN_BROWSER 1", StringComparison.Ordinal ) && sandbox.EndsWith( " -- /home/user/.local/share/claude/versions/2.1.263 auth login", StringComparison.Ordinal ) );
        True( !sandbox.Contains( "/home/user/.claude", StringComparison.Ordinal ) && !sandbox.Contains( "--bind /home/user ", StringComparison.Ordinal ) );
        Throws<InvalidOperationException>( ( ) => BubblewrapCliAgentLauncher.BuildArguments( new CliAgentLaunchRequest
        {
            BinaryPath = "/bin/x",
            InstallPath = "/bin",
            HomePath = "/h",
            WorkingDirectory = "/w",
            Environment = new Dictionary<string, string>( StringComparer.Ordinal ) { ["BAD=KEY"] = "1" }
        } ) );
    }

    private static void CliAgentStreamReaderMapsMessagesAndCursorFrames( )
    {
        CliAgentStreamReader messages = new( CliAgentStreamFormats.Messages );
        var init = messages.Read( "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"s1\",\"model\":\"m\"}" );
        Equal( "s1", init?.SessionId );
        var delta = messages.Read( "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Hel\"}}}" );
        Equal( "Hel", delta?.Text );
        var thinking = messages.Read( "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"hmm\"}}}" );
        Equal( "hmm", thinking?.Reasoning );
        Equal( null, messages.Read( "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}]}}" ) );
        var result = messages.Read( "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Hello\",\"session_id\":\"s1\",\"usage\":{\"input_tokens\":10,\"output_tokens\":4}}" );
        True( result is { Terminal: true, IsError: false, FinalText: null } && result.Usage?.TotalTokens == 14 && messages.SawResult );
        Equal( null, messages.Read( "not json" ) );

        CliAgentStreamReader whole = new( CliAgentStreamFormats.Messages );
        Equal( "Whole", whole.Read( "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Whole\"}]}}" )?.Text );
        var failed = whole.Read( "{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"Not logged in. Please run /login\"}" );
        True( failed is { Terminal: true, IsError: true } && failed.ErrorCategory == AiErrorCategories.Authentication );
        True( failed!.ErrorSummary is not null && !failed.ErrorSummary.Contains( "/login", StringComparison.Ordinal ) );
        CliAgentStreamReader maxTurns = new( CliAgentStreamFormats.Messages );
        var limited = maxTurns.Read( "{\"type\":\"result\",\"subtype\":\"error_max_turns\",\"is_error\":false,\"result\":\"\"}" );
        True( limited is { IsError: true } && limited.ErrorCategory == AiErrorCategories.Unavailable );
        CliAgentStreamReader fallback = new( CliAgentStreamFormats.Messages );
        Equal( "Final", fallback.Read( "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Final\"}" )?.FinalText );

        CliAgentStreamReader cursor = new( CliAgentStreamFormats.Cursor );
        Equal( "c1", cursor.Read( "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"c1\"}" )?.SessionId );
        Equal( "Hi", cursor.Read( "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Hi\"}]},\"timestamp_ms\":1}" )?.Text );
        Equal( null, cursor.Read( "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Hi there\"}]},\"model_call_id\":\"m1\"}" ) );
        Equal( "Tool call declined", cursor.Read( "{\"type\":\"tool_call\",\"subtype\":\"started\",\"call_id\":\"t\"}" )?.Status );
        var cursorResult = cursor.Read( "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Hi there\",\"session_id\":\"c1\"}" );
        True( cursorResult is { Terminal: true, IsError: false, FinalText: null } );
    }

    private static async Task CliAgentBackendOwnsSignInModelsAndSandboxedTurnsAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-cli-agent-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var binary = CreateFakeCliBinary( root, "claude" );
            var options = CreateCliAgentOptions( root, claude: binary );
            var authenticated = false;
            ScriptedCliLauncher launcher = new( )
            {
                Script = async ( request, process ) =>
                {
                    var argv = string.Join( ' ', request.Arguments );
                    if ( argv == "auth status --json" )
                    {
                        process.Stdout.WriteLine( authenticated ? "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}" : "{\"loggedIn\":false}" );
                        process.Exit( 0 );
                    }
                    else if ( argv == "auth login" )
                    {
                        process.Stdout.Write( "Opening browser to sign in…\nIf the browser didn't open, visit: https://claude.com/oauth?code=true&state=abc\nPaste code here if prompted > " );
                        await process.Stdin.Closed.Task;
                        authenticated = process.Stdin.Text == "pasted-code\n";
                        Directory.CreateDirectory( Path.Combine( request.HomePath, ".claude" ) );
                        await File.WriteAllTextAsync( Path.Combine( request.HomePath, ".claude", ".credentials.json" ), "{}" );
                        process.Exit( 0 );
                    }
                    else if ( argv == "auth logout" )
                    {
                        authenticated = false;
                        process.Exit( 0 );
                    }
                    else if ( request.Arguments.Contains( "--print" ) )
                    {
                        await process.Stdin.Closed.Task;
                        if ( process.Stdin.Text == "fail me" )
                        {
                            process.Stdout.WriteLine( "{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"Not logged in\"}" );
                            process.Exit( 1 );
                            return;
                        }
                        process.Stdout.WriteLine( "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"sess-1\"}" );
                        process.Stdout.WriteLine( "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Hel\"}}}" );
                        process.Stdout.WriteLine( "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"lo\"}}}" );
                        process.Stdout.WriteLine( "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Hello\",\"session_id\":\"sess-1\",\"usage\":{\"input_tokens\":3,\"output_tokens\":2}}" );
                        process.Exit( 0 );
                    }
                    else
                    {
                        process.Exit( 2 );
                    }
                }
            };
            await using CliAgentBackend backend = new( new ClaudeCodeProfile( ), options, launcher );
            Equal( AgentBackendIds.ClaudeCode, backend.Id );
            var descriptor = backend.Describe( );
            True( descriptor.Available && descriptor.Capabilities.Login && !descriptor.Capabilities.Tools && descriptor.Capabilities.Conversation );
            Equal( AgentAuthenticationStates.Unauthenticated, descriptor.AuthenticationState );
            Equal( 0, launcher.Requests.Count );

            var probed = await backend.GetLoginStateAsync( default );
            Equal( AgentAuthenticationStates.Unauthenticated, probed.State );
            Equal( "auth status --json", string.Join( ' ', launcher.Requests[0].Arguments ) );
            True( launcher.Requests[0].HomePath == Path.Combine( root, "state", "claude", "home" ) );
            True( launcher.Requests[0].WorkingDirectory.StartsWith( Path.Combine( root, "runtime", "claude" ), StringComparison.Ordinal ) );
            Equal( binary, launcher.Requests[0].BinaryPath );
            Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, UnixModeOf( launcher.Requests[0].HomePath ) );

            var models = await backend.DiscoverModelsAsync( false, default );
            True( models.Error is null && models.Models.Count == 4 && models.DefaultModelId == "sonnet" );
            Equal( 1, launcher.Requests.Count );

            var apiKey = await ThrowsAiAsync( async ( ) => await backend.StartLoginAsync( AgentLoginMethods.ApiKey, null, default ) );
            Equal( AiErrorCategories.Unsupported, apiKey.Category );
            var pending = await backend.StartLoginAsync( AgentLoginMethods.Browser, null, default );
            Equal( AgentAuthenticationStates.Pending, pending.State );
            True( pending.RequiresCode && pending.VerificationUrl == "https://claude.com/oauth?code=true&state=abc" );
            True( launcher.Requests[1].StandardInput );
            Equal( AgentAuthenticationStates.Pending, (await backend.GetLoginStateAsync( default )).State );
            var completed = await backend.CompleteLoginAsync( "pasted-code", default );
            Equal( AgentAuthenticationStates.Authenticated, completed.State );
            Equal( "pasted-code\n", launcher.Processes[1].Stdin.Text );
            True( File.Exists( Path.Combine( root, "state", "claude", "home", ".claude", ".credentials.json" ) ) );
            True( !Directory.Exists( Path.Combine( root, "runtime", "claude" ) ) || Directory.GetDirectories( Path.Combine( root, "runtime", "claude" ) ).Length == 0 );

            RecordingTurnHost host = new( );
            var session = await backend.StartSessionAsync( new AgentSessionOptions
            {
                ModelId = "sonnet",
                DeveloperInstructions = "answer only",
                Tools = []
            }, default );
            await session.RunTurnAsync( new AgentTurnInput { Text = "hello" }, host, default );
            Equal( "Hello", host.Text.ToString( ) );
            Equal( 5L, host.Usage?.TotalTokens );
            var turn = launcher.Requests[^1];
            True( turn.Arguments.Contains( "--print" ) && !turn.Arguments.Contains( "--resume" ) && turn.StandardInput );
            Equal( "hello", launcher.Processes[^1].Stdin.Text );
            Equal( "sess-1", (( CliAgentSession ) session).ContinuationId );
            await session.RunTurnAsync( new AgentTurnInput { Text = "again" }, host, default );
            True( HasArgumentPair( launcher.Requests[^1].Arguments, "--resume", "sess-1" ) );
            True( backend.Describe( ).Error is null );

            var failure = await ThrowsAiAsync( async ( ) =>
            {
                await session.RunTurnAsync( new AgentTurnInput { Text = "fail me" }, host, default );
                return true;
            } );
            Equal( AiErrorCategories.Authentication, failure.Category );
            Equal( AiErrorCategories.Authentication, backend.Describe( ).Error?.Category );

            var loggedOut = await backend.LogoutAsync( default );
            Equal( AgentAuthenticationStates.Unauthenticated, loggedOut.State );
            Equal( "auth logout", string.Join( ' ', launcher.Requests[^1].Arguments ) );
            True( !File.Exists( Path.Combine( root, "state", "claude", "home", ".claude", ".credentials.json" ) ) );
            await backend.StopAsync( default );
            True( !Directory.Exists( Path.Combine( root, "runtime", "claude" ) ) );

            var missing = new CliAgentBackend( new CursorProfile( ), CreateCliAgentOptions( root, cursor: Path.Combine( root, "no-cursor" ) ), launcher );
            var unavailable = missing.Describe( );
            True( !unavailable.Available && unavailable.HealthState == AgentHealthStates.Unavailable );
            var missingModels = await missing.DiscoverModelsAsync( false, default );
            Equal( AiErrorCategories.Unavailable, missingModels.Error?.Category );
            await missing.DisposeAsync( );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task CliAgentGrokDeviceCodeLoginHarvestsUrlAndCodeAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-cli-grok-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var binary = CreateFakeCliBinary( root, "grok" );
            TaskCompletionSource authorize = new( TaskCreationOptions.RunContinuationsAsynchronously );
            ScriptedCliLauncher launcher = new( )
            {
                Script = async ( request, process ) =>
                {
                    if ( string.Join( ' ', request.Arguments ) == "login --device-auth" )
                    {
                        process.Stdout.Write( "\nTo sign in, open this URL in your browser:\n\n  https://accounts.x.ai/oauth2/device?user_code=ABCD-EFGH\n\nConfirm this code in your browser:\n\n  ABCD-EFGH\n\nWaiting for authorization...\n" );
                        await authorize.Task;
                        Directory.CreateDirectory( Path.Combine( request.HomePath, ".grok" ) );
                        await File.WriteAllTextAsync( Path.Combine( request.HomePath, ".grok", "auth.json" ), "{}" );
                        process.Exit( 0 );
                        return;
                    }
                    process.Exit( 3 );
                }
            };
            await using CliAgentBackend backend = new( new GrokProfile( ), CreateCliAgentOptions( root, grok: binary ), launcher );
            Equal( AgentAuthenticationStates.Unauthenticated, (await backend.GetLoginStateAsync( default )).State );
            True( launcher.Requests.Count == 0, "the credential-file probe spawns nothing" );
            var pending = await backend.StartLoginAsync( AgentLoginMethods.DeviceCode, null, default );
            Equal( AgentAuthenticationStates.Pending, pending.State );
            Equal( "https://accounts.x.ai/oauth2/device?user_code=ABCD-EFGH", pending.VerificationUrl );
            True( !pending.RequiresCode && !launcher.Requests[0].StandardInput );
            await Task.Delay( 50 );
            Equal( "ABCD-EFGH", (await backend.GetLoginStateAsync( default )).UserCode );
            var rejected = await ThrowsAiAsync( async ( ) => await backend.CompleteLoginAsync( "nope", default ) );
            Equal( AiErrorCategories.Unsupported, rejected.Category );
            authorize.SetResult( );
            for ( var attempt = 0; attempt < 50 && (await backend.GetLoginStateAsync( default )).State == AgentAuthenticationStates.Pending; attempt++ )
            {
                await Task.Delay( 20 );
            }
            Equal( AgentAuthenticationStates.Authenticated, (await backend.GetLoginStateAsync( default )).State );
            True( backend.Home.CredentialExists );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
