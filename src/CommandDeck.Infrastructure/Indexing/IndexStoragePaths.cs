namespace CommandDeck;

// The index can live beneath a search root. Never index its SQLite files or
// journal their writes: the journal would otherwise continuously notify itself.
internal sealed class IndexStoragePaths
{
    private readonly string[] _exactPaths;
    private readonly string[] _prefixes;

    public IndexStoragePaths( string databasePath )
    {
        var database = PathExpansion.Expand( databasePath );
        var directory = Path.GetDirectoryName( database )!;
        var canonicalDatabase = CanonicalPathBoundary.TryCreate( directory, out var boundary )
            ? Path.Combine( boundary!.CanonicalRoot, Path.GetFileName( database ) )
            : database;

        // Every traversed entry is checked, so the names are built once.
        string[] databases = [database, canonicalDatabase];
        _exactPaths = [.. databases.SelectMany( static value =>
            new[] { value, value + "-wal", value + "-shm", value + "-journal" } )];
        _prefixes = [.. databases.Select( static value => value + ".index-" )];
    }

    public bool Contains( string path )
    {
        foreach ( var exact in _exactPaths )
        {
            if ( string.Equals( path, exact, StringComparison.Ordinal ) )
            {
                return true;
            }
        }

        foreach ( var prefix in _prefixes )
        {
            if ( path.StartsWith( prefix, StringComparison.Ordinal ) )
            {
                return true;
            }
        }

        return false;
    }
}
