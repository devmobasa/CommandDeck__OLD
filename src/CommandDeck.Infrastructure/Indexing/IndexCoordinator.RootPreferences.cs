namespace CommandDeck;

public sealed partial class IndexCoordinator
{
    private readonly List<IndexRootOptions> _configuredRoots;
    private readonly ILauncherPreferenceStore? _rootPreferences;
    private volatile bool _rootPreferencesLoaded;

    private async ValueTask EnsureRootPreferencesAsync( CancellationToken cancellationToken )
    {
        if ( _rootPreferencesLoaded )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            return;
        }

        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _rootPreferencesLoaded )
            {
                return;
            }

            var preferences = _rootPreferences is null ? new IndexRootPreferences( ) :
                await _rootPreferences.GetIndexRootsAsync( cancellationToken ).ConfigureAwait( false );
            ApplyRootPreferences( preferences );
            if ( _rootPreferences is not null )
            {
                _reindexPending = await _catalog.RequiresReindexAsync( cancellationToken ).ConfigureAwait( false );
                if ( _reindexPending )
                {
                    _incremental.PauseUntilFullScan( "reindex-required" );
                }
            }
            _rootPreferencesLoaded = true;
        }
        finally
        {
            _indexLock.Release( );
        }
    }

    private IEnumerable<(IndexRootOptions Options, string Source, bool Enabled)> DescribeRoots( IndexRootPreferences preferences )
    {
        var disabled = preferences.DisabledConfiguredPaths.ToHashSet( StringComparer.Ordinal );
        return _configuredRoots.Select( root => (root, "configuration", !disabled.Contains( IndexRootPaths.Normalize( root.Path ) )) )
            .Concat( preferences.Added.Select( static root => (root.Options, "added", true) ) )
            .GroupBy( static root => IndexRootPaths.Normalize( root.Item1.Path ), StringComparer.Ordinal )
            .Select( static group => group.FirstOrDefault( static root => root.Item3, group.First( ) ) );
    }

    private void ApplyRootPreferences( IndexRootPreferences preferences )
    {
        _options.Indexing.Roots = DescribeRoots( preferences ).Where( static root => root.Enabled )
            .Select( static root => root.Options with { Path = IndexRootPaths.Normalize( root.Options.Path ) } ).ToList( );
        var indexing = _options.Indexing with { Roots = [.. _options.Indexing.Roots] };
        var files = new FileIndexSource( indexing, _options.Privacy, _options.Storage.DatabasePath );
        _incremental.ReplaceRoots( files.Exclusions );
        _files.Dispose( );
        _files = files;
    }

    public async ValueTask<LauncherPreferences> ResetPreferencesAsync( CancellationToken cancellationToken )
    {
        await EnsureRootPreferencesAsync( cancellationToken ).ConfigureAwait( false );
        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _rootPreferences is null )
            {
                throw new InvalidOperationException( "Indexed folder preferences are unavailable." );
            }

            await _incremental.StartAsync( cancellationToken ).ConfigureAwait( false );
            EnsureCatalogMutationOwnership( );

            var before = await _rootPreferences.GetIndexRootsAsync( cancellationToken ).ConfigureAwait( false );
            var rootsChanged = before.Added.Count > 0 || before.DisabledConfiguredPaths.Count > 0;

            if ( !rootsChanged )
            {
                return await _rootPreferences.ResetAsync( cancellationToken ).ConfigureAwait( false );
            }

            await CommitRootChangeAsync( new IndexRootPreferences( ), reset: true, cancellationToken ).ConfigureAwait( false );
            return LauncherPreferences.CreateDefault( );
        }
        finally
        {
            StartBackgroundReindex( );
            _indexLock.Release( );
        }
    }

    public async ValueTask<IndexRootsResult> ManageRootsAsync( string operation, string? path, string? rootId,
        IndexRootPreferences? import, CancellationToken cancellationToken )
    {
        await StartAsync( cancellationToken ).ConfigureAwait( false );
        if ( operation != ProtocolOperations.IndexRoots )
        {
            EnsureCatalogMutationOwnership( );
        }

        await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            var preferences = _rootPreferences is null ? new IndexRootPreferences( ) :
                await _rootPreferences.GetIndexRootsAsync( cancellationToken ).ConfigureAwait( false );
            if ( operation != ProtocolOperations.IndexRoots )
            {
                if ( _rootPreferences is null )
                {
                    throw new InvalidOperationException( "Indexed folder preferences are unavailable." );
                }

                preferences = ChangeRoots( preferences, operation, path, rootId, import );
                await CommitRootChangeAsync( preferences, reset: false, cancellationToken ).ConfigureAwait( false );
            }

            var status = _incremental.GetStatus( );
            return new IndexRootsResult
            {
                ReindexPending = _reindexPending,
                Progress = _reindexProgress,
                ReindexError = _reindexError,
                Roots = DescribeRoots( preferences ).Select( root =>
                {
                    var normalized = IndexRootPaths.Normalize( root.Options.Path );
                    var id = StableId.Create( "index-root", normalized );
                    var runtime = status.Roots.SingleOrDefault( value => value.Id == id );
                    var issue = root.Enabled ? RootIssue( root.Options ) ?? runtime?.LastError : null;
                    return new ManagedIndexRoot
                    {
                        Id = id,
                        Path = normalized,
                        Source = root.Source,
                        Enabled = root.Enabled,
                        Issue = issue,
                        Runtime = runtime,
                        Status = !root.Enabled ? "Disabled" : issue is not null ? "Needs attention" :
                            BackgroundReindexStatus( ) ?? (runtime?.Paused == true ? "Paused" : runtime?.Watched == true ? "Watching" : "Indexed")
                    };
                } ).ToList( )
            };
        }
        finally
        {
            StartBackgroundReindex( );
            _indexLock.Release( );
        }
    }

    private IndexRootPreferences ChangeRoots( IndexRootPreferences preferences, string operation,
        string? path, string? rootId, IndexRootPreferences? import )
    {
        if ( operation == ProtocolOperations.IndexRootsImport )
        {
            if ( import is null )
            {
                throw new InvalidOperationException( "Indexed folder preferences are required." );
            }
            import.Validate( );
            foreach ( var root in import.Added )
            {
                ValidateAddedRoot( root.Options );
            }
            return new IndexRootPreferences
            {
                Added = import.Added.Select( static root => new AddedIndexRoot
                {
                    CreatedAt = root.CreatedAt,
                    Options = root.Options with { Path = IndexRootPaths.Normalize( root.Options.Path ) }
                } ).ToList( ),
                DisabledConfiguredPaths = import.DisabledConfiguredPaths.Select( IndexRootPaths.Normalize ).Distinct( StringComparer.Ordinal ).ToList( )
            };
        }
        if ( operation == ProtocolOperations.IndexAdd )
        {
            var options = new IndexRootOptions { Path = path ?? "" };
            ValidateAddedRoot( options );
            options = options with { Path = IndexRootPaths.Normalize( options.Path ) };
            var normalized = IndexRootPaths.Normalize( options.Path );
            if ( DescribeRoots( preferences ).Any( root => IndexRootPaths.Normalize( root.Options.Path ) == normalized ) )
            {
                throw new InvalidOperationException( "Folder is already listed. Enable its configuration row if disabled." );
            }
            preferences.Added.Add( new AddedIndexRoot { Options = options, CreatedAt = _timeProvider.GetUtcNow( ) } );
            return preferences;
        }

        var selected = DescribeRoots( preferences ).SingleOrDefault( root =>
            StableId.Create( "index-root", IndexRootPaths.Normalize( root.Options.Path ) ) == rootId );
        if ( selected.Options is null )
        {
            throw new InvalidOperationException( $"Unknown index root '{rootId}'." );
        }
        var selectedPath = IndexRootPaths.Normalize( selected.Options.Path );
        switch ( operation )
        {
            case ProtocolOperations.IndexRemove when selected.Source == "added":
                preferences.Added.RemoveAll( root => IndexRootPaths.Normalize( root.Options.Path ) == selectedPath );
                break;
            case ProtocolOperations.IndexDisable when selected.Source == "configuration":
                if ( !preferences.DisabledConfiguredPaths.Contains( selectedPath, StringComparer.Ordinal ) )
                {
                    preferences.DisabledConfiguredPaths.Add( selectedPath );
                }
                break;
            case ProtocolOperations.IndexEnable when selected.Source == "configuration":
                preferences.DisabledConfiguredPaths.RemoveAll( value => value == selectedPath );
                break;
            default:
                throw new InvalidOperationException( "Remove added folders; disable or enable configuration folders." );
        }
        return preferences;
    }

    private void ValidateAddedRoot( IndexRootOptions options )
    {
        ConfigurationValidator.ValidateIndexRoot( options );
        var issue = RootIssue( options );
        if ( issue is not null )
        {
            throw new InvalidOperationException( issue );
        }
    }

    private string? RootIssue( IndexRootOptions options )
    {
        if ( !Directory.Exists( IndexRootPaths.Normalize( options.Path ) ) )
        {
            return "Folder does not exist.";
        }
        if ( !CanonicalPathBoundary.TryCreate( options.Path, out var boundary ) || boundary is null )
        {
            return "Configured root could not be resolved.";
        }

        _files.Exclusions.EnterRoot( boundary, options, out var exclusion );
        return exclusion is null ? null : $"Index root is excluded: {exclusion.Reason}.";
    }
}
