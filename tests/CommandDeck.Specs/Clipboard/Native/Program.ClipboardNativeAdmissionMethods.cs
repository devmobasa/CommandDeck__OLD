using System.Buffers.Binary;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardNativeAdmissionPreservesAliasesAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-native-admission-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            string id, incarnation;
            const string text = "retained 😀\r\n";
            using ( ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) ) )
            {
                store.Enabled = true;
                store.Capture( text );
                var identity = store.SnapshotEntries( new( ), false ).Single( );
                id = identity.Id;
                incarnation = identity.Incarnation;
                store.Pin( id, true );
                var revision = store.ReadDocument( id, incarnation )!.Summary.Revision;
                store.MutateEntry( id, incarnation, revision, "keywords", ["fixture"] );
                string[] names = ["x-special/gnome-copied-files", "text/plain", "Text/Plain; charset=UTF-8"];
                var bytes = Encoding.UTF8.GetBytes( text );
                await FeedNativeAdmissionAsync( store, names, ["copy\n"u8.ToArray( ), bytes, bytes], ( ) =>
                {
                    Equal( 65544L, ClipboardDatabaseScalar( path, "SELECT sum(charge) FROM clipboard_payloads WHERE state=0" ) );
                    Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads WHERE state=0 AND actual IS NOT NULL" ) );
                } );

                var document = store.ReadDocument( id, incarnation )!;
                True( document.Summary.Revision > revision, "recopy advances revision" );
                True( store.Search( "", 10 ).Single( ).Pinned, "recopy preserves pin" );
                Equal( "fixture", document.Summary.Keywords.Single( ) );
                Equal( text, store.ReadCopyText( id, incarnation, document.Summary.Revision ) );
                Equal( 2L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_offers" ) );
                Equal( 2L * bytes.Length, ClipboardDatabaseScalar( path, "SELECT sum(charge) FROM clipboard_payloads" ) );
                Equal( 2, Directory.GetFiles( path + ".payloads" ).Length );
            }

            using ClipboardHistoryStore reopened = new( path, new ClipboardHistoryOptions( ) );
            var retained = reopened.ReadDocument( id, incarnation )!;
            Equal( text, reopened.ReadCopyText( id, incarnation, retained.Summary.Revision ) );
            reopened.Delete( id );
            Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads" ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardNativeAdmissionRejectsInvalidAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-native-admission-invalid-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            using ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) );
            store.Enabled = true;
            foreach ( var bytes in new byte[][] { [], [255], [0] } )
            {
                try
                {
                    await FeedNativeAdmissionAsync( store, ["text/plain"], [bytes] );
                    throw new InvalidOperationException( "Invalid fixture was retained." );
                }
                catch ( Exception exception ) when ( exception is InvalidDataException or DecoderFallbackException ) { }
            }

            await ThrowsAsync<InvalidDataException>( ( ) => FeedNativeAdmissionAsync( store,
                ["text/plain", "text/plain;charset=utf-8"], ["first"u8.ToArray( ), "other"u8.ToArray( )] ) );
            await ThrowsAsync<InvalidDataException>( ( ) => FeedNativeAdmissionAsync( store,
                ["x-special/gnome-copied-files", "text/plain"], ["cut\n"u8.ToArray( ), "secret"u8.ToArray( )] ) );
            await ThrowsAsync<InvalidOperationException>( ( ) => FeedNativeAdmissionAsync( store,
                ["text/plain"], ["revoked"u8.ToArray( )], beforePublish: ( ) => store.Clear( ) ) );
            Equal( 0, store.Search( "", 10 ).Count );
            Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads" ) );
            Equal( 0, Directory.GetFiles( path + ".payloads" ).Length );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task FeedNativeAdmissionAsync( ClipboardHistoryStore store, string[] names, byte[][] contents,
        Action? afterReserve = null, Action? beforePublish = null )
    {
        var selection = Guid.NewGuid( );
        var plan = ClipboardOfferPlan.Create( names, ClipboardConsent.Text );
        using var admission = store.AdmitTextCapture( names, store.CaptureEpoch, 31, selection, 3 );
        afterReserve?.Invoke( );
        uint sequence = 3;
        foreach ( var offer in plan.Offers )
        {
            admission.BeginOffer( offer.InventoryIndex );
            var bytes = contents[offer.InventoryIndex];
            if ( bytes.Length > 0 )
            {
                await admission.AcceptAsync( new( new( ClipboardHelperFrameType.OfferChunk, 31, selection,
                    offer.InventoryIndex, sequence++, bytes.Length ), bytes ), default );
            }

            var size = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian( size, ( ulong ) bytes.Length );
            await admission.AcceptAsync( new( new( ClipboardHelperFrameType.OfferEnd, 31, selection,
                offer.InventoryIndex, sequence++, 8 ), size ), default );
        }

        beforePublish?.Invoke( );
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian( count, ( uint ) plan.Offers.Count );
        await admission.AcceptAsync( new( new( ClipboardHelperFrameType.SelectionEnd, 31, selection, -1, sequence, 4 ), count ), default );
        True( admission.Complete, "native publication follows a complete admitted frame sequence" );
    }
}
