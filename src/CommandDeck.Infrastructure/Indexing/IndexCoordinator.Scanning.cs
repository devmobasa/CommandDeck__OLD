using System.Runtime.CompilerServices;

namespace CommandDeck;

public sealed partial class IndexCoordinator
{

    // Internal counters must be updated before the scan commits. Progress<T>
    // posts callbacks asynchronously and can leave root status incomplete.
    private sealed class ScanProgress( Action<IndexProgress> report ) : IProgress<IndexProgress>
    {
        public void Report( IndexProgress value ) => report( value );
    }

    private async ValueTask<ReindexReport> ReindexCoreAsync(
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken )
    {
        var started = DateTimeOffset.UtcNow;
        var checkpoint = _incremental.CaptureCheckpoint( );
        Counter counter = new( );
        Dictionary<string, long> rootCounts = new( StringComparer.Ordinal );

        async IAsyncEnumerable<IndexedItem> Enumerate(
            [EnumeratorCancellation] CancellationToken token )
        {
            if ( _options.Indexing.IndexDesktopApplications )
            {
                await foreach ( var item in DesktopEntryScanner.ScanAsync( token ).ConfigureAwait( false ) )
                {
                    counter.Discovered++;
                    counter.Indexed++;
                    yield return item;
                }
            }

            if ( _options.Indexing.IndexOmarchyCommands )
            {
                await foreach ( var item in OmarchyMenuScanner.ScanAsync( token ).ConfigureAwait( false ) )
                {
                    counter.Discovered++;
                    counter.Indexed++;
                    yield return item;
                }
            }

            await foreach ( var item in _configured.ScanAsync( token ).ConfigureAwait( false ) )
            {
                counter.Discovered++;
                counter.Indexed++;
                yield return item;
            }

            ScanProgress fileProgress = new( value =>
            {
                if ( value.Stage == "root-complete" && value.RootPath is not null )
                {
                    rootCounts[value.RootPath] = value.Indexed;
                }
                counter.Discovered = Math.Max( counter.Discovered, value.Discovered );
                counter.Indexed = Math.Max( counter.Indexed, value.Indexed );
                counter.Skipped = Math.Max( counter.Skipped, value.Skipped );
                counter.Failed = Math.Max( counter.Failed, value.Failed );
                progress?.Report( value );
            } );

            await foreach ( var item in _files.ScanAsync( fileProgress, token ).ConfigureAwait( false ) )
            {
                counter.Indexed++;
                yield return item;
            }
        }

        progress?.Report( new IndexProgress { Stage = "begin" } );
        await _catalog.ReplaceGenerationAsync(
            Enumerate( cancellationToken ),
            _files.SnapshotDiagnostics,
            _files.SnapshotWorkItems,
            progress,
            cancellationToken ).ConfigureAwait( false );
        _incremental.CompleteFullScan( checkpoint );
        foreach ( (var root, var count) in rootCounts )
        {
            _incremental.RecordRootScan( root, count );
        }

        var completed = DateTimeOffset.UtcNow;
        progress?.Report( new IndexProgress
        {
            Stage = "complete",
            Discovered = counter.Discovered,
            Indexed = counter.Indexed,
            Skipped = counter.Skipped,
            Failed = counter.Failed
        } );

        return new ReindexReport
        {
            StartedAt = started,
            CompletedAt = completed,
            Discovered = counter.Discovered,
            Indexed = counter.Indexed,
            Skipped = counter.Skipped,
            Failed = counter.Failed,
            Scope = IndexReindexScopeKinds.Full
        };
    }

    private async ValueTask<bool> ApplyPathsAsync(
        IReadOnlyList<IncrementalPathRequest> paths,
        CancellationToken cancellationToken )
    {
        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _incremental.IsPausedUntilFullScan )
            {
                return false;
            }

            List<(IncrementalPathRequest Request, string ChangePath, bool IsDirectory)> batch = new( paths.Count );
            foreach ( var request in paths )
            {
                var isDirectory = Directory.Exists( request.Path );
                if ( _files.TryResolveChangePath( request.Path, out var changePath ) )
                {
                    batch.Add( (request, changePath, isDirectory) );
                }
            }

            await ApplyPathBatchAsync( batch, cancellationToken ).ConfigureAwait( false );
            await _catalog.ReplaceExclusionDiagnosticsAsync( _files.SnapshotDiagnostics( ), cancellationToken ).ConfigureAwait( false );
            return true;
        }
        finally
        {
            _indexLock.Release( );
        }
    }

    private async ValueTask ReconcileAsync(
        IReadOnlyList<string> affectedRoots,
        CancellationToken cancellationToken )
    {
        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _incremental.IsPausedUntilFullScan ||
                !_incremental.GetStatus( ).ReconciliationRequired )
            {
                return;
            }

            if ( affectedRoots.Count == 0 )
            {
                var currentStatus = _incremental.GetStatus( );
                if ( string.Equals(
                        currentStatus.ReconciliationReason,
                        "extractor-version-changed",
                        StringComparison.Ordinal ) )
                {
                    await ReindexStaleExtractorPathsAsync( cancellationToken ).ConfigureAwait( false );
                }
                else
                {
                    await ReindexCoreAsync( progress: null, cancellationToken ).ConfigureAwait( false );
                }

                return;
            }

            var status = _incremental.GetStatus( );
            foreach ( var rootPath in affectedRoots )
            {
                var root = status.Roots.SingleOrDefault( value =>
                    string.Equals( value.Path, rootPath, StringComparison.Ordinal ) );
                if ( root is null )
                {
                    continue;
                }

                await ReindexScopedCoreAsync(
                    new IndexReindexScope
                    {
                        Kind = IndexReindexScopeKinds.Root,
                        RootId = root.Id
                    },
                    progress: null,
                    cancellationToken ).ConfigureAwait( false );
            }
        }
        finally
        {
            _indexLock.Release( );
        }
    }

    private async ValueTask ReindexStaleExtractorPathsAsync(
        CancellationToken cancellationToken )
    {
        const int stalePathBatchSize = 256;
        while ( true )
        {
            var paths =
                await _catalog.GetPathsRequiringExtractorVersionAsync(
                    ContentExtractor.ExtractorVersion,
                    stalePathBatchSize,
                    cancellationToken ).ConfigureAwait( false );
            if ( paths.Count == 0 )
            {
                return;
            }

            List<IndexedPathChange> changes = new( paths.Count );
            foreach ( var path in paths )
            {
                var items = await _files.IndexPathAsync(
                    path,
                    cancellationToken ).ConfigureAwait( false );
                changes.Add( new IndexedPathChange
                {
                    Path = path,
                    Items = items
                } );
            }

            await _catalog.ApplyPathChangesAsync( changes, cancellationToken ).ConfigureAwait( false );
        }
    }

    private async ValueTask<T> RunExclusiveAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await operation( cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _indexLock.Release( );
        }
    }

    private async ValueTask<T> RunMutationExclusiveAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        await StartAsync( cancellationToken ).ConfigureAwait( false );
        EnsureCatalogMutationOwnership( );
        return await RunExclusiveAsync( operation, cancellationToken ).ConfigureAwait( false );
    }

    private void EnsureCatalogMutationOwnership( )
    {
        if ( !_incremental.CanMutateCatalog )
        {
            throw new InvalidOperationException(
                "Catalog mutation is owned by another CommandDeck runtime. " +
                "Use the running daemon or stop it before running this command." );
        }
    }

    private sealed class Counter
    {
        public long Discovered;
        public long Indexed;
        public long Skipped;
        public long Failed;
    }
}
