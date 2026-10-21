namespace CommandDeck.Specs;

public static partial class Program
{
    // The owner rejects a settings save validated before a reset or disable and
    // writes nothing; a save validated afterwards, including an enable, succeeds.
    private static async Task AgentAuthorityRejectsAStaleSaveAsync( )
    {
        foreach ( var disable in new[] { false, true } )
        {
            var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-stale-save-{Guid.NewGuid( ):N}" );
            Directory.CreateDirectory( root );
            try
            {
                await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
                await catalog.InitializeAsync( default );
                using AgentAuthority authority = new( catalog.AgentPreferences );
                await authority.UpdateSettingsAsync( new AgentPreferences { Enabled = true }, authority.Snapshot.SessionEpoch, default );
                var validated = authority.Snapshot.SessionEpoch;
                if ( disable )
                {
                    await authority.DisableAsync( new AgentPreferences { Enabled = false }, default );
                }
                else
                {
                    await authority.ResetAsync( default );
                }

                var rejected = await CatchAsync<AiOperationException>( authority.UpdateSettingsAsync(
                    new AgentPreferences { Enabled = true, WebSearchEnabled = true }, validated, default ).AsTask( ) );
                Equal( AiErrorCategories.Conflict, rejected.Category );
                Equal( AgentAuthority.SavingSummary, rejected.SafeSummary );
                var persisted = await catalog.AgentPreferences.GetAgentPreferencesAsync( default );
                True( !persisted.Enabled && !persisted.WebSearchEnabled, "the stale save wrote nothing" );
                Equal( AgentTrustLevels.Default, persisted.TrustLevel );
                True( persisted.TrustLeaseExpiresUnixMilliseconds is null, "no lease is written" );

                var fresh = await authority.UpdateSettingsAsync(
                    new AgentPreferences { Enabled = true }, authority.Snapshot.SessionEpoch, default );
                True( fresh.Preferences.Enabled, "a save validated after the change is allowed" );
            }
            finally
            {
                Directory.Delete( root, recursive: true );
            }
        }
    }

    // A preparer whose session lost its admission stops only the process
    // generation that session ran on. A replacement process started meanwhile
    // keeps running, and a turn at the new epoch then completes on it.
    private static async Task AgentLosingPreparerCleansUpOnlyItsGenerationAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-cleanup-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            ScriptedAgentBackend backend = new( );
            using AgentAuthority authority = new( catalog.AgentPreferences );
            await authority.UpdateSettingsAsync( new AgentPreferences { Enabled = true }, authority.Snapshot.SessionEpoch, default );
            await using AgentConversationCoordinator coordinator = new(
                authority, backend, new AgentContextBroker( authority, root ), new AgentCommandTool( root ), new StubAgentLauncherHost( ) );

            TaskCompletionSource<bool> releaseStart = new( TaskCreationOptions.RunContinuationsAsynchronously );
            backend.BlockSessionStart = releaseStart;
            var losing = coordinator.StartTurnAsync( "work that loses its admission", null, null, null, default ).AsTask( );
            await backend.SessionStartEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            var losingGeneration = backend.Generation;

            // An authority change lands while that session is still starting,
            // and a newer start replaces the process before the loser cleans up.
            await authority.SetTrustAsync( AgentTrustLevels.Packages, null, null, default );
            await coordinator.RevokeStaleAsync( default );
            backend.BlockSessionStart = null;
            var replacement = await backend.StartSessionAsync( new AgentSessionOptions
            {
                TrustLevel = AgentTrustLevels.Packages,
                DeveloperInstructions = "spec",
                Admission = new AgentAdmission( authority.Snapshot.SessionEpoch, authority )
            }, default );
            True( replacement.ProcessGeneration != losingGeneration, "the replacement is a new generation" );

            TaskCompletionSource<bool> releaseCleanup = new( TaskCreationOptions.RunContinuationsAsynchronously );
            backend.BlockNextGenerationStop = releaseCleanup;
            releaseStart.TrySetResult( true );
            await backend.GenerationStopEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            releaseCleanup.TrySetResult( true );
            await CatchAsync<AgentAuthorityChangedException>( losing );

            Equal( losingGeneration, backend.StoppedGenerations.Single( ) );
            True( backend.IsRunning && backend.Generation == replacement.ProcessGeneration,
                "cleanup never reaches the replacement process" );

            var events = coordinator.Subscribe( completeWhenIdle: false, out var subscriptionId );
            await coordinator.StartTurnAsync( "work at the new epoch", null, null, null, default );
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 10 ) );
            AgentTurnResult? done = null;
            await foreach ( var streamed in events.ReadAllAsync( timeout.Token ) )
            {
                done = streamed.Done ?? done;
                if ( done is not null )
                {
                    break;
                }
            }
            coordinator.Unsubscribe( subscriptionId );
            Equal( AgentTurnOutcomes.Completed, done?.Outcome );
            Equal( replacement.ProcessGeneration, backend.Generation );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
