using System.Diagnostics;

namespace CommandDeck;

// Watchers only see changes made while this runtime is alive. On start, the
// journal owner compares each previously scanned root with the size and
// modification time recorded in the catalog and applies only the difference.
public sealed partial class IndexCoordinator
{
    private const int OfflineChangeBatchSize = 256;
    private CancellationTokenSource? _offlineChangesCancellation;
    private Task? _offlineChangesTask;
    private bool _offlineChangesSuperseded;

    private void StartOfflineChangeReconciliation( )
    {
        if ( !_options.Indexing.EnableIncrementalIndexing || !_incremental.CanMutateCatalog )
        {
            return;
        }

        lock ( _startLock )
        {
            if ( _offlineChangesSuperseded || _offlineChangesTask is not null )
            {
                return;
            }

            _offlineChangesCancellation =
                CancellationTokenSource.CreateLinkedTokenSource( _lifetime.Token );
            var token = _offlineChangesCancellation.Token;
            _offlineChangesTask = Task.Run(
                ( ) => ReconcileOfflineChangesAsync( token ),
                CancellationToken.None );
        }
    }

    // A full scan observes every offline change, so it replaces this pass.
    private void SupersedeOfflineChangeReconciliation( )
    {
        lock ( _startLock )
        {
            _offlineChangesSuperseded = true;
            _offlineChangesCancellation?.Cancel( );
        }
    }

    // Runs off the startup path. A failed pass reports a warning and marks its
    // root as overflowed so the incremental engine reconciles that root.
    private async Task ReconcileOfflineChangesAsync( CancellationToken cancellationToken )
    {
        var locked = false;
        try
        {
            await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
            locked = true;

            if ( _incremental.IsPausedUntilFullScan || _incremental.PolicyReconciliationRequired )
            {
                return;
            }

            var elapsed = Stopwatch.StartNew( );
            long changed = 0;
            var roots = _incremental.GetStatus( ).Roots
                .Where( static root => root.Watched && !root.Paused && root.LastSuccessAt is not null )
                .Select( static root => root.Path )
                .ToArray( );
            foreach ( var root in roots )
            {
                try
                {
                    changed += await ReconcileOfflineRootAsync( root, cancellationToken )
                        .ConfigureAwait( false );
                }
                catch ( Exception exception ) when ( !cancellationToken.IsCancellationRequested )
                {
                    ReportDiagnostic(
                        $"event=index.offline_changes.root_failed error_type={exception.GetType( ).Name}",
                        IndexDiagnosticSeverity.Warning );
                    _incremental.ReportWatcherOverflow( root );
                }
            }

            elapsed.Stop( );
            ReportDiagnostic(
                $"event=index.offline_changes.complete roots={roots.Length} changed={changed} " +
                $"duration_ms={( long ) elapsed.Elapsed.TotalMilliseconds}" );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
        }
        catch ( Exception exception )
        {
            ReportDiagnostic(
                $"event=index.offline_changes.failed error_type={exception.GetType( ).Name}",
                IndexDiagnosticSeverity.Warning );
        }
        finally
        {
            if ( locked )
            {
                _indexLock.Release( );
            }
        }
    }

    private async ValueTask<long> ReconcileOfflineRootAsync(
        string root,
        CancellationToken cancellationToken )
    {
        // Catalog rows use canonical paths, which differ from a symlinked root.
        if ( !_files.TryResolveChangePath( root, out var canonicalRoot ) )
        {
            return 0;
        }

        var indexed = (await _catalog.GetIndexedFileStatesAsync( canonicalRoot, cancellationToken )
                .ConfigureAwait( false ))
            .ToDictionary( static state => state.Path, StringComparer.Ordinal );
        HashSet<string> visited = new( StringComparer.Ordinal );
        List<IndexedPathChange> changes = [];
        long changed = 0;

        foreach ( var path in _files.EnumerateDirectoryFiles( root, cancellationToken ) )
        {
            if ( indexed.TryGetValue( path, out var state ) && MatchesDisk( state ) )
            {
                visited.Add( path );
                continue;
            }

            var items = await _files.IndexPathAsync( path, cancellationToken ).ConfigureAwait( false );
            if ( !_files.TryResolveChangePath( path, out var canonicalPath ) )
            {
                continue;
            }

            visited.Add( canonicalPath );
            if ( items.Count == 0 && !indexed.ContainsKey( canonicalPath ) )
            {
                continue;
            }

            changes.Add( new IndexedPathChange { Path = canonicalPath, Items = items } );
            changed += await FlushOfflineChangesAsync( changes, OfflineChangeBatchSize, cancellationToken )
                .ConfigureAwait( false );
        }

        foreach ( var path in indexed.Keys )
        {
            if ( visited.Contains( path ) || File.Exists( path ) || Directory.Exists( path ) )
            {
                continue;
            }

            changes.Add( new IndexedPathChange { Path = path, RemoveDescendants = true } );
            changed += await FlushOfflineChangesAsync( changes, OfflineChangeBatchSize, cancellationToken )
                .ConfigureAwait( false );
        }

        return changed + await FlushOfflineChangesAsync( changes, 1, cancellationToken )
            .ConfigureAwait( false );
    }

    private async ValueTask<int> FlushOfflineChangesAsync(
        List<IndexedPathChange> changes,
        int threshold,
        CancellationToken cancellationToken )
    {
        if ( changes.Count < threshold )
        {
            return 0;
        }

        var count = changes.Count;
        await _catalog.ApplyPathChangesAsync( [.. changes], cancellationToken ).ConfigureAwait( false );
        changes.Clear( );

        return count;
    }

    private static bool MatchesDisk( IndexedFileState state )
    {
        try
        {
            FileInfo info = new( state.Path );

            return info.Exists &&
                info.Length == state.SizeBytes &&
                new DateTimeOffset( info.LastWriteTimeUtc ).ToUnixTimeMilliseconds( ) ==
                    state.ModifiedUnixMilliseconds;
        }
        catch ( Exception exception ) when (
            exception is UnauthorizedAccessException or IOException )
        {
            return false;
        }
    }
}
