namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CliAgentRevocationPreservesNewerTurnScratchAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-cli-revocation-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        TaskCompletionSource releaseDisposal = new( TaskCreationOptions.RunContinuationsAsynchronously );
        try
        {
            var binary = CreateFakeCliBinary( root, "grok" );
            TaskCompletionSource turnStarted = new( TaskCreationOptions.RunContinuationsAsynchronously );
            ScriptedCliLauncher launcher = new( )
            {
                Script = ( request, process ) =>
                {
                    if ( request.Arguments.Contains( "login" ) )
                    {
                        process.Stdout.WriteLine( "https://accounts.x.ai/oauth2/device?user_code=ABCD-EFGH" );
                    }
                    else
                    {
                        turnStarted.TrySetResult( );
                    }
                    return Task.CompletedTask;
                }
            };
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            using AgentAuthority authority = new( catalog.AgentPreferences );
            await authority.UpdateSettingsAsync( new AgentPreferences { Enabled = true }, authority.Snapshot.SessionEpoch, default );
            await using CliAgentBackend backend = new( new GrokProfile( ), CreateCliAgentOptions( root, grok: binary ), launcher );
            var login = await backend.StartLoginAsync( AgentLoginMethods.DeviceCode, null, default );
            Equal( AgentAuthenticationStates.Pending, login.State );
            var loginProcess = launcher.Processes.Single( );
            var loginScratch = launcher.Requests.Single( ).WorkingDirectory;
            loginProcess.BlockDisposal = releaseDisposal;

            await authority.SetTrustAsync( AgentTrustLevels.Packages, null, null, default );
            var epoch = authority.Snapshot.SessionEpoch;
            var revoking = backend.StopBeforeAsync( epoch, default ).AsTask( );
            await loginProcess.DisposeEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            // A turn at the current epoch starts while old login cleanup waits.
            await using var session = await backend.StartSessionAsync(
                AdmittedSession( AgentTrustLevels.Packages, epoch, authority ), default );
            RecordingTurnHost host = new( );
            var turn = session.RunTurnAsync( new AgentTurnInput { Text = "Keep my working directory" }, host, default ).AsTask( );
            await turnStarted.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            var turnProcess = launcher.Processes[^1];
            var turnScratch = launcher.Requests[^1].WorkingDirectory;
            var promptFile = Path.Combine( turnScratch, "prompt.txt" );
            True( HasArgumentPair( launcher.Requests[^1].Arguments, "--prompt-file", promptFile ) );

            releaseDisposal.TrySetResult( );
            await revoking.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            True( !Directory.Exists( loginScratch ), "revoked login cleans up its own scratch" );
            True( backend.IsRunning && !turnProcess.Terminated, "the newer turn keeps running" );
            True( Directory.Exists( turnScratch ), "revocation preserves the newer turn's working directory" );
            Equal( "Keep my working directory", await File.ReadAllTextAsync( promptFile ) );

            await backend.StopBeforeAsync( epoch, default );
            True( !turnProcess.Terminated && File.Exists( promptFile ), "a turn at the current epoch keeps its prompt file" );
            turnProcess.Stdout.WriteLine( "{\"type\":\"result\",\"subtype\":\"success\",\"result\":\"Completed\"}" );
            turnProcess.Exit( 0 );
            await turn.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            Equal( "Completed", host.Text.ToString( ) );
            True( !Directory.Exists( turnScratch ), "the completed turn cleans up its own scratch" );
        }
        finally
        {
            releaseDisposal.TrySetResult( );
            Directory.Delete( root, recursive: true );
        }
    }
}
