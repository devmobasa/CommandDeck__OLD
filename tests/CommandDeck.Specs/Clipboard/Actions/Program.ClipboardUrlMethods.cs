namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardUrlPreservesLiteralsAndLexicalKeys( )
    {
        (string Literal, string Key)[] valid =
        [
            ("https://Example.TEST", "https://example.test/"),
            ("HTTP://Example.TEST:080?b=2&a=1#Case", "http://example.test/?b=2&a=1#Case"),
            ("https://example.test:443/A/../B/%7e?x=%2f%3f", "https://example.test/A/../B/~?x=%2F%3F"),
            ("https://example.test:8443/a%20b/%c3%a9", "https://example.test:8443/a%20b/%C3%A9"),
            ("https://[2001:DB8::1]:443/path", "https://[2001:db8::1]/path"),
            ("http://localhost:0/", "http://localhost:0/"),
            ("https://xn--bcher-kva.example./%F0%9F%98%80", "https://xn--bcher-kva.example./%F0%9F%98%80"),
            ("https://example.test/a%2Eb?empty=#", "https://example.test/a.b?empty=#"),
            ("https://example.test/%2E%2E/a", "https://example.test/../a"),
            ("https://example.test/a;v=1!$&'()*+,=:@?q=/??#/?", "https://example.test/a;v=1!$&'()*+,=:@?q=/??#/?")
        ];
        foreach ( var (literal, key) in valid )
        {
            True( ClipboardHttpUrl.TryParseSingleHttpUrl( literal, out var value, out var rejection ), $"URL fixture rejected: {rejection}" );
            Equal( ClipboardUrlRejection.None, rejection );
            Equal( literal, value!.Literal );
            Equal( key, value.Key );
        }

        var maximum = "https://example.test/" + new string( 'a', 8192 - "https://example.test/".Length );
        True( ClipboardHttpUrl.TryParseSingleHttpUrl( maximum, out _, out _ ), "exact 8 KiB ASCII limit is accepted" );
        True( !ClipboardHttpUrl.TryParseSingleHttpUrl( maximum + "a", out _, out var oversized ), "oversize URL is rejected" );
        Equal( ClipboardUrlRejection.TooLarge, oversized );
    }

    private static void ClipboardUrlRejectsRepairControlsAndAmbiguousAuthorities( )
    {
        string?[] invalid =
        [
            null, "", "example.test", "//example.test/path", " https://example.test", "https://example.test ",
            "https://example.test\n", "https://example.test\r\n", "\ufeffhttps://example.test", "https:\\example.test",
            "file:///tmp/item", "javascript:alert(1)", "https:/example.test", "https:///example.test", "https://",
            "https://user@example.test", "https://user:password@example.test", "https://example.test@evil.test",
            "https://example.test:", "https://example.test:-1", "https://example.test:+80", "https://example.test:65536",
            "https://example.test:80x", "https://[::1", "https://::1", "https://[127.0.0.1]", "https://[fe80::1%25eth0]",
            "https://[::1]evil", "https://-host.test", "https://host-.test", "https://host..test", "https://host..",
            "https://.host", "https://.", "https://under_score.test", "https://bücher.test", "https://host/%",
            "https://host/%2", "https://host/%GG", "https://host/%C0%AF", "https://host/%ED%A0%80", "https://host/%FF",
            "https://host/%C3a%A9", "https://host/%00", "https://host/%0a", "https://host/%7F", "https://host/%C2%85",
            "https://host/%E2%80%AE", "https://host/%E2%81%A6", "https://host/a\0b", "https://host/a\tb",
            "https://host/a b", "https://host/a\u007fb", "https://host/aé", "https://host/{value}", "https://host/\"quote\"",
            "https://host/a|b", "https://host/`cmd`", "https://host/a\\b", "https://host/a^b", "https://host/[value]",
            "https://host/?array[]=1", "https://host/#first#second", "https://%65xample.test/"
        ];
        foreach ( var literal in invalid )
        {
            True( !ClipboardHttpUrl.TryParseSingleHttpUrl( literal, out var value, out var rejection ), "hostile URL must not be repaired or accepted" );
            True( value is null && rejection != ClipboardUrlRejection.None, "rejection exposes no usable destination" );
        }
    }
}
