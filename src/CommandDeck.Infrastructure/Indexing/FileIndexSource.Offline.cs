namespace CommandDeck;

public sealed partial class FileIndexSource
{
    // Yields the file entries a scoped scan of one root's directory visits,
    // pruning directories exactly as that scan does, without reading content.
    internal IEnumerable<string> EnumerateDirectoryFiles(
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
            yield break;
        }

        var owningRoot =
            FindOwningRoot( requestedPath );
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
            yield break;
        }

        try
        {
            if ( (File.GetAttributes( fullPath ) & FileAttributes.ReparsePoint) != 0 &&
                !owningRoot.Value.Options.FollowSymbolicLinks )
            {
                yield break;
            }
        }
        catch ( Exception exception ) when (
            exception is UnauthorizedAccessException or IOException )
        {
            yield break;
        }

        Stack<string> directories = new( );
        HashSet<string> visitedDirectories = new( StringComparer.Ordinal );
        directories.Push( fullPath );
        while ( directories.Count > 0 )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var directory = directories.Pop( );
            if ( !visitedDirectories.Add( directory ) )
            {
                continue;
            }
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries( directory );
            }
            catch ( Exception exception ) when (
                exception is UnauthorizedAccessException or IOException )
            {
                continue;
            }

            foreach ( var entry in entries )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes( entry );
                }
                catch ( Exception exception ) when (
                    exception is UnauthorizedAccessException or IOException )
                {
                    continue;
                }

                if ( (attributes & FileAttributes.Directory) != 0 )
                {
                    var entryRoot = FindOwningRoot( entry );
                    if ( entryRoot is null || entryRoot.Value.Boundary.CanonicalRoot !=
                        owningRoot.Value.Boundary.CanonicalRoot )
                    {
                        continue;
                    }
                    if ( !TryAuthorizeEntry(
                                                owningRoot.Value.Boundary,
                                                owningRoot.Value.Options,
                                                entry,
                                                out var canonicalDirectory,
                                                out var reason,
                                                out var rule ) ||
                                            !owningRoot.Value.Options.IncludeHidden &&
                                            ContainsHiddenSegment( owningRoot.Value.Boundary.GetRelativePath( entry ) ) )
                    {
                        RecordDiagnostic( owningRoot.Value.Boundary, entry, reason, rule );
                        continue;
                    }

                    directories.Push( canonicalDirectory );
                    continue;
                }

                yield return entry;
            }
        }
    }
    private static bool ContainsHiddenSegment( string relativePath ) =>
        relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries )
        .Any( static segment => segment is not ("." or "..") &&
            segment.StartsWith( '.', StringComparison.Ordinal ) );


}
