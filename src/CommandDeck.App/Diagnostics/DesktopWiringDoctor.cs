using System.Text.Json;

namespace CommandDeck;

internal enum DesktopCheckState
{
    Passed,
    Warning,
    Skipped
}

internal sealed record DesktopCheck(
    string Name,
    DesktopCheckState State,
    string Detail,
    string? Remediation = null );

// Read-only checks that the Omarchy plugin, the generated Hyprland module and
// the live binding fit together. None is required, so a machine without
// Omarchy or a Hyprland session reports warnings and skips, never a failure.
internal sealed partial class DesktopWiringDoctor(
    Func<string, string?> environment,
    BoundedProcessRunner run,
    Func<string, bool> commandExists )
{
    internal const string PluginId = "local.commanddeck";
    private const long MaximumConfigBytes = 1024 * 1024;
    private const string InstallHotkey = "Run `commanddeck hotkey install`, then `hyprctl reload`.";

    public static DesktopWiringDoctor CreateDefault( ) =>
        new( Environment.GetEnvironmentVariable, RunBoundedAsync, ProcessRunner.ExistsOnPath );

    public async ValueTask<IReadOnlyList<DesktopCheck>> InspectAsync( CancellationToken cancellationToken )
    {
        HyprlandHotkeyPaths paths;
        try
        {
            paths = HyprlandHotkeyPaths.FromEnvironment( environment );
        }
        catch ( InvalidOperationException exception )
        {
            return [new( "Desktop wiring", DesktopCheckState.Skipped, exception.Message )];
        }

        var omarchyDirectory = Path.Combine( Path.GetDirectoryName( paths.HyprDirectory )!, "omarchy" );
        List<DesktopCheck> checks =
        [
            InspectPluginInstallation( omarchyDirectory ),
            InspectPluginEnabled( omarchyDirectory )
        ];

        // Like `hotkey install`, a binding of the default chord that the user
        // wrote in bindings.lua stands in for the generated module.
        var bindings = ReadBounded( paths.Bindings );
        var userChord = bindings is not null && ReadBounded( paths.HotkeyFile ) is null &&
            HyprlandHotkeyConfig.HasUserBinding( HyprlandHotkeyConfig.RemoveLegacyBlock( bindings ),
                HyprlandHotkeyConfig.DefaultChord )
            ? HyprlandHotkeyConfig.NormalizeChord( HyprlandHotkeyConfig.DefaultChord )
            : null;
        (var module, var chord) = InspectHotkeyModule( paths, userChord );
        checks.Add( module );
        checks.Add( InspectHotkeyRequire( paths, userChord ) );
        if ( bindings is not null && HyprlandHotkeyConfig.HasLegacyBlock( bindings ) )
        {
            checks.Add( new(
                "Earlier binding",
                DesktopCheckState.Warning,
                $"{paths.Bindings} still has the CommandDeck block that starts a process per press",
                "Run `commanddeck hotkey install`; it moves the binding to commanddeck.lua." ) );
        }

        checks.AddRange( await InspectHyprlandAsync( chord, userChord, cancellationToken ).ConfigureAwait( false ) );
        return checks;
    }

    private static DesktopCheck InspectPluginInstallation( string omarchyDirectory )
    {
        const string name = "Shell plugin";
        var directory = Path.Combine( omarchyDirectory, "plugins", PluginId );
        var manifest = ReadJson( Path.Combine( directory, "manifest.json" ), out var problem );
        if ( manifest is null )
        {
            return new( name, DesktopCheckState.Warning, problem ?? $"{directory} is missing",
                "Run ./scripts/install-omarchy-plugin.sh from the CommandDeck checkout." );
        }

        using ( manifest )
        {
            var root = manifest.RootElement;
            if ( root.ValueKind != JsonValueKind.Object || GetString( root, "id" ) != PluginId )
            {
                return new( name, DesktopCheckState.Warning, $"{directory}/manifest.json does not declare {PluginId}",
                    "Reinstall it with ./scripts/install-omarchy-plugin.sh." );
            }

            var version = GetString( root, "version" ) ?? "unknown version";
            return new( name, DesktopCheckState.Passed, $"{PluginId} {version} in {directory}" );
        }
    }

    // Mirrors the shell's PluginRegistry: a third-party plugin is enabled when
    // a valid version-1 shell.json lists it in plugins[] and not in disabledPlugins[].
    private static DesktopCheck InspectPluginEnabled( string omarchyDirectory )
    {
        const string name = "Plugin enabled";
        const string enable = "Run `omarchy plugin enable local.commanddeck`.";
        var path = Path.Combine( omarchyDirectory, "shell.json" );
        var config = ReadJson( path, out var problem );
        if ( config is null )
        {
            return new( name, DesktopCheckState.Warning,
                problem ?? $"{path} is missing; the shell's defaults do not enable {PluginId}", enable );
        }

        using ( config )
        {
            var root = config.RootElement;
            if ( root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty( "version", out var version ) ||
                version.ValueKind != JsonValueKind.Number || version.GetDouble( ) != 1 )
            {
                return new( name, DesktopCheckState.Warning,
                    $"{path} has no \"version\": 1, so the shell ignores it", "Restore a valid shell.json, then " + enable );
            }

            if ( ContainsString( root, "disabledPlugins", PluginId ) )
            {
                return new( name, DesktopCheckState.Warning, $"{PluginId} is in disabledPlugins in {path}", enable );
            }

            var listed = root.TryGetProperty( "plugins", out var plugins ) &&
                plugins.ValueKind == JsonValueKind.Array &&
                plugins.EnumerateArray( ).Any( static entry =>
                    entry.ValueKind == JsonValueKind.Object && GetString( entry, "id" ) == PluginId );
            return listed
                ? new( name, DesktopCheckState.Passed, $"listed in {path}" )
                : new( name, DesktopCheckState.Warning, $"{PluginId} is not in plugins in {path}", enable );
        }
    }

    private static (DesktopCheck Check, string? Chord) InspectHotkeyModule( HyprlandHotkeyPaths paths, string? userChord )
    {
        const string name = "Hotkey module";
        var content = ReadBounded( paths.HotkeyFile );
        if ( content is null && userChord is not null )
        {
            return (new( name, DesktopCheckState.Passed,
                $"{paths.Bindings} binds {userChord} to CommandDeck, so no {HyprlandHotkeyConfig.ModuleName} module is needed" ), null);
        }

        if ( content is null )
        {
            return (new( name, DesktopCheckState.Warning, $"{paths.HotkeyFile} is missing", InstallHotkey ), null);
        }

        var chord = HyprlandHotkeyConfig.ReadChord( content );
        return chord is null
            ? (new( name, DesktopCheckState.Warning, $"{paths.HotkeyFile} was not generated by CommandDeck",
                "Move that file aside, then " + InstallHotkey ), null)
            : (new( name, DesktopCheckState.Passed,
                $"{chord} sends {HyprlandHotkeyConfig.ShortcutId} ({paths.HotkeyFile})" ), chord);
    }

    private static DesktopCheck InspectHotkeyRequire( HyprlandHotkeyPaths paths, string? userChord )
    {
        const string name = "Hotkey require";
        var content = ReadBounded( paths.MainConfig );
        if ( content is null )
        {
            return new( name, DesktopCheckState.Warning,
                $"{paths.MainConfig} is missing; the hotkey needs Omarchy's Lua Hyprland configuration",
                "Bind a key to `omarchy-shell shell toggle local.commanddeck '{}'` in your Hyprland configuration." );
        }

        var count = HyprlandHotkeyConfig.CountRequires( content );
        if ( count == 0 && userChord is not null )
        {
            return new( name, DesktopCheckState.Passed, $"not needed; {paths.Bindings} binds {userChord} to CommandDeck" );
        }

        return count switch
        {
            1 => new( name, DesktopCheckState.Passed, $"{HyprlandHotkeyConfig.ModuleName} is required once in {paths.MainConfig}" ),
            0 => new( name, DesktopCheckState.Warning, $"{paths.MainConfig} does not require {HyprlandHotkeyConfig.ModuleName}", InstallHotkey ),
            _ => new( name, DesktopCheckState.Warning, $"{paths.MainConfig} requires {HyprlandHotkeyConfig.ModuleName} {count} times",
                "Run `commanddeck hotkey install`; it keeps one line." )
        };
    }

    private static bool ContainsString( JsonElement root, string property, string value ) =>
        root.TryGetProperty( property, out var array ) &&
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray( ).Any( entry => entry.ValueKind == JsonValueKind.String && entry.GetString( ) == value );

    private static string? GetString( JsonElement element, string property ) =>
        element.TryGetProperty( property, out var value ) && value.ValueKind == JsonValueKind.String
            ? value.GetString( )
            : null;

    private static JsonDocument? ReadJson( string path, out string? problem )
    {
        problem = null;
        var text = ReadBounded( path );
        if ( text is null )
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse( text );
        }
        catch ( JsonException )
        {
            problem = $"{path} is not valid JSON";
            return null;
        }
    }

    // Null for a missing, unreadable, or implausibly large file.
    private static string? ReadBounded( string path )
    {
        try
        {
            FileInfo file = new( path );
            return file.Exists && file.Length <= MaximumConfigBytes ? File.ReadAllText( path ) : null;
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
            return null;
        }
    }
}
