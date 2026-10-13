namespace CommandDeck.Specs;

public static partial class Program
{
    // An incremental batch that moved a directory streams the new directory and
    // removes the old one. Readers must never see both, or neither.
    private static async Task StagedPathBatchesPublishAsOneTransactionAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-staged-batch-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            IStagedPathChangeStore staged = catalog;
            await catalog.ReplaceAllAsync(
                BatchItems(
                    BatchFile( "/batch/before/source.txt", "batchsource" ),
                    BatchFile( "/batch/before/notes.txt", "batchnotesold" ) ),
                progress: null,
                CancellationToken.None );

            // A change that fails leaves nothing of its batch published.
            await ThrowsAsync<InvalidOperationException>( async ( ) => await staged.ReplacePathsAsync(
            [
                new StagedPathChange( "/batch/after", true, BatchItems( BatchFile( "/batch/after/source.txt", "batchsource" ) ) ),
                new StagedPathChange( "/batch/before", true, BatchItems( ) ),
                new StagedPathChange( "/batch/failing", true, BatchItems( BatchFile( "/elsewhere/stray.txt", "batchstray" ) ) )
            ], CancellationToken.None ) );
            Equal(
                StableId.Create( ResultKinds.File, "/batch/before/source.txt" ),
                (await SearchAsync( catalog, "batchsource" )).Single( ).Id );

            // Changes apply in order, as ApplyPathChangesAsync applies them: a
            // later change replaces the rows an earlier directory staged for its
            // path, stable IDs repeat only across changes, and within a change
            // the first item with a stable ID wins.
            var published = await staged.ReplacePathsAsync(
            [
                new StagedPathChange(
                    "/batch/after",
                    true,
                    BatchItems(
                        BatchFile( "/batch/after/source.txt", "batchsource" ),
                        BatchFile( "/batch/after/notes.txt", "batchnotesold" ),
                        BatchFile( "/batch/after/source.txt", "batchduplicate" ) ) ),
                new StagedPathChange( "/batch/before", true, BatchItems( ) ),
                new StagedPathChange(
                    "/batch/after/notes.txt",
                    false,
                    BatchItems( BatchFile( "/batch/after/notes.txt", "batchnotesnew" ) ) )
            ], CancellationToken.None );

            Equal( 3L, published );
            Equal(
                StableId.Create( ResultKinds.File, "/batch/after/source.txt" ),
                (await SearchAsync( catalog, "batchsource" )).Single( ).Id );
            Equal( 0, (await SearchAsync( catalog, "batchduplicate" )).Count );
            Equal( 0, (await SearchAsync( catalog, "batchnotesold" )).Count );
            Equal(
                StableId.Create( ResultKinds.File, "/batch/after/notes.txt" ),
                (await SearchAsync( catalog, "batchnotesnew" )).Single( ).Id );
            Equal( 2L, (await catalog.GetStatsAsync( CancellationToken.None )).ItemCount );
            var integrity = await catalog.CheckIntegrityAsync( CancellationToken.None );
            True( integrity.Succeeded && !integrity.RebuildRequired, string.Join( ' ', integrity.Messages ) );
            Equal( 0L, await CountUnlinkedCatalogRowsAsync( database ) );
        }
        finally
        {
            DeleteDatabase( database );
        }

        static IndexedItem BatchFile( string path, string body ) => new( )
        {
            StableId = StableId.Create( ResultKinds.File, path ),
            Kind = ResultKinds.File,
            Title = Path.GetFileName( path ),
            Path = path,
            Body = body,
            ProviderId = ProviderIds.Files,
            SourceId = SourceIds.Files
        };

        static async IAsyncEnumerable<IndexedItem> BatchItems( params IndexedItem[] items )
        {
            foreach ( var item in items )
            {
                yield return item;
            }

            await Task.CompletedTask;
        }
    }
}
