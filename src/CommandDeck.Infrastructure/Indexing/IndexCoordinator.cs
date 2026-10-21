using System.Diagnostics;

namespace CommandDeck;

public sealed partial class IndexCoordinator : IIndexCoordinator
{
    private readonly ICatalogProjectionStore _catalog;
    private readonly ICatalogOperations _catalogOperations;
    private readonly CommandDeckOptions _options;
    private FileIndexSource _files;
    private readonly ConfiguredItemSource _configured;
    private readonly IncrementalIndexEngine _incremental;
    private readonly SemaphoreSlim _indexLock = new( 1, 1 );
    private readonly Action<IndexDiagnostic>? _diagnostic;
    private bool _disposed;
    private bool _disabledPolicyChecked;

    public IndexCoordinator(
        ICatalogProjectionStore catalog,
        ICatalogOperations catalogOperations,
        CommandDeckOptions options,
        TimeProvider? timeProvider = null,
        Action<IndexDiagnostic>? diagnostic = null,
        ILauncherPreferenceStore? rootPreferences = null )
    {
        _catalog = catalog;
        _catalogOperations = catalogOperations;
        _options = options;
        _configuredRoots = options.Indexing.Roots.Select( static root =>
            root with { Path = IndexRootPaths.Normalize( root.Path ) } ).ToList( );
        _rootPreferences = rootPreferences;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _diagnostic = diagnostic;
        _files = new FileIndexSource( options.Indexing with { Roots = [.. options.Indexing.Roots] }, options.Privacy, options.Storage.DatabasePath );
        _configured = new ConfiguredItemSource( options );
        _maintenanceStateStore = new IndexMaintenanceStateStore( options.Storage.DatabasePath );
        RestoreMaintenanceState( _maintenanceStateStore.Read( ) );
        _incremental = new IncrementalIndexEngine(
            options,
            _files.Exclusions,
            ApplyPathsAsync,
            ReconcileAsync );
    }

    public ValueTask StartAsync( CancellationToken cancellationToken ) =>
        StartCoreAsync( reconcilePolicy: true, cancellationToken );

    // Specs await this to observe the background policy reconciliation.
    internal Task PolicyReconciliation => _disabledPolicyTask ?? Task.CompletedTask;

    private async ValueTask StartCoreAsync( bool reconcilePolicy, CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );

        await EnsureRootPreferencesAsync( cancellationToken ).ConfigureAwait( false );
        await _incremental.StartAsync( cancellationToken ).ConfigureAwait( false );
        if ( reconcilePolicy && _reindexPending )
        {
            await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
            try
            {
                StartBackgroundReindex( );
            }
            finally
            {
                _indexLock.Release( );
            }
        }

        if ( reconcilePolicy )
        {
            StartDisabledPolicyReconciliation( );
        }

        StartOfflineChangeReconciliation( );

        if ( !_options.Indexing.EnableScheduledMaintenance )
        {
            return;
        }

        EnsureMaintenanceScheduled( );
        lock ( _startLock )
        {
            _maintenanceTask ??= Task.Run(
                ( ) => RunMaintenanceLoopAsync( _lifetime.Token ),
                CancellationToken.None );
        }
    }

    private void StartDisabledPolicyReconciliation( )
    {
        if ( _options.Indexing.EnableIncrementalIndexing || !_incremental.CanMutateCatalog )
        {
            return;
        }

        lock ( _startLock )
        {
            _disabledPolicyTask ??= Task.Run(
                ( ) => EnsureDisabledPolicyCurrentAsync( _lifetime.Token ),
                CancellationToken.None );
        }
    }

    // Runs off the startup path. Failures pause incremental indexing until the next
    // full scan and surface as a warning diagnostic; they never fault the task, so
    // disposal can always await it.
    private async Task EnsureDisabledPolicyCurrentAsync( CancellationToken cancellationToken )
    {
        if ( _options.Indexing.EnableIncrementalIndexing || _disabledPolicyChecked || !_incremental.CanMutateCatalog )
        {
            return;
        }

        var locked = false;
        try
        {
            await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
            locked = true;

            if ( _disabledPolicyChecked )
            {
                return;
            }

            if ( !_reindexPending && _incremental.PolicyReconciliationRequired &&
                            (await _catalog.GetStatsAsync( cancellationToken ).ConfigureAwait( false )).ItemCount > 0 )
            {
                var elapsed = Stopwatch.StartNew( );
                ReportDiagnostic( "event=index.reconciliation.start mode=background reason=policy-changed" );

                await ReindexCoreAsync( progress: null, cancellationToken ).ConfigureAwait( false );

                elapsed.Stop( );
                ReportDiagnostic(
                    $"event=index.reconciliation.complete mode=background duration_ms={( long ) elapsed.Elapsed.TotalMilliseconds}" );
            }

            _disabledPolicyChecked = true;
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
        }
        catch ( Exception exception )
        {
            _incremental.PauseUntilFullScan( "index-policy-reconciliation-failed" );
            ReportDiagnostic(
                $"event=index.reconciliation.failed mode=background error_type={exception.GetType( ).Name}",
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

    private void ReportDiagnostic(
        string message,
        IndexDiagnosticSeverity severity = IndexDiagnosticSeverity.Info ) =>
        _diagnostic?.Invoke( new IndexDiagnostic { Message = message, Severity = severity } );

    public async ValueTask<ReindexReport> ReindexAsync(
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken ) => await ReindexAsync(
            new IndexReindexScope( ),
            progress,
            cancellationToken ).ConfigureAwait( false );

    public async ValueTask<ReindexReport> ReindexAsync(
        IndexReindexScope scope,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ObjectDisposedException.ThrowIf( _disposed, this );

        // A full reindex already brings the policy current, so it must not queue a
        // background reconciliation that would run the same scan first.
        var full = scope.Kind == IndexReindexScopeKinds.Full;
        if ( full )
        {
            SupersedeOfflineChangeReconciliation( );
        }

        // Preferences load under the catalog gate before startup can own it.
        await EnsureRootPreferencesAsync( cancellationToken ).ConfigureAwait( false );
        if ( !full )
        {
            await StartCoreAsync( reconcilePolicy: true, cancellationToken ).ConfigureAwait( false );
        }

        // Own the catalog before starting the worker for a requested full scan.
        // Its startup reconciliation must not publish a redundant generation.
        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        ReindexReport report;
        try
        {
            if ( full )
            {
                await StartCoreAsync( reconcilePolicy: false, cancellationToken ).ConfigureAwait( false );
            }
            EnsureCatalogMutationOwnership( );
            if ( !full && scope.Kind == IndexReindexScopeKinds.Root && !string.IsNullOrWhiteSpace( scope.RootId ) )
            {
                full = _incremental.RestoreWatchers( scope.RootId, cancellationToken: cancellationToken );
            }

            if ( !full )
            {
                report = await ReindexScopedCoreAsync( scope, progress, cancellationToken ).ConfigureAwait( false );
            }
            else
            {
                report = await ReindexCoreAsync( progress, cancellationToken ).ConfigureAwait( false );
                _disabledPolicyChecked = true;
            }
        }
        finally
        {
            StartBackgroundReindex( );
            _indexLock.Release( );
        }

        foreach ( var root in _options.Indexing.Roots )
        {
            _incremental.RetryUnwatchedDirectories( IndexRootPaths.Normalize( root.Path ), cancellationToken );
        }
        return report;
    }

    private async ValueTask<ReindexReport> ReindexScopedCoreAsync(
        IndexReindexScope scope,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken )
    {
        var started = DateTimeOffset.UtcNow;
        var path = ResolveScopePath( scope );
        var directory = scope.Kind is IndexReindexScopeKinds.Directory or
            IndexReindexScopeKinds.Root;
        if ( !_files.TryResolveChangePath( path, out var canonicalPath ) )
        {
            throw new InvalidOperationException(
                "The reindex target must be inside one configured index root." );
        }

        CancelFullScan( );

        progress?.Report( new IndexProgress
        {
            Stage = "scope-begin",
            RootPath = scope.Kind == IndexReindexScopeKinds.Root ? canonicalPath : null,
            CurrentPath = canonicalPath
        } );

        long indexed;
        if ( directory )
        {
            indexed = await ReplaceDirectoryAsync( path, canonicalPath, cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            var items = await _files.IndexPathAsync( path, cancellationToken ).ConfigureAwait( false );
            await _catalog.ApplyPathChangesAsync(
            [
                new IndexedPathChange
                {
                    Path = canonicalPath,
                    Items = items
                }
            ], cancellationToken ).ConfigureAwait( false );
            indexed = items.Count;
        }

        await _catalog.ReplaceExclusionDiagnosticsAsync( _files.SnapshotDiagnostics( ), cancellationToken ).ConfigureAwait( false );
        if ( scope.Kind == IndexReindexScopeKinds.Root )
        {
            _incremental.RecordRootScan( canonicalPath, indexed );
        }

        var completed = DateTimeOffset.UtcNow;
        progress?.Report( new IndexProgress
        {
            Stage = "scope-complete",
            Indexed = indexed,
            CurrentPath = canonicalPath
        } );
        return new ReindexReport
        {
            StartedAt = started,
            CompletedAt = completed,
            Discovered = indexed,
            Indexed = indexed,
            Scope = scope.Kind,
            Target = canonicalPath
        };
    }

    private string ResolveScopePath( IndexReindexScope scope )
    {
        if ( scope.Kind == IndexReindexScopeKinds.Root )
        {
            if ( string.IsNullOrWhiteSpace( scope.RootId ) )
            {
                throw new InvalidOperationException( "A root reindex requires rootId." );
            }

            var result2 = _incremental.GetStatus( ).Roots
                .SingleOrDefault( root => root.Id == scope.RootId )?.Path;
            if ( result2 is null )
            {
                throw new InvalidOperationException( $"Unknown index root '{scope.RootId}'." );
            }

            return result2;
        }

        if ( scope.Kind is not (IndexReindexScopeKinds.File or
            IndexReindexScopeKinds.Directory) || string.IsNullOrWhiteSpace( scope.Path ) )
        {
            throw new InvalidOperationException(
                "Reindex scope must be full, root, directory, or file with its required target." );
        }

        return Path.GetFullPath( PathExpansion.Expand( scope.Path ) );
    }

    public ValueTask<IncrementalIndexStatus> GetIncrementalStatusAsync(
        CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        cancellationToken.ThrowIfCancellationRequested( );
        return ValueTask.FromResult( WithMaintenanceStatus( _incremental.GetStatus( ) ) );
    }

    public ValueTask PauseAsync( string? rootId, CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        cancellationToken.ThrowIfCancellationRequested( );
        _incremental.Pause( rootId );
        return ValueTask.CompletedTask;
    }

    internal void ReportWatcherOverflow( string rootPath )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        _incremental.ReportWatcherOverflow( rootPath );
    }

    internal IReadOnlyList<string> SnapshotWatchedDirectories( ) => _incremental.SnapshotWatchedDirectories( );

    internal IReadOnlyList<IncrementalJournalEvent> SnapshotPendingEvents( ) => _incremental.SnapshotPendingEvents( );

    public async ValueTask ResumeAsync( string? rootId, CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        await StartAsync( cancellationToken ).ConfigureAwait( false );
        EnsureCatalogMutationOwnership( );
        var roots = _incremental.Resume( rootId );
        var targetsChanged = _incremental.RestoreWatchers( rootId, cancellationToken: cancellationToken );
        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            CancelFullScan( );
            if ( targetsChanged )
            {
                await ReindexCoreAsync( progress: null, cancellationToken ).ConfigureAwait( false );
            }
            else
            {
                foreach ( var root in roots )
                {
                    if ( !_files.TryResolveChangePath( root, out var canonicalRoot ) )
                    {
                        continue;
                    }

                    var indexed = await ReplaceDirectoryAsync( root, canonicalRoot, cancellationToken ).ConfigureAwait( false );
                    _incremental.RecordRootScan( canonicalRoot, indexed );
                }
            }
        }
        catch
        {
            _incremental.Pause( rootId );
            throw;
        }
        finally
        {
            StartBackgroundReindex( );
            _indexLock.Release( );
        }

        foreach ( var root in _options.Indexing.Roots )
        {
            _incremental.RetryUnwatchedDirectories( IndexRootPaths.Normalize( root.Path ), cancellationToken );
        }
    }

    public ValueTask<PrivacyAuditResult> AuditPrivacyAsync(
        CancellationToken cancellationToken ) =>
        RunExclusiveAsync( _catalogOperations.AuditPrivacyAsync, cancellationToken );

    public ValueTask<CatalogMaintenanceResult> CheckIntegrityAsync(
        CancellationToken cancellationToken ) => RunMutationExclusiveAsync(
            _catalogOperations.CheckIntegrityAsync,
            cancellationToken );

    public ValueTask<CatalogMaintenanceResult> OptimizeAsync(
        CancellationToken cancellationToken ) =>
        RunMutationExclusiveAsync( _catalogOperations.OptimizeAsync, cancellationToken );

    // Incremental indexing pauses inside the index lock, so no journal batch
    // can land between the repair or purge and the full scan it waits for.
    public ValueTask<CatalogMaintenanceResult> RepairAsync(
        CancellationToken cancellationToken ) => RunMutationExclusiveAsync(
            async token =>
            {
                var result = await _catalogOperations.RepairAsync( token ).ConfigureAwait( false );
                if ( result.RebuildRequired )
                {
                    _reindexPending = false;
                    _incremental.PauseUntilFullScan( "catalog-repair" );
                }

                return result;
            },
            cancellationToken );

    public ValueTask<CatalogMaintenanceResult> PurgeProjectionAsync(
        CancellationToken cancellationToken ) => RunMutationExclusiveAsync(
            async token =>
            {
                var result = await _catalogOperations.PurgeProjectionAsync( token ).ConfigureAwait( false );
                if ( result.Succeeded )
                {
                    _reindexPending = false;
                    _incremental.PauseUntilFullScan( "catalog-purge" );
                }

                return result;
            },
            cancellationToken );

    public async ValueTask DisposeAsync( )
    {
        if ( _disposed )
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel( );
        await BackgroundReindex.ConfigureAwait( false );
        if ( _maintenanceTask is not null )
        {
            try
            {
                await _maintenanceTask.ConfigureAwait( false );
            }
            catch ( OperationCanceledException )
            {
            }
        }

        if ( _disabledPolicyTask is not null )
        {
            try
            {
                await _disabledPolicyTask.ConfigureAwait( false );
            }
            catch ( OperationCanceledException )
            {
            }
        }

        Task? offlineChanges;
        lock ( _startLock )
        {
            _offlineChangesSuperseded = true;
            offlineChanges = _offlineChangesTask;
        }

        if ( offlineChanges is not null )
        {
            await offlineChanges.ConfigureAwait( false );
        }

        lock ( _startLock )
        {
            _offlineChangesCancellation?.Dispose( );
            _offlineChangesCancellation = null;
        }

        await _incremental.DisposeAsync( ).ConfigureAwait( false );
        _files.Dispose( );
        _lifetime.Dispose( );
        _fullScanLock.Dispose( );
        _indexLock.Dispose( );
    }
}
