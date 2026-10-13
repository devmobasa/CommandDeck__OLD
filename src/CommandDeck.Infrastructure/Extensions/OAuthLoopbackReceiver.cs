using System.Text;

namespace CommandDeck;

/// <summary>Reads one bounded loopback callback. Authority comes from Host, state and the configured issuer.</summary>
internal static class OAuthLoopbackReceiver
{
    public static async ValueTask<string> ReadCodeAsync( Stream stream, string redirect, string state, string issuer, CancellationToken token )
    {
        var request = new byte[8193];
        var count = 0;
        while ( count < request.Length )
        {
            var read = await stream.ReadAsync( request.AsMemory( count, 1 ), token ).ConfigureAwait( false );
            if ( read == 0 )
            {
                throw new InvalidDataException( "Incomplete callback." );
            }
            count += read;
            if ( count >= 4 && request.AsSpan( count - 4, 4 ).SequenceEqual( "\r\n\r\n"u8 ) )
            {
                break;
            }
        }
        if ( count > 8192 )
        {
            throw new InvalidDataException( "Callback too large." );
        }

        var lines = Encoding.ASCII.GetString( request, 0, count ).Split( "\r\n" );
        var first = lines[0].Split( ' ' );
        if ( first.Length != 3 || first[0] != "GET" || !first[1].StartsWith( "/callback?", StringComparison.Ordinal ) || first[2] != "HTTP/1.1" ||
             lines.Count( line => line.StartsWith( "Host:", StringComparison.OrdinalIgnoreCase ) ) != 1 ||
             !lines.Any( line => line.Equals( "Host: " + new Uri( redirect ).Authority, StringComparison.OrdinalIgnoreCase ) ) )
        {
            throw new InvalidDataException( "Invalid callback origin." );
        }

        var values = new Dictionary<string, string>( StringComparer.Ordinal );
        foreach ( var pair in first[1][10..].Split( '&' ) )
        {
            var parts = pair.Split( '=', 2 );
            if ( parts.Length != 2 || !values.TryAdd( Uri.UnescapeDataString( parts[0] ), Uri.UnescapeDataString( parts[1] ) ) )
            {
                throw new InvalidDataException( "Invalid callback parameters." );
            }
        }
        if ( values.GetValueOrDefault( "state" ) != state || values.GetValueOrDefault( "iss" ) != issuer ||
             !values.TryGetValue( "code", out var code ) || code.Length is < 1 or > 2048 || values.ContainsKey( "error" ) )
        {
            throw new InvalidDataException( "Callback state or issuer mismatch." );
        }

        return code;
    }
}
