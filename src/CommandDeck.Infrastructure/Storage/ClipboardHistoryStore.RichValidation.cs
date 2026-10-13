namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    private void ValidateRichPayloadReferences( )
    {
        if ( Scalar( """
            SELECT count(*) FROM entries e LEFT JOIN clipboard_selections s ON s.entry_id=e.id
            LEFT JOIN clipboard_offers o ON o.entry_id=e.id LEFT JOIN clipboard_payloads p ON p.id=o.payload_id
            WHERE s.entry_id IS NULL OR s.kind NOT IN ('text','images','links','files') OR typeof(s.preferred_mime)!='text' OR
                typeof(s.dedup_key)!='text' OR length(s.dedup_key)!=64 OR o.entry_id IS NULL OR
                typeof(o.mime)!='text' OR typeof(o.offer_index)!='integer' OR o.offer_index NOT BETWEEN 0 AND 511 OR
                p.id IS NULL OR p.state!=1 OR typeof(p.actual)!='integer' OR p.actual<1 OR typeof(p.digest)!='text' OR length(p.digest)!=64
            """ ) != 0 ||
            Scalar( "SELECT count(*) FROM entries e WHERE (SELECT count(*) FROM clipboard_offers o WHERE o.entry_id=e.id) NOT BETWEEN 1 AND 16" ) != 0 ||
            Scalar( "SELECT count(*) FROM (SELECT entry_id,offer_index FROM clipboard_offers GROUP BY entry_id,offer_index HAVING count(*)>1)" ) != 0 ||
            Scalar( "SELECT count(*) FROM clipboard_selections" ) != Scalar( "SELECT count(*) FROM entries" ) ||
            Scalar( "SELECT count(*) FROM clipboard_payloads WHERE state=1 AND id NOT IN (SELECT payload_id FROM clipboard_offers)" ) != 0 )
        {
            throw new IOException( "Clipboard payload relationships are invalid; existing data was preserved." );
        }

        if ( PolicyRows( ).Where( static row => row.Pinned ).Sum( static row => row.Bytes ) > ( long ) ReadPolicy( ).QuotaMiB * 1024 * 1024 / 2 )
        {
            throw new IOException( "Clipboard pinned payloads exceed their capacity; existing data was preserved." );
        }

        using var command = _connection.CreateCommand( );
        command.CommandText = """
            SELECT e.id,e.text,s.kind,s.preferred_mime,s.dedup_key,o.mime,p.actual,p.digest
            FROM entries e JOIN clipboard_selections s ON s.entry_id=e.id JOIN clipboard_offers o ON o.entry_id=e.id
            JOIN clipboard_payloads p ON p.id=o.payload_id ORDER BY e.id,o.mime
            """;
        using var reader = command.ExecuteReader( );
        List<StoredOffer> offers = [];
        StoredSelection? selection = null;
        while ( reader.Read( ) )
        {
            var id = reader.GetString( 0 );
            if ( selection is not null && selection.Id != id )
            {
                ValidateStoredOffers( selection, offers );
                offers.Clear( );
            }

            selection = new( id, reader.GetString( 1 ), reader.GetString( 2 ), reader.GetString( 3 ), reader.GetString( 4 ) );
            offers.Add( new( reader.GetString( 5 ), reader.GetInt64( 6 ), reader.GetString( 7 ) ) );
        }

        if ( selection is not null )
        {
            ValidateStoredOffers( selection, offers );
        }
    }

    private static void ValidateStoredOffers( StoredSelection selection, IReadOnlyList<StoredOffer> offers )
    {
        var names = offers.Select( static offer => offer.Mime ).ToArray( );
        var plan = ClipboardOfferPlan.Create( names, ClipboardConsent.Rich, chromiumSourceFixturesVerified: true );
        if ( !plan.Accepted || plan.Offers.Count != names.Length || !IsLowerDigest( selection.Digest ) ||
            !names.Contains( selection.Preferred, StringComparer.Ordinal ) ||
            plan.Offers.Any( offer => offers[offer.InventoryIndex].Length > offer.MaximumBytes || !IsLowerDigest( offers[offer.InventoryIndex].Digest ) ) )
        {
            throw new IOException( "Clipboard retained offers are invalid; existing data was preserved." );
        }

        foreach ( var aliases in plan.Offers.GroupBy( static offer => offer.Kind ) )
        {
            var first = offers[aliases.First( ).InventoryIndex];
            if ( aliases.Any( offer => offers[offer.InventoryIndex].Length != first.Length || offers[offer.InventoryIndex].Digest != first.Digest ) )
            {
                throw new IOException( "Clipboard retained aliases disagree; existing data was preserved." );
            }
        }

        var preferred = plan.Offers.Single( offer => offer.RawName == selection.Preferred ).Kind;
        var validPreferred = selection.Kind switch
        {
            ClipboardPresentationKinds.Text => preferred is ClipboardOfferKind.PlainText or ClipboardOfferKind.Html,
            ClipboardPresentationKinds.Images => preferred == ClipboardOfferKind.Png,
            ClipboardPresentationKinds.Links => preferred is ClipboardOfferKind.PlainText or ClipboardOfferKind.UriList,
            ClipboardPresentationKinds.Files => preferred is ClipboardOfferKind.UriList or ClipboardOfferKind.GnomeCopiedFiles,
            _ => false
        };
        if ( !validPreferred )
        {
            throw new IOException( "Clipboard preferred representation is invalid; existing data was preserved." );
        }

        if ( selection.Id.Length == 64 )
        {
            ValidateLegacyOfferIdentity( selection, offers, plan, names );
        }
    }

    private static void ValidateLegacyOfferIdentity( StoredSelection selection, IReadOnlyList<StoredOffer> offers,
        ClipboardOfferPlan plan, string[] names )
    {
        var bytes = StrictUtf8.GetBytes( selection.Text );
        if ( selection.Kind is not (ClipboardPresentationKinds.Text or ClipboardPresentationKinds.Links) || plan.Offers.Any( static offer => offer.Kind != ClipboardOfferKind.PlainText ) ||
            offers.Any( offer => offer.Length != bytes.Length || !string.Equals( offer.Digest, selection.Id, StringComparison.OrdinalIgnoreCase ) ) ||
            selection.Digest != TextOfferDigest( bytes, names ) ||
            selection.Kind == ClipboardPresentationKinds.Links && !ClipboardHttpUrl.TryParseSingleHttpUrl( selection.Text, out _, out _ ) )
        {
            throw new IOException( "Clipboard text identity is invalid; existing data was preserved." );
        }
    }

    private static bool IsLowerDigest( string text ) => text.Length == 64 && text.All( static character => character is >= '0' and <= '9' or >= 'a' and <= 'f' );
    private sealed record StoredSelection( string Id, string Text, string Kind, string Preferred, string Digest );
    private sealed record StoredOffer( string Mime, long Length, string Digest );
}
