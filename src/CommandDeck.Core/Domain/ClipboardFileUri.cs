using System.Globalization;
using System.Text;

namespace CommandDeck;

/// <summary>A lexical local file reference. Construction performs no filesystem I/O.</summary>
public sealed class ClipboardFileUri
{
    public string Literal { get; }
    public string Path { get; }
    public string Key => Path;

    private ClipboardFileUri( string literal, string path )
    {
        Literal = literal;
        Path = path;
    }

    public static bool TryParse( string? text, out ClipboardFileUri? value, out ClipboardUrlRejection rejection )
    {
        value = null;
        rejection = ClipboardHttpUrl.ValidateLiteral( text );
        if ( rejection != ClipboardUrlRejection.None )
        {
            return false;
        }

        var literal = text!;
        if ( !literal.StartsWith( "file:", StringComparison.OrdinalIgnoreCase ) )
        {
            rejection = ClipboardUrlRejection.UnsupportedScheme;
            return false;
        }

        var path = literal[5..];
        if ( path.StartsWith( "//", StringComparison.Ordinal ) )
        {
            var slash = path.IndexOf( '/', 2 );
            var host = slash < 0 ? path[2..] : path[2..slash];
            if ( host.Length != 0 && !host.Equals( "localhost", StringComparison.OrdinalIgnoreCase ) )
            {
                rejection = ClipboardUrlRejection.InvalidAuthority;
                return false;
            }

            path = slash < 0 ? "" : path[slash..];
        }

        if ( !path.StartsWith( '/' ) || path.StartsWith( "//", StringComparison.Ordinal ) || path.IndexOfAny( ['?', '#', '[', ']'] ) >= 0 )
        {
            rejection = ClipboardUrlRejection.InvalidComponent;
            return false;
        }

        var decoded = DecodePath( path );
        if ( decoded is null || decoded.Split( '/' ).Any( static segment => segment is "." or ".." ) )
        {
            rejection = ClipboardUrlRejection.InvalidComponent;
            return false;
        }

        value = new( literal, decoded );
        return true;
    }

    /// <summary>Canonical file URI for a decoded absolute path: empty authority, per-segment UTF-8 percent encoding.</summary>
    public static string FromPath( string path )
    {
        ArgumentException.ThrowIfNullOrEmpty( path );
        if ( !path.StartsWith( '/' ) )
        {
            throw new ArgumentException( "File URIs require an absolute path.", nameof( path ) );
        }

        StringBuilder builder = new( "file://" );
        foreach ( var segment in path.Split( '/' ).Skip( 1 ) )
        {
            builder.Append( '/' );
            foreach ( var value in Encoding.UTF8.GetBytes( segment ) )
            {
                if ( value is >= ( byte ) 'a' and <= ( byte ) 'z' or >= ( byte ) 'A' and <= ( byte ) 'Z' or >= ( byte ) '0' and <= ( byte ) '9'
                    or ( byte ) '-' or ( byte ) '.' or ( byte ) '_' or ( byte ) '~' )
                {
                    builder.Append( ( char ) value );
                }
                else
                {
                    builder.Append( '%' ).Append( value.ToString( "X2", CultureInfo.InvariantCulture ) );
                }
            }
        }

        return builder.ToString( );
    }

    private static string? DecodePath( string path )
    {
        var bytes = new byte[path.Length];
        var length = 0;
        for ( var index = 0; index < path.Length; index++ )
        {
            var current = ( byte ) path[index];
            if ( path[index] == '%' )
            {
                current = byte.Parse( path.AsSpan( index + 1, 2 ), NumberStyles.HexNumber, CultureInfo.InvariantCulture );
                index += 2;
                if ( current is ( byte ) '/' or ( byte ) '\\' )
                {
                    return null;
                }
            }

            bytes[length++] = current;
        }

        // Percent runs were strictly validated by the shared lexical validator.
        return new UTF8Encoding( false, true ).GetString( bytes.AsSpan( 0, length ) );
    }
}
