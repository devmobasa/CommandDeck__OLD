using System.Text;

namespace CommandDeck;

public enum ClipboardUriListRejection
{
    None, Empty, TooLarge, InvalidEncoding, InvalidRecord, TooManyRecords, MixedKinds, InvalidHeader, Move
}

public sealed record ClipboardUriDestination( string Literal, string Key, ClipboardHttpUrl? Http, ClipboardFileUri? File );

/// <summary>Validated structured projection; raw restore bytes remain a separate storage concern.</summary>
public sealed class ClipboardUriList
{
    public IReadOnlyList<ClipboardUriDestination> Destinations { get; }
    public bool IsFiles => Destinations[0].File is not null;
    public int RecordCount { get; }

    private ClipboardUriList( ClipboardUriDestination[] destinations, int recordCount )
    {
        Destinations = Array.AsReadOnly( destinations );
        RecordCount = recordCount;
    }

    public static bool TryParse( ReadOnlySpan<byte> bytes, bool gnomeCopiedFiles,
        out ClipboardUriList? value, out ClipboardUriListRejection rejection )
    {
        value = null;
        rejection = ClipboardUriListRejection.None;
        if ( bytes.Length > 64 * 1024 )
        {
            rejection = ClipboardUriListRejection.TooLarge;
            return false;
        }

        string text;
        try
        {
            text = new UTF8Encoding( false, true ).GetString( bytes );
        }
        catch ( DecoderFallbackException )
        {
            rejection = ClipboardUriListRejection.InvalidEncoding;
            return false;
        }

        if ( !gnomeCopiedFiles && text.StartsWith( '\uFEFF' ) )
        {
            text = text[1..];
        }

        var lines = text.Split( '\n' );
        var first = 0;
        if ( gnomeCopiedFiles )
        {
            rejection = ReadGnomeHeader( lines );
            if ( rejection != ClipboardUriListRejection.None )
            {
                return false;
            }

            first = 1;
        }

        return ParseRecords( lines, first, gnomeCopiedFiles, out value, out rejection );
    }

    private static bool ParseRecords( string[] lines, int first, bool gnome,
        out ClipboardUriList? value, out ClipboardUriListRejection rejection )
    {
        value = null;
        rejection = ClipboardUriListRejection.None;
        List<ClipboardUriDestination> destinations = [];
        HashSet<string> keys = new( StringComparer.Ordinal );
        var records = 0;
        for ( var index = first; index < lines.Length; index++ )
        {
            var line = lines[index];
            if ( index < lines.Length - 1 && line.EndsWith( '\r' ) )
            {
                line = line[..^1];
            }

            if ( line.Any( static character => char.IsControl( character ) ||
                character is >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069' ) || gnome && line.StartsWith( '#' ) )
            {
                rejection = ClipboardUriListRejection.InvalidRecord;
                return false;
            }

            if ( line.Length == 0 || line.StartsWith( '#' ) )
            {
                continue;
            }

            if ( ++records > 256 )
            {
                rejection = ClipboardUriListRejection.TooManyRecords;
                return false;
            }

            var destination = ParseDestination( line );
            if ( destination is null || gnome && destination.File is null )
            {
                rejection = ClipboardUriListRejection.InvalidRecord;
                return false;
            }

            if ( destinations.Count > 0 && (destinations[0].File is null) != (destination.File is null) )
            {
                rejection = ClipboardUriListRejection.MixedKinds;
                return false;
            }

            if ( keys.Add( destination.Key ) )
            {
                destinations.Add( destination );
            }
        }

        if ( destinations.Count == 0 )
        {
            rejection = ClipboardUriListRejection.Empty;
            return false;
        }

        value = new( destinations.ToArray( ), records );
        return true;
    }

    private static ClipboardUriDestination? ParseDestination( string literal )
    {
        if ( ClipboardHttpUrl.TryParseSingleHttpUrl( literal, out var http, out _ ) )
        {
            return new( literal, http!.Key, http, null );
        }

        if ( ClipboardFileUri.TryParse( literal, out var file, out _ ) )
        {
            return new( literal, file!.Key, null, file );
        }

        return null;
    }

    private static ClipboardUriListRejection ReadGnomeHeader( string[] lines )
    {
        if ( lines.Length < 2 )
        {
            return ClipboardUriListRejection.InvalidHeader;
        }

        return lines[0] switch
        {
            "copy" or "copy\r" => ClipboardUriListRejection.None,
            "cut" or "cut\r" => ClipboardUriListRejection.Move,
            _ => ClipboardUriListRejection.InvalidHeader
        };
    }

    public bool HasSameOrderedDestinations( ClipboardUriList other ) => IsFiles == other.IsFiles &&
        Destinations.Select( static destination => destination.Key ).SequenceEqual(
            other.Destinations.Select( static destination => destination.Key ), StringComparer.Ordinal );

    public static bool TryParseChromiumSource( ReadOnlySpan<byte> bytes, out ClipboardHttpUrl? source )
    {
        source = null;
        if ( bytes.Length > 8192 )
        {
            return false;
        }

        if ( bytes.EndsWith( "\r\n"u8 ) )
        {
            bytes = bytes[..^2];
        }
        else if ( bytes.EndsWith( "\n"u8 ) )
        {
            bytes = bytes[..^1];
        }

        // Raw non-ASCII bytes are not valid in this URI grammar; never replace them.
        return !bytes.ContainsAnyInRange( ( byte ) 128, byte.MaxValue ) &&
            ClipboardHttpUrl.TryParseSingleHttpUrl( Encoding.ASCII.GetString( bytes ), out source, out _ );
    }
}
