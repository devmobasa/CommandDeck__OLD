using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    // The Codex backend launches or reuses a process only for an admission whose
    // epoch is still current, never reuses a process across epochs, launches
    // restricted for login and model discovery, and stops by generation only.
    private static async Task CodexBackendAdmitsProcessesOnlyAtTheCurrentEpochAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-codex-admission-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var options = CreateAgentOptions( root, CreateFakeCodexBinary( root ) );
            FakeCodexLauncher launcher = new( );
            await using CodexAgentBackend backend = new( options, new CodexCredentialStore( options.StateDirectory ), _ => launcher, clientVersion: "spec" );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            using AgentAuthority authority = new( catalog.AgentPreferences );
            await authority.LoadAsync( default );

            // An admission from before an authority change launches nothing.
            var revoked = authority.Snapshot.SessionEpoch;
            await authority.SetTrustAsync(
                AgentTrustLevels.Unrestricted, AgentTrustDurations.OneHour,
                DateTimeOffset.UtcNow.AddMinutes( 60 ).ToUnixTimeMilliseconds( ), default );
            await ThrowsAsync<AgentAuthorityChangedException>( ( ) =>
                backend.StartSessionAsync( AdmittedSession( AgentTrustLevels.Unrestricted, revoked, authority ), default ).AsTask( ) );
            Equal( 0, launcher.Requests.Count );

            // A current admission launches, and the session carries the
            // generation the backend picked under its gate.
            var unrestrictedEpoch = authority.Snapshot.SessionEpoch;
            var unrestricted = await backend.StartSessionAsync(
                AdmittedSession( AgentTrustLevels.Unrestricted, unrestrictedEpoch, authority ), default );
            Equal( 1, launcher.Requests.Count );
            Equal( AgentTrustLevels.Unrestricted, launcher.Requests[0].TrustLevel );
            Equal( launcher.Requests[0].Generation, unrestricted.ProcessGeneration );
            await backend.StopBeforeAsync( unrestrictedEpoch, default );
            True( backend.IsRunning, "a revocation for the process's own epoch leaves it running" );

            // After a downgrade, model discovery never reuses the stale
            // unrestricted process: it stops it and launches restricted.
            await authority.SetTrustAsync( AgentTrustLevels.Default, null, null, default );
            var models = await backend.DiscoverModelsAsync( includeHidden: false, default );
            True( models.Error is null );
            Equal( 2, launcher.Requests.Count );
            Equal( AgentTrustLevels.Default, launcher.Requests[1].TrustLevel );
            True( launcher.Servers[0].StopRequested, "the stale unrestricted process is stopped" );

            // A restricted session adopts the restricted process discovery started.
            var restricted = await backend.StartSessionAsync(
                AdmittedSession( AgentTrustLevels.Default, authority.Snapshot.SessionEpoch, authority ), default );
            Equal( 2, launcher.Requests.Count );
            Equal( launcher.Requests[1].Generation, restricted.ProcessGeneration );

            // Cleanup by generation never reaches a process that replaced it.
            await backend.StopGenerationAsync( unrestricted.ProcessGeneration, default );
            True( backend.IsRunning && !launcher.Servers[1].StopRequested, "a stale generation's cleanup leaves the replacement running" );
            await backend.StopGenerationAsync( restricted.ProcessGeneration, default );
            True( !backend.IsRunning && launcher.Servers[1].StopRequested, "cleanup stops the generation it names" );

            // An unrestricted session never starts without an admission.
            await ThrowsAsync<InvalidOperationException>( ( ) =>
                backend.StartSessionAsync( AdmittedSession( AgentTrustLevels.Unrestricted, null, null ), default ).AsTask( ) );
            Equal( 2, launcher.Requests.Count );
            await unrestricted.DisposeAsync( );
            await restricted.DisposeAsync( );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static Task CodexTurnLaunchingDuringADowngradeIsStoppedAsync( ) =>
        CodexLaunchDuringADowngradeIsStoppedAsync( prewarm: false );

    private static Task CodexPrewarmLaunchingDuringADowngradeStaysQuietAsync( ) =>
        CodexLaunchDuringADowngradeIsStoppedAsync( prewarm: true );

    // A level-6 launch that passed admission and is still starting when a
    // downgrade lands is stopped by that downgrade's revocation; the turn fails
    // with the retryable error, a prewarm stays quiet, and nothing is installed.
    private static async Task CodexLaunchDuringADowngradeIsStoppedAsync( bool prewarm )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-codex-launch-race-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var options = CreateAgentOptions( root, CreateFakeCodexBinary( root ) );
            CodexCredentialStore credentials = new( options.StateDirectory );
            credentials.Write( Encoding.UTF8.GetBytes( "{\"OPENAI_API_KEY\":\"synthetic-key\"}" ) );
            FakeCodexLauncher launcher = new( ) { Authenticated = true };
            await using CodexAgentBackend backend = new( options, credentials, _ => launcher, clientVersion: "spec" );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            using AgentAuthority authority = new( catalog.AgentPreferences );
            await authority.UpdateSettingsAsync( new AgentPreferences { Enabled = true }, authority.Snapshot.SessionEpoch, default );
            await authority.SetTrustAsync(
                AgentTrustLevels.Unrestricted, AgentTrustDurations.OneHour,
                DateTimeOffset.UtcNow.AddMinutes( 60 ).ToUnixTimeMilliseconds( ), default );
            await using AgentConversationCoordinator coordinator = new(
                authority, backend, new AgentContextBroker( authority, root ), new AgentCommandTool( root ), new StubAgentLauncherHost( ) );

            TaskCompletionSource<bool> releaseLaunch = new( TaskCreationOptions.RunContinuationsAsynchronously );
            launcher.HoldNextLaunch = releaseLaunch;
            var start = prewarm
                ? coordinator.PrewarmAsync( default ).AsTask( )
                : coordinator.StartTurnAsync( "work across a downgrade", null, null, null, default ).AsTask( );
            await launcher.LaunchEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            await authority.SetTrustAsync( AgentTrustLevels.Default, null, null, default );
            var revocation = coordinator.RevokeStaleAsync( default ).AsTask( );
            releaseLaunch.TrySetResult( true );
            await revocation.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            if ( prewarm )
            {
                await start.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            }
            else
            {
                var rejected = await CatchAsync<AgentAuthorityChangedException>( start );
                Equal( AgentAuthorityChangedException.Summary, rejected.SafeSummary );
            }

            Equal( AgentTrustLevels.Unrestricted, launcher.Requests[0].TrustLevel );
            True( launcher.Servers[0].StopRequested && !backend.IsRunning, "the level-6 process is stopped" );
            True( coordinator.Snapshot( ).Summary is null, "no conversation is installed at the revoked authority" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static AgentSessionOptions AdmittedSession( int trustLevel, long? epoch, IAgentAuthorityEpoch? authority ) => new( )
    {
        TrustLevel = trustLevel,
        DeveloperInstructions = "spec",
        Admission = epoch is { } admitted && authority is not null ? new AgentAdmission( admitted, authority ) : null
    };
}
