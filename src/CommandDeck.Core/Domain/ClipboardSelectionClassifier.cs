using System.Text;

namespace CommandDeck;

/// <summary>Pure post-receive classification; never resolves or opens a copied destination.</summary>
public static partial class ClipboardSelectionClassifier
{
    private static readonly UTF8Encoding StrictUtf8 = new( false, true );
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static ClipboardSelectionContent Classify( ClipboardOfferPlan plan,
        IReadOnlyDictionary<int, ReadOnlyMemory<byte>> received, ClipboardHtmlProjection? htmlProjection = null )
    {
        ArgumentNullException.ThrowIfNull( plan );
        ArgumentNullException.ThrowIfNull( received );
        ValidateReceived( plan, received );

        return ClassifyValidatedInputs( plan, received, htmlProjection );
    }

    private static ClipboardSelectionContent ClassifyValidatedInputs( ClipboardOfferPlan plan,
        IReadOnlyDictionary<int, ReadOnlyMemory<byte>> received, ClipboardHtmlProjection? htmlProjection )
    {
        Representations representations = new( plan, received );
        var groups = representations.Groups;
        var retained = representations.Retained;
        var plain = ReadPlain( representations.Bytes( ClipboardOfferKind.PlainText ) );
        if ( !ClipboardHttpUrl.TryParseSingleHttpUrl( plain, out var plainUrl, out _ ) )
        {
            plainUrl = null;
        }

        var uri = representations.ParseList( ClipboardOfferKind.UriList, false );
        var gnome = representations.ParseGnome( );
        var pairingFailed = groups.ContainsKey( ClipboardOfferKind.GnomeCopiedFiles ) &&
            !groups[ClipboardOfferKind.GnomeCopiedFiles][0].HeaderOnly &&
            (gnome is null || groups.ContainsKey( ClipboardOfferKind.UriList ) && (uri is null || !gnome.HasSameOrderedDestinations( uri )));
        if ( pairingFailed )
        {
            retained.Remove( ClipboardOfferKind.GnomeCopiedFiles );
            retained.Remove( ClipboardOfferKind.UriList );
            uri = null;
            gnome = null;
        }

        var files = gnome ?? (uri?.IsFiles == true ? uri : null);
        var hasHtml = retained.Contains( ClipboardOfferKind.Html );
        var hasPng = retained.Contains( ClipboardOfferKind.Png );
        if ( hasPng && !representations.Bytes( ClipboardOfferKind.Png ).Span.StartsWith( PngSignature ) )
        {
            throw new InvalidDataException( "Clipboard PNG signature is invalid." );
        }

        var kind = ClassifyKind( files, plain, plainUrl, hasPng, hasHtml, uri, pairingFailed );
        var source = representations.ReadSource( kind, plain, plainUrl, hasHtml, hasPng );
        var destinations = Destinations( kind, files, uri, plainUrl );
        var open = OpenDestination( kind, destinations, uri, plainUrl );
        var (text, extracted, truncated) = ProjectionText( kind, plain, hasHtml, htmlProjection, destinations );

        return new( kind, text, extracted, truncated,
            plan.Offers.Any( offer => !offer.HeaderOnly && !retained.Contains( offer.Kind ) ),
            plan.Offers.Where( offer => retained.Contains( offer.Kind ) ).Select( static offer => offer.InventoryIndex ).ToArray( ),
            destinations, open, source );
    }

    private static ClipboardUriDestination? OpenDestination( ClipboardContentKind kind, IReadOnlyList<ClipboardUriDestination> destinations,
        ClipboardUriList? uri, ClipboardHttpUrl? plainUrl )
    {
        var open = destinations.Count == 1 ? destinations[0] : null;
        if ( kind == ClipboardContentKind.Links && uri is not null && plainUrl is not null &&
            (uri.Destinations.Count != 1 || uri.Destinations[0].Key != plainUrl.Key) )
        {
            open = null;
        }

        return open;
    }

    private static (string Text, bool Extracted, bool Truncated) ProjectionText( ClipboardContentKind kind, string? plain,
        bool hasHtml, ClipboardHtmlProjection? htmlProjection, IReadOnlyList<ClipboardUriDestination> destinations )
    {
        var extracted = kind == ClipboardContentKind.Text && plain is null && hasHtml && htmlProjection?.Available == true;
        var text = plain ?? (extracted ? htmlProjection!.Text : "");
        if ( kind is ClipboardContentKind.Files or ClipboardContentKind.Links )
        {
            text = string.Join( '\n', destinations.Select( static destination => destination.File?.Path ?? destination.Literal ) );
        }

        return (text, extracted, extracted && htmlProjection!.Truncated);
    }

    private sealed class Representations
    {
        internal Dictionary<ClipboardOfferKind, ClipboardPlannedOffer[]> Groups { get; }
        internal HashSet<ClipboardOfferKind> Retained { get; }
        private readonly IReadOnlyDictionary<int, ReadOnlyMemory<byte>> _received;

        internal Representations( ClipboardOfferPlan plan, IReadOnlyDictionary<int, ReadOnlyMemory<byte>> received )
        {
            Groups = plan.Offers.GroupBy( static offer => offer.Kind ).ToDictionary( static group => group.Key, static group => group.ToArray( ) );
            Retained = Groups.Keys.ToHashSet( );
            _received = received;
        }

        internal ReadOnlyMemory<byte> Bytes( ClipboardOfferKind kind ) => Groups.TryGetValue( kind, out var offers ) ? _received[offers[0].InventoryIndex] : default;

        internal ClipboardUriList? ParseList( ClipboardOfferKind kind, bool isGnome )
        {
            if ( !Groups.ContainsKey( kind ) )
            {
                return null;
            }

            if ( ClipboardUriList.TryParse( Bytes( kind ).Span, isGnome, out var list, out _ ) )
            {
                return list;
            }

            Retained.Remove( kind );
            return null;
        }

        internal ClipboardUriList? ParseGnome( )
        {
            if ( !Groups.TryGetValue( ClipboardOfferKind.GnomeCopiedFiles, out var offers ) )
            {
                return null;
            }

            var bytes = Bytes( ClipboardOfferKind.GnomeCopiedFiles ).Span;
            var newline = bytes.IndexOf( ( byte ) '\n' );
            if ( newline < 0 || !(bytes[..(newline + 1)].SequenceEqual( "copy\n"u8 ) || bytes[..(newline + 1)].SequenceEqual( "copy\r\n"u8 )) )
            {
                throw new InvalidDataException( "Clipboard control header prohibits capture." );
            }

            if ( offers[0].HeaderOnly )
            {
                if ( newline + 1 != bytes.Length )
                {
                    throw new InvalidDataException( "Clipboard text consent cannot retain a control body." );
                }

                Retained.Remove( ClipboardOfferKind.GnomeCopiedFiles );
                return null;
            }

            return ParseList( ClipboardOfferKind.GnomeCopiedFiles, true );
        }

        internal ClipboardHttpUrl? ReadSource( ClipboardContentKind kind, string? plain, ClipboardHttpUrl? plainUrl, bool hasHtml, bool hasPng )
        {
            if ( !Retained.Contains( ClipboardOfferKind.ChromiumSourceUrl ) )
            {
                return null;
            }

            if ( kind != ClipboardContentKind.Files && (plain is not null || hasHtml || hasPng) &&
                ClipboardUriList.TryParseChromiumSource( Bytes( ClipboardOfferKind.ChromiumSourceUrl ).Span, out var source ) &&
                (plainUrl is null || source!.Key == plainUrl.Key) )
            {
                return source;
            }

            Retained.Remove( ClipboardOfferKind.ChromiumSourceUrl );
            return null;
        }
    }

    private static void ValidateReceived( ClipboardOfferPlan plan, IReadOnlyDictionary<int, ReadOnlyMemory<byte>> received )
    {
        if ( !plan.Accepted || received.Count != plan.Offers.Count )
        {
            throw new InvalidDataException( "Clipboard selection is incomplete or unadmitted." );
        }

        foreach ( var group in plan.Offers.GroupBy( static offer => offer.Kind ) )
        {
            ReadOnlyMemory<byte>? first = null;
            foreach ( var offer in group )
            {
                if ( !received.TryGetValue( offer.InventoryIndex, out var bytes ) || bytes.Length is < 1 || bytes.Length > offer.MaximumBytes )
                {
                    throw new InvalidDataException( "Clipboard offer exceeds its admitted bounds." );
                }

                if ( first.HasValue && !first.Value.Span.SequenceEqual( bytes.Span ) )
                {
                    throw new InvalidDataException( "Clipboard semantic aliases disagree." );
                }

                first = bytes;
            }
        }
    }

    private static string? ReadPlain( ReadOnlyMemory<byte> bytes )
    {
        if ( bytes.IsEmpty )
        {
            return null;
        }

        var text = StrictUtf8.GetString( bytes.Span );
        if ( text.Contains( '\0' ) )
        {
            throw new InvalidDataException( "Clipboard plain text contains a null character." );
        }

        return text;
    }

    private static ClipboardContentKind ClassifyKind( ClipboardUriList? files, string? plain, ClipboardHttpUrl? plainUrl,
        bool hasPng, bool hasHtml, ClipboardUriList? uri, bool pairingFailed )
    {
        if ( files is not null )
        {
            return ClipboardContentKind.Files;
        }

        if ( plain is not null && (plainUrl is null || pairingFailed) )
        {
            return ClipboardContentKind.Text;
        }

        if ( hasPng )
        {
            return ClipboardContentKind.Images;
        }

        if ( hasHtml )
        {
            return ClipboardContentKind.Text;
        }

        if ( uri is not null || plainUrl is not null )
        {
            return ClipboardContentKind.Links;
        }

        throw new InvalidDataException( "Clipboard selection has no usable representation." );
    }

    private static IReadOnlyList<ClipboardUriDestination> Destinations( ClipboardContentKind kind, ClipboardUriList? files,
        ClipboardUriList? uri, ClipboardHttpUrl? plainUrl ) => kind switch
        {
            ClipboardContentKind.Files => files!.Destinations,
            ClipboardContentKind.Links when uri is not null => uri.Destinations,
            ClipboardContentKind.Links when plainUrl is not null => [new( plainUrl.Literal, plainUrl.Key, plainUrl, null )],
            _ => []
        };
}
