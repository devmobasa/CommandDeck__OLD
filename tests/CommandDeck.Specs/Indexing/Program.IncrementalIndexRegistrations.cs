namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunIncrementalIndexRegistrationsAsync( )
    {
        await RunAsync( "full scan exclusion diagnostics survive incremental edits and refresh with changed paths", FullScanExclusionDiagnosticsSurviveIncrementalEditsAsync );
        await RunAsync( "full scan exclusion diagnostics retire removed roots after incremental edits", ( ) => ExerciseExclusionDiagnosticsRootChangeAsync( configured: false ) );
        await RunAsync( "full scan exclusion diagnostics retire disabled roots after incremental edits", ( ) => ExerciseExclusionDiagnosticsRootChangeAsync( configured: true ) );
        await RunAsync( "indexed folder pending scan respects catalog purge hold", ( ) => ExercisePendingRootMaintenanceAsync( repair: false ) );
        await RunAsync( "indexed folder pending scan respects catalog repair hold", ( ) => ExercisePendingRootMaintenanceAsync( repair: true ) );
        await RunAsync( "indexed folder reset requires ownership even with an empty overlay", IndexedFolderEmptyResetRequiresOwnershipAsync );
        await RunAsync( "indexed folder purge and pending full reindex survive reboot with a lost journal window", ( ) => IndexedFolderPurgeSurvivesPendingReindexRestartAsync( reboot: true ) );
        await RunAsync( "indexed folder purge and pending full reindex survive restart with a torn journal tail", ( ) => IndexedFolderPurgeSurvivesPendingReindexRestartAsync( reboot: false ) );
        await RunAsync( "cancelled full reindex keeps subsequent incremental events live", CancelledFullReindexKeepsIncrementalEventsAsync );
        await RunAsync( "indexed folder edit supersedes startup recovery and preserves incremental events", IndexedFolderEditSupersedesStartupRecoveryAsync );
        await RunAsync( "indexed folder edits coalesce full reindexes and watch existing deep files", IndexedFolderEditsReindexAllRootsAndRebuildWatchesAsync );
        await RunAsync( "indexed folder symlink reports pending and failed work", IndexedFolderSymlinkReportsPendingAndFailedWorkAsync );
        await RunAsync( "indexed folder trailing separator restores watches after nested edits", IndexedFolderTrailingSeparatorRestoresWatchesAsync );
        await RunAsync( "indexed folder missing symlink removal retires target watches", ( ) => ExerciseUnavailableRootLinkAsync( configured: false, retargeted: false ) );
        await RunAsync( "indexed folder missing symlink disable retires target watches", ( ) => ExerciseUnavailableRootLinkAsync( configured: true, retargeted: false ) );
        await RunAsync( "indexed folder retargeted symlink removal retires target watches", ( ) => ExerciseUnavailableRootLinkAsync( configured: false, retargeted: true ) );
        await RunAsync( "indexed folder retargeted symlink disable retires target watches", ( ) => ExerciseUnavailableRootLinkAsync( configured: true, retargeted: true ) );
        await RunAsync( "indexed folder non-owner runtime cannot resurrect removed rows", IndexedFolderNonOwnerCannotResurrectRemovedRowsAsync );
        await RunAsync( "indexed folder resumed work reconciles unrelated offline policy across restarts", IndexedFolderResumedWorkReconcilesOfflinePolicyAsync );
        await RunAsync( "indexed folder edit preserves unrelated offline policy inputs", IndexedFolderEditPreservesUnrelatedOfflinePolicyInputsAsync );
        await RunAsync( "indexed folder filesystem root retains covered Search and Home rows", IndexedFolderFilesystemRootRetainsCoveredRowsAsync );
        await RunAsync( "indexed folder scan preserves unrelated pending incremental events", IndexedFolderScanPreservesUnrelatedPendingEventsAsync );
        await RunAsync( "indexed folder purge failure rolls back preferences and coverage", IndexedFolderPurgeFailureRollsBackPreferencesAsync );
        await RunAsync( "indexed folder purge revokes content beyond surviving root extraction limits", ( ) => ExerciseFailedRootCommitAsync( failPurge: false, limitContent: true ) );
        await RunAsync( "indexed folder failed publication purges Search and Home and retries", IndexedFolderFailedPublicationPurgesSearchAndHomeAsync );
        await RunAsync( "indexed folder overlap preserves parent policy and disabled roots across restart", IndexedFolderOverlapAndRestartAsync );
        await RunAsync( "cancelled initial watch admission rebuilds coverage for an existing deep file", CancelledWatchAdmissionRebuildsCoverageAsync );
        Run( "a replaced parent cannot create outside native watches", ReplacedParentCannotCreateOutsideWatches );
        await RunAsync( "overflow reconciliation restores missing native watches", OverflowReconciliationRestoresMissingWatchesAsync );
        await RunAsync( "overflow reconciliation removes stale native watches", OverflowReconciliationRemovesStaleWatchesAsync );
        await RunAsync( "overflow reconciliation covers overlapping roots once", OverflowReconciliationCoversOverlappingRootsAsync );
        await RunAsync( "overflow recovery detects a retargeted retained root", OverflowRecoveryDetectsRetargetedRetainedRootAsync );
        await RunAsync(
            "home search defaults cover downloads code and hidden filenames",
            HomeSearchDefaultsCoverUserFilesAsync );
        await RunAsync(
            "indexing a storage parent ignores its own database and watcher journal",
            IndexStorageDoesNotFeedItsOwnWatcherAsync );
        await RunAsync(
            "full and scoped file scans skip unreadable subdirectories",
            FileScansSkipUnreadableDirectoriesAsync );
        Run( "filesystem root contains ordinary descendants", ( ) =>
        {
            True( CanonicalPathBoundary.TryCreate( "/", out var boundary ) );
            True( boundary!.Contains( "/usr" ) );
            True( boundary.Contains( "/home/user/Documents/note.txt" ) );
        } );
        await RunAsync(
            "per-root status pause and resume converge without blocking active roots",
            PerRootStatusPauseAndResumeConvergeAsync );
        await RunAsync(
            "scoped reindex preserves unrelated rows and personalization",
            ScopedReindexPreservesUnrelatedRowsAndPersonalizationAsync );
        await RunAsync(
            "scoped and overflow rescans keep the rows of a full scan",
            ScopedAndOverflowRescansKeepFullScanRowsAsync );
        await RunAsync(
            "scoped rescans stream without holding the subtree in memory",
            ScopedRescansStreamWithoutHoldingTheSubtreeAsync );
        await RunAsync(
            "staged rescans keep the search rowid link and search inclusion",
            StagedRescansKeepTheSearchLinkAndInclusionAsync );
        await RunAsync(
            "staged path batches publish as one transaction in change order",
            StagedPathBatchesPublishAsOneTransactionAsync );
        await RunAsync(
            "event storm reconciles only the affected root within a batch budget",
            EventStormReconcilesOnlyAffectedRootWithinBatchBudgetAsync );
        await RunAsync(
            "watcher overflow survives restart and reconciles only its root",
            WatcherOverflowSurvivesRestartAndReconcilesOnlyItsRootAsync );
        await RunAsync(
            "scheduled maintenance is due once and persists its result",
            ScheduledMaintenanceIsDueOnceAndPersistsItsResultAsync );
        await RunAsync(
            "incremental index applies create update rename atomic save and delete",
            IncrementalIndexAppliesFileLifecycleAsync );
        await RunAsync( "an application renamed into place with an old modification time becomes searchable",
            InstalledApplicationBecomesSearchableAsync );
        await RunAsync( "a removed or masked application leaves search without a restart", RemovedApplicationLeavesSearchAsync );
        await RunAsync( "an application change after a purge waits for the full scan",
            ApplicationRefreshLeavesAPurgedCatalogToTheFullScanAsync );
        await RunAsync( "an application installed while stopped appears after start",
            ApplicationInstalledWhileStoppedAppearsAfterStartAsync );
        await RunAsync(
            "incremental journal replays an unapplied event after restart",
            IncrementalJournalReplaysAfterRestartAsync );
        Run(
            "incremental journal commits an event burst with a bounded number of flushes",
            IncrementalJournalGroupCommitsBursts );
        Run(
            "incremental journal window survives a crash and reconciles after a reboot",
            IncrementalJournalWindowRecoversAfterCrashOrReboot );
        await RunAsync(
            "incremental engine flushes the journal once per debounce window",
            IncrementalEngineFlushesOncePerDebounceWindowAsync );
        await RunAsync(
            "a journal window lost across a reboot reconciles the index",
            LostJournalWindowReconcilesAfterRebootAsync );
        await RunAsync( "a failed offline root does not abandon later roots",
            OfflineRootFailureDoesNotAbandonLaterRootsAsync );
        await RunAsync(
            "restart applies files created modified and deleted while stopped",
            RestartAppliesChangesMadeWhileStoppedAsync );
        await RunAsync(
            "resume watches a root created after startup",
            ResumeWatchesRootCreatedAfterStartupAsync );
        await RunAsync(
            "root and full reindex watch roots created after startup",
            ReindexWatchesRootsCreatedAfterStartupAsync );
        await RunAsync(
            "watcher closes the initial scan window without rescanning ordinary edits",
            WatcherClosesInitialScanWindowAsync );
        await RunAsync( "superseded reconciliation preserves edits in the full scan window",
            SupersededReconciliationPreservesScanWindowAsync );
        await RunAsync( "superseded reconciliation clears active status when the full scan precedes dispatch",
            SupersededReconciliationClearsActiveStatusAsync );
        await RunAsync(
            "incremental status reports watched and unwatched roots",
            IncrementalStatusReportsRootCoverageAsync );
        await RunAsync( "watcher replacement reconciles interrupted unrelated roots",
            WatcherReplacementReconcilesOtherRootsAsync );
        await RunAsync( "retargeted root resume removes old target results",
            RetargetedRootResumeRemovesOldResultsAsync );
        await RunAsync( "retargeted roots release obsolete external rule watches",
            RetargetedRootDropsObsoleteExternalControlsAsync );
        await RunAsync(
            "excluded trees cost no inotify watches and nested roots are watched once",
            ExcludedTreesCostNoInotifyWatchesAsync );
        await RunAsync(
            "roots below hidden and repository ancestors share the traversal policy",
            RootsBelowHiddenAndRepositoryAncestorsAsync );
        await RunAsync(
            "the watch limit leaves a visible unwatched subtree without an overflow reconcile",
            WatchLimitLeavesAVisibleUnwatchedSubtreeAsync );
        Run(
            "inotify events keep the watched tree in step with moves and new directories",
            InotifyEventsKeepTheWatchedTreeInStep );
        await RunAsync(
            "new and moved directories are watched by the traversal policy",
            NewAndMovedDirectoriesAreWatchedByPolicyAsync );
        await RunAsync(
            "extractor version changes reconcile the durable catalog state",
            ExtractorVersionChangeReconcilesAsync );
        await RunAsync(
            "incremental changes preserve rows owned by other providers",
            IncrementalChangesPreserveOtherProvidersAsync );
        await RunAsync(
            "incremental path removal deletes search rows by rowid through the path index",
            PathRemovalProbesSearchRowsByRowidAsync );
        await RunAsync(
            "incremental path removal matches only the path and its descendants",
            PathRemovalMatchesOnlyThePathAndDescendantsAsync );
        await RunAsync(
            "stale-path pager reads the path index in order without a temporary sort",
            StalePathPagerReadsThePathIndexInOrderAsync );
        await RunAsync(
            "incremental journal has one private runtime owner",
            IncrementalJournalHasOnePrivateRuntimeOwnerAsync );
        await RunAsync(
            "catalog maintenance cancels staged indexing and pauses after purge",
            CatalogMaintenanceSerializesWithIndexingAsync );
        await RunAsync(
            "catalog repair that needs a rebuild pauses indexing until a full scan",
            CatalogRepairPausesIndexingUntilAFullScanAsync );
        await RunAsync(
            "catalog writes reject a second process while the daemon owns indexing",
            CatalogWritesRequireProcessOwnershipAsync );
    }

    private static async Task IncrementalIndexAppliesFileLifecycleAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                documents );
            SqliteCatalog inner = new( options.Storage );
            await inner.InitializeAsync( CancellationToken.None );
            await using TrackingSearchCatalog catalog = new( inner, inner );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );

            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );

            var path = Path.Combine( documents, "created-note.txt" );
            await File.WriteAllTextAsync( path, "incremental alpha marker" );
            True( await WaitForCatalogResultAsync(
                catalog,
                "incremental alpha",
                result => result.Title == "created-note.txt",
                TimeSpan.FromSeconds( 1 ) ) );

            var appliesBeforeBurst = catalog.ApplyPathChangesCount;
            for ( var index = 0; index < 20; index++ )
            {
                await File.WriteAllTextAsync(
                    path,
                    $"incremental beta marker revision {index}" );
            }

            True( await WaitForCatalogResultAsync(
                catalog,
                "revision 19",
                result => result.Title == "created-note.txt",
                TimeSpan.FromSeconds( 1 ) ) );
            True( catalog.ApplyPathChangesCount - appliesBeforeBurst <= 2 );
            True( await WaitUntilAsync(
                async ( ) => (await SearchAsync( catalog, "incremental alpha" )).Count == 0,
                TimeSpan.FromSeconds( 1 ) ) );

            var renamed = Path.Combine( documents, "renamed-note.txt" );
            File.Move( path, renamed );
            True( await WaitForCatalogResultAsync(
                catalog,
                "revision 19",
                result => result.Title == "renamed-note.txt",
                TimeSpan.FromSeconds( 1 ) ) );

            var temporary = Path.Combine( documents, ".atomic-save.tmp" );
            await File.WriteAllTextAsync( temporary, "incremental gamma marker" );
            File.Move( temporary, renamed, overwrite: true );
            True( await WaitForCatalogResultAsync(
                catalog,
                "incremental gamma",
                result => result.Title == "renamed-note.txt",
                TimeSpan.FromSeconds( 1 ) ) );
            True( await WaitUntilAsync(
                async ( ) => (await SearchAsync( catalog, "incremental beta" )).Count == 0,
                TimeSpan.FromSeconds( 1 ) ) );

            File.Delete( renamed );
            True( await WaitUntilAsync(
                async ( ) => (await SearchAsync( catalog, "incremental gamma" )).Count == 0,
                TimeSpan.FromSeconds( 1 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IncrementalJournalReplaysAfterRestartAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var path = Path.Combine( documents, "restart-note.txt" );
        await File.WriteAllTextAsync( path, "durable before marker" );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                documents,
                debounceMilliseconds: 500,
                maximumLatencyMilliseconds: 900 );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );

            await using ( var first = new IndexCoordinator( catalog, catalog, options ) )
            {
                await first.StartAsync( CancellationToken.None );
                await first.ReindexAsync( progress: null, CancellationToken.None );

                // Same size and timestamp: only the journaled event can reveal this edit.
                await RewritePreservingMetadataAsync( path, "durable replay marker" );
                True( await WaitUntilAsync(
                    async ( ) => (await first.GetIncrementalStatusAsync( CancellationToken.None ))
                        .PendingEventCount > 0,
                    TimeSpan.FromSeconds( 1 ) ) );
            }

            Equal( 0, (await SearchAsync( catalog, "durable replay" )).Count );

            await using ( var second = new IndexCoordinator( catalog, catalog, options ) )
            {
                await second.StartAsync( CancellationToken.None );
                True( await WaitForCatalogResultAsync(
                    catalog,
                    "durable replay",
                    result => result.Title == "restart-note.txt",
                    TimeSpan.FromSeconds( 2 ) ) );
                True( await WaitUntilAsync(
                    async ( ) => (await second.GetIncrementalStatusAsync( CancellationToken.None ))
                        .PendingEventCount == 0,
                    TimeSpan.FromSeconds( 1 ) ) );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task WatcherClosesInitialScanWindowAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var path = Path.Combine( documents, "window-note.txt" );
        await File.WriteAllTextAsync( path, "scan window old marker" );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                documents );
            SqliteCatalog inner = new( options.Storage );
            await inner.InitializeAsync( CancellationToken.None );
            await using TrackingSearchCatalog catalog = new( inner, inner );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );

            (var firstItem, var release) = catalog.PauseNextReplacement( item => item.Path == path && item.Kind == ResultKinds.File );
            var scan = indexer.ReindexAsync(
                progress: null,
                CancellationToken.None ).AsTask( );
            try
            {
                await firstItem.WaitAsync( TimeSpan.FromSeconds( 30 ) );
                var scanning = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
                True( scanning.ReconciliationRequired, "the staged generation has not completed startup reconciliation" );
                Equal( 1, scanning.WatchedRootCount );

                await File.WriteAllTextAsync( path, "scan window final marker" );
                True( await WaitUntilAsync(
                    async ( ) => (await indexer.GetIncrementalStatusAsync( CancellationToken.None ))
                        .PendingEventCount > 0,
                    TimeSpan.FromSeconds( 30 ) ) );
            }
            finally
            {
                release( );
            }

            await scan;

            True( await WaitForCatalogResultAsync(
                catalog,
                "window final",
                result => result.Title == "window-note.txt",
                TimeSpan.FromSeconds( 30 ) ) );

            await File.WriteAllTextAsync( path, "scan window ordinary marker" );
            True( await WaitForCatalogResultAsync(
                catalog,
                "window ordinary",
                result => result.Title == "window-note.txt",
                TimeSpan.FromSeconds( 30 ) ) );
            True( await WaitUntilAsync(
                async ( ) => (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).PendingEventCount == 0,
                TimeSpan.FromSeconds( 30 ) ) );
            Equal( 1, catalog.ReplaceAllCount );
            True( !(await indexer.GetIncrementalStatusAsync( CancellationToken.None )).Reconciling );
            True( catalog.ApplyPathChangesCount > 0 );
            True( catalog.LastPathChanges.All( change =>
                string.Equals( change.Path, path, StringComparison.Ordinal ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IncrementalStatusReportsRootCoverageAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var watched = Path.Combine( root, "watched" );
        var missing = Path.Combine( root, "missing" );
        Directory.CreateDirectory( watched );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                watched,
                missing );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );

            var status =
                await indexer.GetIncrementalStatusAsync( CancellationToken.None );
            True( status.Started );
            Equal( 1, status.WatchedRootCount );
            True( status.UnwatchedRoots.Contains( missing, StringComparer.Ordinal ) );
            Equal( ContentExtractor.ExtractorVersion, status.ExtractorVersion );
            var missingRoot = status.Roots.Single( value =>
                string.Equals( value.Path, missing, StringComparison.Ordinal ) );
            True( !missingRoot.Watched );
            True( missingRoot.LastError?.Contains( "does not exist", StringComparison.Ordinal ) == true );
            True( !string.IsNullOrWhiteSpace( missingRoot.Remediation ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task ExtractorVersionChangeReconcilesAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var database = Path.Combine( root, "catalog.db" );
        var path = Path.Combine( documents, "versioned-note.txt" );
        var unaffectedPath = Path.Combine( documents, "unaffected-note.txt" );
        await File.WriteAllTextAsync( path, "extractor old marker" );
        await File.WriteAllTextAsync( unaffectedPath, "unaffected old marker" );

        try
        {
            var options = CreateIncrementalIndexOptions( database, documents );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using ( var first = new IndexCoordinator( catalog, catalog, options ) )
            {
                await first.ReindexAsync( progress: null, CancellationToken.None );
            }

            var statePath = database + ".index-state";
            var state = (await File.ReadAllTextAsync( statePath )).Split( '|' );
            Equal( 3, state.Length );
            state[2] = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes( "outdated-extractor" ) );
            await File.WriteAllTextAsync( statePath, string.Join( '|', state ) );
            await using ( Microsoft.Data.Sqlite.SqliteConnection connection = new(
                $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var command =
                    connection.CreateCommand( );
                command.CommandText = """
                    UPDATE catalog_items
                    SET extractor_version = 'outdated-extractor'
                    WHERE path = $path;
                    """;
                command.Parameters.AddWithValue( "$path", path );
                Equal( 1, await command.ExecuteNonQueryAsync( ) );
            }

            // Same sizes and timestamps keep restart change detection out of this
            // spec: only the extractor-version pass can reveal the new content.
            await RewritePreservingMetadataAsync( path, "extractor new marker" );
            await RewritePreservingMetadataAsync( unaffectedPath, "unaffected new marker" );

            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using ( var second = new IndexCoordinator( tracking, tracking, options ) )
            {
                await second.StartAsync( CancellationToken.None );
                True( await WaitForCatalogResultAsync(
                    catalog,
                    "extractor new",
                    result => result.Title == "versioned-note.txt",
                    TimeSpan.FromSeconds( 2 ) ) );
                // Search rows publish before the reconciliation checkpoint. Wait
                // for its public completion state before asserting that state.
                True( await WaitUntilAsync( async ( ) =>
                {
                    var current = await second.GetIncrementalStatusAsync( CancellationToken.None );
                    return !current.ReconciliationRequired &&
                        current.ExtractorVersion == ContentExtractor.ExtractorVersion;
                }, TimeSpan.FromSeconds( 2 ) ) );
                var status =
                    await second.GetIncrementalStatusAsync( CancellationToken.None );
                True( !status.ReconciliationRequired );
                Equal( ContentExtractor.ExtractorVersion, status.ExtractorVersion );
                Equal( 1, tracking.ApplyPathChangesCount );
                Equal( 1, tracking.LastPathChanges.Count );
                Equal( path, tracking.LastPathChanges[0].Path );
                True( !await WaitForCatalogResultAsync(
                    catalog,
                    "unaffected new",
                    result => result.Title == "unaffected-note.txt",
                    TimeSpan.FromMilliseconds( 150 ) ) );
                True( await WaitForCatalogResultAsync(
                    catalog,
                    "unaffected old",
                    result => result.Title == "unaffected-note.txt",
                    TimeSpan.FromSeconds( 1 ) ) );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

}
