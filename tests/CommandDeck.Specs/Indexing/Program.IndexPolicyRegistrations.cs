namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunIndexPolicyRegistrationsAsync( )
    {
        await RunAsync( "directory renames preserve destinations and moved ignore rules",
            RenamingDirectoriesWithIgnoreInputsPreservesResultsAsync );
        await RunAsync( "previews and AI attachments obey the most specific index root",
            PreviewsUseTheMostSpecificIndexRootAsync );
        await RunAsync( "ordinary edits preserve policy snapshots before and after restart",
            OrdinaryEditsDoNotRewritePolicySnapshotsAsync );
        Run( "root inclusion and exclusion patterns reject null and empty entries", ( ) =>
        {
            foreach ( var patterns in new List<string>[] { null!, [null!], [""], ["bad\0path"] } )
            {
                for ( var inclusion = 0; inclusion < 2; inclusion++ )
                {
                    CommandDeckOptions options = new( )
                    {
                        Indexing = new IndexingOptions
                        {
                            Roots = [new IndexRootOptions
                            {
                                Path = "/tmp",
                                IncludeGlobs = inclusion == 0 ? patterns : [],
                                ExcludeGlobs = inclusion == 0 ? [] : patterns
                            }]
                        }
                    };
                    Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( options ) );
                }
            }
        } );
        await RunAsync( "nested roots and symlink aliases preserve exclusion policy across scan modes",
            NestedRootsAndSymlinksPreserveExclusionPolicyAsync );
        Run( "star-only privacy globs match like the runtime matcher",
            StarOnlyPrivacyGlobsMatchLikeTheRuntimeMatcher );
        await RunAsync( "traversal checks each directory once and agrees with the full policy at every depth",
            TraversalPolicyAgreesWithFullPolicyAtEveryDepthAsync );
        await RunAsync( "symlink escapes stay outside the index at every depth",
            SymlinkEscapesStayOutsideAtEveryDepthAsync );
        await RunAsync( "a directory replaced during a scan emits nothing from outside the root",
            ReplacedDirectoryDuringScanEmitsNothingOutsideAsync );
        await RunAsync( "a directory replaced during a scan cannot reach content extraction",
            ReplacedDirectoryCannotReachContentExtractorAsync );
        await RunAsync( "native timestamps outside DateTime range remain scannable",
            NativeTimestampsOutsideDateTimeRangeRemainScannableAsync );
        await RunAsync( "active traversal observes changed ignore rules",
            ActiveTraversalObservesChangedIgnoreRulesAsync );
        await RunAsync( "a cached root boundary follows a retargeted root",
            CachedRootBoundaryFollowsARetargetedRootAsync );
        await RunAsync( "moved directories take the policy of their destination",
            MovedDirectoriesTakeTheirDestinationPolicyAsync );
        await RunAsync( "scan allocation does not grow with the number of ancestors",
            ScanAllocationDoesNotGrowWithAncestorCountAsync );
        await RunAsync(
            "generated event bursts are filtered before journaling",
            GeneratedFileBurstsDoNotReachTheJournalAsync );
        await RunAsync(
            "repository ignore edits reconcile stale rows and preserve explicit inclusions",
            RepositoryIgnoreEditsReconcileExistingRowsAsync );
        await RunAsync(
            "project marker changes and renames across exclusions converge",
            ProjectMarkerChangesAndExcludedRenamesConvergeAsync );
        await RunAsync(
            "configuration and ignore changes made while stopped reconcile the catalog",
            ExclusionChangesWhileStoppedAreReconciledAsync );
        await RunAsync(
            "background policy reconciliation failure pauses indexing and warns",
            ReconciliationFailurePausesIndexingAndWarnsAsync );
        await RunAsync(
            "full reindex does not queue a second background reconciliation",
            FullReindexDoesNotQueueBackgroundReconciliationAsync );
        await RunAsync(
            "dispose while reconciliation waits for the index lock completes",
            DisposeWhileReconciliationWaitsForTheIndexLockAsync );
        await RunAsync(
            "ancestor repository ignore changes are watched outside the configured root",
            AncestorIgnoreChangesAreWatchedOutsideTheIndexRootAsync );
        await RunAsync(
            "repository ignore patterns agree with Git wildmatch semantics",
            RepositoryIgnorePatternsAgreeWithGitAsync );
        await RunAsync(
            "nested repository ignores tracked files and explicit overrides are consistent",
            NestedRepositoryIgnoresAndOverridesAreConsistentAsync );
        await RunAsync(
            "developer directory defaults exclude generated trees only in their project scope",
            DeveloperDirectoryDefaultsAreProjectScopedAsync );
        await RunAsync(
            "directory exclusions agree across direct updates scoped scans and previews",
            DirectoryExclusionsApplyToDirectUpdatesAndPreviewsAsync );
        await RunAsync(
            "file type policies separate filename metadata and content search",
            FileTypePoliciesSeparateSearchFieldsAsync );
        await RunAsync(
            "failed staged generation keeps the known-good index for readers and reopen",
            FailedGenerationPreservesKnownGoodCatalogAsync );
        await RunAsync(
            "generation staging keeps readers and other writers available",
            GenerationStagingKeepsReadersAndWritersAvailableAsync );
        await RunAsync(
            "generation activation truncates the WAL and keeps results and connections unchanged",
            GenerationActivationTruncatesWalAndKeepsResultsAsync );
        await RunAsync(
            "activation checkpoint does not make writers wait for a reader's busy timeout",
            ActivationCheckpointDoesNotWaitForReadersAsync );
        await RunAsync(
            "file type extraction limits override global input and result bounds",
            FileTypeExtractionLimitsOverrideDefaultsAsync );
        await RunAsync(
            "hostile extractors are bounded and receive a restricted environment",
            HostileExtractorsAreIsolatedAsync );
        await RunAsync(
            "special files cannot block content extraction",
            SpecialFilesCannotBlockContentExtractionAsync );
        await RunAsync(
            "archive extraction is searchable bounded and traversal safe",
            ArchiveExtractionIsBoundedAsync );

        Run( "file type policy configuration requires filename globs", ( ) =>
        {
            CommandDeckOptions options = new( )
            {
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = "/tmp",
                            FileTypePolicies =
                            [
                                new FileTypeIndexPolicyOptions
                                {
                                    Pattern = "nested/*.txt"
                                }
                            ]
                        }
                    ]
                }
            };

            Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( options ) );
        } );

        Run( "file type extraction limits must be positive", ( ) =>
        {
            CommandDeckOptions options = new( )
            {
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = "/tmp",
                            FileTypePolicies =
                            [
                                new FileTypeIndexPolicyOptions
                                {
                                    Pattern = "*.pdf",
                                    MaximumExtractedBytes = 0
                                }
                            ]
                        }
                    ]
                }
            };
            Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( options ) );
        } );

        Run( "scheduled maintenance interval is bounded", ( ) =>
        {
            CommandDeckOptions options = new( )
            {
                Indexing = new IndexingOptions
                {
                    Roots = [],
                    MaintenanceIntervalMinutes = 0
                }
            };
            Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( options ) );
        } );
    }
}
