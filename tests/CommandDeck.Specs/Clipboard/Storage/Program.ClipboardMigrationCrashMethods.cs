namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardMigrationRecoversKilledProcessAsync( )
    {
        foreach ( var stage in new[] { "migration-marked", "row-migrated", "schema-switched",
            "projection-schema-created", "projection-rows-written", "projection-schema-committed" } )
        {
            var root = Directory.CreateTempSubdirectory( "commanddeck-migration-crash-" ).FullName;
            try
            {
                await KillClipboardLedgerFixtureAsync( root, stage, "--clipboard-migration-crash-fixture" );
                var path = Path.Combine( root, "history.db" );
                var before = ClipboardDatabaseScalar( path, "SELECT count(*) FROM entries" );
                Equal( 12L, before );
                if ( stage is "migration-marked" or "row-migrated" )
                {
                    Equal( 1L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
                    Equal( stage == "row-migrated" ? 1L : 0L,
                        ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_migrated_entries" ) );
                }
                else
                {
                    Equal( stage == "projection-schema-committed" ? 3L : 2L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
                    Equal( 12L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_offers" ) );
                }

                using ( ClipboardHistoryStore resumed = new( path, new ClipboardHistoryOptions( ) ) )
                {
                    Equal( 12, resumed.MigrationReview.EntryCount );
                    Equal( 4, resumed.MigrationReview.PinnedCount );
                    Equal( ClipboardConsent.Text, resumed.Policy.Consent );
                    True( resumed.Search( "", 100 ).Select( static entry => entry.Text ).Order( StringComparer.Ordinal )
                        .SequenceEqual( Enumerable.Range( 0, 12 ).Select( static index => "legacy " + index ).Order( StringComparer.Ordinal ) ),
                        "every preserved source row is available after recovery" );
                    Equal( 4L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
                    Equal( 12L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_projections" ) );
                    Equal( 12L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads WHERE state=1" ) );
                    Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM sqlite_master WHERE name LIKE 'clipboard_fts%'" ) );
                    Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM sqlite_master WHERE name='clipboard_payload_migration'" ) );
                    Equal( 12, Directory.GetFiles( path + ".payloads" ).Length );
                }

                using ClipboardHistoryStore again = new( path, new ClipboardHistoryOptions( ) );
                Equal( 12, again.MigrationReview.EntryCount );
            }
            finally
            {
                Directory.Delete( root, recursive: true );
            }
        }
    }

    private static int RunClipboardMigrationCrashFixture( string root, string stage )
    {
        if ( !Path.GetFileName( root ).StartsWith( "commanddeck-migration-crash-", StringComparison.Ordinal ) ||
            !Directory.Exists( root ) || Directory.EnumerateFileSystemEntries( root ).Any( ) ||
            stage is not ("migration-marked" or "row-migrated" or "schema-switched" or
                "projection-schema-created" or "projection-rows-written" or "projection-schema-committed") )
        {
            return 2;
        }

        var path = Path.Combine( root, "history.db" );
        WriteLegacyClipboard( path, 12, 4, DateTimeOffset.UtcNow.ToUnixTimeSeconds( ), enabled: true );
        using ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ), null, null, checkpoint =>
        {
            if ( checkpoint == stage )
            {
                Console.WriteLine( stage );
                Console.Out.Flush( );
                using ManualResetEvent hold = new( false );
                hold.WaitOne( );
            }
        } );
        return 3;
    }
}
