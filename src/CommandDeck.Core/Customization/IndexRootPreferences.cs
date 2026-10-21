namespace CommandDeck;

public static class IndexRootPaths
{
    public static string Normalize( string path ) =>
        Path.TrimEndingDirectorySeparator( PathExpansion.Expand( path ) );
}

public sealed class AddedIndexRoot
{
    public required IndexRootOptions Options { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class IndexRootPreferences
{
    public const int MaximumAddedRoots = 128;
    public List<AddedIndexRoot> Added { get; init; } = [];
    public List<string> DisabledConfiguredPaths { get; init; } = [];

    public void Validate( )
    {
        if ( Added is null || DisabledConfiguredPaths is null || Added.Count > MaximumAddedRoots ||
            DisabledConfiguredPaths.Count > 512 )
        {
            throw new InvalidOperationException( "Indexed folders accept at most 128 added and 512 disabled roots." );
        }
        foreach ( var root in Added )
        {
            ConfigurationValidator.ValidateIndexRoot( root?.Options );
        }
        foreach ( var path in DisabledConfiguredPaths )
        {
            ConfigurationValidator.ValidateIndexRoot( new IndexRootOptions { Path = path } );
        }
    }
}

public sealed class ManagedIndexRoot
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string Source { get; init; }
    public bool Enabled { get; init; }
    public required string Status { get; init; }
    public string? Issue { get; init; }
    public IndexRootStatus? Runtime { get; init; }
}

public sealed class IndexRootsResult
{
    public bool ReindexPending { get; init; }
    public IndexProgress? Progress { get; init; }
    public string? ReindexError { get; init; }
    public List<ManagedIndexRoot> Roots { get; init; } = [];
}
