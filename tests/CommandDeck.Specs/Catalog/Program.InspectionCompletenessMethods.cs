namespace CommandDeck.Specs;

public static partial class Program
{
    private static IndexWorkItem[] InspectionFixtureWorkItems( ) =>
        Enumerable.Range( 0, 300 ).Select( number => new IndexWorkItem
        {
            Id = $"work:{number:D3}",
            State = IndexWorkStates.Pending,
            Root = "/safe-root",
            RelativePath = $"item-{number:D3}.txt",
            Reason = "pending",
            UpdatedUnixMilliseconds = number
        } ).ToArray( );

    private static async Task InspectionPagesBeyondStoredSampleAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-inspection-completeness-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) }
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceGenerationAsync(
                CatalogOperationItems( ),
                static ( ) => [],
                InspectionFixtureWorkItems,
                progress: null,
                CancellationToken.None );

            await using IndexCoordinator indexer = new( catalog, catalog, options );
            var page = await indexer.InspectAsync( 256, 20, default );
            Equal( 300L, page.TotalWorkItemCount );
            Equal( 300L, page.PendingItemCount );
            Equal( 20, page.WorkItems.Count );
            Equal( "work:256", page.WorkItems[0].Id );
            True( page.WorkItemsTruncated );

            indexer.ReportWatcherOverflow( Path.Combine( root, "pending" ) );
            var mixed = await indexer.InspectAsync( 256, 20, default );
            True( !mixed.WorkItemCountsExact, "a capped merge must identify lower-bound counts" );
            True( mixed.TotalWorkItemCount >= 300 );
            True( mixed.PendingItemCount >= 300 );
            True( mixed.WorkItemsTruncated, "the capped merge cannot claim end of list" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task VerifyInspectionSocketBeyondSampleAsync(
        UnixProtocolClient client,
        SqliteCatalog catalog,
        CancellationToken cancellationToken )
    {
        await catalog.ReplaceGenerationAsync(
            CatalogOperationItems( ),
            static ( ) => [],
            InspectionFixtureWorkItems,
            progress: null,
            cancellationToken );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 139,
            Operation = ProtocolOperations.CatalogInspect,
            Offset = 256,
            Limit = 20
        }, cancellationToken );
        var message = await ReadMessageAsync(
            client, 139, ProtocolMessageTypes.CatalogInspection, cancellationToken );
        ArgumentNullException.ThrowIfNull( message?.CatalogInspection );
        True( message.CatalogInspection.TotalWorkItemCount >= 300 );
        True( message.CatalogInspection.PendingItemCount >= 300 );
        if ( message.CatalogInspection.WorkItemCountsExact )
        {
            Equal( 20, message.CatalogInspection.WorkItems.Count );
            Equal( "work:256", message.CatalogInspection.WorkItems[0].Id );
        }
        else
        {
            True( message.CatalogInspection.WorkItemsTruncated,
                "an incomplete socket page cannot claim the end of work" );
        }
    }
}
