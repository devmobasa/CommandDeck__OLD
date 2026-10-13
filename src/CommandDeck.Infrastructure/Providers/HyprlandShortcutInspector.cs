using System.Globalization;
using System.Text.Json;

namespace CommandDeck;

public static class HyprlandShortcutInspector
{
    private static readonly ShortcutTarget[] Targets =
    [
        new("commanddeck", "CommandDeck", "local.commanddeck", ["CommandDeck"]),
        new("omarchy-control", "Omarchy Control", "omarchy-menu toggle root", ["Omarchy menu"]),
        new("applications", "Applications", "omarchy-menu toggle apps", ["Installed applications"]),
        new("system-menu", "System Menu", "omarchy-menu toggle system", ["System menu"])
    ];

    public static async ValueTask<IReadOnlyList<ShortcutBinding>> InspectAsync(
        CancellationToken cancellationToken )
    {
        if ( !ProcessRunner.ExistsOnPath( "hyprctl" ) )
        {
            return EmptyBindings(
                ShortcutStatuses.NotConfigured,
                "hyprctl is unavailable on PATH." );
        }

        var result = await ProcessRunner.RunAsync(
            "hyprctl",
            ["-j", "binds"],
            standardInput: null,
            workingDirectory: null,
            TimeSpan.FromMilliseconds( 600 ),
            cancellationToken ).ConfigureAwait( false );
        if ( result.TimedOut )
        {
            return EmptyBindings(
                ShortcutStatuses.NotConfigured,
                "Shortcut inspection timed out." );
        }

        if ( result.ExitCode != 0 )
        {
            var detail = string.IsNullOrWhiteSpace( result.StandardError )
                ? $"hyprctl exited with status {result.ExitCode}."
                : result.StandardError.Trim( );
            return EmptyBindings( ShortcutStatuses.NotConfigured, detail );
        }

        return Parse( result.StandardOutput );
    }

    public static async ValueTask<ShortcutInventory> InspectInventoryAsync(
        CancellationToken cancellationToken )
    {
        if ( !ProcessRunner.ExistsOnPath( "hyprctl" ) )
        {
            return new ShortcutInventory
            {
                Detail = "hyprctl is unavailable on PATH."
            };
        }
        var result = await ProcessRunner.RunAsync(
            "hyprctl", ["-j", "binds"], standardInput: null, workingDirectory: null,
            TimeSpan.FromMilliseconds( 600 ), cancellationToken ).ConfigureAwait( false );
        if ( result.TimedOut || result.ExitCode != 0 )
        {
            return new ShortcutInventory
            {
                Detail = result.TimedOut
                    ? "Shortcut inspection timed out."
                    : string.IsNullOrWhiteSpace( result.StandardError )
                        ? $"hyprctl exited with status {result.ExitCode}."
                        : result.StandardError.Trim( )
            };
        }
        return ParseInventory( result.StandardOutput );
    }

    public static ShortcutInventory ParseInventory( string json )
    {
        try
        {
            using var document = JsonDocument.Parse( json );
            if ( document.RootElement.ValueKind != JsonValueKind.Array )
            {
                return new ShortcutInventory { Detail = "hyprctl returned an unexpected response." };
            }
            return new ShortcutInventory
            {
                Available = true,
                Chords = document.RootElement.EnumerateArray( )
                    .Select( FormatChord )
                    .Where( static value => value.Length > 0 )
                    .Select( TryNormalizeInventoryChord )
                    .Where( static value => value is not null )
                    .Select( static value => value! )
                    .Distinct( StringComparer.OrdinalIgnoreCase )
                    .Order( StringComparer.OrdinalIgnoreCase )
                    .ToList( )
            };
        }
        catch ( JsonException )
        {
            return new ShortcutInventory { Detail = "hyprctl returned malformed JSON." };
        }
    }

    private static string? TryNormalizeInventoryChord( string chord )
    {
        try
        {
            return SavedSearchValidator.NormalizeShortcut( chord );
        }
        catch ( InvalidOperationException )
        {
            return null;
        }
    }

    public static IReadOnlyList<ShortcutBinding> Parse( string json )
    {
        var chords = Targets.ToDictionary(
            static target => target.Id,
            static _ => new HashSet<string>( StringComparer.OrdinalIgnoreCase ),
            StringComparer.Ordinal );
        try
        {
            using var document = JsonDocument.Parse( json );
            if ( document.RootElement.ValueKind != JsonValueKind.Array )
            {
                return EmptyBindings(
                    ShortcutStatuses.NotConfigured,
                    "hyprctl returned an unexpected response." );
            }

            foreach ( var bind in document.RootElement.EnumerateArray( ) )
            {
                var dispatcher = GetString( bind, "dispatcher" ) ?? string.Empty;
                var argument = GetString( bind, "arg" ) ?? string.Empty;
                var command = $"{dispatcher} {argument}";
                var description = GetString( bind, "description" ) ?? string.Empty;
                var chord = FormatChord( bind );
                if ( chord.Length == 0 )
                {
                    continue;
                }

                foreach ( var target in Targets )
                {
                    if ( command.Contains( target.Command, StringComparison.Ordinal ) ||
                        target.Descriptions.Contains( description, StringComparer.OrdinalIgnoreCase ) )
                    {
                        chords[target.Id].Add( chord );
                    }
                }
            }
        }
        catch ( JsonException )
        {
            return EmptyBindings(
                ShortcutStatuses.NotConfigured,
                "hyprctl returned malformed JSON." );
        }

        return Targets.Select( target => new ShortcutBinding
        {
            SurfaceId = target.Id,
            Label = target.Label,
            Chords = chords[target.Id].Order( StringComparer.OrdinalIgnoreCase ).ToList( ),
            Status = chords[target.Id].Count > 0
                ? ShortcutStatuses.Configured
                : ShortcutStatuses.NotConfigured,
            IsAvailable = chords[target.Id].Count > 0
        } ).ToArray( );
    }

    private static string FormatChord( JsonElement bind )
    {
        var modifiers = GetInt64( bind, "modmask" );
        List<string> parts = [];
        if ( (modifiers & 64) != 0 )
        {
            parts.Add( "Super" );
        }

        if ( (modifiers & 4) != 0 )
        {
            parts.Add( "Ctrl" );
        }

        if ( (modifiers & 8) != 0 )
        {
            parts.Add( "Alt" );
        }

        if ( (modifiers & 1) != 0 )
        {
            parts.Add( "Shift" );
        }

        var key = GetString( bind, "key" );
        if ( string.IsNullOrWhiteSpace( key ) )
        {
            var keyCode = GetInt64( bind, "keycode" );
            if ( keyCode > 0 )
            {
                key = $"code:{keyCode.ToString( CultureInfo.InvariantCulture )}";
            }
        }

        if ( string.IsNullOrWhiteSpace( key ) )
        {
            return string.Empty;
        }

        parts.Add( NormalizeKey( key ) );
        return string.Join( '+', parts );
    }

    private static string NormalizeKey( string key ) => key.ToUpperInvariant( ) switch
    {
        "ESCAPE" => "Escape",
        "SPACE" => "Space",
        "RETURN" => "Enter",
        _ when key.Length == 1 => key.ToUpperInvariant( ),
        _ => string.Concat( key[..1].ToUpperInvariant( ), key[1..].ToLowerInvariant( ) )
    };

    private static string? GetString( JsonElement element, string property ) =>
        element.TryGetProperty( property, out var value ) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString( )
            : null;

    private static long GetInt64( JsonElement element, string property ) =>
        element.TryGetProperty( property, out var value ) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64( out var parsed )
            ? parsed
            : 0;

    private static ShortcutBinding[] EmptyBindings(
        string status,
        string? detail ) =>
        Targets.Select( target => new ShortcutBinding
        {
            SurfaceId = target.Id,
            Label = target.Label,
            Status = status,
            Detail = detail,
            IsAvailable = false
        } ).ToArray( );

    private sealed record ShortcutTarget(
        string Id,
        string Label,
        string Command,
        IReadOnlyList<string> Descriptions );
}
