using System.Text.Json;

namespace CommandDeck;

// The live half of the desktop wiring checks: what the running compositor
// actually binds and which global shortcuts the shell has registered.
internal sealed partial class DesktopWiringDoctor
{
    internal const string Hyprctl = "hyprctl";
    internal static readonly TimeSpan HyprctlTimeout = TimeSpan.FromSeconds( 2 );

    internal static readonly ProcessExecutionLimits HyprctlLimits = new( )
    {
        MaximumStandardOutputBytes = 4 * 1024 * 1024,
        MaximumStandardErrorBytes = 16 * 1024,
        MaximumMemoryBytes = 256L * 1024 * 1024
    };

    // Hyprland's modmask bits in the order Omarchy writes modifiers.
    private static readonly (int Bit, string Name)[] ModifierBits =
        [(64, "SUPER"), (4, "CTRL"), (8, "ALT"), (1, "SHIFT"), (16, "MOD2"), (32, "MOD3"), (128, "MOD5")];

    private async ValueTask<IReadOnlyList<DesktopCheck>> InspectHyprlandAsync(
        string? expectedChord,
        string? userChord,
        CancellationToken cancellationToken )
    {
        const string binding = "Hyprland binding";
        const string shortcut = "Global shortcut";
        var unavailable = string.IsNullOrEmpty( environment( "HYPRLAND_INSTANCE_SIGNATURE" ) )
            ? "no Hyprland session in this environment"
            : !commandExists( Hyprctl ) ? "hyprctl is not on PATH" : null;
        if ( unavailable is not null )
        {
            return
            [
                new( binding, DesktopCheckState.Skipped, unavailable ),
                new( shortcut, DesktopCheckState.Skipped, unavailable )
            ];
        }

        var binds = await QueryAsync( "binds", cancellationToken ).ConfigureAwait( false );
        var shortcuts = await QueryAsync( "globalshortcuts", cancellationToken ).ConfigureAwait( false );
        return
        [
            binds.Document is null
                ? new( binding, DesktopCheckState.Skipped, binds.Problem! )
                : InspectBinding( binds.Document, expectedChord, userChord ),
            shortcuts.Document is null
                ? new( shortcut, DesktopCheckState.Skipped, shortcuts.Problem! )
                : InspectShortcut( shortcuts.Document )
        ];
    }

    // CommandDeck's Lua bind reports dispatcher "__lua", so its description identifies it.
    // A binding the user wrote carries no CommandDeck description, so it is
    // found by its chord.
    private static DesktopCheck InspectBinding( JsonDocument document, string? expectedChord, string? userChord )
    {
        const string name = "Hyprland binding";
        using ( document )
        {
            var binds = document.RootElement.EnumerateArray( )
                .Where( static bind => bind.ValueKind == JsonValueKind.Object ).ToList( );
            var chords = binds
                .Where( static bind => GetString( bind, "description" ) == HyprlandHotkeyConfig.BindingDescription )
                .Select( FormatChord )
                .Where( static chord => chord.Length > 0 )
                .Distinct( StringComparer.Ordinal )
                .ToList( );
            if ( chords.Count == 0 && userChord is not null )
            {
                var canonical = Canonical( userChord );
                return binds.Any( bind => FormatChord( bind ) == canonical )
                    ? new( name, DesktopCheckState.Passed, $"{canonical} is active, from your binding" )
                    : new( name, DesktopCheckState.Warning, $"Hyprland does not bind {canonical}",
                        "Run `hyprctl reload`, then `hyprctl configerrors`." );
            }

            if ( chords.Count == 0 )
            {
                return new( name, DesktopCheckState.Warning, "Hyprland has no active CommandDeck binding",
                    "Run `hyprctl reload`, then `hyprctl configerrors`; if it is still missing, " +
                    "run `commanddeck hotkey install`." );
            }

            var active = string.Join( ", ", chords );
            if ( expectedChord is not null && !chords.Contains( Canonical( expectedChord ), StringComparer.Ordinal ) )
            {
                return new( name, DesktopCheckState.Warning,
                    $"Hyprland binds {active}, but commanddeck.lua binds {expectedChord}",
                    "Run `hyprctl reload`." );
            }

            return new( name, DesktopCheckState.Passed, $"{active} is active" );
        }
    }

    private static DesktopCheck InspectShortcut( JsonDocument document )
    {
        const string name = "Global shortcut";
        using ( document )
        {
            var registered = document.RootElement.EnumerateArray( ).Any( static entry =>
                entry.ValueKind == JsonValueKind.Object &&
                GetString( entry, "name" ) == HyprlandHotkeyConfig.ShortcutId );
            return registered
                ? new( name, DesktopCheckState.Passed, $"{HyprlandHotkeyConfig.ShortcutId} is registered by the running shell" )
                : new( name, DesktopCheckState.Warning,
                    $"{HyprlandHotkeyConfig.ShortcutId} is not registered, so the hotkey reaches nothing",
                    "Enable the plugin, then run `omarchy restart shell`." );
        }
    }

    private async ValueTask<(JsonDocument? Document, string? Problem)> QueryAsync(
        string request,
        CancellationToken cancellationToken )
    {
        ProcessResult result;
        try
        {
            result = await run( Hyprctl, ["-j", request], HyprctlTimeout, HyprctlLimitsFor( environment ), cancellationToken )
                .ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or
            System.ComponentModel.Win32Exception )
        {
            return (null, $"hyprctl could not start: {exception.Message}");
        }

        if ( result.TimedOut || result.ExitCode != 0 || result.AnyOutputLimitExceeded || result.MemoryLimitExceeded )
        {
            var reason = result.TimedOut
                ? "timed out"
                : result.AnyOutputLimitExceeded || result.MemoryLimitExceeded
                    ? "exceeded its output or memory limit"
                    : $"exited with status {result.ExitCode}";
            return (null, $"`hyprctl -j {request}` {reason}");
        }

        try
        {
            var document = JsonDocument.Parse( result.StandardOutput );
            if ( document.RootElement.ValueKind == JsonValueKind.Array )
            {
                return (document, null);
            }

            document.Dispose( );
        }
        catch ( JsonException )
        {
        }

        return (null, $"`hyprctl -j {request}` returned an unexpected response");
    }

    // hyprctl finds the compositor through these variables; nothing else is passed.
    internal static ProcessExecutionLimits HyprctlLimitsFor( Func<string, string?> environment )
    {
        Dictionary<string, string> variables = new( StringComparer.Ordinal )
        {
            ["PATH"] = environment( "PATH" ) ?? "/usr/bin:/bin",
            ["LANG"] = "C.UTF-8",
            ["LC_ALL"] = "C.UTF-8"
        };
        foreach ( var name in new[] { "HYPRLAND_INSTANCE_SIGNATURE", "XDG_RUNTIME_DIR" } )
        {
            if ( environment( name ) is { Length: > 0 } value )
            {
                variables[name] = value;
            }
        }

        return new( )
        {
            MaximumStandardOutputBytes = HyprctlLimits.MaximumStandardOutputBytes,
            MaximumStandardErrorBytes = HyprctlLimits.MaximumStandardErrorBytes,
            MaximumMemoryBytes = HyprctlLimits.MaximumMemoryBytes,
            EnvironmentVariables = variables
        };
    }

    private static string FormatChord( JsonElement bind )
    {
        var key = GetString( bind, "key" );
        if ( string.IsNullOrWhiteSpace( key ) )
        {
            key = bind.TryGetProperty( "keycode", out var code ) && code.TryGetInt32( out var number ) && number > 0
                ? "code:" + number.ToString( System.Globalization.CultureInfo.InvariantCulture )
                : null;
        }

        if ( key is null )
        {
            return string.Empty;
        }

        var modifiers = bind.TryGetProperty( "modmask", out var mask ) && mask.TryGetInt32( out var bits ) ? bits : 0;
        var names = ModifierBits.Where( modifier => (modifiers & modifier.Bit) != 0 ).Select( static modifier => modifier.Name );
        return string.Join( " + ", names.Append( key.ToUpperInvariant( ) ) );
    }

    // A chord in the modifier order FormatChord uses, so the two compare equal.
    private static string Canonical( string chord )
    {
        var parts = chord.Split( '+', StringSplitOptions.TrimEntries );
        HashSet<string> given = new( parts.Take( parts.Length - 1 ), StringComparer.OrdinalIgnoreCase );
        var modifiers = ModifierBits.Select( static modifier => modifier.Name ).Where( given.Contains );
        return string.Join( " + ", modifiers.Append( parts[^1].ToUpperInvariant( ) ) );
    }

    private static ValueTask<ProcessResult> RunBoundedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        ProcessExecutionLimits limits,
        CancellationToken cancellationToken ) =>
        ProcessRunner.RunBoundedAsync( fileName, arguments, null, timeout, limits, cancellationToken );
}
