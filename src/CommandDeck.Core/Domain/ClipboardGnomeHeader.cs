namespace CommandDeck;

public enum ClipboardGnomeHeaderResult
{
    Copy, Cut, Invalid
}

public readonly record struct ClipboardGnomeHeader( ClipboardGnomeHeaderResult Result, ReadOnlyMemory<byte> Bytes )
{
    /// <summary>Consumes through the first LF only, without buffering or prefetching body bytes.</summary>
    public static async Task<ClipboardGnomeHeader> ReadAsync( Stream offer, CancellationToken cancellationToken )
    {
        var bytes = new byte[8];
        for ( var length = 0; length < bytes.Length; length++ )
        {
            if ( await offer.ReadAsync( bytes.AsMemory( length, 1 ), cancellationToken ).ConfigureAwait( false ) == 0 )
            {
                return new( ClipboardGnomeHeaderResult.Invalid, bytes.AsMemory( 0, length ) );
            }

            if ( bytes[length] != ( byte ) '\n' )
            {
                continue;
            }

            var header = bytes.AsMemory( 0, length + 1 );
            var result = header.Span.SequenceEqual( "copy\n"u8 ) || header.Span.SequenceEqual( "copy\r\n"u8 )
                ? ClipboardGnomeHeaderResult.Copy
                : header.Span.SequenceEqual( "cut\n"u8 ) || header.Span.SequenceEqual( "cut\r\n"u8 )
                    ? ClipboardGnomeHeaderResult.Cut : ClipboardGnomeHeaderResult.Invalid;
            return new( result, header );
        }

        return new( ClipboardGnomeHeaderResult.Invalid, bytes );
    }
}
