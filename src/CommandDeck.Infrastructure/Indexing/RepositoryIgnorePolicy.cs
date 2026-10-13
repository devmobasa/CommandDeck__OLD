using System.Collections.Concurrent;

namespace CommandDeck;

internal sealed class RepositoryIgnorePolicy( IndexPolicyInputs inputs )
{
    private const int MaximumPatterns = 16_384;
    private const int MaximumPatternCharacters = 1024;
    private readonly ConcurrentDictionary<string, CachedRules> _rules = new( StringComparer.Ordinal );

    public IndexExclusion? Match( string path, bool directory )
    {
        var context = RepositoryContext.Outside;
        foreach ( var ancestor in Ancestors( path, directory ) )
        {
            var exclusion = EnterDirectory( context, ancestor, markers: null, out context );
            if ( exclusion is not null )
            {
                return exclusion;
            }
        }
        return directory ? null : MatchScopes( context.Scopes, path, directory: false );
    }

    // Applies the rules in force at the parent to a directory, then adds the
    // rules that directory contributes for its own descendants.
    public IndexExclusion? EnterDirectory( RepositoryContext parent, string directory,
        IReadOnlySet<string>? markers, out RepositoryContext context )
    {
        context = parent;

        // A nested ignore file cannot resurrect an excluded parent. Check the
        // directory before reading its rules or crossing a nested repository.
        var exclusion = MatchScopes( parent.Scopes, directory, directory: true );
        if ( exclusion is not null )
        {
            return exclusion;
        }

        var repository = (markers ?? inputs.Markers( directory )).Contains( ".git" );
        if ( !repository && !parent.InRepository )
        {
            return null;
        }

        List<ScopedRules> scopes = repository ? [] : [.. parent.Scopes];
        if ( repository )
        {
            var excludes = RepositoryExcludes( directory );
            if ( excludes.Error is not null )
            {
                return excludes.Error;
            }
            if ( excludes.Path is not null )
            {
                scopes.Add( ReadRules( directory, excludes.Path ) );
            }
        }
        scopes.Add( ReadRules( directory, Path.Combine( directory, ".gitignore" ) ) );

        context = new RepositoryContext( [.. scopes], InRepository: true );
        return null;
    }

    public IndexExclusion? MatchFile( RepositoryContext parent, string path )
    {
        foreach ( var scope in parent.Scopes )
        {
            var current = ReadRules( scope.Directory, scope.Source );
            if ( current.Fingerprint != scope.Fingerprint )
            {
                // A changed rule can exclude a directory that this traversal
                // already entered. Recheck ancestry until a fresh context is
                // built, rather than testing a directory-only rule as a file.
                return Match( path, directory: false );
            }
        }

        return MatchScopes( parent.Scopes, path, directory: false );
    }

    internal static string[] Ancestors( string path, bool includeLeaf )
    {
        List<string> ancestors = [];
        var current = includeLeaf ? path : Path.GetDirectoryName( path );
        while ( current is not null )
        {
            ancestors.Add( current );
            current = Path.GetDirectoryName( current );
        }
        ancestors.Reverse( );
        return ancestors.ToArray( );
    }

    private IndexExclusion? MatchScopes( ScopedRules[] scopes, string path, bool directory )
    {
        IndexExclusion? match = null;
        foreach ( var scope in scopes )
        {
            // Keep the ancestry, but refresh a rule file changed since that
            // directory was entered. Unchanged files use the metadata cache.
            var current = ReadRules( scope.Directory, scope.Source );
            if ( current.Error is not null )
            {
                return new IndexExclusion(
                IndexExclusionReasons.IgnoreRulesUnavailable, $"{current.Source}: {current.Error}" );
            }
            var relative = Path.GetRelativePath( scope.Directory, path );
            foreach ( var pattern in current.Patterns )
            {
                if ( pattern.Matches( relative, directory ) )
                {
                    match = pattern.Negated ? null : new IndexExclusion(
                        IndexExclusionReasons.RepositoryIgnore, $"{scope.Source}:{pattern.Line}: {pattern.Text}" );
                }
            }
        }
        return match;
    }

    private ScopedRules ReadRules( string directory, string file )
    {
        inputs.RegisterControlFile( file, directory );
        var text = inputs.ReadText( file );
        if ( !_rules.TryGetValue( file, out var cached ) || cached.Fingerprint != text.Fingerprint )
        {
            cached = ParseRules( text );
            _rules[file] = cached;
        }
        return new ScopedRules( directory, file, cached.Patterns, cached.Error, cached.Fingerprint );
    }

    private static CachedRules ParseRules( PolicyText text )
    {
        if ( text.Error is not null )
        {
            return new CachedRules( text.Fingerprint, [], text.Error );
        }
        List<GitIgnorePattern> rules = [];
        using StringReader reader = new( text.Text.TrimStart( '\uFEFF' ) );
        var line = 0;
        while ( reader.ReadLine( ) is { } value )
        {
            line++;
            if ( value.Length > MaximumPatternCharacters || rules.Count >= MaximumPatterns )
            {
                return new CachedRules( text.Fingerprint, [], "rule-file-limit-exceeded" );
            }
            var pattern = GitIgnorePattern.Parse( value, line );
            if ( pattern is not null )
            {
                rules.Add( pattern );
            }
        }
        return new CachedRules( text.Fingerprint, rules.ToArray( ), null );
    }

    private (string? Path, IndexExclusion? Error) RepositoryExcludes( string root )
    {
        var gitDirectory = Path.Combine( root, ".git" );
        if ( !Directory.Exists( gitDirectory ) )
        {
            inputs.RegisterControlFile( gitDirectory, root );
            var pointer = inputs.ReadText( gitDirectory );
            if ( pointer.Error is not null )
            {
                return (null, new IndexExclusion(
                IndexExclusionReasons.IgnoreRulesUnavailable, $"{gitDirectory}: {pointer.Error}" ));
            }
            if ( !pointer.Text.StartsWith( "gitdir: ", StringComparison.Ordinal ) )
            {
                return (null, null);
            }
            gitDirectory = ResolvePointer( root, pointer.Text[8..].TrimEnd( '\r', '\n' ) );
            if ( gitDirectory is null )
            {
                return (null, null);
            }
        }
        inputs.RegisterControlFile( Path.Combine( gitDirectory, "commondir" ), root );
        var common = inputs.ReadText( Path.Combine( gitDirectory, "commondir" ) );
        if ( common.Error is not null )
        {
            return (null, new IndexExclusion(
            IndexExclusionReasons.IgnoreRulesUnavailable, $"{gitDirectory}/commondir: {common.Error}" ));
        }
        if ( common.Text.Length > 0 )
        {
            gitDirectory = ResolvePointer( gitDirectory, common.Text.TrimEnd( '\r', '\n' ) );
        }
        return (gitDirectory is null ? null : Path.Combine( gitDirectory, "info", "exclude" ), null);
    }

    private static string? ResolvePointer( string directory, string value )
    {
        try
        {
            return Path.GetFullPath( value, directory );
        }
        catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException )
        {
            return null;
        }
    }

    private sealed record CachedRules( string Fingerprint, GitIgnorePattern[] Patterns, string? Error );
    internal sealed record ScopedRules(
        string Directory, string Source, GitIgnorePattern[] Patterns, string? Error, string Fingerprint );
}

// The ignore rules in force below one directory, outermost first.
internal sealed record RepositoryContext( RepositoryIgnorePolicy.ScopedRules[] Scopes, bool InRepository )
{
    public static RepositoryContext Outside { get; } = new( [], false );
}

internal sealed record IndexExclusion( string Reason, string? Rule );
