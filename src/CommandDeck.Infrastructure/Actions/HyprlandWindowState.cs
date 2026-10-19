using System.Globalization;
using System.Text.Json;

namespace CommandDeck;

// A client as `hyprctl -j clients` or `activewindow` reports it. Fullscreen is
// Hyprland's mode bitmask: 1 maximized, 2 fullscreen.
internal sealed record HyprlandWindow(
    string Address,
    string Class,
    string Title,
    int MonitorId,
    bool Floating,
    int Fullscreen,
    int Width,
    int Height )
{
    public string Selector => HyprlandWindowState.AddressPrefix + Address;
}

internal sealed record HyprlandMonitor( int Id, string Name, bool Disabled, MonitorLayout Layout );

// Reads the compositor JSON that window commands depend on. Malformed or
// incomplete entries are skipped, never guessed.
internal static class HyprlandWindowState
{
    internal const string AddressPrefix = "address:";
    private const int MaximumAddressDigits = 16;
    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create( "0123456789abcdef" );

    // A literal window address, never a class or title pattern that could match
    // a different window.
    public static bool IsWindowSelector( string selector ) =>
        selector.StartsWith( AddressPrefix + "0x", StringComparison.Ordinal ) &&
        selector.Length > AddressPrefix.Length + 2 &&
        selector.Length <= AddressPrefix.Length + 2 + MaximumAddressDigits &&
        selector.AsSpan( AddressPrefix.Length + 2 ).IndexOfAnyExcept( HexDigits ) < 0;

    public static HyprlandWindow? ParseActiveWindow( string json ) =>
        Parse( json, static root => root.ValueKind == JsonValueKind.Object ? ReadWindow( root ) : null );

    public static IReadOnlyList<HyprlandWindow> ParseClients( string json ) =>
        Parse( json, static root => ReadArray( root, ReadWindow ) ) ?? [];

    public static IReadOnlyList<HyprlandMonitor> ParseMonitors( string json ) =>
        Parse( json, static root => ReadArray( root, ReadMonitor ) ) ?? [];

    private static T? Parse<T>( string json, Func<JsonElement, T?> read )
        where T : class
    {
        if ( string.IsNullOrWhiteSpace( json ) )
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse( json );
            return read( document.RootElement );
        }
        catch ( JsonException )
        {
            return null;
        }
    }

    private static List<T> ReadArray<T>( JsonElement root, Func<JsonElement, T?> read )
        where T : class
    {
        List<T> values = [];
        if ( root.ValueKind != JsonValueKind.Array )
        {
            return values;
        }

        foreach ( var element in root.EnumerateArray( ) )
        {
            if ( element.ValueKind == JsonValueKind.Object && read( element ) is { } value )
            {
                values.Add( value );
            }
        }

        return values;
    }

    private static HyprlandWindow? ReadWindow( JsonElement element )
    {
        var address = String( element, "address" );
        if ( address is null || !IsWindowSelector( AddressPrefix + address ) ||
            Integer( element, "monitor" ) is not { } monitor )
        {
            return null;
        }

        var size = Pair( element, "size" );
        return new HyprlandWindow(
            address,
            String( element, "class" ) ?? String( element, "initialClass" ) ?? "window",
            String( element, "title" ) ?? string.Empty,
            monitor,
            element.TryGetProperty( "floating", out var floating ) && floating.ValueKind == JsonValueKind.True,
            Integer( element, "fullscreen" ) ?? 0,
            size.First,
            size.Second );
    }

    private static HyprlandMonitor? ReadMonitor( JsonElement element )
    {
        var name = String( element, "name" );
        var reserved = Reserved( element );
        if ( name is null || reserved is null ||
            Integer( element, "id" ) is not { } id ||
            Integer( element, "x" ) is not { } x || Integer( element, "y" ) is not { } y ||
            Integer( element, "width" ) is not { } width || Integer( element, "height" ) is not { } height ||
            !element.TryGetProperty( "scale", out var scale ) || scale.ValueKind != JsonValueKind.Number )
        {
            return null;
        }

        MonitorLayout layout = new( x, y, width, height, scale.GetDouble( ), Integer( element, "transform" ) ?? 0,
            reserved[0], reserved[1], reserved[2], reserved[3] );
        var disabled = element.TryGetProperty( "disabled", out var flag ) && flag.ValueKind == JsonValueKind.True;
        return new HyprlandMonitor( id, name, disabled, layout );
    }

    // Left, top, right, and bottom, in the order hyprctl prints them.
    private static int[]? Reserved( JsonElement element )
    {
        if ( !element.TryGetProperty( "reserved", out var reserved ) ||
            reserved.ValueKind != JsonValueKind.Array || reserved.GetArrayLength( ) != 4 )
        {
            return null;
        }

        var edges = reserved.EnumerateArray( )
            .Select( static edge => edge.ValueKind == JsonValueKind.Number && edge.TryGetInt32( out var value ) && value >= 0 ? value : -1 )
            .ToArray( );
        return edges.All( static edge => edge >= 0 ) ? edges : null;
    }

    private static (int First, int Second) Pair( JsonElement element, string property )
    {
        if ( !element.TryGetProperty( property, out var pair ) ||
            pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength( ) != 2 )
        {
            return (0, 0);
        }

        var first = pair[0].ValueKind == JsonValueKind.Number && pair[0].TryGetInt32( out var a ) ? a : 0;
        var second = pair[1].ValueKind == JsonValueKind.Number && pair[1].TryGetInt32( out var b ) ? b : 0;
        return (first, second);
    }

    private static string? String( JsonElement element, string property ) =>
        element.TryGetProperty( property, out var value ) && value.ValueKind == JsonValueKind.String
            ? value.GetString( )
            : null;

    private static int? Integer( JsonElement element, string property ) =>
        element.TryGetProperty( property, out var value ) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32( out var number )
            ? number
            : null;

    internal static string Number( int value ) => value.ToString( CultureInfo.InvariantCulture );
}
