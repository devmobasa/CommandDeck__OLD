using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExerciseClipboardNativeWatcherAsync( Process server, string root, string socket,
        string? executable, CancellationToken token )
    {
        using ClipboardHistoryStore store = new( Path.Combine( root, "captured.db" ), new ClipboardHistoryOptions( ) );
        var starts = 0;
        await using ClipboardCapture capture = new( store, ( ) =>
        {
            Interlocked.Increment( ref starts );
            var start = ClipboardPrivateFixtureStart( "--clipboard-native-fixture", root );
            start.Environment["WAYLAND_DISPLAY"] = socket;
            if ( executable is not null )
            {
                start.FileName = Path.GetFullPath( executable );
                start.ArgumentList.Clear( );
                start.ArgumentList.Add( "clipboard-host" );
                start.ArgumentList.Add( "--capture" );
            }

            return start;
        } );
        capture.Start( "unused-injected-private-fixture", [] );
        await Task.Delay( 50, token );
        Equal( 0, starts );
        store.Enabled = true;
        await WaitFor( ( ) => capture.Status == "Capturing text" );
        Equal( 0, store.Search( "", 10 ).Count );
        await Send( "raw" );
        await WaitFor( ( ) => store.Search( "", 10 ).Count == 1 );
        var first = store.SnapshotEntries( new( ), false ).Single( );
        Equal( "synthetic\r\n😀é\n", store.ReadDocument( first.Id, first.Incarnation )!.Text );
        await Send( "stall" );
        await Send( "limit" );
        await WaitFor( ( ) => store.Search( "", 10 ).Count == 2 );
        await Send( "stall" );
        await WaitFor( ( ) => ClipboardDatabaseScalar( Path.Combine( root, "captured.db" ), "SELECT count(*) FROM clipboard_payloads WHERE state=0" ) == 1 );
        store.Clear( );
        await WaitFor( ( ) => starts >= 2 && capture.Status == "Capturing text" );
        Equal( 0, store.Search( "", 10 ).Count );
        Equal( 0L, ClipboardDatabaseScalar( Path.Combine( root, "captured.db" ), "SELECT count(*) FROM clipboard_payloads" ) );
        await Send( "gnome-cut" );
        await Send( "ordinary" );
        await WaitFor( ( ) => store.Search( "", 10 ).Count == 1 );
        var second = store.SnapshotEntries( new( ), false ).Single( );
        True( first.Incarnation != second.Incarnation, "Clear revokes the old capture epoch and old browser identity" );
        store.Enabled = false;
        await WaitFor( ( ) => capture.Status == "Capture is disabled" );

        var database = Path.Combine( root, "captured.db" );
        var startsBeforeRich = starts;
        EnableRichFixture( store );
        await WaitFor( ( ) => capture.Status == "Capturing text and rich content" );
        await Send( "rich" );
        await WaitFor( ( ) => store.Search( "", 10 ).Count == 2 );
        Equal( 1L, ClipboardDatabaseScalar( database, "SELECT count(*) FROM clipboard_offers WHERE mime='text/html'" ) );
        var rich = store.SnapshotEntries( QueryParser.Parse( "rich" ).Expression, false ).Single( );
        Equal( "synthetic rich", store.ReadDocument( rich.Id, rich.Incarnation )!.Text );
        Equal( 32, rich.Id.Length );
        Equal( "text", ClipboardDatabaseScalarText( database, "SELECT kind FROM clipboard_selections WHERE entry_id='" + rich.Id + "'" ) );

        // A signature-invalid PNG is received, rejected after classification, and cannot restart the helper.
        await Send( "large" );
        // Zero reservations is also true before admission. Wait for this source
        // to finish before treating that state as classification cleanup.
        await server.StandardInput.WriteLineAsync( "wait-receive" );
        Equal( "finished", await server.StandardOutput.ReadLineAsync( token ) );
        await WaitFor( ( ) => ClipboardDatabaseScalar( database, "SELECT count(*) FROM clipboard_payloads WHERE state=0" ) == 0 &&
            ClipboardDatabaseScalar( database, "SELECT count(*) FROM clipboard_payloads WHERE charge>=16777216" ) == 0 );
        var ordinaryRevision = store.ReadDocument( second.Id, second.Incarnation )!.Summary.Revision;
        await Send( "ordinary" );
        await WaitFor( ( ) => store.ReadDocument( second.Id, second.Incarnation )!.Summary.Revision > ordinaryRevision );
        Equal( 2, store.Search( "", 10 ).Count );
        Equal( startsBeforeRich + 1, starts );
        Equal( "Capturing text and rich content", capture.Status );
        store.Enabled = false;
        await WaitFor( ( ) => capture.Status == "Capture is disabled" );

        async Task WaitFor( Func<bool> condition )
        {
            while ( !condition( ) )
            {
                await Task.Delay( 20, token );
            }
        }

        async Task Send( string scenario )
        {
            await server.StandardInput.WriteLineAsync( scenario );
            Equal( "sent", await server.StandardOutput.ReadLineAsync( token ) );
        }
    }
}
