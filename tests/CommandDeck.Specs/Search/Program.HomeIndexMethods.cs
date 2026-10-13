namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FileScansSkipUnreadableDirectoriesAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var root = CreateIncrementalSpecRoot( );
        var blocked = Path.Combine( root, "unreadable" );
        Directory.CreateDirectory( blocked );
        await File.WriteAllTextAsync( Path.Combine( root, "readable.txt" ), "visible marker" );
        await File.WriteAllTextAsync( Path.Combine( blocked, "inaccessible.txt" ), "unreadable marker" );
        File.SetUnixFileMode( blocked, UnixFileMode.None );
        try
        {
            using FileIndexSource source = new( new IndexingOptions
            {
                Roots = [new IndexRootOptions { Path = root }]
            }, new PrivacyOptions( ) );
            List<IndexedItem> items = [];
            await foreach ( var item in source.ScanAsync( null, CancellationToken.None ) )
            {
                items.Add( item );
            }
            Equal( "readable.txt", items.Single( ).Title );
            True( source.SnapshotWorkItems( ).Any( item => item.Reason == "directory-read-failed" ) );
            Equal( "readable.txt", (await source.IndexDirectoryAsync( root, CancellationToken.None )).Single( ).Title );
        }
        finally
        {
            File.SetUnixFileMode( blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task HomeSearchDefaultsCoverUserFilesAsync( )
    {
        var home = CreateIncrementalSpecRoot( );
        string[] paths = ["Downloads/photo.png", "code/project/source.cs", ".local/notes/.hidden.txt",
            "Documents/note.txt", "Documents/.hidden-note.txt"];
        try
        {
            foreach ( var path in paths )
            {
                var fullPath = Path.Combine( home, path );
                Directory.CreateDirectory( Path.GetDirectoryName( fullPath )! );
                await File.WriteAllTextAsync( fullPath, "searchable document body" );
            }
            await File.WriteAllTextAsync( Path.Combine( home, ".env" ), "excluded marker" );

            var defaults = CommandDeckOptions.CreateDefault( ).Indexing;
            IndexingOptions indexing = new( )
            {
                Roots = defaults.Roots.Select( root => new IndexRootOptions
                {
                    Path = root.Path.Replace( "~", home, StringComparison.Ordinal ),
                    IncludeHidden = root.IncludeHidden,
                    IncludeFileNameInSearch = root.IncludeFileNameInSearch,
                    IncludeMetadataInSearch = root.IncludeMetadataInSearch,
                    IncludeContentInSearch = root.IncludeContentInSearch,
                    ExcludeGlobs = root.ExcludeGlobs
                } ).ToList( )
            };
            using FileIndexSource source = new( indexing, new PrivacyOptions( ) );
            List<IndexedItem> items = [];
            await foreach ( var item in source.ScanAsync( null, CancellationToken.None ) )
            {
                items.Add( item );
            }

            foreach ( var path in paths )
            {
                var item = items.Single( item => item.Kind == ResultKinds.File &&
                                                 item.Path == Path.Combine( home, path ) );
                True( item.IncludeTitleInSearch );
                True( item.IncludeMetadataInSearch );
                Equal( path.StartsWith( "Documents/", StringComparison.Ordinal )
                    ? "searchable document body" : null, item.Body );
            }
            True( defaults.Roots.All( root => root.Path != "~/Projects" ) );
            True( items.All( item => item.Path != Path.Combine( home, ".env" ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( home );
        }
    }

    private static async Task IndexStorageDoesNotFeedItsOwnWatcherAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        Directory.CreateDirectory( root );
        try
        {
            var note = Path.Combine( root, "real-note.txt" );
            await File.WriteAllTextAsync( note, "original marker" );
            await File.WriteAllTextAsync( Path.Combine( root, "catalog.db-notes.txt" ), "ordinary neighbor" );
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ), root );
            SqliteCatalog inner = new( options.Storage );
            await inner.InitializeAsync( CancellationToken.None );
            await using TrackingSearchCatalog catalog = new( inner, inner );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );
            await indexer.ReindexAsync( null, CancellationToken.None );
            Equal( 2L, (await inner.GetStatsAsync( CancellationToken.None )).FileCount );
            using FileIndexSource source = new( options.Indexing, options.Privacy, options.Storage.DatabasePath );
            Equal( 0, (await source.IndexPathAsync( options.Storage.DatabasePath, CancellationToken.None )).Count );
            Equal( 2, (await source.IndexDirectoryAsync( root, CancellationToken.None )).Count );

            await File.WriteAllTextAsync( note, "updated marker" );
            True( await WaitForCatalogResultAsync( catalog, "updated marker",
                result => result.Title == "real-note.txt", TimeSpan.FromSeconds( 2 ) ) );
            True( await WaitUntilAsync( async ( ) =>
                (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).PendingEventCount == 0,
                TimeSpan.FromSeconds( 2 ) ) );
            var applies = catalog.ApplyPathChangesCount;
            await Task.Delay( 400 );
            Equal( applies, catalog.ApplyPathChangesCount );
            Equal( 0L, (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).PendingEventCount );
            Equal( 2L, (await inner.GetStatsAsync( CancellationToken.None )).FileCount );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }
}
