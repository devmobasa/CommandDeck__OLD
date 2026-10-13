namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunCatalogRegistrationsAsync( )
    {
        await RunAsync( "an old catalog cancellation token cannot interrupt a later pooled borrower",
            OldCatalogTokenCannotInterruptLaterBorrowerAsync );
        await RunAsync( "home query probes items by stable id instead of scanning the catalog",
            HomeQueryProbesItemsInsteadOfScanningTheCatalogAsync );
        await RunAsync( "fuzzy candidate lookup uses the source and title indexes",
            FuzzyCandidateLookupUsesTitleIndexesAsync );
        await RunAsync( "sparse fuzzy lookup preserves visible rows without scanning disabled catalog growth",
            SparseFuzzyLookupIgnoresDisabledCatalogGrowthAsync );
        await RunAsync( "catalog search preserves row metadata across lexical empty and fuzzy queries",
            CatalogSearchPreservesProjectionAcrossQueryShapesAsync );
        await RunAsync( "excluded titles cannot consume the sparse fuzzy candidate budget",
            ExcludedTitlesCannotConsumeFuzzyCandidateBudgetAsync );
        await RunAsync( "fuzzy search keeps names excluded from search out of matches",
            FuzzySearchKeepsExcludedNamesOutOfMatchesAsync );
        await RunAsync( "customizations snapshot probes availability by stable id",
            CustomizationsSnapshotProbesAvailabilityByStableIdAsync );
        await RunAsync( "home folder search scope persists and filters before result limits",
            HomeSearchScopePersistsAndFiltersCandidatesAsync );
        await RunAsync( "hidden items leave every catalog path and restore with bookmark and alias intact",
            HiddenItemsLeaveEveryCatalogPathAndRestoreAsync );
        await RunAsync( "hidden items survive reindexing export and reset",
            HiddenItemsSurviveReindexExportAndResetAsync );
        await RunAsync( "hidden items are bounded and require durable identity",
            HiddenItemsAreBoundedAndRequireDurableIdentityAsync );
        await RunAsync( "coordinator drops hidden candidates from live providers",
            CoordinatorDropsHiddenCandidatesFromLiveProvidersAsync );
        await RunAsync(
            "catalog operations inspect maintain and safely purge the rebuildable projection",
            CatalogOperationsInspectMaintainAndPurgeAsync );
        await RunAsync( "projection consistency probes linked search rows instead of nested scans",
            ProjectionConsistencyAvoidsNestedSearchScansAsync );
        await RunAsync( "projection consistency detects and repairs every kind of drift",
            ProjectionConsistencyDetectsEveryDriftAsync );
        await RunAsync( "integrity check accepts fields the search policy excludes",
            IntegrityAcceptsFieldsExcludedFromSearchAsync );
        await RunAsync( "repair keeps fields the search policy excludes out of search",
            RepairKeepsExcludedFieldsOutOfSearchAsync );
        await RunAsync( "catalog migration restores search inclusion from stored policy",
            CatalogMigrationRestoresSearchInclusionFromPolicyAsync );
        await RunAsync( "integrity check and repair scale linearly with the catalog",
            IntegrityCheckAndRepairScaleLinearlyAsync );
        await RunAsync( "catalog optimize vacuums only when it frees space",
            CatalogOptimizeVacuumsOnlyWhenItFreesSpaceAsync );
        await RunAsync( "inspection pages beyond the stored work sample",
            InspectionPagesBeyondStoredSampleAsync );
        await RunAsync( "pooled catalog connections apply the configured pragmas on every open",
            PooledCatalogConnectionsApplyTheirPragmasAsync );
        await RunAsync( "a briefly blocked catalog writer waits for the configured busy timeout",
            BlockedCatalogWriterWaitsForTheConfiguredTimeoutAsync );

        await RunAsync( "SQLite FTS5 smoke test", async ( ) =>
        {
            var database = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-spec-{Guid.NewGuid( ):N}.db" );
            try
            {
                SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
                await catalog.InitializeAsync( CancellationToken.None );
                True( await catalog.VerifyFts5Async( CancellationToken.None ) );

                async IAsyncEnumerable<IndexedItem> Items( )
                {
                    yield return new IndexedItem
                    {
                        StableId = "file:one",
                        Kind = ResultKinds.File,
                        Title = "architecture.md",
                        Subtitle = "/tmp/architecture.md",
                        Body = "command palette architecture",
                        ProviderId = "spec",
                        SourceId = SourceIds.Files
                    };
                    await Task.CompletedTask;
                }

                await catalog.ReplaceAllAsync( Items( ), null, CancellationToken.None );
                var results = await catalog.SearchAsync(
                    QueryParser.Parse( "architecture" ),
                    10,
                    CancellationToken.None );
                Equal( 1, results.Count );
                Equal( "architecture.md", results[0].Title );
                await catalog.DisposeAsync( );
            }
            finally
            {
                foreach ( var suffix in new[] { string.Empty, "-shm", "-wal" } )
                {
                    File.Delete( database + suffix );
                }
            }
        } );

        await RunAsync( "universal catalog search tolerates string line numbers", async ( ) =>
        {
            var database = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-spec-{Guid.NewGuid( ):N}.db" );
            try
            {
                SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
                await catalog.InitializeAsync( CancellationToken.None );

                async IAsyncEnumerable<IndexedItem> Items( )
                {
                    yield return new IndexedItem
                    {
                        StableId = "app:brave.desktop",
                        Kind = ResultKinds.Application,
                        Title = "Brave",
                        PayloadJson = PayloadWriter.Create(
                            ("desktopFile", "/usr/share/applications/brave.desktop"),
                            ("exec", "brave %U") ),
                        ProviderId = "applications",
                        SourceId = SourceIds.Applications
                    };
                    yield return new IndexedItem
                    {
                        StableId = "configuration:brave",
                        Kind = ResultKinds.Configuration,
                        Title = "browserClass",
                        Subtitle = "Brave",
                        Path = "/tmp/hyprland.conf",
                        PayloadJson = PayloadWriter.Create(
                            ("line", "69"),
                            ("value", "Brave") ),
                        ProviderId = "configuration",
                        SourceId = SourceIds.Configuration
                    };
                    yield return new IndexedItem
                    {
                        StableId = "file:browser-notes",
                        Kind = ResultKinds.File,
                        Title = "browser-notes.txt",
                        Body = $"{string.Join( ' ', Enumerable.Repeat( "noise", 5_000 ) )} Brave",
                        ProviderId = "files",
                        SourceId = SourceIds.Files
                    };
                    for ( var index = 0; index < 100; index++ )
                    {
                        yield return new IndexedItem
                        {
                            StableId = $"file:unrelated-{index}",
                            Kind = ResultKinds.File,
                            Title = $"unrelated-{index}.txt",
                            Body = "unrelated",
                            ProviderId = "files",
                            SourceId = SourceIds.Files
                        };
                    }
                    await Task.CompletedTask;
                }

                await catalog.ReplaceAllAsync( Items( ), null, CancellationToken.None );
                var results = await catalog.SearchAsync(
                    QueryParser.Parse( "Brave" ),
                    10,
                    CancellationToken.None );
                Equal( 3, results.Count );
                True( results.Any( result => result.Title == "Brave" ) );
                True( results.Any( result => result.Details.Any( detail =>
                    detail.Label == "Key location" && detail.Value == "69" ) ) );

                SearchCoordinator search = new(
                    [new CatalogSearchProvider( catalog )],
                    new SearchOptions( ) );
                var ranked = await search.SearchAsync(
                    new SearchRequest { Query = "Brave", Limit = 10 },
                    CancellationToken.None );
                Equal( ResultKinds.Application, ranked[0].Kind );
                True( ranked[0].Score > 0.8 );
                await catalog.DisposeAsync( );
            }
            finally
            {
                foreach ( var suffix in new[] { string.Empty, "-shm", "-wal" } )
                {
                    File.Delete( database + suffix );
                }
            }
        } );

        await RunAsync( "desktop scanner applies user masking and desktop visibility", async ( ) =>
        {
            var root = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-desktop-spec-{Guid.NewGuid( ):N}" );
            var userData = Path.Combine( root, "user" );
            var systemData = Path.Combine( root, "system" );
            var userApplications = Path.Combine( userData, "applications" );
            var systemApplications = Path.Combine( systemData, "applications" );
            Directory.CreateDirectory( userApplications );
            Directory.CreateDirectory( systemApplications );

            var oldDataHome = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" );
            var oldDataDirectories = Environment.GetEnvironmentVariable( "XDG_DATA_DIRS" );
            var oldDesktop = Environment.GetEnvironmentVariable( "XDG_CURRENT_DESKTOP" );
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine( userApplications, "masked.desktop" ),
                    "[Desktop Entry]\nType=Application\nName=Masked\nExec=/usr/bin/true\nHidden=true\n" );
                await File.WriteAllTextAsync(
                    Path.Combine( systemApplications, "masked.desktop" ),
                    "[Desktop Entry]\nType=Application\nName=Must not leak\nExec=/usr/bin/true\n" );
                await File.WriteAllTextAsync(
                    Path.Combine( systemApplications, "visible.desktop" ),
                    "[Desktop Entry]\nType=Application\nName=Visible\nExec=/usr/bin/true\nStartupWMClass=VisibleClass\nOnlyShowIn=Hyprland;\nTryExec=/usr/bin/true\n" );
                await File.WriteAllTextAsync(
                    Path.Combine( systemApplications, "gnome.desktop" ),
                    "[Desktop Entry]\nType=Application\nName=GNOME only\nExec=/usr/bin/true\nOnlyShowIn=GNOME;\n" );

                Environment.SetEnvironmentVariable( "XDG_DATA_HOME", userData );
                Environment.SetEnvironmentVariable( "XDG_DATA_DIRS", systemData );
                Environment.SetEnvironmentVariable( "XDG_CURRENT_DESKTOP", "Hyprland" );

                List<IndexedItem> items = [];
                await foreach ( var item in DesktopEntryScanner.ScanAsync(
                    CancellationToken.None ) )
                {
                    items.Add( item );
                }

                Equal( 1, items.Count );
                Equal( "Visible", items[0].Title );
                Equal( "VisibleClass", items[0].StartupWmClass );
            }
            finally
            {
                Environment.SetEnvironmentVariable( "XDG_DATA_HOME", oldDataHome );
                Environment.SetEnvironmentVariable( "XDG_DATA_DIRS", oldDataDirectories );
                Environment.SetEnvironmentVariable( "XDG_CURRENT_DESKTOP", oldDesktop );
                Directory.Delete( root, recursive: true );
            }
        } );

    }
}
