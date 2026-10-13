using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardFileUrisPreserveLocalLexicalReferences( )
    {
        foreach ( var (literal, path) in new[]
        {
            ("file:///no-such-clipboard-fixture/a%20b%23c%3Fd%25.txt", "/no-such-clipboard-fixture/a b#c?d%.txt"),
            ("FILE://LOCALHOST/tmp/caf%C3%A9", "/tmp/café"),
            ("file:/tmp/a", "/tmp/a"), ("file:///tmp/%252F", "/tmp/%2F"),
            ("file:///tmp/a!$&'()*+,;=:@~_-", "/tmp/a!$&'()*+,;=:@~_-"),
            ("file:///", "/"), ("file:///tmp//a", "/tmp//a")
        } )
        {
            True( ClipboardFileUri.TryParse( literal, out var value, out var rejection ), literal );
            Equal( ClipboardUrlRejection.None, rejection );
            Equal( literal, value!.Literal );
            Equal( path, value.Path );
            Equal( path, value.Key );
        }

        foreach ( var literal in new[]
        {
            "file://remote/tmp/a", "file://user@localhost/tmp/a", "file://localhost:80/tmp/a",
            "file://localhost", "file:relative", "file:~/tmp", "file:////remote/share",
            "file:///tmp/a?query", "file:///tmp/a#fragment", "file:///tmp/%2fetc", "file:///tmp/%5Cetc",
            "file:///tmp/../secret", "file:///tmp/%2e%2E/secret", "file:///./a", "file:///tmp/a\\b",
            "file:///tmp/a b", "file:///tmp/café", "file:///tmp/%FF", "file:///tmp/%E2%80%AEname",
            "file:///tmp/%00", "file:///tmp/%0A", "file:///tmp/[a]", " file:///tmp/a", "file:///tmp/a\r\n"
        } )
        {
            True( !ClipboardFileUri.TryParse( literal, out var value, out _ ), literal );
            True( value is null );
        }
    }

    private static void ClipboardUriListsPreserveOrderedUniqueDestinations( )
    {
        var raw = Encoding.UTF8.GetBytes( "\uFEFF# comment café\r\n\nHTTPS://EXAMPLE.COM:443/%7Euser\r\nhttps://example.com/~user\nhttps://example.com/a/../b?z=2&z=1" );
        var copy = raw.ToArray( );
        True( ClipboardUriList.TryParse( raw, false, out var value, out var rejection ) );
        Equal( ClipboardUriListRejection.None, rejection );
        True( !value!.IsFiles );
        Equal( 3, value.RecordCount );
        Equal( 2, value.Destinations.Count );
        Equal( "HTTPS://EXAMPLE.COM:443/%7Euser", value.Destinations[0].Literal );
        Equal( "https://example.com/~user", value.Destinations[0].Key );
        Equal( "https://example.com/a/../b?z=2&z=1", value.Destinations[1].Key );
        True( copy.AsSpan( ).SequenceEqual( raw ), "parsing does not rewrite raw restore bytes" );

        True( ClipboardUriList.TryParse( "file:///tmp/a%20b\nfile://localhost/tmp/a%20b\nfile:/tmp/c\n"u8,
            false, out var files, out _ ) );
        True( files!.IsFiles );
        Equal( 3, files.RecordCount );
        Equal( 2, files.Destinations.Count );
        Equal( "/tmp/a b", files.Destinations[0].File!.Path );
        True( ClipboardUriList.TryParse( Encoding.ASCII.GetBytes( string.Concat( Enumerable.Repeat( "https://example.com/\n", 256 ) ) ),
            false, out var duplicates, out _ ) );
        Equal( 256, duplicates!.RecordCount );
        Equal( 1, duplicates.Destinations.Count );
    }

    private static void ClipboardUriListsRejectIncompleteAndMixedRepresentations( )
    {
        foreach ( var literal in new[]
        {
            "", "# only comment\n", "https://example.com/\r", "https://example.com/\rhttps://other.test/",
            "https://example.com/\nfile:///tmp/a", "https://example.com/\nunsupported:thing", "https://example.com/\n relative",
            "https://example.com/\nhttps://bad.test/ ", "https://example.com/\n# bad\0comment", "https://example.com/\n#\u202Ecomment",
            "https://example.com/\nhttps://bad.test/%C0%AF", "https://example.com/\nhttps://user@bad.test/",
            "https://example.com/\n\uFEFFhttps://bad.test/", "https://example.com/\n# bad\rcomment"
        } )
        {
            True( !ClipboardUriList.TryParse( Encoding.UTF8.GetBytes( literal ), false, out var value, out _ ), literal );
            True( value is null, "invalid lists never expose partial destinations" );
        }

        True( !ClipboardUriList.TryParse( [0xff], false, out _, out var encoding ) );
        Equal( ClipboardUriListRejection.InvalidEncoding, encoding );
        True( !ClipboardUriList.TryParse( new byte[65537], false, out _, out var oversized ) );
        Equal( ClipboardUriListRejection.TooLarge, oversized );
        True( !ClipboardUriList.TryParse( Encoding.ASCII.GetBytes( string.Concat( Enumerable.Repeat( "https://example.com/\n", 257 ) ) ),
            false, out _, out var crowded ) );
        Equal( ClipboardUriListRejection.TooManyRecords, crowded );
        True( !ClipboardUriList.TryParse( Encoding.ASCII.GetBytes( "https://example.com/" + new string( 'a', 8192 ) ), false, out _, out _ ) );
    }

    private static void ClipboardGnomePairingAndBrowserSourceAreStrict( )
    {
        True( ClipboardUriList.TryParse( "copy\r\nfile:///tmp/a\r\nfile:///tmp/b\n"u8, true, out var gnome, out _ ) );
        True( ClipboardUriList.TryParse( "file://localhost/tmp/a\nfile:///tmp/a\nfile:/tmp/b"u8, false, out var uri, out _ ) );
        True( gnome!.HasSameOrderedDestinations( uri! ) );
        True( ClipboardUriList.TryParse( "file:///tmp/b\nfile:///tmp/a"u8, false, out var reversed, out _ ) );
        True( !gnome.HasSameOrderedDestinations( reversed! ) );
        foreach ( var literal in new[] { "copy", "COPY\nfile:///a", "\uFEFFcopy\nfile:///a", "copy\n# comment\nfile:///a",
            "copy\nhttps://example.com/", "copy\n", "copy\nfile:///a\r" } )
        {
            True( !ClipboardUriList.TryParse( Encoding.UTF8.GetBytes( literal ), true, out _, out _ ), literal );
        }

        foreach ( var newline in new[] { "\n", "\r\n" } )
        {
            True( !ClipboardUriList.TryParse( Encoding.ASCII.GetBytes( "cut" + newline + "file:///a" ), true, out _, out var move ) );
            Equal( ClipboardUriListRejection.Move, move );
        }

        foreach ( var suffix in new[] { "", "\n", "\r\n" } )
        {
            True( ClipboardUriList.TryParseChromiumSource( Encoding.ASCII.GetBytes( "https://EXAMPLE.com/%7e" + suffix ), out var source ) );
            Equal( "https://EXAMPLE.com/%7e", source!.Literal );
        }

        foreach ( var literal in new[] { "\uFEFFhttps://example.com/", "https://example.com/\n\n", "https://example.com/\r",
            "https://example.com/\nhttps://other.test/", "file:///tmp/a", "https://example.com/café" } )
        {
            True( !ClipboardUriList.TryParseChromiumSource( Encoding.UTF8.GetBytes( literal ), out _ ), literal );
        }
    }
}
