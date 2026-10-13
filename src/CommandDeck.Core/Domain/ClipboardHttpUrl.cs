using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CommandDeck;

public enum ClipboardUrlRejection
{
    None, Empty, TooLarge, InvalidCharacter, UnsupportedScheme, InvalidAuthority,
    InvalidPort, InvalidPercentEncoding, EncodedControl, InvalidComponent
}

/// <summary>An exact validated literal and a lexical comparison key; never a repaired URI.</summary>
public sealed class ClipboardHttpUrl
{
    private static readonly UTF8Encoding StrictUtf8 = new( false, true );
    public string Literal { get; }
    public string Key { get; }
    public string Scheme { get; }

    private ClipboardHttpUrl( string literal, string key, string scheme )
    {
        Literal = literal;
        Key = key;
        Scheme = scheme;
    }

    public static bool TryParseSingleHttpUrl( string? text, out ClipboardHttpUrl? value, out ClipboardUrlRejection rejection )
    {
        value = null;
        rejection = ValidateLiteral( text );
        if ( rejection != ClipboardUrlRejection.None )
        {
            return false;
        }

        var literal = text!;
        var delimiter = literal.IndexOf( "://", StringComparison.Ordinal );
        if ( delimiter < 0 )
        {
            rejection = ClipboardUrlRejection.UnsupportedScheme;
            return false;
        }

        var scheme = literal[..delimiter].ToLowerInvariant( );
        if ( scheme is not ("http" or "https") )
        {
            rejection = ClipboardUrlRejection.UnsupportedScheme;
            return false;
        }

        var authorityStart = delimiter + 3;
        var authorityEnd = literal.IndexOfAny( ['/', '?', '#'], authorityStart );
        if ( authorityEnd < 0 )
        {
            authorityEnd = literal.Length;
        }

        rejection = ParseAuthority( literal[authorityStart..authorityEnd], scheme, out var authorityKey );
        if ( rejection != ClipboardUrlRejection.None )
        {
            return false;
        }

        var tail = literal[authorityEnd..];
        rejection = ValidateTail( tail );
        if ( rejection != ClipboardUrlRejection.None )
        {
            return false;
        }

        var keyTail = NormalizePercentEncoding( tail );
        if ( keyTail.Length == 0 || keyTail[0] != '/' )
        {
            keyTail = "/" + keyTail;
        }

        value = new( literal, scheme + "://" + authorityKey + keyTail, scheme );
        return true;
    }

    internal static ClipboardUrlRejection ValidateLiteral( string? text )
    {
        if ( string.IsNullOrEmpty( text ) )
        {
            return ClipboardUrlRejection.Empty;
        }

        if ( text.Length > 8192 )
        {
            return ClipboardUrlRejection.TooLarge;
        }

        if ( text.Any( static character => !IsUnreserved( character ) &&
            ":/?#[]@!$&'()*+,;=%".IndexOf( character, StringComparison.Ordinal ) < 0 ) )
        {
            return ClipboardUrlRejection.InvalidCharacter;
        }

        return ValidatePercentEncoding( text );
    }

    private static ClipboardUrlRejection ParseAuthority( string authority, string scheme, out string key )
    {
        key = "";
        string host;
        string portSuffix;
        if ( authority.StartsWith( '[' ) )
        {
            var bracket = authority.IndexOf( ']' );
            if ( bracket < 0 || authority.Contains( '%', StringComparison.Ordinal ) ||
                !IPAddress.TryParse( authority[1..bracket], out var address ) || address.AddressFamily != AddressFamily.InterNetworkV6 )
            {
                return ClipboardUrlRejection.InvalidAuthority;
            }

            host = authority[..(bracket + 1)];
            portSuffix = authority[(bracket + 1)..];
        }
        else
        {
            var colon = authority.IndexOf( ':' );
            host = colon < 0 ? authority : authority[..colon];
            portSuffix = colon < 0 ? "" : authority[colon..];
            if ( !ValidDnsHost( host ) )
            {
                return ClipboardUrlRejection.InvalidAuthority;
            }
        }

        if ( portSuffix.Length > 0 )
        {
            if ( portSuffix[0] != ':' || portSuffix.Length == 1 ||
                portSuffix.AsSpan( 1 ).ContainsAnyExceptInRange( '0', '9' ) ||
                !int.TryParse( portSuffix.AsSpan( 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var port ) || port > 65535 )
            {
                return ClipboardUrlRejection.InvalidPort;
            }

            if ( scheme == "http" && port == 80 || scheme == "https" && port == 443 )
            {
                portSuffix = "";
            }
        }

        key = host.ToLowerInvariant( ) + portSuffix;
        return ClipboardUrlRejection.None;
    }

    private static bool ValidDnsHost( string host )
    {
        if ( host.Length is 0 or > 253 )
        {
            return false;
        }

        var labels = host.TrimEnd( '.' ).Split( '.' );
        return !host.EndsWith( "..", StringComparison.Ordinal ) && labels.All( static label =>
            label.Length is > 0 and <= 63 && char.IsAsciiLetterOrDigit( label[0] ) && char.IsAsciiLetterOrDigit( label[^1] ) &&
            label.All( static character => char.IsAsciiLetterOrDigit( character ) || character == '-' ) );
    }

    private static ClipboardUrlRejection ValidateTail( string tail )
    {
        // '[' and ']' are authority delimiters, not raw path/query/fragment data.
        return tail.Contains( '[', StringComparison.Ordinal ) || tail.Contains( ']', StringComparison.Ordinal ) ||
            tail.Count( static character => character == '#' ) > 1
            ? ClipboardUrlRejection.InvalidComponent : ClipboardUrlRejection.None;
    }

    private static ClipboardUrlRejection ValidatePercentEncoding( string text )
    {
        for ( var index = 0; index < text.Length; index++ )
        {
            if ( text[index] != '%' )
            {
                continue;
            }

            List<byte> bytes = [];
            do
            {
                if ( index + 2 >= text.Length || !byte.TryParse( text.AsSpan( index + 1, 2 ), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var value ) )
                {
                    return ClipboardUrlRejection.InvalidPercentEncoding;
                }

                bytes.Add( value );
                index += 3;
            } while ( index < text.Length && text[index] == '%' );
            index--;

            try
            {
                var decoded = StrictUtf8.GetString( bytes.ToArray( ) );
                if ( decoded.Any( static character => char.IsControl( character ) || character is >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069' ) )
                {
                    return ClipboardUrlRejection.EncodedControl;
                }
            }
            catch ( DecoderFallbackException )
            {
                return ClipboardUrlRejection.InvalidPercentEncoding;
            }
        }

        return ClipboardUrlRejection.None;
    }

    private static string NormalizePercentEncoding( string text )
    {
        StringBuilder key = new( text.Length );
        for ( var index = 0; index < text.Length; index++ )
        {
            if ( text[index] != '%' )
            {
                key.Append( text[index] );
                continue;
            }

            var decoded = ( char ) byte.Parse( text.AsSpan( index + 1, 2 ), NumberStyles.HexNumber, CultureInfo.InvariantCulture );
            if ( IsUnreserved( decoded ) )
            {
                key.Append( decoded );
            }
            else
            {
                key.Append( '%' ).Append( char.ToUpperInvariant( text[index + 1] ) ).Append( char.ToUpperInvariant( text[index + 2] ) );
            }

            index += 2;
        }

        return key.ToString( );
    }

    private static bool IsUnreserved( char value ) => char.IsAsciiLetterOrDigit( value ) || value is '-' or '.' or '_' or '~';
}
