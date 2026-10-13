using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardDigestsPreserveNamesBytesAndOrderIndependence( )
    {
        var first = DigestFixture( [("text/plain", "a\r\nb"), ("text/html", "<b>a</b>")] );
        Equal( first, DigestFixture( [("text/html", "<b>a</b>"), ("text/plain", "a\r\nb")] ) );
        True( first != DigestFixture( [("text/plain", "a\nb"), ("text/html", "<b>a</b>")] ) );
        True( first != DigestFixture( [("TEXT/PLAIN", "a\r\nb"), ("text/html", "<b>a</b>")] ) );
        True( first != DigestFixture( [("text/plain", "a\r\nb")] ) );
        True( DigestFixture( [("text/a", "bc")] ) != DigestFixture( [("text/ab", "c")] ), "name/content boundaries are length-delimited" );
        True( DigestFixture( [("text/plain", "")] ) != DigestFixture( [("text/html", "")] ) );
    }

    private static void ClipboardDigestsRejectUnverifiedLengthsAndMarkers( )
    {
        foreach ( var length in new[] { -1L, 1L, 3L, long.MaxValue } )
        {
            using MemoryStream stream = new( "ab"u8.ToArray( ) );
            Throws<InvalidDataException>( ( ) => ClipboardOfferDigest.Compute( [new( "text/plain", length, stream )] ) );
        }

        foreach ( var mime in new[] { "", "text/plain\n", ClipboardOfferPlan.OwnerMime, ClipboardOfferPlan.OwnerMime + ";id=test" } )
        {
            using MemoryStream stream = new( );
            Throws<InvalidDataException>( ( ) => ClipboardOfferDigest.Compute( [new( mime, 0, stream )] ) );
        }

        using MemoryStream empty = new( );
        Throws<InvalidDataException>( ( ) => ClipboardOfferDigest.Compute( [new( "text/plain", 0, empty ), new( "text/plain", 0, empty )] ) );
        Throws<InvalidDataException>( ( ) => ClipboardOfferDigest.Compute( [] ) );
    }

    private static string DigestFixture( (string Mime, string Text)[] offers )
    {
        var values = offers.Select( static offer =>
        {
            var bytes = Encoding.UTF8.GetBytes( offer.Text );
            return new ClipboardDigestOffer( offer.Mime, bytes.Length, new MemoryStream( bytes ) );
        } ).ToArray( );
        try
        {
            return ClipboardOfferDigest.Compute( values );
        }
        finally
        {
            foreach ( var value in values )
            {
                value.Content.Dispose( );
            }
        }
    }
}
