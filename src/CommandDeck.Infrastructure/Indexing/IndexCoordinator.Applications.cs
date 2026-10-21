namespace CommandDeck;

// Desktop entries live outside the index roots, so the catalog owner watches the
// XDG application directories itself. Any change there rescans every directory
// and replaces the application rows as one set: modification times play no part,
// so a package file with an old timestamp, a rename into place, a removal and a
// user entry that shadows a system one all apply the same way. The owner also
// rescans once at start for packages changed while it was stopped.
public sealed partial class IndexCoordinator
{
    private readonly List<FileSystemWatcher> _applicationWatchers = [];
    private bool _applicationWatchingStarted;
    private bool _applicationsChanged;
    private Task? _applicationRefresh;

    // Specs await this to observe a pending application refresh.
    internal Task ApplicationRefresh
    {
        get
        {
            lock ( _startLock )
            {
                return _applicationRefresh ?? Task.CompletedTask;
            }
        }
    }

    // A directory created or replaced after start is watched after a restart.
    private void StartApplicationWatching( )
    {
        if ( !_options.Indexing.IndexDesktopApplications || !_options.Indexing.EnableIncrementalIndexing ||
            !_incremental.CanMutateCatalog )
        {
            return;
        }

        lock ( _startLock )
        {
            if ( _applicationWatchingStarted )
            {
                return;
            }

            _applicationWatchingStarted = true;
        }

        List<FileSystemWatcher> watchers = [];
        foreach ( var directory in DesktopEntryScanner.GetApplicationDirectories( )
            .Distinct( StringComparer.Ordinal ).Where( Directory.Exists ) )
        {
            try
            {
                FileSystemWatcher watcher = new( directory )
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite | NotifyFilters.Attributes
                };
                watcher.Created += ( _, _ ) => RequestApplicationRefresh( );
                watcher.Changed += ( _, _ ) => RequestApplicationRefresh( );
                watcher.Deleted += ( _, _ ) => RequestApplicationRefresh( );
                watcher.Renamed += ( _, _ ) => RequestApplicationRefresh( );
                watcher.Error += ( _, _ ) => RequestApplicationRefresh( );
                watcher.EnableRaisingEvents = true;
                watchers.Add( watcher );
            }
            catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException )
            {
                ReportDiagnostic( $"event=index.applications.watch_failed error_type={exception.GetType( ).Name}",
                    IndexDiagnosticSeverity.Warning );
            }
        }

        // Disposal stays outside the lock that the watchers' own events take.
        bool stopped;
        lock ( _startLock )
        {
            stopped = _lifetime.IsCancellationRequested;
            if ( !stopped )
            {
                _applicationWatchers.AddRange( watchers );
            }
        }

        if ( stopped )
        {
            watchers.ForEach( static watcher => watcher.Dispose( ) );
            return;
        }

        RequestApplicationRefresh( );
    }

    private void RequestApplicationRefresh( )
    {
        lock ( _startLock )
        {
            _applicationsChanged = true;
            if ( _applicationRefresh is not null || _lifetime.IsCancellationRequested )
            {
                return;
            }

            _applicationRefresh = Task.Run( ( ) => RefreshApplicationsAsync( _lifetime.Token ), CancellationToken.None );
        }
    }

    // Waits out a burst of package changes, then rescans until no change
    // arrived during the last pass.
    private async Task RefreshApplicationsAsync( CancellationToken cancellationToken )
    {
        var debounce = TimeSpan.FromMilliseconds( _options.Indexing.IncrementalDebounceMilliseconds );
        try
        {
            while ( true )
            {
                await Task.Delay( debounce, cancellationToken ).ConfigureAwait( false );
                lock ( _startLock )
                {
                    if ( !_applicationsChanged )
                    {
                        _applicationRefresh = null;
                        return;
                    }

                    _applicationsChanged = false;
                }

                try
                {
                    await PublishApplicationsAsync( cancellationToken ).ConfigureAwait( false );
                }
                catch ( Exception exception ) when ( !cancellationToken.IsCancellationRequested )
                {
                    ReportDiagnostic( $"event=index.applications.failed error_type={exception.GetType( ).Name}",
                        IndexDiagnosticSeverity.Warning );
                }
            }
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
        }
    }

    // A full scan publishes the applications too. The refresh waits for one that
    // is running and leaves a required one, such as after a purge, to do the work.
    private async ValueTask PublishApplicationsAsync( CancellationToken cancellationToken )
    {
        await _fullScanLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
            try
            {
                if ( _incremental.IsPausedUntilFullScan ||
                    await _catalog.RequiresReindexAsync( cancellationToken ).ConfigureAwait( false ) )
                {
                    ReportDiagnostic( "event=index.applications.skipped reason=full-scan-required" );
                    return;
                }

                List<IndexedItem> items = [];
                await foreach ( var item in DesktopEntryScanner.ScanAsync( cancellationToken ).ConfigureAwait( false ) )
                {
                    items.Add( item );
                }

                await _catalog.ReplaceApplicationsAsync( items, cancellationToken ).ConfigureAwait( false );
                ReportDiagnostic( $"event=index.applications.refreshed items={items.Count}" );
            }
            finally
            {
                _indexLock.Release( );
            }
        }
        finally
        {
            _fullScanLock.Release( );
        }
    }

    private async ValueTask StopApplicationWatchingAsync( )
    {
        List<FileSystemWatcher> retired;
        Task? refresh;
        lock ( _startLock )
        {
            retired = [.. _applicationWatchers];
            _applicationWatchers.Clear( );
            refresh = _applicationRefresh;
        }

        foreach ( var watcher in retired )
        {
            watcher.Dispose( );
        }

        if ( refresh is not null )
        {
            await refresh.ConfigureAwait( false );
        }
    }
}
