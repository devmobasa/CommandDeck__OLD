namespace CommandDeck.Specs;

public static partial class Program
{
    private const string WiredBindsJson = """
        [
          {"modmask":64,"key":"SPACE","dispatcher":"__lua","arg":"12","description":"Omarchy menu"},
          {"modmask":4,"key":"SPACE","dispatcher":"__lua","arg":"39","description":"CommandDeck"}
        ]
        """;

    private const string WiredShortcutsJson = """
        [{"name":"commanddeck:toggle","description":"Toggle CommandDeck"}]
        """;

    private static async Task DoctorDesktopChecksPassWhenWiredAsync( )
    {
        using DesktopDoctorFixture fixture = new( );
        fixture.InstallPlugin( );
        fixture.WriteShellConfig( """{"version":1,"plugins":[{"id":"local.commanddeck"}]}""" );
        fixture.InstallHotkey( "CTRL + SPACE" );
        fixture.Respond( "binds", WiredBindsJson );
        fixture.Respond( "globalshortcuts", WiredShortcutsJson );

        var checks = await fixture.InspectAsync( );

        True( checks.Select( static check => check.Name ).SequenceEqual(
            ["Shell plugin", "Plugin enabled", "Hotkey module", "Hotkey require", "Hyprland binding", "Global shortcut"] ) );
        True( checks.All( static check => check.State == DesktopCheckState.Passed ),
            string.Join( "; ", checks.Select( static check => $"{check.Name}: {check.State} {check.Detail}" ) ) );
        True( checks.Single( static check => check.Name == "Hyprland binding" ).Detail.StartsWith( "CTRL + SPACE", StringComparison.Ordinal ) );

        True( fixture.Calls.Select( static call => string.Join( ' ', call.Arguments ) ).SequenceEqual( ["-j binds", "-j globalshortcuts"] ) );
        foreach ( var call in fixture.Calls )
        {
            Equal( "hyprctl", call.FileName );
            Equal( TimeSpan.FromSeconds( 2 ), call.Timeout );
            True( call.Limits.ClearEnvironment );
            True( call.Limits.MaximumStandardOutputBytes <= 4 * 1024 * 1024 );
            var environment = call.Limits.EnvironmentVariables!;
            Equal( "spec-instance", environment["HYPRLAND_INSTANCE_SIGNATURE"] );
            Equal( fixture.RuntimeDirectory, environment["XDG_RUNTIME_DIR"] );
            True( !environment.ContainsKey( "SPEC_SECRET" ) );
        }
    }

    private static async Task DoctorDesktopChecksExplainAMissingInstallationAsync( )
    {
        using DesktopDoctorFixture fixture = new( );
        fixture.Environment.Remove( "HYPRLAND_INSTANCE_SIGNATURE" );

        var checks = await fixture.InspectAsync( );

        Remedy( checks, "Shell plugin", DesktopCheckState.Warning, "install-omarchy-plugin.sh" );
        Remedy( checks, "Plugin enabled", DesktopCheckState.Warning, "omarchy plugin enable local.commanddeck" );
        Remedy( checks, "Hotkey module", DesktopCheckState.Warning, "commanddeck hotkey install" );
        Remedy( checks, "Hotkey require", DesktopCheckState.Warning, "omarchy-shell shell toggle local.commanddeck" );
        Equal( DesktopCheckState.Skipped, checks.Single( static check => check.Name == "Hyprland binding" ).State );
        Equal( DesktopCheckState.Skipped, checks.Single( static check => check.Name == "Global shortcut" ).State );
        Equal( 0, fixture.Calls.Count );

        fixture.Environment["HYPRLAND_INSTANCE_SIGNATURE"] = "spec-instance";
        fixture.HyprctlInstalled = false;
        var noHyprctl = await fixture.InspectAsync( );
        True( noHyprctl.Single( static check => check.Name == "Global shortcut" ).Detail.Contains( "not on PATH", StringComparison.Ordinal ) );
        Equal( 0, fixture.Calls.Count );

        fixture.Environment.Remove( "HOME" );
        var homeless = await fixture.InspectAsync( );
        Equal( DesktopCheckState.Skipped, homeless.Single( ).State );
    }

    private static async Task DoctorDesktopChecksReportStaleWiringAsync( )
    {
        using DesktopDoctorFixture fixture = new( );
        fixture.InstallPlugin( );
        fixture.WriteShellConfig( """{"version":1,"plugins":[{"id":"local.commanddeck"}],"disabledPlugins":["local.commanddeck"]}""" );
        fixture.InstallHotkey( "CTRL + SPACE" );
        File.AppendAllText( fixture.Paths.MainConfig, "require(\"hypr.commanddeck\")\n" );
        File.WriteAllText( fixture.Paths.Bindings, StockBindingsLua + LegacyBindingBlock );
        fixture.Respond( "binds", """[{"modmask":64,"key":"space","dispatcher":"__lua","arg":"4","description":"CommandDeck"}]""" );
        fixture.Respond( "globalshortcuts", """[{"name":"other:toggle","description":"Other"}]""" );

        var checks = await fixture.InspectAsync( );

        Remedy( checks, "Plugin enabled", DesktopCheckState.Warning, "omarchy plugin enable" );
        True( checks.Single( static check => check.Name == "Plugin enabled" ).Detail.Contains( "disabledPlugins", StringComparison.Ordinal ) );
        Remedy( checks, "Hotkey require", DesktopCheckState.Warning, "keeps one line" );
        Remedy( checks, "Earlier binding", DesktopCheckState.Warning, "commanddeck hotkey install" );
        Remedy( checks, "Hyprland binding", DesktopCheckState.Warning, "hyprctl reload" );
        True( checks.Single( static check => check.Name == "Hyprland binding" ).Detail
            .Contains( "binds SUPER + SPACE, but commanddeck.lua binds CTRL + SPACE", StringComparison.Ordinal ) );
        Remedy( checks, "Global shortcut", DesktopCheckState.Warning, "omarchy restart shell" );

        fixture.WriteShellConfig( """{"plugins":[{"id":"local.commanddeck"}]}""" );
        File.WriteAllText( fixture.Paths.HotkeyFile, "hl.bind(\"CTRL + SPACE\", hl.dsp.exec_cmd(\"x\"))\n" );
        fixture.Respond( "binds", """[{"modmask":4,"key":"SPACE","dispatcher":"__lua","arg":"4","description":"Other"}]""" );
        var stale = await fixture.InspectAsync( );
        True( stale.Single( static check => check.Name == "Plugin enabled" ).Detail.Contains( "version", StringComparison.Ordinal ) );
        Remedy( stale, "Hotkey module", DesktopCheckState.Warning, "Move that file aside" );
        True( stale.Single( static check => check.Name == "Hyprland binding" ).Detail.Contains( "no active CommandDeck binding", StringComparison.Ordinal ) );

        foreach ( var failure in new[]
        {
            new ProcessResult { ExitCode = 1, StandardOutput = "", StandardError = "HYPRLAND_INSTANCE_SIGNATURE invalid" },
            new ProcessResult { ExitCode = 0, StandardOutput = "", StandardError = "", TimedOut = true },
            new ProcessResult { ExitCode = 0, StandardOutput = "[", StandardError = "", OutputLimitExceeded = true },
            new ProcessResult { ExitCode = 0, StandardOutput = "Couldn't connect", StandardError = "" }
        } )
        {
            fixture.Results["binds"] = failure;
            fixture.Results["globalshortcuts"] = failure;
            var unavailable = await fixture.InspectAsync( );
            Equal( DesktopCheckState.Skipped, unavailable.Single( static check => check.Name == "Hyprland binding" ).State );
            Equal( DesktopCheckState.Skipped, unavailable.Single( static check => check.Name == "Global shortcut" ).State );
        }
    }

    private static async Task DoctorPrintsDesktopRemediationAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, _) = await CreateCliCatalogAsync( root, "Doctor notes" );
            var doctor = await RunCommandDeckWithEnvironmentAsync(
                new Dictionary<string, string?>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine( root, "config" ),
                    ["XDG_STATE_HOME"] = Path.Combine( root, "state" ),
                    ["HYPRLAND_INSTANCE_SIGNATURE"] = null
                },
                "doctor", "--config", config );

            True( doctor.StandardOutput.Contains( "WARN  Hotkey module", StringComparison.Ordinal ), doctor.StandardOutput );
            True( doctor.StandardOutput.Contains( "Fix: Run `commanddeck hotkey install`, then `hyprctl reload`.", StringComparison.Ordinal ) );
            True( doctor.StandardOutput.Contains( "SKIP  Hyprland binding", StringComparison.Ordinal ) );
            True( doctor.StandardOutput.Contains( "SKIP  Global shortcut", StringComparison.Ordinal ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void Remedy( IReadOnlyList<DesktopCheck> checks, string name, DesktopCheckState state, string remediation )
    {
        var check = checks.Single( value => value.Name == name );
        Equal( state, check.State );
        True( check.Remediation?.Contains( remediation, StringComparison.Ordinal ) == true, $"{name}: {check.Remediation}" );
    }

    private sealed record HyprctlCall( string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout, ProcessExecutionLimits Limits );

    private sealed class DesktopDoctorFixture : IDisposable
    {
        public DesktopDoctorFixture( )
        {
            Root = Path.Combine( Path.GetTempPath( ), $"commanddeck-doctor-spec-{Guid.NewGuid( ):N}" );
            RuntimeDirectory = Path.Combine( Root, "runtime" );
            Environment = new( StringComparer.Ordinal )
            {
                ["HOME"] = Root,
                ["XDG_CONFIG_HOME"] = Path.Combine( Root, "config" ),
                ["XDG_STATE_HOME"] = Path.Combine( Root, "state" ),
                ["XDG_RUNTIME_DIR"] = RuntimeDirectory,
                ["HYPRLAND_INSTANCE_SIGNATURE"] = "spec-instance",
                ["PATH"] = "/usr/bin:/bin",
                ["SPEC_SECRET"] = "not for hyprctl"
            };
            Paths = HyprlandHotkeyPaths.FromEnvironment( name => Environment.GetValueOrDefault( name ) );
            Directory.CreateDirectory( Paths.HyprDirectory );
        }

        public string Root { get; }
        public string RuntimeDirectory { get; }
        public Dictionary<string, string> Environment { get; }
        public HyprlandHotkeyPaths Paths { get; }
        public Dictionary<string, ProcessResult> Results { get; } = new( StringComparer.Ordinal );
        public List<HyprctlCall> Calls { get; } = [];
        public bool HyprctlInstalled { get; set; } = true;

        private string OmarchyDirectory => Path.Combine( Root, "config", "omarchy" );

        public void InstallPlugin( )
        {
            var directory = Path.Combine( OmarchyDirectory, "plugins", "local.commanddeck" );
            Directory.CreateDirectory( directory );
            File.WriteAllText( Path.Combine( directory, "manifest.json" ), """{"id":"local.commanddeck","version":"0.1.0"}""" );
        }

        public void WriteShellConfig( string json )
        {
            Directory.CreateDirectory( OmarchyDirectory );
            File.WriteAllText( Path.Combine( OmarchyDirectory, "shell.json" ), json );
        }

        public void InstallHotkey( string chord )
        {
            File.WriteAllText( Paths.MainConfig, StockHyprlandLua );
            new HyprlandHotkeyInstaller( Paths, new FixedTimeProvider( HotkeyClock ) ).Install( chord );
        }

        public void Respond( string request, string json ) =>
            Results[request] = new ProcessResult { ExitCode = 0, StandardOutput = json, StandardError = "" };

        public ValueTask<IReadOnlyList<DesktopCheck>> InspectAsync( )
        {
            Calls.Clear( );
            DesktopWiringDoctor doctor = new(
                name => Environment.GetValueOrDefault( name ),
                ( file, arguments, timeout, limits, _ ) =>
                {
                    Calls.Add( new( file, arguments, timeout, limits ) );
                    return ValueTask.FromResult( Results[arguments[^1]] );
                },
                _ => HyprctlInstalled );
            return doctor.InspectAsync( CancellationToken.None );
        }

        public void Dispose( ) => Directory.Delete( Root, recursive: true );
    }
}
