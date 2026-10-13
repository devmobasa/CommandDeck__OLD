using System.Security.Cryptography;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardStreamClassificationBoundsMemoryAsync( )
    {
        var plainPlan = ClipboardOfferPlan.Create( ["text/plain"], ClipboardConsent.Text );
        using MemoryStream warmup = new( "warmup"u8.ToArray( ) );
        _ = await ClipboardSelectionClassifier.ClassifyAsync( plainPlan,
            new Dictionary<int, ClipboardClassificationOffer> { [0] = ClassificationOffer( warmup ) }, null, CancellationToken.None );

        var html = new byte[1024 * 1024];
        Array.Fill( html, ( byte ) 'x' );
        var png = new byte[16 * 1024 * 1024];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo( png, 0 );
        using ClassificationReadStream first = new( html );
        using ClassificationReadStream alias = new( html );
        using ClassificationReadStream image = new( png );
        var plan = ClipboardOfferPlan.Create( ["text/html", "Text/Html; charset=UTF-8", "image/png"], ClipboardConsent.Rich );
        Dictionary<int, ClipboardClassificationOffer> offers = new( )
        {
            [0] = ClassificationOffer( first ),
            [1] = ClassificationOffer( alias ),
            [2] = ClassificationOffer( image )
        };
        var before = GC.GetTotalAllocatedBytes( precise: true );
        var result = await ClipboardSelectionClassifier.ClassifyAsync( plan, offers, new( true, "inert", false ), CancellationToken.None );
        var allocated = GC.GetTotalAllocatedBytes( precise: true ) - before;
        True( allocated < 4 * 1024 * 1024, "18 MiB of offers must not become a full-selection RAM allocation" );
        Equal( ClipboardContentKind.Images, result.Kind );
        Equal( "", result.Text );
        Equal( 3, result.RetainedIndexes.Count );
        True( new[] { first, alias, image }.All( static stream => stream.MaximumRequest <= 60 * 1024 ) );
        True( first.BytesRead >= 2L * html.Length && alias.BytesRead >= 2L * html.Length,
            "aliases are compared byte for byte after digest verification" );
    }

    private static async Task ClipboardStreamClassificationRejectsDamageAndCancellationAsync( )
    {
        var plan = ClipboardOfferPlan.Create( ["text/html", "TEXT/HTML", "text/plain"], ClipboardConsent.Rich );
        using ClassificationReadStream first = new( "<p>first</p>"u8.ToArray( ) );
        using ClassificationReadStream other = new( "<p>other</p>"u8.ToArray( ) );
        using ClassificationReadStream plain = new( "safe fallback"u8.ToArray( ) );
        Dictionary<int, ClipboardClassificationOffer> offers = new( )
        {
            [0] = ClassificationOffer( first ),
            [1] = ClassificationOffer( other ),
            [2] = ClassificationOffer( plain )
        };
        await ThrowsAsync<InvalidDataException>( async ( ) => await ClipboardSelectionClassifier.ClassifyAsync( plan, offers, null, CancellationToken.None ) );
        offers[1] = offers[1] with
        {
            Digest = new string( '0', 64 )
        };
        await ThrowsAsync<InvalidDataException>( async ( ) => await ClipboardSelectionClassifier.ClassifyAsync( plan, offers, null, CancellationToken.None ) );

        var textPlan = ClipboardOfferPlan.Create( ["text/plain"], ClipboardConsent.Text );
        var valid = ClassificationOffer( plain );
        await ThrowsAsync<InvalidDataException>( async ( ) => await ClipboardSelectionClassifier.ClassifyAsync( textPlan,
            new Dictionary<int, ClipboardClassificationOffer> { [0] = valid with { Length = valid.Length + 1 } }, null, CancellationToken.None ) );
        using CancellationTokenSource cancellation = new( );
        plain.OnRead = cancellation.Cancel;
        await ThrowsAsync<OperationCanceledException>( async ( ) => await ClipboardSelectionClassifier.ClassifyAsync( textPlan,
            new Dictionary<int, ClipboardClassificationOffer> { [0] = valid }, null, cancellation.Token ) );
        True( plain.CanRead, "classification never disposes the caller's payload lease" );
    }

    private static ClipboardClassificationOffer ClassificationOffer( MemoryStream content )
    {
        content.Position = 0;
        var digest = Convert.ToHexStringLower( SHA256.HashData( content ) );
        content.Position = 0;
        return new( content, content.Length, digest );
    }

    private sealed class ClassificationReadStream( byte[] bytes ) : MemoryStream( bytes, writable: false )
    {
        internal int MaximumRequest { get; private set; }
        internal long BytesRead { get; private set; }
        internal Action? OnRead { get; set; }

        public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        {
            MaximumRequest = Math.Max( MaximumRequest, buffer.Length );
            var read = await base.ReadAsync( buffer, cancellationToken );
            BytesRead += read;
            OnRead?.Invoke( );
            return read;
        }
    }
}
