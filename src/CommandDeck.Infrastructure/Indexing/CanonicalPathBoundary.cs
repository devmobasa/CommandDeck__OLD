namespace CommandDeck;

internal sealed class CanonicalPathBoundary
{
    private readonly ulong _mountId;
    private readonly LinuxDirectoryIdentity _identity;

    private CanonicalPathBoundary(
        string lexicalRoot,
        string canonicalRoot,
        ulong mountId,
        LinuxDirectoryIdentity identity )
    {
        LexicalRoot = lexicalRoot;
        CanonicalRoot = canonicalRoot;
        _mountId = mountId;
        _identity = identity;
    }

    public string LexicalRoot { get; }
    public string CanonicalRoot { get; }

    public static bool TryCreate( string path, out CanonicalPathBoundary? boundary )
    {
        boundary = null;
        try
        {
            var lexicalRoot = Normalize( PathExpansion.Expand( path ) );
            if ( !Directory.Exists( lexicalRoot ) )
            {
                return false;
            }

            var canonicalRoot = ResolveExistingPath( lexicalRoot );
            if ( !LinuxMountIdentity.TryGet( canonicalRoot, out var mountId ) ||
                !LinuxMountIdentity.TryGetDirectory( canonicalRoot, followSymbolicLinks: false, out var identity ) )
            {
                return false;
            }

            boundary = new CanonicalPathBoundary( lexicalRoot, canonicalRoot, mountId, identity );
            return true;
        }
        catch ( Exception exception ) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException )
        {
            return false;
        }
    }

    public bool TryResolve(
        string path,
        bool followSymbolicLinks,
        out string canonicalPath )
    {
        canonicalPath = string.Empty;
        try
        {
            var requested = Normalize( path );
            string relative;
            if ( IsWithin( requested, LexicalRoot ) )
            {
                relative = Path.GetRelativePath( LexicalRoot, requested );
            }
            else if ( IsWithin( requested, CanonicalRoot ) )
            {
                relative = Path.GetRelativePath( CanonicalRoot, requested );
            }
            else
            {
                return false;
            }

            var current = CanonicalRoot;
            foreach ( var segment in Split( relative ) )
            {
                // The root itself is ".", and a normalized path inside the root
                // has no parent segments.
                if ( segment == "." )
                {
                    continue;
                }

                if ( segment == ".." )
                {
                    return false;
                }

                // One status call gives both the link check and the mount of a
                // plain component; a normalized parent plus a plain name is
                // already normalized.
                var candidate = Path.Combine( current, segment );
                if ( !LinuxMountIdentity.TryGetEntry( candidate, out var entry ) )
                {
                    return false;
                }

                var mountId = entry.MountId;
                if ( entry.SymbolicLink )
                {
                    if ( !followSymbolicLinks )
                    {
                        return false;
                    }

                    current = ResolveComponent( candidate, File.GetAttributes( candidate ) );
                    if ( !LinuxMountIdentity.TryGet( current, out mountId ) )
                    {
                        return false;
                    }
                }
                else
                {
                    current = candidate;
                }

                if ( !IsWithin( current, CanonicalRoot ) || mountId != _mountId )
                {
                    return false;
                }
            }

            canonicalPath = current;
            return true;
        }
        catch ( Exception exception ) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException )
        {
            return false;
        }
    }

    public bool TryMapPath( string path, bool followSymbolicLinks, out string canonicalPath )
    {
        if ( TryResolve( path, followSymbolicLinks, out canonicalPath ) )
        {
            return true;
        }

        canonicalPath = string.Empty;
        try
        {
            var requested = Normalize( path );
            string relative;
            if ( IsWithin( requested, LexicalRoot ) )
            {
                relative = Path.GetRelativePath( LexicalRoot, requested );
            }
            else if ( IsWithin( requested, CanonicalRoot ) )
            {
                relative = Path.GetRelativePath( CanonicalRoot, requested );
            }
            else
            {
                return false;
            }

            var current = CanonicalRoot;
            var segments = Split( relative ).ToArray( );
            for ( var index = 0; index < segments.Length; index++ )
            {
                var candidate = Path.Combine( current, segments[index] );
                if ( !File.Exists( candidate ) && !Directory.Exists( candidate ) )
                {
                    canonicalPath = Normalize( Path.Combine( [current, .. segments[index..]] ) );
                    return IsWithin( canonicalPath, CanonicalRoot );
                }

                var attributes = File.GetAttributes( candidate );
                if ( (attributes & FileAttributes.ReparsePoint) != 0 )
                {
                    if ( !followSymbolicLinks )
                    {
                        return false;
                    }

                    current = ResolveComponent( candidate, attributes );
                }
                else
                {
                    current = Normalize( candidate );
                }

                if ( !IsWithin( current, CanonicalRoot ) ||
                    !LinuxMountIdentity.TryGet( current, out var mountId ) ||
                    mountId != _mountId )
                {
                    return false;
                }
            }

            canonicalPath = current;
            return true;
        }
        catch ( Exception exception ) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException )
        {
            return false;
        }
    }

    // A cached boundary is reusable while its configured path still resolves to
    // the same directory on the same mount, and the canonical path still names
    // that directory. Any change fails the check, and the caller rebuilds.
    public bool IsCurrent( ) =>
        LinuxMountIdentity.TryGetDirectory( LexicalRoot, followSymbolicLinks: true, out var lexical ) &&
        lexical == _identity &&
        LinuxMountIdentity.TryGetDirectory( CanonicalRoot, followSymbolicLinks: false, out var canonical ) &&
        canonical == _identity;

    public bool IsOnRootMount( ulong mountId ) => mountId == _mountId;

    public bool Contains( string path )
    {
        var normalized = Normalize( path );
        return IsWithin( normalized, LexicalRoot ) || IsWithin( normalized, CanonicalRoot );
    }

    public bool ContainsOpenedPath( string canonicalPath, ulong mountId )
    {
        var normalized = Normalize( canonicalPath );
        return mountId == _mountId && IsWithin( normalized, CanonicalRoot );
    }

    public string GetRelativePath( string path )
    {
        var normalized = Normalize( path );
        var root = IsWithin( normalized, LexicalRoot ) ? LexicalRoot : CanonicalRoot;
        return Path.GetRelativePath( root, normalized );
    }

    private static string ResolveExistingPath( string path )
    {
        var root = Path.GetPathRoot( path )!;
        var current = root;
        foreach ( var segment in Split( Path.GetRelativePath( root, path ) ) )
        {
            var candidate = Path.Combine( current, segment );
            var attributes = File.GetAttributes( candidate );
            current = (attributes & FileAttributes.ReparsePoint) != 0
                ? ResolveComponent( candidate, attributes )
                : Normalize( candidate );
        }

        return current;
    }

    private static string ResolveComponent( string path, FileAttributes attributes )
    {
        FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
            ? new DirectoryInfo( path )
            : new FileInfo( path );
        var target = info.ResolveLinkTarget( returnFinalTarget: true );
        return Normalize( target?.FullName ?? path );
    }

    private static string[] Split( string relative ) =>
        relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries );

    private static string Normalize( string path ) =>
        Path.TrimEndingDirectorySeparator( Path.GetFullPath( path ) );

    private static bool IsWithin( string path, string root )
    {
        var comparison = OperatingSystem.IsWindows( )
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if ( string.Equals( path, root, comparison ) )
        {
            return true;
        }

        var separated = Path.EndsInDirectorySeparator( root ) ||
            (path.Length > root.Length && path[root.Length] == Path.DirectorySeparatorChar);
        return separated && path.StartsWith( root, comparison );
    }
}
