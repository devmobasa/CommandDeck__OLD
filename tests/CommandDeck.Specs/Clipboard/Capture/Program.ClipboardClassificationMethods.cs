using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardClassificationPreservesPrecedenceAndTargets( )
    {
        var png = Convert.FromBase64String( PreviewPngBase64 );
        var files = ClassifyOffers( ("text/plain", "caption"u8.ToArray( )), ("image/png", png),
            ("text/uri-list", "file:///fixture/not-opened.txt\n"u8.ToArray( )) );
        Equal( ClipboardContentKind.Files, files.Kind );
        Equal( "/fixture/not-opened.txt", files.OpenDestination!.File!.Path );
        Equal( 3, files.RetainedIndexes.Count );
        var text = ClassifyOffers( ("text/plain", "caption"u8.ToArray( )), ("image/png", png) );
        Equal( ClipboardContentKind.Text, text.Kind );
        True( text.OpenDestination is null );
        var image = ClassifyOffers( ("text/plain", "https://example.invalid/"u8.ToArray( )), ("image/png", png) );
        Equal( ClipboardContentKind.Images, image.Kind );
        True( image.OpenDestination is null );
        var html = ClassifyOffers( ("text/html", "<b>raw</b>"u8.ToArray( )), ("text/plain", "https://example.invalid/"u8.ToArray( )) );
        Equal( ClipboardContentKind.Text, html.Kind );
        True( html.OpenDestination is null );
        Equal( "https://example.invalid/", html.Text );
        var matching = ClassifyOffers( ("text/plain", "https://EXAMPLE.invalid:443/"u8.ToArray( )),
            ("text/uri-list", "https://example.invalid/\nhttps://example.invalid/\n"u8.ToArray( )) );
        Equal( ClipboardContentKind.Links, matching.Kind );
        Equal( 1, matching.Destinations.Count );
        True( matching.OpenDestination is not null );
        var conflicting = ClassifyOffers( ("text/plain", "https://example.invalid/second"u8.ToArray( )),
            ("text/uri-list", "https://example.invalid/first\n"u8.ToArray( )) );
        True( conflicting.OpenDestination is null );
        Equal( 2, conflicting.RetainedIndexes.Count );
        Equal( ClipboardContentKind.Text, ClassifyOffers( ("text/plain", "https://one.invalid/\nhttps://two.invalid/"u8.ToArray( )) ).Kind );
        Equal( " \r\n", ClassifyOffers( ("text/plain", " \r\n"u8.ToArray( )) ).Text );
    }

    private static void ClipboardClassificationRejectsConflictsBeforeFallback( )
    {
        Throws<InvalidDataException>( ( ) => ClassifyOffers( ("text/plain", "same"u8.ToArray( )),
            ("Text/Plain; charset=UTF-8", "different"u8.ToArray( )) ) );
        Throws<InvalidDataException>( ( ) => ClassifyOffers( ("text/plain", "fallback"u8.ToArray( )),
            ("text/uri-list", "invalid one"u8.ToArray( )), ("TEXT/URI-LIST", "invalid two"u8.ToArray( )) ) );
        foreach ( var header in new[] { "cut\nfile:///a", "COPY\nfile:///a", "\uFEFFcopy\nfile:///a", "copy" } )
        {
            Throws<InvalidDataException>( ( ) => ClassifyOffers( ("text/plain", "fallback"u8.ToArray( )),
                ("x-special/gnome-copied-files", Encoding.UTF8.GetBytes( header )) ) );
        }

        var reduced = ClassifyOffers( ("text/plain", "safe fallback"u8.ToArray( )), ("text/uri-list", "file:///a\nhttps://mixed.invalid/"u8.ToArray( )) );
        True( reduced.ReducedFidelity && reduced.OpenDestination is null );
        Equal( 1, reduced.RetainedIndexes.Count );
        var pairing = ClassifyOffers( ("text/plain", "https://display.invalid/"u8.ToArray( )),
            ("x-special/gnome-copied-files", "copy\nfile:///a"u8.ToArray( )), ("text/uri-list", "file:///b"u8.ToArray( )) );
        Equal( ClipboardContentKind.Text, pairing.Kind );
        True( pairing.ReducedFidelity && pairing.OpenDestination is null );
        Equal( 1, pairing.RetainedIndexes.Count );
        Throws<InvalidDataException>( ( ) => ClassifyOffers( ("text/uri-list", "invalid"u8.ToArray( )) ) );
        Throws<InvalidDataException>( ( ) => ClassifyOffers( ("image/png", "not PNG"u8.ToArray( )), ("text/plain", "fallback"u8.ToArray( )) ) );
        Throws<DecoderFallbackException>( ( ) => ClassifyOffers( ("text/plain", [0xff]) ) );
    }

    private static void ClipboardClassificationKeepsProjectionAndSourceSeparate( )
    {
        var plan = ClipboardOfferPlan.Create( ["text/html"], ClipboardConsent.Rich );
        Dictionary<int, ReadOnlyMemory<byte>> raw = new( )
        {
            [0] = "<b>raw</b>"u8.ToArray( )
        };
        var projected = ClipboardSelectionClassifier.Classify( plan, raw, new( true, "inert", true ) );
        True( projected.ExtractedText && projected.Truncated );
        Equal( "inert", projected.Text );
        var unavailable = ClipboardSelectionClassifier.Classify( plan, raw, new( false, "", false, "WorkerFailed" ) );
        Equal( "", unavailable.Text );
        Equal( 1, unavailable.RetainedIndexes.Count );
        var source = ClassifyOffers( ("text/plain", "caption"u8.ToArray( )), ("chromium/x-source-url", "https://source.invalid/\n"u8.ToArray( )) );
        Equal( "https://source.invalid/", source.ReportedSource!.Literal );
        True( source.OpenDestination is null );
        var wrong = ClassifyOffers( ("text/plain", "https://main.invalid/"u8.ToArray( )), ("chromium/x-source-url", "https://source.invalid/\n"u8.ToArray( )) );
        True( wrong.ReportedSource is null && wrong.ReducedFidelity );
        Equal( "https://main.invalid/", wrong.OpenDestination!.Literal );
        var files = ClassifyOffers( ("text/uri-list", "file:///fixture/never-read"u8.ToArray( )), ("chromium/x-source-url", "https://source.invalid/"u8.ToArray( )) );
        True( files.ReportedSource is null );
        Throws<InvalidDataException>( ( ) => ClassifyOffers( ("chromium/x-source-url", "https://source.invalid/"u8.ToArray( )) ) );
        var aliases = ClassifyOffers( ("text/plain", "same"u8.ToArray( )), ("Text/Plain; charset=UTF-8", "same"u8.ToArray( )) );
        Equal( 2, aliases.RetainedIndexes.Count );
        True( !aliases.ReducedFidelity );
        var textPlan = ClipboardOfferPlan.Create( ["application/unknown", "text/plain", "x-special/gnome-copied-files"], ClipboardConsent.Text );
        var textOnly = ClipboardSelectionClassifier.Classify( textPlan,
            new Dictionary<int, ReadOnlyMemory<byte>> { [1] = "safe"u8.ToArray( ), [2] = "copy\n"u8.ToArray( ) } );
        Equal( 1, textOnly.RetainedIndexes.Single( ) );
        True( !textOnly.ReducedFidelity, "discarding a permitted control header is not reduced fidelity" );
        Throws<InvalidDataException>( ( ) => ClipboardSelectionClassifier.Classify( textPlan,
            new Dictionary<int, ReadOnlyMemory<byte>> { [1] = "safe"u8.ToArray( ) } ) );
    }

    private static ClipboardSelectionContent ClassifyOffers( params (string Mime, byte[] Bytes)[] offers )
    {
        var plan = ClipboardOfferPlan.Create( offers.Select( static offer => offer.Mime ).ToArray( ), ClipboardConsent.Rich, chromiumSourceFixturesVerified: true );
        return ClipboardSelectionClassifier.Classify( plan, offers.Select( ( offer, index ) => (index, offer.Bytes) )
            .ToDictionary( static offer => offer.index, static offer => ( ReadOnlyMemory<byte> ) offer.Bytes ) );
    }
}
