namespace CommandDeck;

internal sealed partial class IndexExclusionPolicy
{
    public bool ShouldObserve( string path )
    {
        var root = FindRoot( path );
        if ( root is null || !root.Value.Boundary.TryMapPath( path,
                root.Value.Options.FollowSymbolicLinks, out var canonicalPath ) )
        {
            return false;
        }        // A deleted directory has no attributes. Its surviving ancestors still
        // enforce subtree exclusions; retaining the leaf event removes stale rows.
        return Match( root.Value.Boundary, root.Value.Options, path, canonicalPath,
            Directory.Exists( canonicalPath ) ) is null;
    }

    public string[] RuleChangeScopes( string path )
    {
        var registered = _inputs.ControlScopes( path ).ToArray( );
        var name = Path.GetFileName( path );
        if ( registered.Length == 0 && !IndexPolicyInputs.IsMarkerName( name ) &&
            !path.EndsWith( "/.git/info/exclude", StringComparison.Ordinal ) )
        {
            return [];
        }
        _inputs.Invalidate( path );
        List<string> scopes = [.. registered];
        var root = FindRoot( path );
        var parent = Path.GetDirectoryName( path );
        if ( root is not null && parent is not null )
        {
            var options = root.Value.Options;
            string? scope = null;
            if ( options.RespectGitIgnore && name is ".gitignore" or ".git" )
            {
                scope = parent;
            }
            else if ( options.ExcludeGeneratedDirectories && IndexPolicyInputs.IsMarkerName( name ) )
            {
                scope = name == "pyvenv.cfg" ? Path.GetDirectoryName( parent ) : parent;
            }
            else if ( options.RespectGitIgnore && path.EndsWith( "/.git/info/exclude", StringComparison.Ordinal ) )
            {
                scope = Path.GetDirectoryName( Path.GetDirectoryName( parent ) );
            }
            if ( scope is not null && ShouldObserve( scope ) )
            {
                scopes.Add( scope );
            }
        }
        return scopes.SelectMany( AffectedScopes ).Distinct( StringComparer.Ordinal ).ToArray( );
    }

    public string[] ExternalControlDirectories( )
    {
        var roots = ResolveRoots( ).Select( static root => root.Boundary.CanonicalRoot ).ToArray( );
        _inputs.PruneControlScopes( roots );
        return _inputs.ControlFiles( )
            .Where( path => !roots.Any( root => IsWithin( path, root ) ) )
            .Select( static path => ExistingParent( path ) )
            .Where( static path => path is not null ).Cast<string>( )
            .Distinct( StringComparer.Ordinal ).ToArray( );
    }

    private IEnumerable<string> AffectedScopes( string scope )
    {
        foreach ( (var boundary, _) in ResolveRoots( ) )
        {
            if ( IsWithin( boundary.LexicalRoot, scope ) || IsWithin( boundary.CanonicalRoot, scope ) )
            {
                yield return boundary.LexicalRoot;
            }
            else if ( boundary.Contains( scope ) )
            {
                yield return scope;
            }
        }
    }

    private static string? ExistingParent( string path )
    {
        var parent = Path.GetDirectoryName( path );
        while ( parent is not null && !Directory.Exists( parent ) )
        {
            parent = Path.GetDirectoryName( parent );
        }
        return parent;
    }

    internal static bool IsWithin( string path, string root ) => path == root ||
        path.StartsWith( Path.EndsInDirectorySeparator( root ) ? root : root + Path.DirectorySeparatorChar, StringComparison.Ordinal );
}
