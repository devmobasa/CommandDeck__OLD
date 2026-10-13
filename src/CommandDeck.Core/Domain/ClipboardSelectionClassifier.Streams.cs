using System.Security.Cryptography;

namespace CommandDeck;

public sealed record ClipboardClassificationOffer( Stream Content, long Length, string Digest );

public static partial class ClipboardSelectionClassifier
{
    /// <summary>Validates held payload streams with bounded buffers; HTML and PNG remain external restore data.</summary>
    public static async ValueTask<ClipboardSelectionContent> ClassifyAsync( ClipboardOfferPlan plan,
        IReadOnlyDictionary<int, ClipboardClassificationOffer> received, ClipboardHtmlProjection? htmlProjection,
        CancellationToken token )
    {
        ArgumentNullException.ThrowIfNull( plan );
        ArgumentNullException.ThrowIfNull( received );
        token.ThrowIfCancellationRequested( );
        ValidateStreams( plan, received );

        var buffer = new byte[ClipboardHelperHeader.MaximumPayload];
        Dictionary<int, ReadOnlyMemory<byte>> semanticInputs = [];
        foreach ( var offer in plan.Offers )
        {
            semanticInputs.Add( offer.InventoryIndex, await ReadVerifiedSemanticInputAsync( offer, received[offer.InventoryIndex], buffer, token ).ConfigureAwait( false ) );
        }

        await CompareStreamAliasesAsync( plan, received, buffer, token ).ConfigureAwait( false );
        token.ThrowIfCancellationRequested( );
        return ClassifyValidatedInputs( plan, semanticInputs, htmlProjection );
    }

    private static void ValidateStreams( ClipboardOfferPlan plan, IReadOnlyDictionary<int, ClipboardClassificationOffer> received )
    {
        if ( !plan.Accepted || plan.Offers.Count != received.Count ||
            received.Values.Select( static offer => offer.Content ).Distinct( ReferenceEqualityComparer.Instance ).Count( ) != received.Count )
        {
            throw new InvalidDataException( "Clipboard selection streams are incomplete or unadmitted." );
        }

        foreach ( var offer in plan.Offers )
        {
            if ( !received.TryGetValue( offer.InventoryIndex, out var stream ) || !stream.Content.CanRead || !stream.Content.CanSeek ||
                stream.Length is < 1 || stream.Length > offer.MaximumBytes || stream.Content.Length != stream.Length ||
                stream.Digest.Length != 64 || stream.Digest.Any( static character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f') ) )
            {
                throw new InvalidDataException( "Clipboard classification stream exceeds its admitted bounds." );
            }
        }
    }

    private static async ValueTask<byte[]> ReadVerifiedSemanticInputAsync( ClipboardPlannedOffer offer, ClipboardClassificationOffer received,
        byte[] buffer, CancellationToken token )
    {
        // Markup only contributes presence here. Its text comes from the separate sandbox.
        // PNG needs its signature here; its decoder later receives its own verified snapshot.
        var semanticLength = offer.Kind switch
        {
            ClipboardOfferKind.Html => 0,
            ClipboardOfferKind.Png => Math.Min( 8, received.Length ),
            _ => received.Length
        };
        var semantic = new byte[checked(( int ) semanticLength)];
        using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
        received.Content.Position = 0;
        long position = 0;
        while ( position < received.Length )
        {
            var read = await received.Content.ReadAsync( buffer.AsMemory( 0, ( int ) Math.Min( buffer.Length, received.Length - position ) ), token ).ConfigureAwait( false );
            if ( read == 0 )
            {
                throw new InvalidDataException( "Clipboard classification stream ended early." );
            }

            hash.AppendData( buffer.AsSpan( 0, read ) );
            if ( position < semantic.Length )
            {
                buffer.AsSpan( 0, Math.Min( read, semantic.Length - ( int ) position ) ).CopyTo( semantic.AsSpan( ( int ) position ) );
            }

            position += read;
        }

        if ( await received.Content.ReadAsync( buffer.AsMemory( 0, 1 ), token ).ConfigureAwait( false ) != 0 ||
            received.Content.Length != received.Length || Convert.ToHexStringLower( hash.GetHashAndReset( ) ) != received.Digest )
        {
            throw new InvalidDataException( "Clipboard classification stream changed." );
        }

        return semantic;
    }

    private static async Task CompareStreamAliasesAsync( ClipboardOfferPlan plan, IReadOnlyDictionary<int, ClipboardClassificationOffer> received,
        byte[] firstBuffer, CancellationToken token )
    {
        var otherBuffer = new byte[firstBuffer.Length];
        foreach ( var group in plan.Offers.GroupBy( static offer => offer.Kind ) )
        {
            var first = received[group.First( ).InventoryIndex];
            foreach ( var alias in group.Skip( 1 ) )
            {
                var other = received[alias.InventoryIndex];
                if ( first.Length != other.Length )
                {
                    throw new InvalidDataException( "Clipboard semantic aliases disagree." );
                }

                first.Content.Position = 0;
                other.Content.Position = 0;
                long remaining = first.Length;
                while ( remaining > 0 )
                {
                    var count = ( int ) Math.Min( firstBuffer.Length, remaining );
                    await first.Content.ReadExactlyAsync( firstBuffer.AsMemory( 0, count ), token ).ConfigureAwait( false );
                    await other.Content.ReadExactlyAsync( otherBuffer.AsMemory( 0, count ), token ).ConfigureAwait( false );
                    if ( !firstBuffer.AsSpan( 0, count ).SequenceEqual( otherBuffer.AsSpan( 0, count ) ) )
                    {
                        throw new InvalidDataException( "Clipboard semantic aliases disagree." );
                    }

                    remaining -= count;
                }
            }
        }
    }
}
