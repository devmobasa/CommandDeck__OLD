namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunFeatureRegistrationsAsync( )
    {
        await RunAsync( "copy action preserves empty whitespace and literal stdin but rejects missing payloads", CopyActionPreservesEmptyAndLiteralStdinAsync );
        await RunAsync( "URL action preserves alias stripping and escaped literal argv", UrlActionPreservesAliasAndEscapedArgvAsync );
        await RunAsync( "application argument actions preserve hidden state and declared inputs", ApplicationArgumentsPreserveHiddenInputsAsync );
        await RunAsync( "learned corpus ranks and dense candidates survive small limits", ( ) => RunLearnedTopMatchCorpusAsync( benchmark: false ) );
        await RunAsync( "accepted usage bounds real SQLite write contention", AcceptedUsageBoundsRealSqliteContentionAsync );
        await RunAsync( "accepted runtime usage survives dismissal and history failure", RuntimeAcceptedUsageSurvivesCancellationAndFailureAsync );
        await RunAsync( "usage and query history gates remain independent", UsageAndQueryHistoryGatesRemainIndependentAsync );
        await RunAsync( "reset ranking forgets one result's use without recording any", ResetRankingForgetsOneResultsUseAsync );
        await RunAsync( "learned queries promote abbreviations behind every gate", LearnedQueriesPromoteAbbreviationsAsync );
        await RunAsync( "settings history choices override the configuration at run time", HistoryChoicesOverrideTheConfigurationAsync );
        await RunAsync( "a history choice read before a newer one never restores it", HistoryChoicesReadBeforeAChangeNeverRestoreItAsync );
        await RunAsync( "omarchy menu state follows its settings choice", OmarchyMenuStateFollowsItsChoiceAsync );
        await RunAsync( "learned promotion rejects navigation and falls back without signals", LearnedPromotionRejectsNavigationAndMissingSignalsAsync );
        await RunAsync( "learned promotion respects strong match and age gates", LearnedPromotionGatesAsync );
        await RunAsync( "learned promotion preserves caps fairness intent and deterministic ties", LearnedPromotionCapsAndTiesAsync );
        await RunAsync( "learned promotion batches history and respects toggle matrix", LearnedPromotionTogglesAndBatchesAsync );
        await RunAsync( "learned partial matches escape fixed source ordering", LearnedSignalAppearsAboveSourceGroupsAsync );
        await RunAsync( "desktop launches return while applications remain running", DesktopLaunchDoesNotWaitForApplicationExitAsync );
        await RunAsync( "file opens return after managed launch acceptance", FileOpenUsesManagedLaunchAsync );
        await RunAsync(
            "adaptive ranking is optional inspectable expiring exportable and resettable",
            AdaptiveRankingLifecycleIsReversibleAsync );
        await RunAsync(
            "query history is opt-in bounded expiring deletable and resettable",
            QueryHistoryLifecycleIsPrivateAsync );
        await RunAsync(
            "per-kind time-aware and context ranking are clock controlled and reversible",
            PerKindTimeAwareAndContextRankingAreControlledAsync );
        Run(
            "ranking validation names the setting that is out of range",
            RankingValidationNamesTheSettingOutOfRange );
        await RunAsync(
            "filter aliases persist resolve reject conflicts and reset",
            FilterAliasesPersistResolveAndResetAsync );
        await RunAsync(
            "local suggestions are bounded cancellable private and network silent",
            LocalSuggestionsAreBoundedPrivateAndNetworkSilentAsync );
        await RunAsync(
            "saved searches persist typed queries and reject shortcut conflicts",
            SavedSearchesPersistTypedQueriesAndRejectConflictsAsync );
        await RunAsync( "clipboard shortcut preferences persist and reject conflicts", ClipboardShortcutPreferencesAsync );
        await RunAsync(
            "file and configuration workflows use literal typed actions",
            FileAndConfigurationWorkflowsUseLiteralTypedActionsAsync );
        await RunAsync( "runtime retains search rows when optional MIME detection fails",
            RuntimeSearchSurvivesMimeDetectionFailureAsync );
        await RunAsync(
            "runtime detects MIME types only for the file rows it shows",
            RuntimeResolvesMimeTypesOnlyForShownFilesAsync );
        await RunAsync(
            "runtime search does not wait for slow MIME detection",
            RuntimeSearchDoesNotWaitForSlowMimeDetectionAsync );
        await RunAsync(
            "runtime search cancellation ends MIME detection",
            RuntimeSearchCancellationEndsMimeDetectionAsync );
        await RunAsync(
            "runtime bounds MIME detection that ignores cancellation and joins it at shutdown",
            RuntimeBoundsAndJoinsAbandonedMimeDetectionAsync );
        await RunAsync(
            "MIME type cache evicts the least recently used path",
            MimeTypeCacheEvictsLeastRecentlyUsedPathAsync );
        await RunAsync( "application invocation keeps user arguments as argv", async ( ) =>
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
                        StableId = "app:dev.zed.Zed.desktop",
                        Kind = ResultKinds.Application,
                        Title = "Zed",
                        Subtitle = "High-performance code editor",
                        PayloadJson = PayloadWriter.Create(
                            ("desktopFile", "/usr/share/applications/dev.zed.Zed.desktop"),
                            ("desktopId", "dev.zed.Zed.desktop"),
                            ("exec", "zed %U") ),
                        ProviderId = "applications",
                        SourceId = SourceIds.Applications
                    };
                    yield return new IndexedItem
                    {
                        StableId = "app:org.example.Workbench.desktop",
                        Kind = ResultKinds.Application,
                        Title = "Workbench",
                        Subtitle = "A different application",
                        PayloadJson = PayloadWriter.Create(
                            ("desktopFile", "/usr/share/applications/org.example.Workbench.desktop"),
                            ("desktopId", "org.example.Workbench.desktop"),
                            ("exec", "workbench %U") ),
                        ProviderId = "applications",
                        SourceId = SourceIds.Applications
                    };
                    await Task.CompletedTask;
                }

                await catalog.ReplaceAllAsync( Items( ), null, CancellationToken.None );
                var zed = (await catalog.SearchAsync(
                    QueryParser.Parse( "zed" ),
                    10,
                    CancellationToken.None )).Single( );
                await catalog.SetAliasAsync( zed, "z", CancellationToken.None );
                SearchCoordinator search = new(
                    [new CatalogSearchProvider( catalog, catalog )],
                    new SearchOptions( ),
                    catalog,
                    catalog );
                var results = await search.SearchAsync(
                    new SearchRequest { Query = "zed ~/Desktop/test.txt", Limit = 10 },
                    CancellationToken.None );

                Equal( 1, results.Count );
                Equal( "Zed", results[0].Title );
                var action = results[0].Actions[0];
                Equal( ActionKinds.LaunchDesktop, action.Kind );
                Equal(
                    "~/Desktop/test.txt",
                    LaunchArgumentCodec.Decode( action.Arguments["launchArguments"] )[0] );
                Equal( "app:dev.zed.Zed.desktop", results[0].Metadata["catalogResultId"] );

                var otherResults = await search.SearchAsync(
                    new SearchRequest { Query = "zed ~/Desktop/other.txt", Limit = 10 },
                    CancellationToken.None );
                Equal( 1, otherResults.Count );
                True( !string.Equals( results[0].Id, otherResults[0].Id, StringComparison.Ordinal ) );

                var aliasResults = await search.SearchAsync(
                    new SearchRequest { Query = "z ~/Desktop/alias.txt", Limit = 10 },
                    CancellationToken.None );
                Equal( 1, aliasResults.Count );
                Equal(
                    "~/Desktop/alias.txt",
                    LaunchArgumentCodec.Decode(
                        aliasResults[0].Actions[0].Arguments["launchArguments"] )[0] );

                foreach ( var (query, expression, arguments) in new[]
                {
                    ("app: zed https://example.test --private-window", new QueryExpression( ),
                        "https://example.test\u001f--private-window"),
                    ("zed --new-window \"/demo/my notes.txt\"", new QueryExpression( ),
                        "--new-window\u001f/demo/my notes.txt"),
                    ("zed https://example.test --private-window", new QueryExpression
                    {
                        IncludeFilterIds = ["applications"],
                        Clauses = [new QueryClause { Text = "-private-window", Excluded = true }]
                    }, "https://example.test\u001f--private-window")
                } )
                {
                    var launched = await search.SearchAsync( new SearchRequest
                    {
                        Query = query,
                        Expression = expression,
                        Limit = 10
                    }, CancellationToken.None );

                    Equal( 1, launched.Count );
                    Equal( "app:dev.zed.Zed.desktop", launched[0].Metadata["catalogResultId"] );
                    Equal( arguments, string.Join( '\u001f',
                        LaunchArgumentCodec.Decode( launched[0].Actions[0].Arguments["launchArguments"] ) ) );
                }

                await catalog.SetAliasAsync( zed, "workbench", CancellationToken.None );
                var unrelatedAliasResults = await search.SearchAsync(
                    new SearchRequest
                    {
                        Query = "workbench ~/Desktop/identity.txt",
                        Limit = 10
                    },
                    CancellationToken.None );
                Equal( 1, unrelatedAliasResults.Count );
                Equal( "Zed", unrelatedAliasResults[0].Title );
                Equal(
                    "app:dev.zed.Zed.desktop",
                    unrelatedAliasResults[0].Metadata["catalogResultId"] );

                var ordinaryAliasSearch = await search.SearchAsync(
                    new SearchRequest { Query = "workbench settings", Limit = 10 },
                    CancellationToken.None );
                Equal( "Zed", ordinaryAliasSearch[0].Title );
                True( !ordinaryAliasSearch[0].Actions[0].Arguments.ContainsKey( "launchArguments" ) );
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

        await RunAsync(
            "detail fields remain ordered and serialize over the protocol",
            DetailFieldsRemainOrderedAndSerializeAsync );

        await RunAsync(
            "catalog migrates the previous schema without losing data",
            CatalogMigratesPreviousSchemaAsync );

        await RunAsync(
            "catalog repairs migration 2 databases without rebuild state",
            CatalogRepairsMigrationTwoWithoutStateAsync );

        await RunAsync(
            "catalog migration links existing search rows to catalog rows",
            CatalogMigrationLinksExistingSearchRowsAsync );

        await RunAsync(
            "catalog refuses a database written by a newer schema without changing it",
            CatalogRefusesNewerSchemaAsync );

        await RunAsync(
            "window icons follow desktop entry match order",
            WindowIconsFollowDesktopEntryMatchOrderAsync );

        await RunAsync(
            "configuration results collapse per source and windows win equal scores",
            ConfigurationResultsCollapsePerSourceAsync );

        Run(
            "Omarchy current menu routes preserve IDs and hide power leaves",
            OmarchyCurrentMenuRoutesPreserveIds );

        Run(
            "shortcut inspection reports effective matching chords",
            ShortcutInspectionReportsEffectiveChords );

        await RunAsync(
            "shortcut inspection distinguishes unconfigured and failed inspection",
            ShortcutInspectionDistinguishesFailuresAsync );

        await RunAsync(
            "favorites aliases preferences and home survive catalog rebuilds",
            CustomizationAndHomeSurviveRebuildAsync );

        await RunAsync(
            "storage rejects new overflow and preserves legacy customization rows",
            FavoritesRejectOverflowInsteadOfTruncatingExportAsync );

        await RunAsync(
            "disabled live sources do not start their provider",
            DisabledLiveSourcesDoNotStartProviderAsync );

        await RunAsync(
            "Home preserves explicit bookmark order over score",
            HomePreservesBookmarkOrderOverScoreAsync );

        await RunAsync(
            "Home frecency considers older frequent results",
            HomeFrecencyConsidersOlderFrequentResultsAsync );

        await RunAsync(
            "personalization copy and preview operations leave usage unchanged",
            PersonalizationCopyAndPreviewLeaveUsageUnchangedAsync );

        await RunAsync(
            "web fallback performs no network activity before invocation",
            WebFallbackDoesNotConnectBeforeInvocationAsync );

        Run(
            "every configurable source ships a label and an icon",
            ConfigurableSourcesShipLabelAndIcon );

    }
}
