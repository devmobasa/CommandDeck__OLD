namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardRichBrowserFiltersKindsAndPresentsInertMetadataAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-rich-browser-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new( ) );
            EnableRichFixture( store );
            ImagePreviewRenderer decoder = new( ( _, _, _, _, _ ) => ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "32 32\ndata:image/png;base64," + PreviewPngBase64,
                StandardError = ""
            } ), ( ) => true );
            ClipboardPayloadProjection projections = new( new( "/missing-html-fixture" ), decoder );
            await FeedRichAdmissionAsync( store, ["text/plain"], ["plain note"u8.ToArray( )], projections );
            await FeedRichAdmissionAsync( store, ["text/uri-list", "text/plain"],
                ["https://example.invalid/page"u8.ToArray( ), "https://example.invalid/page"u8.ToArray( )], projections );
            await FeedRichAdmissionAsync( store, ["text/uri-list"], ["file:///tmp/first.txt\r\nfile:///tmp/second.txt\r\n"u8.ToArray( )], projections );
            await FeedRichAdmissionAsync( store, ["image/png"], [Convert.FromBase64String( PreviewPngBase64 )], projections );

            ClipboardHistoryService service = new( store, projections: projections );
            service.RegisterOwner( "one" );
            var instance = Guid.NewGuid( ).ToString( "N" );
            var session = service.Open( "one", instance, 1 );
            Equal( 4, service.Query( "one", session, new( ), false, 1 ).Entries.Count );
            var image = service.Query( "one", session, new( ), false, 2, kind: "images" ).Entries.Single( );
            Equal( "images", image.Presentation.Kind );
            Equal( 32, image.Presentation.ImageWidth );
            Equal( 32, image.Presentation.ImageHeight );
            Equal( 32, image.Id.Length );
            var link = service.Query( "one", session, new( ), false, 3, kind: "links" ).Entries.Single( );
            True( link.Presentation.HasOpenDestination, "single agreeing URL exposes an Open destination flag" );
            Equal( 1, link.Presentation.DestinationCount );
            var preview = service.Preview( "one", session, link.Id, link.Incarnation, link.Revision, 3, 0 );
            Equal( "https://example.invalid/page", preview.OpenDestination );
            Equal( "https://example.invalid/page", preview.Slice.Text );
            var files = service.Query( "one", session, new( ), false, 4, kind: "files" ).Entries.Single( );
            Equal( 2, files.Presentation.DestinationCount );
            True( !files.Presentation.HasOpenDestination, "two references have no single Open destination" );
            Equal( 1, service.Query( "one", session, QueryParser.Parse( "metadata:png" ).Expression, false, 5 ).Entries.Count );
            Equal( 1, service.Query( "one", session, QueryParser.Parse( "metadata:files" ).Expression, false, 6 ).Entries.Count );
            var textPage = service.Query( "one", session, new( ), false, 7, kind: "text", limit: 1 );
            Throws<InvalidOperationException>( ( ) => service.Query( "one", session, new( ), false, 7, textPage.Cursor, kind: "links" ) );

            var imagePage = service.Query( "one", session, new( ), false, 8, kind: "images" ).Entries.Single( );
            var response = await service.HandleAsync( "one", new( )
            {
                RequestId = 1,
                Operation = ProtocolOperations.ClipboardPreview,
                Clipboard = new( )
                {
                    SessionId = session,
                    EntryId = imagePage.Id,
                    Incarnation = imagePage.Incarnation,
                    EntryRevision = imagePage.Revision,
                    QueryGeneration = 8
                }
            }, default );
            True( response.Preview!.ImageSource!.StartsWith( "data:image/png;base64,", StringComparison.Ordinal ), "image preview is an inert data URL" );
            Equal( "", response.Preview.Slice.Text );

            foreach ( var (entryId, kindFilter) in new[] { (new string( 'a', 40 ), null), (imagePage.Id, "video") } )
            {
                Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new( )
                {
                    RequestId = 2,
                    Operation = kindFilter is null ? ProtocolOperations.ClipboardPreview : ProtocolOperations.ClipboardQuery,
                    Clipboard = new( )
                    {
                        SessionId = session,
                        EntryId = entryId,
                        Incarnation = imagePage.Incarnation,
                        EntryRevision = 1,
                        QueryGeneration = 1,
                        KindFilter = kindFilter
                    }
                } ) );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
