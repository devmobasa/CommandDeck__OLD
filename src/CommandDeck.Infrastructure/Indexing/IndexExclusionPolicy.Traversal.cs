namespace CommandDeck;

// Traversal checks each directory's contribution to the policy once. A child of
// an authorized directory is then checked on its own path only, in the order
// Match uses: storage, privacy, hidden, root exclusions, inclusions, generated
// directories, and repository ignore rules. For the same canonical path the
// result equals Match.
internal sealed partial class IndexExclusionPolicy
{
    // Settles what every child of the root would otherwise re-evaluate: privacy
    // rules on the root and its ancestors, root-level globs, and repository
    // rules from the filesystem root down to the index root.
    public AuthorizedDirectory? EnterRoot(
        CanonicalPathBoundary boundary,
        IndexRootOptions options,
        out IndexExclusion? exclusion )
    {
        var root = boundary.CanonicalRoot;
        exclusion = MatchPrivacy( boundary.LexicalRoot );
        foreach ( var ancestor in RepositoryIgnorePolicy.Ancestors( root, includeLeaf: true ) )
        {
            exclusion ??= MatchPrivacy( ancestor );
        }

        if ( exclusion is not null )
        {
            return null;
        }

        var rootExclusion = TryMatchRootPatterns( boundary, root, options.ExcludeGlobs, out var excludedBy )
            ? new IndexExclusion( IndexExclusionReasons.RootGlob, excludedBy )
            : null;
        var included = rootExclusion is null &&
            TryMatchRootPatterns( boundary, root, options.IncludeGlobs, out _ );

        var repository = RepositoryContext.Outside;
        IndexExclusion? ignore = null;
        if ( options.RespectGitIgnore && rootExclusion is null && !included )
        {
            foreach ( var ancestor in RepositoryIgnorePolicy.Ancestors( root, includeLeaf: true ) )
            {
                ignore = _repository.EnterDirectory( repository, ancestor, markers: null, out repository );
                if ( ignore is not null )
                {
                    break;
                }
            }
        }

        AuthorizedDirectoryPolicy policy = new( included, rootExclusion, Generated: null, ignore, repository );
        return new AuthorizedDirectory( boundary, options, root, policy, markers: null );
    }

    // Authorizes a canonical directory inside a root by stepping down from the
    // root through each intermediate directory, as traversal would reach it.
    public AuthorizedDirectory? EnterDirectory(
        CanonicalPathBoundary boundary,
        IndexRootOptions options,
        string directory,
        out IndexExclusion? exclusion )
    {
        var current = EnterRoot( boundary, options, out exclusion );
        if ( current is null )
        {
            return null;
        }

        var relative = Path.GetRelativePath( boundary.CanonicalRoot, directory );
        if ( relative == "." )
        {
            return current;
        }

        if ( Path.IsPathRooted( relative ) || relative == ".." ||
            relative.StartsWith( ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
        {
            exclusion = new IndexExclusion( IndexExclusionReasons.PathBoundary, null );
            return null;
        }

        var path = boundary.CanonicalRoot;
        foreach ( var segment in relative.Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
        {
            path = Path.Join( path, segment );
            exclusion = MatchChild( current, path, directory: true, out var child );
            if ( exclusion is not null || child is null )
            {
                return null;
            }

            current = child;
        }

        return current;
    }

    // Checks one entry of an authorized directory whose canonical path is the
    // parent's path plus the entry name (the entry is not a symbolic link).
    public IndexExclusion? MatchChild(
        AuthorizedDirectory parent,
        string path,
        bool directory,
        out AuthorizedDirectory? child )
    {
        child = null;
        var options = parent.Options;
        var policy = parent.Policy;

        if ( _storage?.Contains( path ) == true )
        {
            return new IndexExclusion( IndexExclusionReasons.IndexStorage, null );
        }

        var privacy = MatchPrivacy( path );
        if ( privacy is not null )
        {
            return privacy;
        }

        var name = Path.GetFileName( path );
        if ( !options.IncludeHidden && name.Length > 1 && name[0] == '.' )
        {
            return new IndexExclusion( IndexExclusionReasons.Hidden, "includeHidden=false" );
        }

        if ( policy.RootExclusion is not null )
        {
            return policy.RootExclusion;
        }

        if ( TryMatchLeafPatterns( parent.Boundary, path, options.ExcludeGlobs, out var excludedBy ) )
        {
            return new IndexExclusion( IndexExclusionReasons.RootGlob, excludedBy );
        }

        if ( policy.IncludedByGlob || TryMatchLeafPatterns( parent.Boundary, path, options.IncludeGlobs, out _ ) )
        {
            if ( directory )
            {
                AuthorizedDirectoryPolicy included = new( true, null, null, null, RepositoryContext.Outside );
                child = new AuthorizedDirectory( parent.Boundary, options, path, included, markers: null );
            }

            return null;
        }

        return MatchChildDefaults( parent, path, name, directory, out child );
    }

    private IndexExclusion? MatchChildDefaults(
        AuthorizedDirectory parent,
        string path,
        string name,
        bool directory,
        out AuthorizedDirectory? child )
    {
        child = null;
        var options = parent.Options;
        var policy = parent.Policy;

        // A directory an include glob could reach below is opened anyway; its
        // children that are not included inherit its default exclusion.
        var opensInclusion = directory && options.IncludeGlobs.Count > 0 &&
            CouldIncludeDescendant( parent.Boundary, path, options.IncludeGlobs );

        IReadOnlySet<string>? markers = null;
        var generated = policy.Generated;
        if ( generated is null && directory && options.ExcludeGeneratedDirectories )
        {
            string? rule = name;
            if ( !GeneratedDirectoryPolicy.IsAlwaysGenerated( name ) )
            {
                markers = _inputs.Markers( path );
                rule = _generated.Match( path, markers, parent.GetMarkers( _inputs ) );
            }

            generated = rule is null ? null : new IndexExclusion( IndexExclusionReasons.GeneratedDirectory, rule );
        }

        if ( generated is not null && !opensInclusion )
        {
            return generated;
        }

        var ignore = generated is null ? policy.Ignore : null;
        var repository = policy.Repository;
        if ( generated is null && ignore is null && options.RespectGitIgnore )
        {
            ignore = directory
                ? _repository.EnterDirectory( policy.Repository, path, markers, out repository )
                : _repository.MatchFile( policy.Repository, path );
        }

        if ( ignore is not null && !opensInclusion )
        {
            return ignore;
        }

        if ( directory )
        {
            AuthorizedDirectoryPolicy inherited = new( false, null, generated, ignore, repository );
            child = new AuthorizedDirectory( parent.Boundary, options, path, inherited, markers );
        }

        return null;
    }

    private IndexExclusion? MatchPrivacy( string path ) =>
        _privacy.TryGetDenial( path, out var rule )
            ? new IndexExclusion( IndexExclusionReasons.PrivacyRule, rule )
            : null;

    private static bool TryMatchLeafPatterns(
        CanonicalPathBoundary boundary,
        string path,
        List<string> patterns,
        out string matched )
    {
        matched = string.Empty;
        return patterns.Count > 0 &&
            (PathPolicy.TryMatch( path, patterns, out matched ) ||
            PathPolicy.TryMatch( boundary.GetRelativePath( path ), patterns, out matched ));
    }
}
