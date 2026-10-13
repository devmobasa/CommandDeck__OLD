namespace CommandDeck.Specs;

using Microsoft.Data.Sqlite;

public static partial class Program
{
    // Home cost must follow usage and favorites, not catalog size: the planner has
    // to probe catalog_items and extension_commands by stable_id rather than scan
    // them for every usage or favorite row.
    private static async Task HomeQueryProbesItemsInsteadOfScanningTheCatalogAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-home-plan-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var connection = await catalog.OpenAsync( CancellationToken.None );
            await SeedHomeRowsAsync( connection );

            var preferences = await catalog.GetPreferencesAsync( CancellationToken.None );
            var enabled = preferences.Sources
                .Where( static value => value.EnabledOnHome )
                .Select( static value => value.SourceId )
                .ToHashSet( StringComparer.Ordinal );
            await using var command = catalog.CreateHomeResultsCommand( connection, preferences, enabled );
            var plan = await ExplainQueryPlanAsync( command );

            var scans = plan.Where( static step =>
                step.Contains( "SCAN used_item", StringComparison.Ordinal ) ||
                step.Contains( "SCAN favorite_item", StringComparison.Ordinal ) ||
                step.Contains( "SCAN catalog_items", StringComparison.Ordinal ) ||
                step.Contains( "SCAN extension_commands", StringComparison.Ordinal ) ||
                step.Contains( "SCAN hidden", StringComparison.Ordinal ) ||
                step.Contains( "AUTOMATIC", StringComparison.Ordinal ) ).ToArray( );
            True( scans.Length == 0, $"no catalog scans in plan: {string.Join( " | ", plan )}" );
            True( plan.Any( static step => step.Contains( "SEARCH hidden USING PRIMARY KEY", StringComparison.Ordinal ) ),
                "hidden items are excluded by a primary-key probe before the candidate limits" );
            True( plan.Any( static step => step.Contains( "SEARCH used_item USING PRIMARY KEY", StringComparison.Ordinal ) ) );
            True( plan.Any( static step => step.Contains( "SEARCH favorite_item USING PRIMARY KEY", StringComparison.Ordinal ) ) );
            True( plan.Any( static step => step.Contains( "SEARCH i USING PRIMARY KEY", StringComparison.Ordinal ) ) );

            var results = await catalog.GetHomeResultsAsync( preferences, 20, CancellationToken.None );
            Equal( 2, results.Count );
            True( results.Any( static result => result.Id == "app:brave.desktop" && result.Section?.Id == "bookmarks" ) );
            True( results.Any( static result => result.Id == "app:zed.desktop" && result.Section?.Id == "recent-frequent" ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    // Selected sources range composite indexes; the bounded merge must never
    // walk titles belonging to disabled sources.
    private static async Task FuzzyCandidateLookupUsesTitleIndexesAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-fuzzy-plan-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var connection = await catalog.OpenAsync( CancellationToken.None );
            var query = QueryParser.Parse( "brave" );

            await using var bySource = SqliteCatalog.CreateFuzzyCandidateCommand(
                connection,
                query,
                [SourceIds.Applications],
                'b' );
            var sourcePlan = await ExplainQueryPlanAsync( bySource );
            True( sourcePlan.Any( static step =>
                step.Contains( "USING INDEX ix_catalog_items_source_title_nocase", StringComparison.Ordinal ) ) );

            await using var unfiltered = SqliteCatalog.CreateFuzzyCandidateCommand( connection, query, [], 'b' );
            var unfilteredPlan = await ExplainQueryPlanAsync( unfiltered );
            True( unfilteredPlan.Any( static step =>
                step.Contains( "USING INDEX ix_catalog_items_title_nocase", StringComparison.Ordinal ) ) );

            await using ( var seed = connection.CreateCommand( ) )
            {
                seed.CommandText = """
                    WITH RECURSIVE numbers(n) AS (
                        SELECT 0 UNION ALL SELECT n + 1 FROM numbers WHERE n < 549
                    )
                    INSERT INTO catalog_items(stable_id, kind, title, provider_id, source_id)
                    SELECT printf('candidate:%04d', n), 'file', printf('Brave %04d', n), 'catalog',
                           CASE WHEN n % 2 = 0 THEN 'applications' ELSE 'files' END
                    FROM numbers;

                    WITH RECURSIVE numbers(n) AS (
                        SELECT 0 UNION ALL SELECT n + 1 FROM numbers WHERE n < 5999
                    )
                    INSERT INTO catalog_items(stable_id, kind, title, provider_id, source_id)
                    SELECT 'disabled:' || n, 'file', printf('Brave -disabled %04d', n), 'catalog', 'configuration'
                    FROM numbers;

                    INSERT INTO hidden_items(stable_id, title_snapshot, source_id, hidden_unix_ms)
                    SELECT stable_id, title, source_id, 0 FROM catalog_items
                    WHERE stable_id < 'candidate:0020';
                    """;
                await seed.ExecuteNonQueryAsync( );
            }

            await using var multipleSources = SqliteCatalog.CreateFuzzyCandidateCommand(
                connection, query, [SourceIds.Applications, SourceIds.Files], 'b' );
            var candidateSql = multipleSources.CommandText;
            var multipleSourcePlan = await ExplainQueryPlanAsync( multipleSources );
            True( multipleSourcePlan.Any( static step =>
                step.Contains( "ix_catalog_items_source_title_nocase", StringComparison.Ordinal ) ),
                $"Multiple sources must range their own titles: {string.Join( " | ", multipleSourcePlan )}" );

            multipleSources.CommandText = candidateSql;
            List<string> candidates = [];
            await using var reader = await multipleSources.ExecuteReaderAsync( );
            var stableIdOrdinal = reader.GetOrdinal( "stable_id" );
            var sourceIdOrdinal = reader.GetOrdinal( "source_id" );

            while ( await reader.ReadAsync( ) )
            {
                candidates.Add( reader.GetString( stableIdOrdinal ) );
                True( reader.GetString( sourceIdOrdinal ) is SourceIds.Applications or SourceIds.Files );
            }

            // Disabled and hidden rows precede the admitted range. Filtering
            // after LIMIT would consume the whole budget before any visible row.
            Equal( 512, candidates.Count );
            Equal( string.Join( ',', Enumerable.Range( 20, 512 ).Select( static index => $"candidate:{index:D4}" ) ),
                string.Join( ',', candidates ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    // The customizations snapshot backs settings, export, and reconnect recovery.
    // Its cost must follow the favorites count, not the catalog.
    private static async Task CustomizationsSnapshotProbesAvailabilityByStableIdAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-customizations-plan-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var connection = await catalog.OpenAsync( CancellationToken.None );
            await SeedHomeRowsAsync( connection );
            await using ( var orphan = connection.CreateCommand( ) )
            {
                orphan.CommandText = """
                    INSERT INTO favorites(
                        stable_id, position, title_snapshot, source_id, created_unix_ms, updated_unix_ms)
                    VALUES ('app:removed.desktop', 1, 'Removed', 'applications', 0, 0);
                    """;
                await orphan.ExecuteNonQueryAsync( );
            }

            await using var command = SqliteLauncherPreferenceStore.CreateCustomizationsCommand( connection );
            var plan = await ExplainQueryPlanAsync( command );
            var scans = plan.Where( static step =>
                step.Contains( "SCAN catalog_items", StringComparison.Ordinal ) ||
                step.Contains( "SCAN extension_commands", StringComparison.Ordinal ) ||
                step.Contains( "AUTOMATIC", StringComparison.Ordinal ) ).ToArray( );
            True( scans.Length == 0, $"no catalog scans in plan: {string.Join( " | ", plan )}" );
            True( plan.Any( static step => step.Contains( "SEARCH i USING PRIMARY KEY", StringComparison.Ordinal ) ) );

            var export = await catalog.ExportAsync( CancellationToken.None );
            Equal( 2, export.Customizations.Count );
            True( export.Customizations[0].StableId == "app:brave.desktop" && export.Customizations[0].IsAvailable );
            True( export.Customizations[1].StableId == "app:removed.desktop" && !export.Customizations[1].IsAvailable );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task SeedHomeRowsAsync( SqliteConnection connection )
    {
        await using var seed = connection.CreateCommand( );
        seed.CommandText = """
            INSERT INTO catalog_items(stable_id, kind, title, provider_id, source_id)
            VALUES ('app:brave.desktop', 'application', 'Brave', 'applications', 'applications'),
                   ('app:zed.desktop', 'application', 'Zed', 'applications', 'applications'),
                   ('file:notes', 'file', 'notes.txt', 'catalog', 'files');
            INSERT INTO usage(stable_id, use_count, last_used_unix_ms)
            VALUES ('app:zed.desktop', 3, $now);
            INSERT INTO favorites(
                stable_id, position, title_snapshot, source_id, created_unix_ms, updated_unix_ms)
            VALUES ('app:brave.desktop', 0, 'Brave', 'applications', $now, $now);
            """;
        seed.Parameters.AddWithValue( "$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds( ) );

        await seed.ExecuteNonQueryAsync( );
    }

    private static async Task<string[]> ExplainQueryPlanAsync( SqliteCommand command )
    {
        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;

        List<string> steps = [];
        await using var reader = await command.ExecuteReaderAsync( );
        while ( await reader.ReadAsync( ) )
        {
            steps.Add( reader.GetString( 3 ) );
        }

        return [.. steps];
    }

    private static async Task HomeSearchScopePersistsAndFiltersCandidatesAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-home-search-{Guid.NewGuid( ):N}.db" );
        var home = PathExpansion.Expand( "~" );
        True( CanonicalPathBoundary.TryCreate( home, out var boundary ) );
        home = boundary!.CanonicalRoot;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( HomeSearchItems( home ), null, CancellationToken.None );
            var defaults = await catalog.GetPreferencesAsync( CancellationToken.None );
            True( !defaults.SearchHomeOnly );
            var outside = await catalog.SearchAsync(
                QueryParser.Parse( "file: title:needle-a" ), 20, CancellationToken.None );
            Equal( 20, outside.Count );
            foreach ( var result in outside )
            {
                await catalog.SetFavoriteAsync( result, true, CancellationToken.None );
            }
            await catalog.SetAliasAsync( outside[0], "outside-note", CancellationToken.None );
            var inside = (await catalog.SearchAsync(
                QueryParser.Parse( "file: title:needle-z" ), 1, CancellationToken.None )).Single( );
            await catalog.SetFavoriteAsync( inside, true, CancellationToken.None );

            var enabled = await catalog.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = defaults.Sources,
                SearchHomeOnly = true,
                ReduceMotion = true
            }, CancellationToken.None );
            True( enabled.SearchHomeOnly && enabled.ReduceMotion );
            await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = database } );
            True( (await reopened.GetPreferencesAsync( CancellationToken.None )).SearchHomeOnly );

            // Outside rows would fill both the ordinary LIMIT and the 512-row
            // fuzzy candidate budget if filtering happened after retrieval.
            foreach ( var query in new[] { "file: needle", "file: nedle", "file:" } )
            {
                var results = await reopened.SearchAsync(
                    QueryParser.Parse( query ), 1, CancellationToken.None );
                Equal( inside.Id, results.Single( ).Id );
            }
            Equal( 0, (await reopened.SearchAsync( QueryParser.Parse( "file: sibling" ),
                10, CancellationToken.None )).Count );
            Equal( "home-config", (await reopened.SearchAsync( QueryParser.Parse( "cfg: setting" ),
                10, CancellationToken.None )).Single( ).Id );
            Equal( "system-app", (await reopened.SearchAsync( QueryParser.Parse( "app: scopeapp" ),
                10, CancellationToken.None )).Single( ).Id );
            True( await reopened.FindAliasAsync( "outside-note", CancellationToken.None ) is null );
            Equal( inside.Id, (await reopened.GetHomeResultsAsync( enabled, 1, CancellationToken.None )).Single( ).Id );

            await reopened.ReplacePreferencesAsync( defaults, CancellationToken.None );
            True( await reopened.FindAliasAsync( "outside-note", CancellationToken.None ) is not null );
            Equal( 20, (await reopened.SearchAsync( QueryParser.Parse( "file: needle" ),
                20, CancellationToken.None )).Count );
            True( !(await reopened.GetPreferencesAsync( CancellationToken.None )).SearchHomeOnly );
            await reopened.ReplacePreferencesAsync( enabled, CancellationToken.None );
            await reopened.SetFavoriteAsync( inside, false, CancellationToken.None );
            await reopened.UsageHistory.RecordInvocationAsync( inside.Id, "open", null, CancellationToken.None );
            foreach ( var result in outside )
            {
                await reopened.SetFavoriteAsync( result, false, CancellationToken.None );
                await reopened.UsageHistory.RecordInvocationAsync( result.Id, "open", null, CancellationToken.None );
            }
            Equal( inside.Id, (await reopened.GetHomeResultsAsync( enabled, 1, CancellationToken.None )).Single( ).Id );

            await reopened.ResetAsync( CancellationToken.None );
            True( !(await reopened.GetPreferencesAsync( CancellationToken.None )).SearchHomeOnly );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> HomeSearchItems( string home )
    {
        await Task.Yield( );
        for ( var index = 0; index < 520; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"outside-{index}",
                Kind = ResultKinds.File,
                Title = $"needle-a{index:D3}.txt",
                Path = $"/outside/needle-a{index:D3}.txt",
                ProviderId = "files",
                SourceId = SourceIds.Files
            };
        }
        yield return new IndexedItem
        {
            StableId = "inside",
            Kind = ResultKinds.File,
            Title = "needle-z.txt",
            Path = Path.Combine( home, "Documents", "nested", "needle-z.txt" ),
            ProviderId = "files",
            SourceId = SourceIds.Files
        };
        yield return new IndexedItem
        {
            StableId = "sibling",
            Kind = ResultKinds.File,
            Title = "sibling.txt",
            Path = home + "-backup/sibling.txt",
            ProviderId = "files",
            SourceId = SourceIds.Files
        };
        yield return new IndexedItem
        {
            StableId = "home-config",
            Kind = ResultKinds.Configuration,
            Title = "setting",
            Path = Path.Combine( home, ".config", "scope.conf" ),
            ProviderId = "files",
            SourceId = SourceIds.Configuration
        };
        yield return new IndexedItem
        {
            StableId = "system-config",
            Kind = ResultKinds.Configuration,
            Title = "setting",
            Path = "/etc/scope.conf",
            ProviderId = "files",
            SourceId = SourceIds.Configuration
        };
        yield return new IndexedItem
        {
            StableId = "system-app",
            Kind = ResultKinds.Application,
            Title = "scopeapp",
            Path = "/usr/share/applications/scopeapp.desktop",
            ProviderId = "applications",
            SourceId = SourceIds.Applications
        };
    }
}
