namespace CommandDeck.Specs;

public static partial class Program
{
    // A settings save that is still shutting the backend down must not write
    // back the trust level and lease it read before a downgrade completed.
    private static async Task AgentSettingsSaveCannotRestoreRevokedTrustAsync( )
    {
        ScriptedAgentBackend backend = new( );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                var raise = await runtime.CreateAgentTrustChallengeAsync( "owner", AgentTrustLevels.Unrestricted, default );
                await runtime.ApplyAgentTrustAsync(
                    "owner", raise.Id, AgentTrustLevels.Unrestricted, AgentLimits.UnrestrictedPhrase, AgentTrustDurations.OneHour, default );
                var raised = await runtime.GetAgentPreferencesAsync( default );
                Equal( AgentTrustLevels.Unrestricted, raised.TrustLevel );
                True( raised.TrustLeaseExpiresUnixMilliseconds is not null, "the raise holds a one-hour lease" );

                // A web-search change shuts the backend down before it writes.
                TaskCompletionSource<bool> releaseStop = new( TaskCreationOptions.RunContinuationsAsynchronously );
                backend.BlockNextStop = releaseStop;
                var pendingSave = runtime.SetAgentPreferencesAsync( new AgentPreferences
                {
                    Enabled = true,
                    TrustLevel = raised.TrustLevel,
                    TrustDuration = raised.TrustDuration,
                    TrustLeaseExpiresUnixMilliseconds = raised.TrustLeaseExpiresUnixMilliseconds,
                    WebSearchEnabled = true
                }, default ).AsTask( );
                await backend.StopEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
                try
                {
                    var lower = await runtime.CreateAgentTrustChallengeAsync( "owner", AgentTrustLevels.Default, default );
                    var lowered = await runtime.ApplyAgentTrustAsync( "owner", lower.Id, AgentTrustLevels.Default, null, null, default );
                    Equal( AgentTrustLevels.Default, lowered.Level );
                }
                finally
                {
                    releaseStop.TrySetResult( true );
                }

                var saved = await pendingSave.WaitAsync( TimeSpan.FromSeconds( 10 ) );
                var persisted = await runtime.GetAgentPreferencesAsync( default );
                foreach ( var preferences in new[] { saved, persisted } )
                {
                    Equal( AgentTrustLevels.Default, preferences.TrustLevel );
                    True( preferences.TrustDuration is null, "the downgrade's cleared duration survives the save" );
                    True( preferences.TrustLeaseExpiresUnixMilliseconds is null, "the revoked lease is not restored" );
                    True( preferences.WebSearchEnabled, "the save's own change persists" );
                }
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    // Resetting customizations ends the assistant's authority: the unrestricted
    // turn and its backend stop, and the reset record holds no trust or lease.
    private static async Task AgentCustomizationResetRevokesAssistantAuthorityAsync( )
    {
        ScriptedAgentBackend backend = new( );
        TaskCompletionSource turnStarted = new( TaskCreationOptions.RunContinuationsAsynchronously );
        backend.Script = async ( _, _, token ) =>
        {
            turnStarted.TrySetResult( );
            await Task.Delay( Timeout.Infinite, token ).ConfigureAwait( false );
        };
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                var raise = await runtime.CreateAgentTrustChallengeAsync( "owner", AgentTrustLevels.Unrestricted, default );
                await runtime.ApplyAgentTrustAsync(
                    "owner", raise.Id, AgentTrustLevels.Unrestricted, AgentLimits.UnrestrictedPhrase, AgentTrustDurations.OneHour, default );
                await runtime.StartAgentTurnAsync( "work while resetting", null, null, null, "owner", default );
                await turnStarted.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
                var stopsBeforeReset = backend.StopCount;

                await runtime.ResetCustomizationsAsync( default );

                // Inspect live state before any preference read, which would
                // otherwise revoke a surviving unrestricted turn on its own.
                Equal( 0L, runtime.GetAgentConversation( "owner" ).ActiveTurnId );
                True( !backend.IsRunning && backend.StopCount > stopsBeforeReset, "reset stops the unrestricted backend" );
                var reset = await runtime.GetAgentPreferencesAsync( default );
                True( !reset.Enabled, "reset disables the assistant" );
                Equal( AgentTrustLevels.Default, reset.TrustLevel );
                True( reset.TrustDuration is null && reset.TrustLeaseExpiresUnixMilliseconds is null, "reset leaves no lease" );
                Equal( 0, (await runtime.GetAgentGrantsAsync( default )).Grants.Count );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    // A reset that arrives while the owner is mid-write waits for it, then
    // supersedes it: no trust, lease, setting, or grant written before survives.
    private static async Task AgentAuthorityResetWaitsForAnInFlightWriteAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-reset-race-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            PausingAgentPreferenceStore store = new( catalog.AgentPreferences );
            using AgentAuthority authority = new( store );
            await authority.UpdateSettingsAsync( new AgentPreferences { Enabled = true }, authority.Snapshot.SessionEpoch, default );
            await authority.SetTrustAsync(
                AgentTrustLevels.Unrestricted,
                AgentTrustDurations.OneHour,
                DateTimeOffset.UtcNow.AddMinutes( 60 ).ToUnixTimeMilliseconds( ),
                default );
            await authority.AddGrantAsync( new AgentGrant
            {
                Id = "grant-before-reset",
                Kind = AgentGrantKinds.Folder,
                Scope = root,
                PayloadDigest = "digest",
                State = AgentGrantStates.Active
            }, default );

            TaskCompletionSource<bool> releaseRead = new( TaskCreationOptions.RunContinuationsAsynchronously );
            store.HoldNextRead = releaseRead;
            var save = authority.UpdateSettingsAsync(
                new AgentPreferences { Enabled = true, WebSearchEnabled = true }, authority.Snapshot.SessionEpoch, default ).AsTask( );
            await store.ReadHeld.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            var reset = authority.ResetAsync( default ).AsTask( );
            True( !reset.IsCompleted, "reset waits for the write that holds the owner" );
            releaseRead.TrySetResult( true );
            await save.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            await reset.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            var persisted = await catalog.AgentPreferences.GetAgentPreferencesAsync( default );
            foreach ( var preferences in new[] { persisted, authority.Current } )
            {
                True( !preferences.Enabled && !preferences.WebSearchEnabled, "reset supersedes the in-flight save" );
                Equal( AgentTrustLevels.Default, preferences.TrustLevel );
                True( preferences.TrustDuration is null && preferences.TrustLeaseExpiresUnixMilliseconds is null, "no lease survives reset" );
            }
            Equal( 0, (await catalog.AgentPreferences.GetAgentGrantsAsync( default )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // Lease enforcement must never write back a snapshot older than a trust
    // change that finished while it was reading.
    private static async Task AgentLeaseEnforcementKeepsAFreshRaiseAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-lease-race-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 10, 8, 12, 0, 0, TimeSpan.Zero ) );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) }, clock );
            await catalog.InitializeAsync( default );
            var expired = await catalog.AgentPreferences.SetAgentPreferencesAsync(
                new AgentPreferences { Enabled = true }.WithTrust(
                    AgentTrustLevels.Unrestricted,
                    AgentTrustDurations.OneHour,
                    clock.GetUtcNow( ).AddMinutes( 60 ).ToUnixTimeMilliseconds( ) ),
                default );
            clock.Advance( TimeSpan.FromMinutes( 61 ) );

            PausingAgentPreferenceStore store = new( catalog.AgentPreferences );
            AgentAuthority authority = new( store, clock );
            AgentTrustService trust = new( authority, clock );
            TaskCompletionSource<bool> releaseRead = new( TaskCreationOptions.RunContinuationsAsynchronously );
            store.HoldNextRead = releaseRead;
            var enforcement = authority.LoadAsync( default ).AsTask( );
            await store.ReadHeld.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            // The raise starts while enforcement holds its expired read.
            var challenge = trust.CreateChallenge( "owner", AgentTrustLevels.Unrestricted, expired );
            var freshLease = clock.GetUtcNow( ).AddMinutes( 60 ).ToUnixTimeMilliseconds( );
            var raise = trust.ApplyAsync(
                "owner", challenge.Id, AgentTrustLevels.Unrestricted, AgentLimits.UnrestrictedPhrase, AgentTrustDurations.OneHour, default ).AsTask( );
            releaseRead.TrySetResult( true );
            var enforced = await enforcement.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            True( enforced.LeaseExpired, "enforcement ended the expired lease" );
            await raise.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            var persisted = await catalog.AgentPreferences.GetAgentPreferencesAsync( default );
            Equal( AgentTrustLevels.Unrestricted, persisted.TrustLevel );
            True( persisted.TrustLeaseExpiresUnixMilliseconds == freshLease, "the fresh lease survives enforcement" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // Only the authority owner may hold the preference store, so no other type
    // can write trust, settings, or grants around the owner's gate.
    private static void AgentAuthorityIsTheOnlyPreferenceStoreHolder( )
    {
        var root = FindRepositoryRoot( );
        const string catalog = "src/CommandDeck.Infrastructure/Storage/SqliteCatalog.cs";
        const string compositionRoot = "src/CommandDeck.App/Runtime/CommandDeckRuntime.Agent.cs";
        string[] storeHolders =
        [
            "src/CommandDeck.Core/Abstractions/AgentInterfaces.cs",
            "src/CommandDeck.Infrastructure/Storage/SqliteAgentPreferenceStore.cs",
            "src/CommandDeck.Infrastructure/Agent/AgentAuthority.cs",
            catalog
        ];
        var composed = false;
        foreach ( var path in Directory.EnumerateFiles( Path.Combine( root, "src" ), "*.cs", SearchOption.AllDirectories )
            .Where( IsProductionSource ) )
        {
            var relative = Path.GetRelativePath( root, path );
            var source = File.ReadAllText( path );
            if ( !storeHolders.Contains( relative, StringComparer.Ordinal ) )
            {
                AssertSourceExcludes( path, source, ["IAgentPreferenceStore"] );
            }
            var storeReads = CountOccurrences( source, "atalog.AgentPreferences" );
            if ( relative == compositionRoot )
            {
                Equal( 1, storeReads );
                True( source.Contains( "new AgentAuthority( catalog.AgentPreferences", StringComparison.Ordinal ),
                    "the composition root hands the catalog's store only to the authority owner" );
                composed = true;
            }
            else if ( relative != catalog )
            {
                Equal( 0, storeReads );
            }
        }
        True( composed, "the composition root was scanned" );

        // Raw SQL must not reach around the owner either: only the agent store
        // touches grants or its preference key, and every other write to the
        // shared preference table names the keys it owns.
        const string agentStore = "src/CommandDeck.Infrastructure/Storage/SqliteAgentPreferenceStore.cs";
        const string schema = "src/CommandDeck.Infrastructure/Storage/SqliteCatalog.Schema.cs";
        foreach ( var path in Directory.EnumerateFiles( Path.Combine( root, "src" ), "*.cs", SearchOption.AllDirectories )
            .Where( IsProductionSource ) )
        {
            var relative = Path.GetRelativePath( root, path );
            if ( relative is agentStore or schema )
            {
                continue;
            }
            var source = File.ReadAllText( path );
            AssertSourceExcludes( path, source, ["agent_grants", "'agent.preferences'", "UPDATE ai_preferences", "REPLACE INTO ai_preferences"] );
            for ( var index = source.IndexOf( "DELETE FROM ai_preferences", StringComparison.Ordinal );
                index >= 0;
                index = source.IndexOf( "DELETE FROM ai_preferences", index + 1, StringComparison.Ordinal ) )
            {
                True( string.CompareOrdinal( source, index, "DELETE FROM ai_preferences WHERE key IN (", 0, 41 ) == 0,
                    $"{relative} deletes shared preference rows without naming its keys" );
            }
        }
    }

    // Passes every call through and can hold the next preference read after it
    // completes, so a spec can finish another change inside that window.
    private sealed class PausingAgentPreferenceStore( IAgentPreferenceStore inner ) : IAgentPreferenceStore
    {
        private TaskCompletionSource<bool>? _holdNextRead;

        public TaskCompletionSource<bool>? HoldNextRead
        {
            set => Volatile.Write( ref _holdNextRead, value );
        }

        public TaskCompletionSource ReadHeld { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask<AgentPreferences> GetAgentPreferencesAsync( CancellationToken cancellationToken )
        {
            var preferences = await inner.GetAgentPreferencesAsync( cancellationToken ).ConfigureAwait( false );
            var hold = Interlocked.Exchange( ref _holdNextRead, null );
            if ( hold is not null )
            {
                ReadHeld.TrySetResult( );
                await hold.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
            }
            return preferences;
        }

        public ValueTask<AgentPreferences> SetAgentPreferencesAsync( AgentPreferences preferences, CancellationToken cancellationToken ) =>
            inner.SetAgentPreferencesAsync( preferences, cancellationToken );

        public ValueTask<IReadOnlyList<AgentGrant>> GetAgentGrantsAsync( CancellationToken cancellationToken ) =>
            inner.GetAgentGrantsAsync( cancellationToken );

        public ValueTask<AgentGrant> AddAgentGrantAsync( AgentGrant grant, CancellationToken cancellationToken ) =>
            inner.AddAgentGrantAsync( grant, cancellationToken );

        public ValueTask<bool> UpdateAgentGrantStateAsync(
            string grantId, string state, long validatedUnixMilliseconds, CancellationToken cancellationToken ) =>
            inner.UpdateAgentGrantStateAsync( grantId, state, validatedUnixMilliseconds, cancellationToken );

        public ValueTask<bool> RemoveAgentGrantAsync( string grantId, CancellationToken cancellationToken ) =>
            inner.RemoveAgentGrantAsync( grantId, cancellationToken );

        public ValueTask ClearAgentGrantsAsync( CancellationToken cancellationToken ) =>
            inner.ClearAgentGrantsAsync( cancellationToken );
    }
}
