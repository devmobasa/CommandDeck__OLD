using System.Runtime.CompilerServices;

namespace CommandDeck;

public sealed partial class FileIndexSource
{
    // Materializes a scoped scan for callers that need the whole list; the index
    // coordinator streams ScanDirectoryAsync into the catalog instead.
    public async ValueTask<IReadOnlyList<IndexedItem>> IndexDirectoryAsync(
        string path,
        CancellationToken cancellationToken )
    {
        List<IndexedItem> items = [];
        HashSet<string> stableIds = new( StringComparer.Ordinal );
        await foreach ( var item in ScanDirectoryAsync( path, cancellationToken ).ConfigureAwait( false ) )
        {
            if ( stableIds.Add( item.StableId ) )
            {
                items.Add( item );
            }
        }

        return items;
    }

    // Streams the files below a directory with the full scan's traversal: the
    // directory's own root from that directory down, then every configured root
    // nested inside it. Scoped, resumed, and overflow rescans all use it. A file
    // reached twice (through a followed link) is emitted twice; consumers keep
    // the first.
    public IAsyncEnumerable<IndexedItem> ScanDirectoryAsync( string path, CancellationToken cancellationToken ) =>
        ScanDirectoryAsync( path, null, cancellationToken );

    public async IAsyncEnumerable<IndexedItem> ScanDirectoryAsync(
        string path,
        IProgress<IndexProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken )
    {
        if ( !TryStartDirectoryScan( path, out var start, out var scope ) )
        {
            yield break;
        }

        ScanCounters counters = new( );
        var roots = ScannableRoots( );
        if ( start is not null )
        {
            await foreach ( var item in ScanRootAsync(
                start,
                NestedRoots( start.Boundary, roots ),
                counters,
                progress,
                cancellationToken ).ConfigureAwait( false ) )
            {
                yield return item;
            }
        }

        // A scoped refresh must visit explicitly configured nested roots even
        // when the outer root prunes their parent as generated or ignored.
        foreach ( (var boundary, var options) in roots )
        {
            if ( boundary.CanonicalRoot == scope || !IndexExclusionPolicy.IsWithin( boundary.CanonicalRoot, scope ) )
            {
                continue;
            }

            ClearScopeDiagnostics( boundary.LexicalRoot );
            var nestedStart = Exclusions.EnterRoot( boundary, options, out var exclusion );
            if ( nestedStart is null )
            {
                RecordDiagnostic( boundary, boundary.CanonicalRoot, exclusion!.Reason, exclusion.Rule );
                continue;
            }

            await foreach ( var item in ScanRootAsync(
                nestedStart,
                NestedRoots( boundary, roots ),
                counters,
                progress,
                cancellationToken ).ConfigureAwait( false ) )
            {
                yield return item;
            }
        }
    }

    // Resolves the scope's canonical path and, when the directory itself is
    // authorized under its most specific root, the context to scan it from.
    private bool TryStartDirectoryScan( string path, out AuthorizedDirectory? start, out string scope )
    {
        start = null;
        scope = string.Empty;
        string requestedPath;
        try
        {
            requestedPath = Path.GetFullPath( path );
            ClearScopeDiagnostics( requestedPath );
        }
        catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException )
        {
            return false;
        }

        if ( !TryResolveChangePath( requestedPath, out scope ) )
        {
            return false;
        }

        var owningRoot = FindOwningRoot( requestedPath );
        if ( owningRoot is null ||
            !owningRoot.Value.Boundary.TryResolve(
                requestedPath,
                owningRoot.Value.Options.FollowSymbolicLinks,
                out var fullPath ) ||
            !Directory.Exists( fullPath ) ||
            !IsAuthorizedPath(
                owningRoot.Value.Boundary,
                owningRoot.Value.Options,
                requestedPath,
                fullPath ) )
        {
            return true;
        }

        start = Exclusions.EnterDirectory(
            owningRoot.Value.Boundary,
            owningRoot.Value.Options,
            fullPath,
            out var exclusion );
        if ( start is null && exclusion is not null )
        {
            RecordDiagnostic( owningRoot.Value.Boundary, requestedPath, exclusion.Reason, exclusion.Rule );
        }

        return true;
    }

    // Configured roots that exist and have a canonical root of their own; two
    // roots naming one directory are ambiguous and skipped, as in a full scan.
    private List<(CanonicalPathBoundary Boundary, IndexRootOptions Options)> ScannableRoots( )
    {
        var roots = Exclusions.ResolveRoots( );
        return roots
            .Where( root => roots.Count( other =>
                string.Equals( other.Boundary.CanonicalRoot, root.Boundary.CanonicalRoot, StringComparison.Ordinal ) ) == 1 )
            .ToList( );
    }

    private static HashSet<string> NestedRoots(
        CanonicalPathBoundary boundary,
        List<(CanonicalPathBoundary Boundary, IndexRootOptions Options)> roots ) =>
        roots
            .Where( value =>
                value.Boundary.CanonicalRoot.Length > boundary.CanonicalRoot.Length &&
                boundary.Contains( value.Boundary.CanonicalRoot ) )
            .Select( static value => value.Boundary.CanonicalRoot )
            .ToHashSet( StringComparer.Ordinal );
}
