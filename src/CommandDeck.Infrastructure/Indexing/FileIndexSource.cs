namespace CommandDeck;

public sealed partial class FileIndexSource : IDisposable
{
    private const int MaximumDiagnostics = 512;
    private const int MaximumDiagnosticPathCharacters = 512;
    private readonly IndexingOptions _options;
    private readonly PathPolicy _pathPolicy;
    private readonly ContentExtractor _contentExtractor;
    internal IndexExclusionPolicy Exclusions { get; }
    private readonly IndexStoragePaths? _storagePaths;
    private readonly List<IndexExclusionDiagnostic> _diagnostics = [];
    private readonly List<IndexWorkItem> _workItems = [];

    public FileIndexSource( IndexingOptions options, PrivacyOptions privacy, string? databasePath = null )
        : this( options, privacy, databasePath, null )
    {
    }

    private FileIndexSource( IndexingOptions options, PrivacyOptions privacy, string? databasePath, IndexExclusionPolicy? exclusions )
    {
        _options = options;
        _pathPolicy = new PathPolicy( privacy );
        Exclusions = exclusions ?? new IndexExclusionPolicy( options, privacy, databasePath, cacheRootBoundaries: true );
        _contentExtractor = new ContentExtractor( options );
        _storagePaths = databasePath is null ? null : new IndexStoragePaths( databasePath );
    }

    internal FileIndexSource CreateScanSource( IndexingOptions options, PrivacyOptions privacy, string databasePath ) =>
        new( options, privacy, databasePath, Exclusions );

    public IReadOnlyList<IndexExclusionDiagnostic> SnapshotDiagnostics( ) =>
        _diagnostics.ToArray( );

    internal void ReplaceDiagnostics( IReadOnlyList<IndexExclusionDiagnostic> diagnostics )
    {
        _diagnostics.Clear( );
        _diagnostics.AddRange( diagnostics );
    }

    public IReadOnlyList<IndexWorkItem> SnapshotWorkItems( ) =>
        _workItems.ToArray( );

    internal IndexWorkItem DescribeWorkItem( string path, string state, string reason ) =>
        CreateWorkItem( path, state, reason );

    public async ValueTask<IReadOnlyList<IndexedItem>> IndexPathAsync(
        string path,
        CancellationToken cancellationToken )
    {
        string requestedPath;
        try
        {
            requestedPath = Path.GetFullPath( path );
            ClearScopeDiagnostics( requestedPath );
        }
        catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException )
        {
            RecordWorkItem( path, "invalid-path" );
            return [];
        }

        var owningRoot =
            FindOwningRoot( requestedPath );
        if ( owningRoot is null ||
            !owningRoot.Value.Boundary.TryResolve(
                requestedPath,
                owningRoot.Value.Options.FollowSymbolicLinks,
                out var fullPath ) ||
            !File.Exists( fullPath ) ||
            !IsAuthorizedPath(
                owningRoot.Value.Boundary,
                owningRoot.Value.Options,
                requestedPath,
                fullPath ) )
        {
            return [];
        }

        var relative = Path.GetRelativePath( owningRoot.Value.Boundary.CanonicalRoot, fullPath );

        FileAttributes attributes;
        FileInfo info;
        try
        {
            attributes = File.GetAttributes( fullPath );
            if ( (attributes & FileAttributes.Directory) != 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0 &&
                !owningRoot.Value.Options.FollowSymbolicLinks )
            {
                return [];
            }

            info = new FileInfo( fullPath );
        }
        catch ( Exception exception ) when (
            exception is UnauthorizedAccessException or IOException )
        {
            RecordWorkItem( fullPath, "metadata-read-failed" );
            return [];
        }

        var policy = FileIndexPolicyResolver.Resolve(
            owningRoot.Value.Options,
            fullPath );
        string? body = null;
        if ( policy.IncludeContent )
        {
            try
            {
                var extraction = await _contentExtractor.ExtractAsync(
                    fullPath,
                    info.Length,
                    policy,
                    cancellationToken ).ConfigureAwait( false );
                body = extraction.Text;
                if ( extraction.FailureReason is not null )
                {
                    RecordWorkItem( fullPath, extraction.FailureReason );
                }
            }
            catch ( Exception exception ) when (
                exception is UnauthorizedAccessException or IOException )
            {
                // A transient extractor/read failure must not make an ordinary
                // watcher update delete metadata that the full scan preserves.
                body = null;
                RecordWorkItem( fullPath, "content-extraction-failed" );
            }
        }

        if ( !owningRoot.Value.Boundary.IsCurrent( ) ||
            !owningRoot.Value.Boundary.TryResolve(
                requestedPath,
                owningRoot.Value.Options.FollowSymbolicLinks,
                out var verifiedPath ) ||
            !string.Equals( verifiedPath, fullPath, StringComparison.Ordinal ) )
        {
            return [];
        }

        var modified = new DateTimeOffset( info.LastWriteTimeUtc ).ToUnixTimeMilliseconds( );
        List<IndexedItem> items =
        [
            new IndexedItem
            {
                StableId = StableId.Create(ResultKinds.File, fullPath),
                Kind = ResultKinds.File,
                Title = info.Name,
                Subtitle = relative,
                Path = fullPath,
                Icon = GuessIcon(info.Extension),
                Body = body,
                SizeBytes = info.Length,
                ModifiedUnixMilliseconds = modified,
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files,
                ExtractorVersion = ContentExtractor.ExtractorVersion,
                IncludeTitleInSearch = policy.IncludeFileName,
                IncludeMetadataInSearch = policy.IncludeMetadata,
                IndexPolicies = [.. policy.Provenance]
            }
        ];

        if ( body is not null && StructuredConfigExtractor.IsStructured( fullPath ) )
        {
            foreach ( var config in StructuredConfigExtractor.Extract( fullPath, body ) )
            {
                items.Add( CreateConfigurationItem(
                    fullPath,
                    info.Length,
                    modified,
                    config,
                    policy ) );
            }
        }

        return items;
    }

    public bool TryResolveChangePath( string path, out string canonicalPath )
    {
        canonicalPath = string.Empty;
        string requestedPath;
        try
        {
            requestedPath = Path.GetFullPath( path );
        }
        catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException )
        {
            return false;
        }

        var owningRoot =
            FindOwningRoot( requestedPath );
        return owningRoot is not null && owningRoot.Value.Boundary.TryMapPath(
            requestedPath,
            owningRoot.Value.Options.FollowSymbolicLinks,
            out canonicalPath );
    }

    public void Dispose( ) => _contentExtractor.Dispose( );
}
