namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class TrackingSearchCatalog( ISearchCatalog inner, ICatalogOperations operations ) :
        ISearchCatalog,
        ICatalogOperations
    {
        private TaskCompletionSource? _replacementPaused;
        private TaskCompletionSource? _replacementReleased;
        private Func<IndexedItem, bool>? _pausePredicate;

        public int ReplaceAllCount { get; private set; }
        public int ApplyPathChangesCount { get; private set; }
        public IReadOnlyList<IndexedPathChange> LastPathChanges { get; private set; } = [];

        // When set, every replacement throws it after counting the attempt.
        public Exception? ReplacementFailure { get; init; }
        public Func<string, Exception?>? IndexedFileStateFailure { get; init; }

        public (Task FirstItem, Action Release) PauseNextReplacement( Func<IndexedItem, bool>? predicate = null )
        {
            _pausePredicate = predicate;
            _replacementPaused = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously );
            _replacementReleased = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously );
            return (_replacementPaused.Task, ( ) => _replacementReleased.TrySetResult( ));
        }

        public ValueTask InitializeAsync( CancellationToken cancellationToken ) =>
            inner.InitializeAsync( cancellationToken );

        public async ValueTask ReplaceAllAsync(
            IAsyncEnumerable<IndexedItem> items,
            IProgress<IndexProgress>? progress,
            CancellationToken cancellationToken )
        {
            ReplaceAllCount++;
            ThrowConfiguredFailure( );

            await inner.ReplaceAllAsync(
                PauseAfterFirstItemAsync( items, cancellationToken ),
                progress,
                cancellationToken );
        }

        public async ValueTask ReplaceGenerationAsync(
            IAsyncEnumerable<IndexedItem> items,
            Func<IReadOnlyList<IndexExclusionDiagnostic>> diagnostics,
            IProgress<IndexProgress>? progress,
            CancellationToken cancellationToken )
        {
            ReplaceAllCount++;
            ThrowConfiguredFailure( );

            await inner.ReplaceGenerationAsync(
                PauseAfterFirstItemAsync( items, cancellationToken ),
                diagnostics,
                progress,
                cancellationToken );
        }

        private void ThrowConfiguredFailure( )
        {
            if ( ReplacementFailure is { } failure )
            {
                throw failure;
            }
        }

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            int limit,
            CancellationToken cancellationToken ) =>
            inner.SearchAsync( query, limit, cancellationToken );

        public ValueTask<IReadOnlyList<SearchResult>> SearchSourcesAsync(
            ParsedQuery query,
            IReadOnlyList<string> sourceIds,
            int limit,
            CancellationToken cancellationToken ) =>
            inner.SearchSourcesAsync( query, sourceIds, limit, cancellationToken );

        public ValueTask<CatalogStats> GetStatsAsync( CancellationToken cancellationToken ) =>
            inner.GetStatsAsync( cancellationToken );

        public ValueTask<bool> VerifyFts5Async( CancellationToken cancellationToken ) =>
            inner.VerifyFts5Async( cancellationToken );

        public ValueTask<bool> RequiresReindexAsync( CancellationToken cancellationToken ) =>
            inner.RequiresReindexAsync( cancellationToken );

        public ValueTask ApplyPathChangesAsync(
            IReadOnlyList<IndexedPathChange> changes,
            CancellationToken cancellationToken )
        {
            ApplyPathChangesCount++;
            LastPathChanges = [.. changes];
            return inner.ApplyPathChangesAsync( changes, cancellationToken );
        }

        public ValueTask ReplaceApplicationsAsync(
            IReadOnlyList<IndexedItem> items,
            CancellationToken cancellationToken ) =>
            inner.ReplaceApplicationsAsync( items, cancellationToken );

        public ValueTask<IReadOnlyList<string>> GetPathsRequiringExtractorVersionAsync(
            string extractorVersion,
            int limit,
            CancellationToken cancellationToken ) =>
            inner.GetPathsRequiringExtractorVersionAsync(
                extractorVersion,
                limit,
                cancellationToken );

        public ValueTask ReplaceExclusionDiagnosticsAsync(
            IReadOnlyList<IndexExclusionDiagnostic> diagnostics,
            CancellationToken cancellationToken ) =>
            inner.ReplaceExclusionDiagnosticsAsync( diagnostics, cancellationToken );

        public ValueTask<IReadOnlyList<SearchResult>> GetHomeResultsAsync(
            LauncherPreferences preferences,
            int limit,
            CancellationToken cancellationToken ) =>
            inner.GetHomeResultsAsync( preferences, limit, cancellationToken );

        public ValueTask<CatalogInspection> InspectAsync( CancellationToken cancellationToken ) =>
            operations.InspectAsync( cancellationToken );

        public ValueTask<CatalogInspection> InspectAsync( int workItemOffset, int workItemLimit, CancellationToken cancellationToken ) =>
            operations.InspectAsync( workItemOffset, workItemLimit, cancellationToken );

        public ValueTask<IReadOnlyList<string>> GetIndexedPathsAfterAsync( string? afterPath, int limit, CancellationToken cancellationToken ) =>
            inner.GetIndexedPathsAfterAsync( afterPath, limit, cancellationToken );

        public ValueTask<IReadOnlyList<IndexedFileState>> GetIndexedFileStatesAsync( string rootPath, CancellationToken cancellationToken )
        {
            var failure = IndexedFileStateFailure?.Invoke( rootPath );
            return failure is null
                ? inner.GetIndexedFileStatesAsync( rootPath, cancellationToken )
                : ValueTask.FromException<IReadOnlyList<IndexedFileState>>( failure );
        }

        public async ValueTask ReplaceGenerationAsync( IAsyncEnumerable<IndexedItem> items,
            Func<IReadOnlyList<IndexExclusionDiagnostic>> diagnostics, Func<IReadOnlyList<IndexWorkItem>> workItems,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken )
        {
            ReplaceAllCount++;
            ThrowConfiguredFailure( );

            await inner.ReplaceGenerationAsync( PauseAfterFirstItemAsync( items, cancellationToken ), diagnostics, workItems, progress, cancellationToken );
        }

        public ValueTask<PrivacyAuditResult> AuditPrivacyAsync(
            CancellationToken cancellationToken ) =>
            operations.AuditPrivacyAsync( cancellationToken );

        public ValueTask<CatalogMaintenanceResult> CheckIntegrityAsync(
            CancellationToken cancellationToken ) =>
            operations.CheckIntegrityAsync( cancellationToken );

        public ValueTask<CatalogMaintenanceResult> OptimizeAsync(
            CancellationToken cancellationToken ) =>
            operations.OptimizeAsync( cancellationToken );

        public ValueTask<CatalogMaintenanceResult> RepairAsync(
            CancellationToken cancellationToken ) =>
            operations.RepairAsync( cancellationToken );

        public ValueTask<CatalogMaintenanceResult> PurgeProjectionAsync(
            CancellationToken cancellationToken ) =>
            operations.PurgeProjectionAsync( cancellationToken );

        public ValueTask DisposeAsync( ) => inner.DisposeAsync( );

        private async IAsyncEnumerable<IndexedItem> PauseAfterFirstItemAsync(
            IAsyncEnumerable<IndexedItem> items,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken )
        {
            var first = true;
            await foreach ( var item in items.WithCancellation( cancellationToken ) )
            {
                yield return item;
                if ( first && _replacementPaused is not null && _replacementReleased is not null && (_pausePredicate?.Invoke( item ) ?? true) )
                {
                    first = false;
                    _replacementPaused.TrySetResult( );
                    await _replacementReleased.Task.WaitAsync( cancellationToken );
                }
            }
        }
    }
}
