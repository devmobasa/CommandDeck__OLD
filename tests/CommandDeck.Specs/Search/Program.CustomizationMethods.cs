namespace CommandDeck.Specs;

using Microsoft.Data.Sqlite;

public static partial class Program
{
    private static async Task FileIndexSourcePreservesTraversalBehaviorAsync( )
    {
        var root = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-file-index-spec-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await File.WriteAllTextAsync( Path.Combine( root, "notes.txt" ), "hello world" );
            await File.WriteAllTextAsync( Path.Combine( root, ".hidden.txt" ), "hidden" );
            await File.WriteAllTextAsync(
                Path.Combine( root, "settings.json" ),
                "{\"theme\":\"dark\",\"entries\":[{\"name\":\"outer\",\"entries\":[{\"name\":\"inner\",\"value\":\"nested\"}]}]}" );

            FileIndexSource source = new(
                new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = root,
                            IncludeHidden = false,
                            IncludeContentInSearch = true
                        }
                    ]
                },
                new PrivacyOptions { NeverIndex = [] } );
            List<IndexedItem> items = [];
            await foreach ( var item in source.ScanAsync(
                progress: null,
                CancellationToken.None ) )
            {
                items.Add( item );
            }

            True( items.Any( item =>
                item.Kind == ResultKinds.File && item.Title == "notes.txt" ) );
            True( items.Any( item =>
                item.Kind == ResultKinds.File && item.Title == "settings.json" ) );
            True( items.Any( item =>
                item.Kind == ResultKinds.Configuration &&
                item.Title == "theme" &&
                item.Body == "dark" ) );
            True( items.Any( item =>
                item.Kind == ResultKinds.Configuration &&
                item.Title == "entries[outer].entries[inner].value" &&
                item.Body == "nested" ) );
            True( items.All( item => !item.Title.Contains(
                "entries[outer].entries[outer]",
                StringComparison.Ordinal ) ) );
            True( items.All( item => item.Title != ".hidden.txt" ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task CustomizationAndHomeSurviveRebuildAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-customization-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: true ), null, CancellationToken.None );

            var application = (await catalog.SearchAsync(
                QueryParser.Parse( "Brave" ),
                10,
                CancellationToken.None )).Single( value => value.Kind == ResultKinds.Application );
            var file = (await catalog.SearchAsync(
                QueryParser.Parse( "notes" ),
                10,
                CancellationToken.None )).Single( value => value.Kind == ResultKinds.File );
            True( application.CanCustomize && file.CanCustomize );

            var alias = await catalog.SetAliasAsync(
                application,
                "b",
                CancellationToken.None );
            True( alias.IsFavorite && alias.Aliases.SequenceEqual( ["b"] ) );
            await catalog.SetFavoriteAsync( file, true, CancellationToken.None );
            await catalog.ReorderFavoritesAsync(
                [file.Id, application.Id],
                CancellationToken.None );

            var exactAlias = await catalog.FindAliasAsync( "B", CancellationToken.None );
            Equal( application.Id, exactAlias?.Id );
            Equal( "B", exactAlias?.Metadata["exactAlias"] );
            var recasedAlias = await catalog.SetAliasAsync(
                application,
                "B",
                CancellationToken.None );
            Equal( "B", recasedAlias.Aliases[0] );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.SetAliasAsync( file, "B", CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.SetAliasAsync( application, "app", CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.SetAliasAsync( application, "home", CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.SetAliasAsync( application, "settings", CancellationToken.None ) );

            var defaults = LauncherPreferences.CreateDefault( );
            LauncherPreferences configured = new( )
            {
                Sources = defaults.Sources.Select( source => new SourcePreference
                {
                    SourceId = source.SourceId,
                    Position = source.Position,
                    EnabledInSearch = source.SourceId == SourceIds.Applications
                        ? false
                        : source.EnabledInSearch,
                    EnabledOnHome = source.SourceId is
                        SourceIds.WindowsWorkspaces or
                        SourceIds.OmarchyControl or
                        SourceIds.Applications
                        ? false
                        : source.EnabledOnHome
                } ).ToList( ),
                HideAfterAction = false,
                ReduceMotion = true,
                WebFallbackQuicklinkId = "arch-wiki"
            };
            var stored = await catalog.ReplacePreferencesAsync(
                configured,
                CancellationToken.None );
            True( stored.ReduceMotion );
            True( !stored.HideAfterAction );
            True( !(await catalog.GetPreferencesAsync( CancellationToken.None )).HideAfterAction );
            Equal( "arch-wiki", stored.WebFallbackQuicklinkId );

            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = defaults.Sources.Select( ( source, index ) => new SourcePreference
                    {
                        SourceId = index == 0 ? "unknown-source" : source.SourceId,
                        EnabledInSearch = source.EnabledInSearch,
                        EnabledOnHome = source.EnabledOnHome,
                        Position = source.Position
                    } ).ToList( )
                }, CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = defaults.Sources.Select( ( source, index ) => new SourcePreference
                    {
                        SourceId = index == 1 ? defaults.Sources[0].SourceId : source.SourceId,
                        EnabledInSearch = source.EnabledInSearch,
                        EnabledOnHome = source.EnabledOnHome,
                        Position = source.Position
                    } ).ToList( )
                }, CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = defaults.Sources.Select( source => new SourcePreference
                    {
                        SourceId = source.SourceId,
                        EnabledInSearch = source.EnabledInSearch,
                        EnabledOnHome = source.EnabledOnHome,
                        Position = 1
                    } ).ToList( )
                }, CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = defaults.Sources.Select( ( source, index ) => new SourcePreference
                    {
                        SourceId = source.SourceId,
                        EnabledInSearch = source.EnabledInSearch,
                        EnabledOnHome = source.EnabledOnHome,
                        Position = index == 0 ? 0 : source.Position
                    } ).ToList( )
                }, CancellationToken.None ) );

            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog, catalog )],
                new SearchOptions( ),
                catalog,
                catalog );
            var disabled = await coordinator.SearchAsync(
                new SearchRequest { Query = "Brav", Limit = 20 },
                CancellationToken.None );
            True( disabled.All( value => value.SourceId != SourceIds.Applications ) );
            var explicitApplications = await coordinator.SearchAsync(
                new SearchRequest
                {
                    Query = "Brav",
                    Expression = new QueryExpression { IncludeFilterIds = ["applications"] },
                    Limit = 20
                },
                CancellationToken.None );
            True( explicitApplications.All( value => value.Id != application.Id ) );

            var home = await coordinator.SearchAsync(
                new SearchRequest { Query = string.Empty, Limit = 20 },
                CancellationToken.None );
            Equal( 1, home.Count );
            Equal( file.Id, home[0].Id );
            True( home.All( value => value.Section?.Id == "bookmarks" ) );

            var removedAlias = await catalog.SetAliasAsync(
                application,
                null,
                CancellationToken.None );
            True( removedAlias.IsFavorite && removedAlias.Aliases.Count == 0 );

            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: false ), null, CancellationToken.None );
            var snapshot = await catalog.GetSnapshotAsync(
                includeShortcuts: false,
                CancellationToken.None );
            var orphan = snapshot.Customizations.Single( value => value.StableId == file.Id );
            True( !orphan.IsAvailable );
            await catalog.RemoveOrphanAsync( orphan.Id, CancellationToken.None );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.RemoveOrphanAsync(
                    snapshot.Customizations.Single( value => value.StableId == application.Id ).Id,
                    CancellationToken.None ) );

            var export = await catalog.ExportAsync( CancellationToken.None );
            Equal( 3, export.Version );
            Equal( 1, export.Customizations.Count );
            True( export.Customizations.All( value => value.Title != "Brav" ) );

            await catalog.ResetAsync( CancellationToken.None );
            var reset = await catalog.GetSnapshotAsync( false, CancellationToken.None );
            Equal( 0, reset.Customizations.Count );
            True( !reset.Preferences.ReduceMotion );
            True( reset.Preferences.HideAfterAction );
            var emptyHome = await coordinator.SearchAsync(
                new SearchRequest { Query = string.Empty, Limit = 20 },
                CancellationToken.None );
            True( emptyHome.Count == 0 );
            await catalog.DisposeAsync( );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task DisabledLiveSourcesDoNotStartProviderAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-provider-preference-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            var defaults = LauncherPreferences.CreateDefault( );
            await catalog.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = defaults.Sources.Select( source => new SourcePreference
                {
                    SourceId = source.SourceId,
                    EnabledInSearch = source.SourceId == SourceIds.WindowsWorkspaces
                        ? false
                        : source.EnabledInSearch,
                    EnabledOnHome = source.EnabledOnHome,
                    Position = source.Position
                } ).ToList( )
            }, CancellationToken.None );

            CountingSearchProvider hyprland = new( "hyprland" );
            SearchCoordinator coordinator = new(
                [hyprland],
                new SearchOptions( ),
                preferences: catalog );
            _ = await coordinator.SearchAsync(
                new SearchRequest { Query = "terminal", Limit = 20 },
                CancellationToken.None );
            Equal( 0, hyprland.Calls );

            _ = await coordinator.SearchAsync(
                new SearchRequest { Query = "> terminal", Limit = 20 },
                CancellationToken.None );
            Equal( 0, hyprland.Calls );

            _ = await coordinator.SearchAsync(
                new SearchRequest
                {
                    Query = "terminal",
                    Expression = new QueryExpression { IncludeFilterIds = ["windows"] },
                    Limit = 20
                },
                CancellationToken.None );
            Equal( 1, hyprland.Calls );
            await catalog.DisposeAsync( );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task FavoritesRejectOverflowInsteadOfTruncatingExportAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-favorite-limit-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            for ( var index = 0; index < CustomizationLimits.MaximumFavorites; index++ )
            {
                await catalog.SetFavoriteAsync( new SearchResult
                {
                    Id = $"file:favorite-{index:D3}",
                    Kind = ResultKinds.File,
                    Title = $"Favorite {index:D3}",
                    ProviderId = "files",
                    SourceId = SourceIds.Files,
                    CanCustomize = true
                }, true, CancellationToken.None );
            }

            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.SetFavoriteAsync( new SearchResult
                {
                    Id = "file:favorite-overflow",
                    Kind = ResultKinds.File,
                    Title = "Favorite overflow",
                    ProviderId = "files",
                    SourceId = SourceIds.Files,
                    CanCustomize = true
                }, true, CancellationToken.None ) );

            var snapshot = await catalog.GetSnapshotAsync( false, CancellationToken.None );
            var export = await catalog.ExportAsync( CancellationToken.None );
            Equal( CustomizationLimits.MaximumFavorites, snapshot.Customizations.Count );
            Equal( CustomizationLimits.MaximumFavorites, export.Customizations.Count );

            await using ( SqliteConnection connection = new( $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var insert = connection.CreateCommand( );
                insert.CommandText = """
                    INSERT INTO favorites(
                        stable_id, position, title_snapshot, source_id,
                        created_unix_ms, updated_unix_ms)
                    VALUES(
                        'file:legacy-overflow', 257, 'Legacy overflow',
                        'files', 1, 1);
                    """;
                await insert.ExecuteNonQueryAsync( );
            }

            snapshot = await catalog.GetSnapshotAsync( false, CancellationToken.None );
            export = await catalog.ExportAsync( CancellationToken.None );
            Equal( CustomizationLimits.MaximumFavorites + 1, snapshot.Customizations.Count );
            Equal( CustomizationLimits.MaximumFavorites + 1, export.Customizations.Count );
            await catalog.ReorderFavoritesAsync(
                snapshot.Customizations.Select( value => value.StableId ).Reverse( ).ToList( ),
                CancellationToken.None );
            var reordered = await catalog.GetSnapshotAsync( false, CancellationToken.None );
            Equal( "file:legacy-overflow", reordered.Customizations[0].StableId );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task HomePreservesBookmarkOrderOverScoreAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-home-order-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync(
                CustomizationItems( includeFile: true ),
                null,
                CancellationToken.None );
            var application = (await catalog.SearchAsync(
                QueryParser.Parse( "Brave" ),
                10,
                CancellationToken.None )).Single( value => value.Kind == ResultKinds.Application );
            var file = (await catalog.SearchAsync(
                QueryParser.Parse( "notes" ),
                10,
                CancellationToken.None )).Single( value => value.Kind == ResultKinds.File );
            await catalog.SetFavoriteAsync( application, true, CancellationToken.None );
            await catalog.SetFavoriteAsync( file, true, CancellationToken.None );
            await catalog.ReorderFavoritesAsync(
                [file.Id, application.Id],
                CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync(
                application.Id,
                application.Actions[0].Id,
                "Brave",
                CancellationToken.None );

            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog, catalog )],
                new SearchOptions( ),
                catalog,
                catalog );
            var home = await coordinator.SearchAsync(
                new SearchRequest { Query = string.Empty, Limit = 20 },
                CancellationToken.None );
            Equal( file.Id, home[0].Id );
            Equal( application.Id, home[1].Id );
            await catalog.DisposeAsync( );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task HomeFrecencyConsidersOlderFrequentResultsAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-home-frecency-spec-{Guid.NewGuid( ):N}.db" );
        DateTimeOffset now = new( 2026, 8, 30, 12, 0, 0, TimeSpan.Zero );
        try
        {
            SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database },
                new FixedTimeProvider( now ) );
            await catalog.InitializeAsync( CancellationToken.None );

            async IAsyncEnumerable<IndexedItem> Items( )
            {
                for ( var index = 0; index < 300; index++ )
                {
                    yield return ApplicationItem(
                        $"app-{index:D3}.desktop",
                        $"Application {index:D3}",
                        $"app-{index:D3}" );
                }

                await Task.CompletedTask;
            }

            await catalog.ReplaceAllAsync( Items( ), null, CancellationToken.None );
            await using ( SqliteConnection connection = new( $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var command = connection.CreateCommand( );
                command.CommandText = """
                    INSERT INTO usage(stable_id, use_count, last_used_unix_ms)
                    VALUES($id, $uses, $last_used);
                    """;
                var id = command.Parameters.Add( "$id", SqliteType.Text );
                var uses = command.Parameters.Add( "$uses", SqliteType.Integer );
                var lastUsed = command.Parameters.Add( "$last_used", SqliteType.Integer );

                id.Value = "app:app-000.desktop";
                uses.Value = 100;
                lastUsed.Value = now.AddDays( -365 ).ToUnixTimeMilliseconds( );
                await command.ExecuteNonQueryAsync( );

                for ( var index = 1; index < 300; index++ )
                {
                    id.Value = $"app:app-{index:D3}.desktop";
                    uses.Value = 1;
                    lastUsed.Value = now.AddSeconds( -index ).ToUnixTimeMilliseconds( );
                    await command.ExecuteNonQueryAsync( );
                }
            }

            var home = await catalog.GetHomeResultsAsync(
                LauncherPreferences.CreateDefault( ),
                20,
                CancellationToken.None );
            True( home.Any( result => result.Id == "app:app-000.desktop" ) );
            Equal( "app:app-000.desktop", home[0].Id );
            await catalog.DisposeAsync( );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

}
