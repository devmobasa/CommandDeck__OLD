using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    // A registry written by a build with features this one does not know stays
    // stored untouched, while Settings can still save source preferences.
    private static async Task UnreadableExtensionRegistryKeepsSourcePreferencesEditableAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-registry-recovery-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root );
        try
        {
            var databasePath = Path.Combine( root, "catalog.db" );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = databasePath } );
            await catalog.InitializeAsync( CancellationToken.None );
            const string unreadable = """{"version":1,"revision":7,"installations":[{"manifest":{"requiredFeatures":["future.feature@1"]}}]}""";
            await using ( SqliteConnection connection = new( "Data Source=" + databasePath ) )
            {
                await connection.OpenAsync( );
                await using var insert = connection.CreateCommand( );
                insert.CommandText = "INSERT INTO extension_registry(id, revision, document_json) VALUES(1, 7, $document);";
                insert.Parameters.AddWithValue( "$document", unreadable );
                await insert.ExecuteNonQueryAsync( );
            }

            using var registry = await ExtensionPackageRegistry.OpenAsync( catalog, Path.Combine( root, "artifacts" ), CancellationToken.None );

            catalog.Contributions = registry;
            True( registry.Error is not null && registry.Error.Contains( "Extensions are unavailable", StringComparison.Ordinal ),
                "an unreadable registry disables extensions" );
            Equal( 7L, registry.Snapshot.Revision );

            var preferences = await catalog.GetPreferencesAsync( CancellationToken.None );
            Equal( 7L, preferences.RegistryRevision );
            var moved = preferences.Sources[^1];
            var patched = await catalog.PatchSourcePreferencesAsync( new SourcePreferencePatch
            {
                RegistryRevision = preferences.RegistryRevision,
                PreferenceRevision = preferences.PreferenceRevision,
                Changes = [new SourcePreferenceChange { SourceId = moved.SourceId, Position = 1 }]
            }, CancellationToken.None );
            Equal( moved.SourceId, patched.Sources[0].SourceId );

            await using ( SqliteConnection connection = new( "Data Source=" + databasePath ) )
            {
                await connection.OpenAsync( );
                await using var read = connection.CreateCommand( );
                read.CommandText = "SELECT document_json FROM extension_registry WHERE id = 1;";
                Equal( unreadable, ( string? ) await read.ExecuteScalarAsync( ) );
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }
}
