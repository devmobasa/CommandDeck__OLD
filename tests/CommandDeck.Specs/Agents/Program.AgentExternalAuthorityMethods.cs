namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AgentTurnStreamFollowsExternalAuthorityRefreshAsync( )
    {
        ScriptedAgentBackend backend = new( );
        using CancellationTokenSource stop = new( );
        (var runtime, var _, var root, var _) = await StartAgentRuntimeAsync( backend, stop.Token );
        try
        {
            await using ( runtime )
            {
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                await using var client = await ConnectWithRetryAsync( Path.Combine( root, "commanddeck.sock" ) );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.AgentTurnStart,
                    Query = "first turn"
                }, default );
                var first = await CollectUntilDoneAsync( client, 1 );
                True( first.Any( message => message.AgentEvent?.Done?.Outcome == AgentTurnOutcomes.Completed ) );

                await using SqliteCatalog otherCatalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
                await otherCatalog.InitializeAsync( default );
                using AgentAuthority other = new( otherCatalog.AgentPreferences );
                await other.SetTrustAsync( AgentTrustLevels.Launcher, null, null, default );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 2,
                    Operation = ProtocolOperations.AgentTurnStart,
                    Query = "turn under new authority"
                }, default );
                var second = await CollectUntilDoneAsync( client, 2 );
                True( second.Any( message => message.AgentEvent?.Done?.Outcome == AgentTurnOutcomes.Completed ),
                    "the turn's stream follows the session replaced during its authority refresh" );
                Equal( 2, backend.Sessions );
            }
        }
        finally
        {
            stop.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentAuthorityObservesExternalRevocationAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-external-authority-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            using AgentAuthority daemon = new( catalog.AgentPreferences );
            using AgentAuthority other = new( catalog.AgentPreferences );
            await daemon.UpdateSettingsAsync( new AgentPreferences { Enabled = true }, daemon.Snapshot.SessionEpoch, default );
            var admitted = await daemon.SetTrustAsync( AgentTrustLevels.Packages, null, null, default );

            await other.SetTrustAsync( AgentTrustLevels.Default, null, null, default );
            var observed = (await daemon.LoadAsync( default )).Snapshot;
            Equal( AgentTrustLevels.Default, observed.Preferences.TrustLevel );
            True( observed.SessionEpoch > admitted.SessionEpoch, "observing external revocation expires old admissions" );
            Equal( observed.SessionEpoch, (await daemon.LoadAsync( default )).Snapshot.SessionEpoch );

            await other.SetTrustAsync( AgentTrustLevels.Packages, null, null, default );
            var restored = (await daemon.LoadAsync( default )).Snapshot;
            True( restored.SessionEpoch > observed.SessionEpoch, "restored permissions do not revive old admissions" );
            await ThrowsAsync<AiOperationException>( async ( ) => await daemon.UpdateSettingsAsync(
                new AgentPreferences { Enabled = true, WebSearchEnabled = true }, admitted.SessionEpoch, default ) );
            True( !daemon.Current.WebSearchEnabled, "a save based on the old admission writes nothing" );

            await other.SetTrustAsync( AgentTrustLevels.Default, null, null, default );
            var written = await daemon.UpdateSettingsAsync(
                new AgentPreferences { Enabled = true }, restored.SessionEpoch, default );
            Equal( AgentTrustLevels.Default, written.Preferences.TrustLevel );
            True( written.SessionEpoch > restored.SessionEpoch,
                "a writer that first observes external revocation also expires old admissions" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
