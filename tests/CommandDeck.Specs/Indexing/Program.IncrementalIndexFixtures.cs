namespace CommandDeck.Specs;

public static partial class Program
{
    private static CommandDeckOptions CreateIncrementalIndexOptions(
        string database,
        string root,
        string? secondRoot = null,
        int debounceMilliseconds = 40,
        int maximumLatencyMilliseconds = 300 ) =>
        new( )
        {
            Storage = new StorageOptions { DatabasePath = database },
            Indexing = new IndexingOptions
            {
                Roots = secondRoot is null
                    ? [new IndexRootOptions { Path = root, IncludeContentInSearch = true }]
                    :
                    [
                        new IndexRootOptions { Path = root, IncludeContentInSearch = true },
                        new IndexRootOptions { Path = secondRoot, IncludeContentInSearch = true }
                    ],
                IndexDesktopApplications = false,
                IndexOmarchyCommands = false,
                IncrementalDebounceMilliseconds = debounceMilliseconds,
                IncrementalMaximumLatencyMilliseconds = maximumLatencyMilliseconds
            }
        };

    private static async Task RewritePreservingMetadataAsync( string path, string contents )
    {
        var modified = File.GetLastWriteTimeUtc( path );
        if ( new FileInfo( path ).Length != System.Text.Encoding.UTF8.GetByteCount( contents ) )
        {
            throw new InvalidOperationException( "The rewrite must preserve the file size." );
        }

        await File.WriteAllTextAsync( path, contents );
        File.SetLastWriteTimeUtc( path, modified );
    }

    private static async Task<IReadOnlyList<SearchResult>> SearchAsync(
        ISearchCatalog catalog,
        string query ) =>
        await catalog.SearchAsync(
            QueryParser.Parse( query ),
            10,
            CancellationToken.None );

    private static Task<bool> WaitForCatalogResultAsync(
        ISearchCatalog catalog,
        string query,
        Func<SearchResult, bool> predicate,
        TimeSpan timeout ) =>
        WaitUntilAsync(
            async ( ) => (await SearchAsync( catalog, query )).Any( predicate ),
            timeout );

    private static async Task<bool> WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout )
    {
        using CancellationTokenSource timeoutSource = new( timeout );
        while ( !timeoutSource.IsCancellationRequested )
        {
            if ( await condition( ) )
            {
                return true;
            }

            try
            {
                await Task.Delay( 20, timeoutSource.Token );
            }
            catch ( OperationCanceledException )
            {
                break;
            }
        }

        return false;
    }

    private static string CreateIncrementalSpecRoot( ) =>
        Path.Combine( Path.GetTempPath( ), $"commanddeck-incremental-spec-{Guid.NewGuid( ):N}" );

    private static void DeleteIncrementalSpecRoot( string root )
    {
        if ( Directory.Exists( root ) )
        {
            Directory.Delete( root, recursive: true );
        }
    }

}
