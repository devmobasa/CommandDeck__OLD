namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardBrowserBoundsEncodedSummaryMetadata( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-summary-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new( ) );
            store.Enabled = true;
            var source = new string( '\t', 500 ) + "👩‍👩‍👧‍👦 café";
            store.Capture( source );
            var identity = store.SnapshotEntries( new( ), false ).Single( );
            var document = store.ReadDocument( identity.Id, identity.Incarnation )!;
            var keywords = Enumerable.Range( 0, 16 ).Select( index => index.ToString( "x" ) + string.Concat( Enumerable.Repeat( "😀", 15 ) ) ).ToArray( );
            store.MutateEntry( identity.Id, identity.Incarnation, document.Summary.Revision, "keywords", keywords );
            document = store.ReadDocument( identity.Id, identity.Incarnation )!;
            var encoded = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( document.Summary, CommandDeckJsonContext.Default.ClipboardEntrySummary );
            True( encoded.Length <= 4096, "summary includes bounded final JSON metadata and escaped text" );
            Equal( 16, document.Summary.Keywords.Count );
            True( System.Text.Encoding.UTF8.GetByteCount( document.Summary.Summary ) <= 512, "summary text retains its independent byte cap" );
            Equal( source, document.Text );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardBrowserSnapshotsSurviveRecopyAndSkipTombstones( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-browser-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
            store.Enabled = true;
            for ( var index = 0; index < 130; index++ )
            {
                store.Capture( "entry " + index );
            }

            ClipboardHistoryService service = new( store );
            service.RegisterOwner( "one" );
            var session = service.Open( "one", Guid.NewGuid( ).ToString( "N" ), 1 );
            var first = service.Query( "one", session, new( ), false, 1 );
            Equal( 50, first.Entries.Count );
            True( first.Cursor is not null, "first page has a bounded cursor" );
            var identities = store.SnapshotEntries( new( ), false );
            var removed = identities[50];
            var oldText = store.ReadDocument( removed.Id, removed.Incarnation )!.Text;
            store.Delete( removed.Id );
            store.Capture( oldText );
            store.Capture( "entry 0" );
            store.Capture( "new arrival" );

            Throws<InvalidOperationException>( ( ) => service.Query( "one", session, new( ), true, 1, first.Cursor ) );
            var second = service.Query( "one", session, new( ), false, 1, first.Cursor );
            True( second.NewerAvailable, "recopy and new data advertise refresh" );
            True( second.Entries.All( entry => entry.Id != removed.Id ), "recreated entry cannot fill its old snapshot position" );
            Throws<InvalidOperationException>( ( ) => service.Query( "one", session, new( ), false, 1, first.Cursor ) );
            var last = service.Query( "one", session, new( ), false, 1, second.Cursor );
            Equal( 29, last.Entries.Count );
            True( last.Cursor is null, "paging terminates" );
            var all = first.Entries.Concat( second.Entries ).Concat( last.Entries ).ToArray( );
            Equal( 129, all.Select( entry => entry.Id ).Distinct( ).Count( ) );
            True( all.All( entry => entry.Summary != "new arrival" ), "new capture does not enter an existing snapshot" );
            Equal( 100, service.Query( "one", session, new( ), false, 2, limit: 100 ).Entries.Count );
            Equal( 1, service.Query( "one", session, new( ), false, 3, limit: 1 ).Entries.Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardBrowserRejectsStaleAndCrossOwnerActions( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-browser-owner-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
            store.Enabled = true;
            store.Capture( "private café\r\n<script>inert</script>" );
            ClipboardHistoryService service = new( store );
            service.RegisterOwner( "one" );
            service.RegisterOwner( "two" );
            var session = service.Open( "one", Guid.NewGuid( ).ToString( "N" ), 1 );
            QueryExpression expression = new( )
            {
                Clauses = [new( ) { Text = "cafe" }]
            };
            var page = service.Query( "one", session, expression, false, 1 );
            var entry = page.Entries.Single( );
            Throws<InvalidOperationException>( ( ) => service.Preview( "two", session, entry.Id, entry.Incarnation, entry.Revision, 1, 0 ) );
            Throws<InvalidOperationException>( ( ) => service.Preview( "one", session, entry.Id, entry.Incarnation, entry.Revision, 2, 0 ) );
            var preview = service.Preview( "one", session, entry.Id, entry.Incarnation, entry.Revision, 1, 0 );
            True( preview.Slice.Text.Contains( "\r\n<script>" ), "preview is exact inert source data" );
            Equal( 1, preview.Slice.MatchCount );
            service.Mutate( "one", session, entry.Id, entry.Incarnation, entry.Revision, "keywords", ["work", "WORK", " Café "] );
            Throws<InvalidOperationException>( ( ) => service.Mutate( "one", session, entry.Id, entry.Incarnation, entry.Revision, "delete" ) );
            var updated = service.Query( "one", session, new( )
            {
                Clauses = [new( ) { Field = QueryFieldIds.Metadata, Text = "work" }]
            }, false, 2 ).Entries.Single( );
            Equal( 2, updated.Keywords.Count );
            service.Mutate( "one", session, updated.Id, updated.Incarnation, updated.Revision, "pin" );
            var pinned = service.Query( "one", session, new( ), true, 3 ).Entries.Single( );
            True( pinned.Pinned, "pinned filter uses current state" );
            Throws<InvalidOperationException>( ( ) => service.Clear( "one", page.StoreRevision ) );
            service.Clear( "one", store.StoreRevision );
            Throws<InvalidOperationException>( ( ) => service.Query( "one", session, new( ), false, 4 ) );
            Equal( 0, store.Search( "", 10 ).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardBrowserBoundsOwnersGenerationsAndLifetime( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-browser-bounds-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
            ClipboardBrowserClock clock = new( );
            ClipboardHistoryService service = new( store, clock );
            var instance = Guid.NewGuid( ).ToString( "N" );
            service.RegisterOwner( "one" );
            service.CloseGeneration( "one", instance, 1 );
            Throws<InvalidOperationException>( ( ) => service.Open( "one", instance, 1 ) );
            var old = service.Open( "one", instance, 2 );
            service.Open( "one", instance, 2 );
            Throws<InvalidOperationException>( ( ) => service.Open( "one", instance, 2 ) );
            var current = service.Open( "one", instance, 3 );
            Throws<InvalidOperationException>( ( ) => service.Query( "one", old, new( ), false, 1 ) );
            Throws<InvalidOperationException>( ( ) => service.CloseGeneration( "one", instance, 2 ) );
            service.Query( "one", current, new( ), false, 1 );
            service.Handle( "one", new ProtocolRequest
            {
                RequestId = 1,
                Operation = ProtocolOperations.ClipboardClose,
                Clipboard = new( ) { FrontendInstance = instance, OpenGeneration = 3, SessionId = current }
            } );
            current = service.Open( "one", instance, 3 );
            service.Query( "one", current, new( ), false, 1 );
            clock.Timestamp = 120;
            Throws<InvalidOperationException>( ( ) => service.Query( "one", current, new( ), false, 2 ) );
            for ( var index = 0; index < 4; index++ )
            {
                var owner = "client" + index;
                service.RegisterOwner( owner );
                service.Open( owner, instance, 1 );
                service.Open( owner, instance, 1 );
            }

            Throws<InvalidOperationException>( ( ) => service.Open( "one", instance, 3 ) );
            service.RevokeOwner( "client0" );
            Throws<InvalidOperationException>( ( ) => service.Open( "client0", instance, 2 ) );
            service.RemoveOwner( "client0" );
            Throws<InvalidOperationException>( ( ) => service.Open( "client0", instance, 3 ) );
            service.Open( "one", instance, 3 );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private sealed class ClipboardBrowserClock : TimeProvider
    {
        public long Timestamp;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp( ) => Timestamp;
    }
}
