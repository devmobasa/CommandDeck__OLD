using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AgentPreferencesAndGrantsPersistWithoutSecretsAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-agent-store-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            Equal( AgentTrustLevels.Default, (await catalog.AgentPreferences.GetAgentPreferencesAsync( default )).TrustLevel );
            var saved = await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true, ModelId = "m", TrustLevel = 3 }, default );
            Equal( 3, (await catalog.AgentPreferences.GetAgentPreferencesAsync( default )).TrustLevel );
            await catalog.AiPreferences.SetAiSelectionAsync( null, null, default );
            Equal( 3, (await catalog.AgentPreferences.GetAgentPreferencesAsync( default )).TrustLevel );
            var grant = await catalog.AgentPreferences.AddAgentGrantAsync( new AgentGrant
            {
                Id = "grant1",
                Kind = AgentGrantKinds.Folder,
                Scope = "/home/user/notes",
                PayloadDigest = "abc",
                State = AgentGrantStates.Active
            }, default );
            Equal( AgentGrantStates.Active, grant.State );
            True( await catalog.AgentPreferences.UpdateAgentGrantStateAsync( "grant1", AgentGrantStates.Revoked, 5, default ) );
            Equal( AgentGrantStates.Revoked, (await catalog.AgentPreferences.GetAgentGrantsAsync( default ))[0].State );

            await using SqliteCatalog reopened = new(
                new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            Equal( 3, (await reopened.AgentPreferences.GetAgentPreferencesAsync( default )).TrustLevel );
            Equal( AgentGrantStates.Revoked,
                (await reopened.AgentPreferences.GetAgentGrantsAsync( default ))[0].State );

            var export = await catalog.ExportAsync( default );
            var exportJson = JsonSerializer.Serialize( export, CommandDeckJsonContext.Default.CustomizationExport );
            True( !exportJson.Contains( "grant1", StringComparison.Ordinal ) && !exportJson.Contains( "agent.preferences", StringComparison.Ordinal ) );
            // Launcher reset leaves the assistant's records to their owner, which
            // the runtime reset calls; only the owner clears them.
            await catalog.ResetAsync( default );
            Equal( 1, (await catalog.AgentPreferences.GetAgentGrantsAsync( default )).Count );
            Equal( 3, (await catalog.AgentPreferences.GetAgentPreferencesAsync( default )).TrustLevel );
            using AgentAuthority authority = new( catalog.AgentPreferences );
            await authority.ResetAsync( default );
            Equal( 0, (await catalog.AgentPreferences.GetAgentGrantsAsync( default )).Count );
            Equal( AgentTrustLevels.Default, (await catalog.AgentPreferences.GetAgentPreferencesAsync( default )).TrustLevel );
            True( saved.Enabled );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
