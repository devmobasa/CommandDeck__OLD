using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardRichMetadataEscapingSurvivesReopenAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-rich-escaping-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            var targets = Enumerable.Range( 0, 8 ).Select( index => $"https://fixture{index}.invalid/?" + new string( '&', 7900 ) ).ToArray( );
            using ( ClipboardHistoryStore store = new( path, new( ) ) )
            {
                EnableRichFixture( store );
                await FeedRichAdmissionAsync( store, ["text/uri-list"], [Encoding.UTF8.GetBytes( string.Join( '\n', targets ) )] );
                True( ClipboardDatabaseScalar( path, "SELECT length(cast(metadata AS blob)) FROM clipboard_projections" ) > 256 * 1024,
                    "valid URI bytes can expand beyond 256 KiB when encoded as JSON" );

                await FeedRichAdmissionAsync( store, ["text/uri-list", "text/plain"],
                    ["https://first.invalid/"u8.ToArray( ), "https://second.invalid/"u8.ToArray( )] );
                var ambiguous = store.SnapshotEntries( QueryParser.Parse( "first.invalid" ).Expression, false ).Single( );
                var ambiguousRevision = store.ReadDocument( ambiguous.Id, ambiguous.Incarnation )!.Summary.Revision;
                var metadata = store.ReadProjectionMetadata( ambiguous.Id, ambiguous.Incarnation, ambiguousRevision )!;
                Equal( 1, metadata.Destinations.Length );
                True( metadata.OpenDestination is null, "conflicting main URL candidates cannot regain an Open target through metadata" );
            }

            using ClipboardHistoryStore reopened = new( path, new( ) );
            var identity = reopened.SnapshotEntries( QueryParser.Parse( "fixture0" ).Expression, false ).Single( );
            var revision = reopened.ReadDocument( identity.Id, identity.Incarnation )!.Summary.Revision;
            True( reopened.ReadProjectionMetadata( identity.Id, identity.Incarnation, revision )!.Destinations.SequenceEqual( targets ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardRichImagePinBudgetAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-rich-pin-budget-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            using ( ClipboardHistoryStore store = new( path, new( )
            {
                MaximumEntries = 10
            } ) )
            {
                EnableRichFixture( store, 64 );
                if ( ImagePreviewToolsAvailable( ) )
                {
                    await FeedRichAdmissionAsync( store, ["image/png"], [Convert.FromBase64String( PreviewPngBase64 )] );
                    var identity = store.SnapshotEntries( new( ), false ).Single( );
                    var revision = store.ReadDocument( identity.Id, identity.Incarnation )!.Summary.Revision;
                    var metadata = store.ReadProjectionMetadata( identity.Id, identity.Incarnation, revision )!;
                    Equal( 32, metadata.ImageWidth );
                    Equal( 32, metadata.ImageHeight );
                    store.Clear( );
                }

                // Controlled decoder output isolates payload accounting from PNG fixture fidelity.
                ImagePreviewRenderer decoder = new( ( _, _, _, _, _ ) => ValueTask.FromResult( new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = "32 32\ndata:image/png;base64," + PreviewPngBase64,
                    StandardError = ""
                } ), ( ) => true );
                ClipboardPayloadProjection projections = new( new( "/missing-html-fixture" ), decoder );
                for ( var index = 0; index < 5; index++ )
                {
                    var bytes = new byte[8 * 1024 * 1024];
                    Convert.FromBase64String( PreviewPngBase64 ).CopyTo( bytes, 0 );
                    bytes[^1] = ( byte ) index;
                    await FeedRichAdmissionAsync( store, ["image/png"], [bytes], projections );
                    var unpinned = store.Search( "", 10 ).Single( static entry => !entry.Pinned );
                    Equal( index < 4, store.Pin( unpinned.Id, true ) );
                }

                Equal( 4, store.Search( "", 10 ).Count( static entry => entry.Pinned ) );
                Equal( 40L * 1024 * 1024, ClipboardDatabaseScalar( path, "SELECT sum(charge) FROM clipboard_payloads" ) );
            }

            using ClipboardHistoryStore reopened = new( path, new( )
            {
                MaximumEntries = 10
            } );
            Equal( 5, reopened.Search( "", 10 ).Count );
            Equal( 4, reopened.Search( "", 10 ).Count( static entry => entry.Pinned ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardRichClearDuringProjectionAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-rich-clear-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            using ClipboardHistoryStore store = new( path, new( ) );
            EnableRichFixture( store );
            TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource released = new( TaskCreationOptions.RunContinuationsAsynchronously );
            ClipboardHtmlProjectionService html = new( Environment.ProcessPath!, async ( _, _, _, _, _ ) =>
            {
                entered.SetResult( );
                await released.Task;
                return new ProcessResult { ExitCode = 0, StandardOutput = "{\"available\":true,\"text\":\"must not publish\",\"truncated\":false}", StandardError = "" };
            } );
            var capture = FeedRichAdmissionAsync( store, ["text/html"], ["<b>private fixture</b>"u8.ToArray( )], new( html ) );
            try
            {
                await entered.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                store.Clear( );
                Equal( 0, store.Search( "", 10 ).Count );
                True( ClipboardDatabaseScalar( path, "SELECT sum(charge) FROM clipboard_payloads" ) > 0,
                    "Clear retains charges while the projection still holds its payload lease" );
            }
            finally
            {
                released.TrySetResult( );
            }

            await ThrowsAsync<InvalidOperationException>( ( ) => capture );
            Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM entries" ) );
            Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads" ) );
            Equal( 0, Directory.GetFiles( path + ".payloads" ).Length );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
