using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardOfferPlannerBoundsAndOrdersInventory( )
    {
        string[] inventory = ["image/png", "text/html;charset=utf-8", "text/plain;charset=utf-8",
            "text/uri-list", "x-special/gnome-copied-files", "text/plain", "text/html",
            "TEXT/URI-LIST", "chromium/x-source-url", "application/unsupported"];
        var plan = ClipboardOfferPlan.Create( inventory, ClipboardConsent.Rich, true );
        True( plan.Accepted );
        Equal( 9, plan.Offers.Count );
        Equal( 16 * 1024 * 1024 + 2 * 1024 * 1024 + 4 * 64 * 1024 + 8192, plan.ReservedBytes );
        Equal( "x-special/gnome-copied-files|text/plain|text/plain;charset=utf-8|TEXT/URI-LIST|text/uri-list|text/html|text/html;charset=utf-8|image/png|chromium/x-source-url",
            string.Join( '|', plan.Offers.Select( static offer => offer.RawName ) ) );
        foreach ( var offer in plan.Offers )
        {
            Equal( inventory[offer.InventoryIndex], offer.RawName );
        }

        var reversed = ClipboardOfferPlan.Create( inventory.Reverse( ).ToArray( ), ClipboardConsent.Rich, true );
        Equal( string.Join( '|', plan.Offers.Select( static offer => offer.RawName ) ),
            string.Join( '|', reversed.Offers.Select( static offer => offer.RawName ) ) );
        var text = ClipboardOfferPlan.Create( inventory, ClipboardConsent.Text );
        Equal( 3, text.Offers.Count );
        Equal( 65536 + 8, text.ReservedBytes );
        True( text.Offers[0].HeaderOnly );
        Equal( 8, text.Offers[0].MaximumBytes );
        True( text.Offers.Skip( 1 ).All( static offer => offer.Kind == ClipboardOfferKind.PlainText ) );
        Equal( 8, ClipboardOfferPlan.Create( inventory, ClipboardConsent.Rich ).Offers.Count );
        Equal( ClipboardInventoryRejection.Unsupported,
            ClipboardOfferPlan.Create( ["chromium/x-source-url"], ClipboardConsent.Rich, true ).Rejection );
        Equal( ClipboardInventoryRejection.CaptureDisabled, ClipboardOfferPlan.Create( inventory, ClipboardConsent.None ).Rejection );

        var crowded = Enumerable.Range( 0, 511 ).Select( static index => "application/unsupported-" + index ).Append( "text/plain" ).ToArray( );
        Equal( 1, ClipboardOfferPlan.Create( crowded, ClipboardConsent.Text ).Offers.Count );
        Equal( ClipboardInventoryRejection.InvalidInventory,
            ClipboardOfferPlan.Create( crowded.Append( "image/png" ).ToArray( ), ClipboardConsent.Rich ).Rejection );
    }

    private static void ClipboardOfferPlannerRejectsHintsAndAliasOverflow( )
    {
        string[][] excessAliases =
        [
            ["text/plain", "text/plain;charset=utf-8", "TEXT/PLAIN"],
            ["text/html", "TEXT/HTML", "text/html ; charset = UTF-8"],
            ["text/uri-list", "TEXT/URI-LIST", "Text/Uri-List"],
            ["image/png", "IMAGE/PNG"],
            ["x-special/gnome-copied-files", "X-SPECIAL/GNOME-COPIED-FILES"],
            ["chromium/x-source-url", "CHROMIUM/X-SOURCE-URL"]
        ];
        foreach ( var names in excessAliases )
        {
            foreach ( var consent in new[] { ClipboardConsent.Text, ClipboardConsent.Rich } )
            {
                var plan = ClipboardOfferPlan.Create( names.Append( "application/unsupported" ).ToArray( ), consent );
                Equal( ClipboardInventoryRejection.AliasLimit, plan.Rejection );
                Equal( 0, plan.ReservedBytes );
                Equal( 0, plan.Offers.Count );
            }
        }

        foreach ( var hint in new[] { "x-kde-passwordManagerHint", "X-KDE-PASSWORDMANAGERHINT ; broken",
            "application/x-nspasteboard-concealed-type", "ExcludeClipboardContentFromMonitorProcessing" } )
        {
            var plan = ClipboardOfferPlan.Create( ["text/plain", hint], ClipboardConsent.Rich );
            Equal( ClipboardInventoryRejection.SensitiveHint, plan.Rejection );
            Equal( 0, plan.Offers.Count );
        }

        Equal( ClipboardInventoryRejection.SelfWrite,
            ClipboardOfferPlan.Create( ["text/plain", ClipboardOfferPlan.OwnerMime], ClipboardConsent.Text ).Rejection );
        var repeated = ClipboardOfferPlan.Create( ["text/plain;charset=utf-8", "text/plain", "text/plain;charset=utf-8", "TEXT", "STRING", "UTF8_STRING"], ClipboardConsent.Text );
        True( repeated.Accepted, "a browser inventory repeating one name is admitted" );
        Equal( 2, repeated.Offers.Count );
        True( repeated.Offers.Select( static offer => offer.InventoryIndex ).SequenceEqual( [1, 0] ), "the first occurrence of a repeated name is requested" );
        foreach ( var names in new[] { new[] { "", "text/plain" },
            new[] { "text/plain\n" }, new[] { "text/plain\t;charset=utf-8" }, new[] { "text/pläin" }, new[] { new string( 'a', 257 ) } } )
        {
            Equal( ClipboardInventoryRejection.InvalidInventory, ClipboardOfferPlan.Create( names, ClipboardConsent.Rich ).Rejection );
        }

        foreach ( var unsupported in new[] { "text/plain;charset=latin1", "text/plain;charset=utf-8;charset=UTF-8",
            "text/plain;other=value", "image/png;charset=utf-8", "text/plain; charset", "TEXT", "STRING", "text/x-moz-url" } )
        {
            Equal( ClipboardInventoryRejection.Unsupported, ClipboardOfferPlan.Create( [unsupported], ClipboardConsent.Rich ).Rejection );
        }

        Equal( ClipboardOfferKind.PlainText,
            ClipboardOfferPlan.Create( ["TeXt/PlAiN ; CHARSET = UtF-8"], ClipboardConsent.Text ).Offers.Single( ).Kind );
    }

    private static async Task ClipboardGnomeHeaderStopsBeforeBodyAsync( )
    {
        foreach ( var (literal, expected) in new[]
        {
            ("copy\n", ClipboardGnomeHeaderResult.Copy), ("copy\r\n", ClipboardGnomeHeaderResult.Copy),
            ("cut\n", ClipboardGnomeHeaderResult.Cut), ("cut\r\n", ClipboardGnomeHeaderResult.Cut),
            ("COPY\n", ClipboardGnomeHeaderResult.Invalid), (" copy\n", ClipboardGnomeHeaderResult.Invalid),
            ("\uFEFFcopy\n", ClipboardGnomeHeaderResult.Invalid), ("copy\rX\n", ClipboardGnomeHeaderResult.Invalid),
            ("not-copy\n", ClipboardGnomeHeaderResult.Invalid)
        } )
        {
            var bytes = Encoding.UTF8.GetBytes( literal + "file:///must-not-be-read\n" );
            using var stream = new HeaderOnlyReadStream( bytes );
            var result = await ClipboardGnomeHeader.ReadAsync( stream, CancellationToken.None );
            Equal( expected, result.Result );
            Equal( Math.Min( 8, Encoding.UTF8.GetByteCount( literal ) ), ( int ) stream.Position );
            True( result.Bytes.Span.SequenceEqual( bytes.AsSpan( 0, ( int ) stream.Position ) ) );
        }

        using var truncated = new HeaderOnlyReadStream( "copy"u8.ToArray( ) );
        Equal( ClipboardGnomeHeaderResult.Invalid, (await ClipboardGnomeHeader.ReadAsync( truncated, CancellationToken.None )).Result );
    }

    private sealed class HeaderOnlyReadStream( byte[] bytes ) : MemoryStream( bytes )
    {
        public override ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        {
            Equal( 1, buffer.Length );
            return base.ReadAsync( buffer, cancellationToken );
        }
    }
}
