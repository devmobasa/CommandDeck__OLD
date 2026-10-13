namespace CommandDeck;

// A directory that traversal authorized under one root. It carries what the
// directory and its ancestors already decided, so each child is checked on
// its own path instead of re-evaluating every ancestor.
internal sealed class AuthorizedDirectory
{
    private IReadOnlySet<string>? _markers;

    internal AuthorizedDirectory(
        CanonicalPathBoundary boundary,
        IndexRootOptions options,
        string path,
        AuthorizedDirectoryPolicy policy,
        IReadOnlySet<string>? markers )
    {
        Boundary = boundary;
        Options = options;
        Path = path;
        Policy = policy;
        _markers = markers;
    }

    public CanonicalPathBoundary Boundary
    {
        get;
    }
    public IndexRootOptions Options
    {
        get;
    }

    // The canonical directory path; children are enumerated below it.
    public string Path
    {
        get;
    }
    internal AuthorizedDirectoryPolicy Policy
    {
        get;
    }

    internal IReadOnlySet<string> GetMarkers( IndexPolicyInputs inputs ) => _markers ??= inputs.Markers( Path );
}

// What a directory's ancestry settled for its children:
// - IncludedByGlob: the directory or an ancestor matched an include glob, so
//   generated and repository-ignore defaults no longer apply.
// - RootExclusion: the root itself matched an exclude glob, so every child that
//   passes the leaf privacy and hidden checks is excluded by it.
// - Generated/Ignore: the directory was opened only because an include glob
//   could match below it; children that are not included inherit the
//   exclusion it would otherwise have had.
// - Repository: the ignore rules in force for the directory's children.
internal sealed record AuthorizedDirectoryPolicy(
    bool IncludedByGlob,
    IndexExclusion? RootExclusion,
    IndexExclusion? Generated,
    IndexExclusion? Ignore,
    RepositoryContext Repository );
