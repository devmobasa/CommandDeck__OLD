namespace CommandDeck.Specs;

public static partial class Program
{
    // Builds a conversation coordinator directly on the in-process fake backend
    // so a spec can drive prewarm and the sweep against a controllable clock.
    private static AgentConversationCoordinator CreateAgentCoordinator(
        SqliteCatalog catalog,
        ScriptedAgentBackend backend,
        string homeDirectory,
        TimeProvider clock )
    {
        AgentAuthority authority = new( catalog.AgentPreferences, clock );
        return new(
            authority,
            backend,
            new AgentContextBroker( authority, homeDirectory ),
            new AgentCommandTool( homeDirectory ),
            new StubAgentLauncherHost( ),
            packages: null,
            helper: null,
            clock: clock );
    }

    private static async Task AgentPrewarmStartsTheSessionWithoutRunningATurnAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-agent-prewarm-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            ScriptedAgentBackend backend = new( );
            AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 9, 4, 12, 0, 0, TimeSpan.Zero ) );
            await using var coordinator = CreateAgentCoordinator( catalog, backend, root, clock );

            // Prewarm spawns and connects a session but never starts a turn.
            await coordinator.PrewarmAsync( default );
            Equal( 1, backend.Sessions );
            var warmed = coordinator.Snapshot( );
            True( warmed.Summary is not null, "a prewarmed conversation exists" );
            Equal( 0, warmed.Entries.Count );
            Equal( 0, warmed.ActiveTurnId );
            True( warmed.Summary!.ExpiresUnixMilliseconds is not null,
                "the prewarmed conversation gets an idle expiry so the sweep can reclaim it" );

            // Prewarming again is idempotent: no second spawn or thread start.
            await coordinator.PrewarmAsync( default );
            Equal( 1, backend.Sessions );

            // The first real turn reuses the warm session instead of spawning.
            await coordinator.StartTurnAsync( "hello", null, null, null, default );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( coordinator.Snapshot( ).Summary?.State == AgentConversationStates.Completed ),
                TimeSpan.FromSeconds( 2 ) ) );
            Equal( 1, backend.Sessions );
            Equal( 2, coordinator.Snapshot( ).Entries.Count );

            // A disabled assistant prewarms into a quiet no-op: no spawn, no throw.
            await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences { Enabled = false }, default );
            ScriptedAgentBackend disabledBackend = new( );
            await using ( var disabled = CreateAgentCoordinator( catalog, disabledBackend, root, clock ) )
            {
                await disabled.PrewarmAsync( default );
                Equal( 0, disabledBackend.Sessions );
            }

            // An enabled but signed-out assistant also no-ops without a spawn.
            await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            ScriptedAgentBackend signedOutBackend = new( )
            {
                Authenticated = false
            };
            await using ( var signedOut = CreateAgentCoordinator( catalog, signedOutBackend, root, clock ) )
            {
                await signedOut.PrewarmAsync( default );
                Equal( 0, signedOutBackend.Sessions );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentSweepKeepsTheProcessWarmPastTheConversationTtlAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-agent-keepalive-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            ScriptedAgentBackend backend = new( );
            backend.Script = ( input, host, token ) => host.OnTextAsync( "ok", token );
            AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 9, 4, 12, 0, 0, TimeSpan.Zero ) );
            await using var coordinator = CreateAgentCoordinator( catalog, backend, root, clock );

            await coordinator.StartTurnAsync( "hello", null, null, null, default );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( coordinator.Snapshot( ).Summary?.State == AgentConversationStates.Completed ),
                TimeSpan.FromSeconds( 2 ) ) );
            Equal( 1, backend.Sessions );

            // A sweep before the conversation TTL keeps everything.
            clock.Advance( TimeSpan.FromMinutes( 5 ) );
            await coordinator.SweepAsync( default );
            True( coordinator.Snapshot( ).Summary is not null, "the conversation survives before its TTL" );
            True( !backend.Stopped && backend.IsRunning );

            // Past the conversation TTL the sweep drops the transcript but keeps
            // the sandboxed process warm so the next prompt skips cold start.
            clock.Advance( TimeSpan.FromMinutes( AgentLimits.DefaultConversationTtlMinutes ) );
            await coordinator.SweepAsync( default );
            True( coordinator.Snapshot( ).Summary is null, "the transcript is cleared at the conversation TTL" );
            True( !backend.Stopped && backend.IsRunning,
                "the sandboxed process stays warm after the transcript is dropped" );
            Equal( 1, backend.Sessions );

            // Past the keep-alive window with no live conversation, the sweep
            // finally reclaims the warm process.
            clock.Advance( TimeSpan.FromMinutes( AgentLimits.DefaultBackendKeepAliveMinutes ) );
            await coordinator.SweepAsync( default );
            True( backend.Stopped && !backend.IsRunning,
                "the warm process is reclaimed after the keep-alive window" );
            Equal( 1, backend.StopCount );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentSweepDoesNotStopASessionThatIsStillPreparingAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-agent-reclaim-race-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            ScriptedAgentBackend backend = new( );
            AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 9, 4, 12, 0, 0, TimeSpan.Zero ) );
            await using var coordinator = CreateAgentCoordinator( catalog, backend, root, clock );

            await coordinator.PrewarmAsync( default );
            Equal( 1, backend.Sessions );
            clock.Advance( TimeSpan.FromMinutes( AgentLimits.DefaultConversationTtlMinutes ) );
            await coordinator.SweepAsync( default );
            True( coordinator.Snapshot( ).Summary is null, "the transcript is cleared at the conversation TTL" );
            True( backend.IsRunning, "the sandboxed process stays warm after the transcript is dropped" );

            clock.Advance( TimeSpan.FromMinutes( AgentLimits.DefaultBackendKeepAliveMinutes ) );
            backend.BlockSessionStart = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
            var prewarm = coordinator.PrewarmAsync( default ).AsTask( );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( backend.Sessions == 2 ),
                TimeSpan.FromSeconds( 2 ) ) );
            var sweep = coordinator.SweepAsync( default ).AsTask( );
            await Task.Delay( 50 );
            True( !backend.Stopped, "sweep must not stop a session that is still preparing" );
            backend.BlockSessionStart.TrySetResult( true );
            await prewarm;
            await sweep;
            True( !backend.Stopped && backend.IsRunning,
                "an in-flight prepare keeps the warm process" );
            True( coordinator.Snapshot( ).Summary is not null,
                "the delayed incoming session finishes instead of failing" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentSweepEndsAnUnrestrictedTurnWhenItsLeaseExpiresAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-agent-lease-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 9, 4, 12, 0, 0, TimeSpan.Zero ) );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) }, clock );
            await catalog.InitializeAsync( default );
            var lease = clock.GetUtcNow( ).AddMinutes( AgentLimits.TrustLeaseHourMinutes ).ToUnixTimeMilliseconds( );
            await catalog.AgentPreferences.SetAgentPreferencesAsync(
                new AgentPreferences { Enabled = true }.WithTrust( AgentTrustLevels.Unrestricted, AgentTrustDurations.OneHour, lease ),
                default );
            ScriptedAgentBackend backend = new( );
            TaskCompletionSource started = new( TaskCreationOptions.RunContinuationsAsynchronously );
            backend.Script = async ( input, host, token ) =>
            {
                started.TrySetResult( );
                await Task.Delay( Timeout.InfiniteTimeSpan, token );
            };
            await using var coordinator = CreateAgentCoordinator( catalog, backend, root, clock );
            var events = coordinator.Subscribe( completeWhenIdle: false, out var subscriptionId );

            await coordinator.StartTurnAsync( "keep working", null, null, null, default );
            await started.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
            Equal( AgentTrustLevels.Unrestricted, backend.LastOptions!.TrustLevel );

            // Inside the lease, periodic maintenance leaves the unrestricted turn alone.
            clock.Advance( TimeSpan.FromMinutes( AgentLimits.TrustLeaseHourMinutes - 1 ) );
            await coordinator.SweepAsync( default );
            True( coordinator.HasActiveTurn, "the unrestricted turn runs while its lease is valid" );
            True( backend.IsRunning, "the full-session process runs while its lease is valid" );

            // Past the lease, maintenance alone must end the turn, persist the
            // downgrade, and stop the full-session process; no client request
            // or new turn is needed to revoke the authority.
            clock.Advance( TimeSpan.FromMinutes( 2 ) );
            await coordinator.SweepAsync( default );
            True( !coordinator.HasActiveTurn, "the unrestricted turn ends once its lease expires" );
            Equal( AgentTrustLevels.Default, (await catalog.AgentPreferences.GetAgentPreferencesAsync( default )).TrustLevel );
            True( !backend.IsRunning, "the full-session process stops once its lease expires" );

            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
            AgentTurnResult? done = null;
            await foreach ( var streamed in events.ReadAllAsync( timeout.Token ) )
            {
                done = streamed.Done ?? done;
            }
            coordinator.Unsubscribe( subscriptionId );
            Equal( AgentTurnOutcomes.Cancelled, done?.Outcome );
            Equal( "Unrestricted lease expired.", done?.Error?.Summary );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private sealed class StubAgentLauncherHost : IAgentLauncherHost
    {
        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( SearchRequest request, CancellationToken cancellationToken ) =>
            ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );

        public SearchResult? ResolveCachedResult( string resultId, string invocationReference ) => null;

        public ValueTask<ActionExecutionResult> InvokeAsync(
            string resultId,
            string actionId,
            string invocationReference,
            string query,
            bool confirmed,
            CancellationToken cancellationToken ) =>
            ValueTask.FromResult( ActionExecutionResult.Failure( "The stub launcher host performs no actions." ) );
    }
}
