namespace CommandDeck.Specs;

using Microsoft.Data.Sqlite;

public static partial class Program
{
    private static async Task CatalogMigratesPreviousSchemaAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-migration-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using ( SqliteConnection connection = new( $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var command = connection.CreateCommand( );
                command.CommandText = """
                    CREATE TABLE catalog_items (
                        stable_id TEXT PRIMARY KEY,
                        kind TEXT NOT NULL,
                        title TEXT NOT NULL,
                        subtitle TEXT,
                        path TEXT,
                        icon TEXT,
                        payload_json TEXT,
                        size_bytes INTEGER,
                        modified_unix_ms INTEGER,
                        provider_id TEXT NOT NULL
                    ) WITHOUT ROWID;

                    INSERT INTO catalog_items(
                        stable_id, kind, title, icon, provider_id)
                    VALUES('app:legacy.desktop', 'application', 'Legacy', 'legacy-icon', 'spec');
                    """;
                await command.ExecuteNonQueryAsync( );
            }

            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            SqliteCatalog concurrentCatalog = new( new StorageOptions { DatabasePath = database } );
            await Task.WhenAll(
                catalog.InitializeAsync( CancellationToken.None ).AsTask( ),
                concurrentCatalog.InitializeAsync( CancellationToken.None ).AsTask( ) );
            True( await catalog.RequiresReindexAsync( CancellationToken.None ) );

            await using SqliteConnection migrated = new( $"Data Source={database}" );
            await migrated.OpenAsync( );
            await using var check = migrated.CreateCommand( );
            check.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM schema_migrations),
                    (SELECT COUNT(*) FROM pragma_table_info('catalog_items')
                        WHERE name = 'startup_wm_class'),
                    (SELECT COUNT(*) FROM pragma_table_info('catalog_items')
                        WHERE name = 'source_id'),
                    (SELECT COUNT(*) FROM catalog_items
                        WHERE stable_id = 'app:legacy.desktop'),
                    (SELECT COUNT(*) FROM pragma_index_list('catalog_items')
                        WHERE name = 'ix_catalog_items_source_title_nocase'),
                    (SELECT COUNT(*) FROM pragma_table_info('extension_storage')
                        WHERE name IN ('installation_key', 'revision', 'values_json'));
                """;
            await using var reader = await check.ExecuteReaderAsync( );
            True( await reader.ReadAsync( ) );
            Equal( 22L, reader.GetInt64( 0 ) );
            Equal( 1L, reader.GetInt64( 1 ) );
            Equal( 1L, reader.GetInt64( 2 ) );
            Equal( 1L, reader.GetInt64( 3 ) );
            Equal( 1L, reader.GetInt64( 4 ) );
            Equal( 3L, reader.GetInt64( 5 ) );
            await catalog.ReplaceAllAsync(
                EmptyIndexedItems( ),
                progress: null,
                CancellationToken.None );
            True( !await catalog.RequiresReindexAsync( CancellationToken.None ) );
            await concurrentCatalog.DisposeAsync( );
            await catalog.DisposeAsync( );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task CatalogRepairsMigrationTwoWithoutStateAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-migration-repair-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using ( SqliteConnection connection = new( $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var command = connection.CreateCommand( );
                command.CommandText = """
                    CREATE TABLE schema_migrations (
                        version INTEGER PRIMARY KEY,
                        applied_unix_ms INTEGER NOT NULL,
                        description TEXT NOT NULL
                    );

                    CREATE TABLE catalog_items (
                        stable_id TEXT PRIMARY KEY,
                        kind TEXT NOT NULL,
                        title TEXT NOT NULL,
                        subtitle TEXT,
                        path TEXT,
                        icon TEXT,
                        payload_json TEXT,
                        size_bytes INTEGER,
                        modified_unix_ms INTEGER,
                        provider_id TEXT NOT NULL,
                        startup_wm_class TEXT
                    ) WITHOUT ROWID;

                    INSERT INTO schema_migrations(version, applied_unix_ms, description)
                    VALUES
                        (1, 0, 'Initial catalog schema'),
                        (2, 0, 'Index application window classes');
                    """;
                await command.ExecuteNonQueryAsync( );
            }

            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            True( await catalog.RequiresReindexAsync( CancellationToken.None ) );

            await using SqliteConnection repaired = new( $"Data Source={database}" );
            await repaired.OpenAsync( );
            await using var check = repaired.CreateCommand( );
            check.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM schema_migrations WHERE version = 3),
                    (SELECT value FROM catalog_state WHERE key = 'reindex_required');
                """;
            await using var reader = await check.ExecuteReaderAsync( );
            True( await reader.ReadAsync( ) );
            Equal( 1L, reader.GetInt64( 0 ) );
            Equal( "1", reader.GetString( 1 ) );
            await catalog.DisposeAsync( );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> EmptyIndexedItems( )
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async Task WindowIconsFollowDesktopEntryMatchOrderAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-icon-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( WindowIconItems( ), null, CancellationToken.None );

            var icons =
                await catalog.ResolveApplicationIconsAsync(
                    ["EXACT.DESKTOP", "STARTUP-ONLY", "EXTENSIONLESS", "Missing.Mixed"],
                    CancellationToken.None );
            Equal( "exact-icon", icons["EXACT.DESKTOP"] );
            Equal( "startup-icon", icons["STARTUP-ONLY"] );
            Equal( "extension-icon", icons["EXTENSIONLESS"] );
            Equal( "missing.mixed", icons["Missing.Mixed"] );
            await catalog.DisposeAsync( );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> WindowIconItems( )
    {
        yield return ApplicationItem( "Exact.desktop", "exact-icon" );
        yield return ApplicationItem(
            "conflict.desktop",
            "lower-priority-icon",
            "Exact.desktop" );
        yield return ApplicationItem(
            "startup-owner.desktop",
            "startup-icon",
            "startup-only" );
        yield return ApplicationItem( "extensionless.desktop", "extension-icon" );
        await Task.CompletedTask;
    }

    private static IndexedItem ApplicationItem(
        string desktopId,
        string icon,
        string? startupWmClass = null ) =>
        new( )
        {
            StableId = $"app:{desktopId}",
            Kind = ResultKinds.Application,
            Title = desktopId,
            Icon = icon,
            StartupWmClass = startupWmClass,
            ProviderId = "spec",
            SourceId = SourceIds.Applications
        };

    private static void DeleteDatabase( string database )
    {
        foreach ( var suffix in new[] { string.Empty, "-shm", "-wal" } )
        {
            File.Delete( database + suffix );
        }
    }

    private static async Task ConfigurationResultsCollapsePerSourceAsync( )
    {
        const string crowdedPath = "/tmp/identity.json";
        const string otherPath = "/tmp/other.json";
        SearchResult[] sourceResults =
        [
            ConfigurationResult("best", crowdedPath, 0.9),
            ConfigurationResult("second", crowdedPath, 0.8),
            ConfigurationResult("third", crowdedPath, 0.7),
            ConfigurationResult("fourth", crowdedPath, 0.6),
            ConfigurationResult("other", otherPath, 0.65),
            new SearchResult
            {
                Id = "window:one",
                Kind = ResultKinds.Window,
                Title = "Window",
                ProviderId = "spec",
                SourceId = SourceIds.WindowsWorkspaces,
                Score = 0.8
            }
        ];
        SearchCoordinator coordinator = new(
            [new StaticSearchProvider( sourceResults )],
            new SearchOptions( ) );

        var results = await coordinator.SearchAsync(
            new SearchRequest { Limit = 50 },
            CancellationToken.None );
        var crowded = results
            .Where( result => result.Metadata.GetValueOrDefault( "path" ) == crowdedPath )
            .ToArray( );
        Equal( 2, crowded.Length );
        Equal( "best", crowded[0].Title );
        Equal( "second", crowded[1].Title );
        Equal( "2", crowded[0].Metadata["collapsedCount"] );
        True( crowded[0].Subtitle?.StartsWith(
            "2 more matches",
            StringComparison.Ordinal ) == true );
        True( crowded[0].Details.Any( detail =>
            detail.Label == "Related" && detail.Value == "2 more matches in this file" ) );
        True( !crowded[1].Metadata.ContainsKey( "collapsedCount" ) );
        Equal( 1, results.Count( result =>
            result.Metadata.GetValueOrDefault( "path" ) == otherPath ) );
        True( results.ToList( ).FindIndex( result => result.Id == "window:one" ) <
            results.ToList( ).FindIndex( result => result.Id == "configuration:second" ) );
    }

    private static SearchResult ConfigurationResult(
        string title,
        string path,
        double score ) =>
        new( )
        {
            Id = $"configuration:{title}",
            Kind = ResultKinds.Configuration,
            Title = title,
            Subtitle = "configuration value",
            ProviderId = "spec",
            SourceId = SourceIds.Configuration,
            Score = score,
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                ["path"] = path
            }
        };

    private static async IAsyncEnumerable<IndexedItem> DetailFieldItems( )
    {
        yield return DetailItem(
            "application",
            ResultKinds.Application,
            "/usr/share/applications/example.desktop",
            ("desktopFile", "/usr/share/applications/example.desktop"),
            ("desktopId", "example.desktop"),
            ("exec", "example %U"),
            ("terminal", "false") );
        yield return DetailItem(
            "configuration",
            ResultKinds.Configuration,
            "/tmp/hyprland.conf",
            ("line", "27"),
            ("key", "windowrule"),
            ("section", "general"),
            ("value", "float") );
        yield return DetailItem( "file", ResultKinds.File, "/tmp/notes.txt" );
        yield return DetailItem(
            "omarchy",
            ResultKinds.Omarchy,
            "/usr/bin/omarchy-test",
            ("command", "omarchy-test"),
            ("source", "/usr/bin/omarchy-test") );
        yield return DetailItem(
            "quicklink",
            ResultKinds.Quicklink,
            null,
            ("urlTemplate", "https://example.test/?q={query}"),
            ("alias", "ex") );
        yield return DetailItem(
            "custom-command",
            ResultKinds.CustomCommand,
            null,
            ("argv", "printf\u001fhello"),
            ("workingDirectory", "/tmp"),
            ("confirm", "true") );
        await Task.CompletedTask;
    }

    private static IndexedItem DetailItem(
        string id,
        string kind,
        string? path,
        params (string Key, string? Value)[] payload ) =>
        new( )
        {
            StableId = $"{kind}:{id}",
            Kind = kind,
            Title = id,
            Path = path,
            PayloadJson = payload.Length == 0 ? null : PayloadWriter.Create( payload ),
            ProviderId = "spec",
            SourceId = kind switch
            {
                ResultKinds.Application => SourceIds.Applications,
                ResultKinds.File => SourceIds.Files,
                ResultKinds.Configuration => SourceIds.Configuration,
                ResultKinds.Omarchy => SourceIds.OmarchyControl,
                ResultKinds.Quicklink or ResultKinds.CustomCommand => SourceIds.CommandsQuicklinks,
                _ => SourceIds.Status
            }
        };

    private static void AssertDetailLabels(
        IReadOnlyList<SearchResult> results,
        string kind,
        params string[] expected )
    {
        var result = results.Single( candidate => candidate.Kind == kind );
        Equal(
            string.Join( '|', expected ),
            string.Join( '|', result.Details.Select( static detail => detail.Label ) ) );
    }

}
