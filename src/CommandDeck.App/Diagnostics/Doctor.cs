using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CommandDeck;

public static class Doctor
{
    public static async ValueTask<int> RunAsync(
        CommandDeckRuntime runtime,
        string? configPath,
        CancellationToken cancellationToken )
    {
        List<Check> checks =
        [
            Check.Value("Operating system", OperatingSystem.IsLinux(), RuntimeInformation.OSDescription),
            Check.Value(
                "Architecture",
                RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64,
                RuntimeInformation.ProcessArchitecture.ToString()),
            Check.Value(
                "Native AOT runtime",
                !RuntimeFeature.IsDynamicCodeSupported,
                RuntimeFeature.IsDynamicCodeSupported
                    ? "JIT build (allowed for development)"
                    : "Native AOT"),
            Check.Value(
                "Hyprland session",
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                    "HYPRLAND_INSTANCE_SIGNATURE")),
                Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") ?? "not detected"),
            Check.Command("hyprctl", required: true),
            Check.Command("xdg-open", required: true),
            Check.Command("omarchy-shell", required: false),
            Check.Command("wl-copy", required: false)
        ];

        if ( runtime.Options.Search.Calculator.UseQalc is not null )
        {
            // Not required: the warning never changes Doctor's exit code.
            checks.Add( Check.Value( "Calculator configuration", false,
                "search.calculator.useQalc is deprecated and ignored; remove it", required: false ) );
        }

        if ( runtime.Options.Indexing.EnablePdfExtraction )
        {
            checks.Add( Check.Command( "pdftotext", required: true ) );
        }

        if ( runtime.Options.Indexing.EnablePandocExtraction )
        {
            checks.Add( Check.Command( "pandoc", required: true ) );
        }

        var fts = await runtime.Catalog.VerifyFts5Async( cancellationToken ).ConfigureAwait( false );
        checks.Add( Check.Value( "SQLite FTS5", fts, fts ? "available" : "unavailable" ) );

        // Optional: never fails Doctor. The PATH-selected client's own status
        // response is authoritative, not package state or /usr/bin/herdr.
        var herdr = await runtime.Herdr.GetStatusAsync( cancellationToken ).ConfigureAwait( false );
        checks.Add( Check.Value(
            "Herdr default session",
            herdr.Availability == HerdrAvailability.Available,
            $"{herdr.Detail} (PATH-selected herdr client)",
            required: false ) );

        var resolvedConfig = string.IsNullOrWhiteSpace( configPath )
            ? PathExpansion.GetDefaultConfigPath( )
            : PathExpansion.Expand( configPath );
        checks.Add( Check.Value(
            "Configuration",
            true,
            File.Exists( resolvedConfig )
                ? resolvedConfig
                : $"{resolvedConfig} (defaults in use)" ) );

        var stats = await runtime.Catalog.GetStatsAsync( cancellationToken )
            .ConfigureAwait( false );
        checks.Add( Check.Value(
            "Catalog",
            true,
            $"{stats.ItemCount} items; {stats.DatabaseBytes} bytes" ) );

        var indexing = await runtime.Indexer.GetIncrementalStatusAsync(
            cancellationToken ).ConfigureAwait( false );
        foreach ( var root in indexing.Roots )
        {
            (var passed, var detail) = DescribeIndexRoot( root );
            checks.Add( Check.Value(
                $"Index root {root.Id}",
                passed,
                detail,
                required: false ) );
        }

        await AddAssistantChecksAsync( runtime, checks, cancellationToken ).ConfigureAwait( false );
        var desktop = await DesktopWiringDoctor.CreateDefault( ).InspectAsync( cancellationToken )
            .ConfigureAwait( false );
        checks.AddRange( desktop.Select( Check.FromDesktop ) );

        Console.WriteLine( "CommandDeck doctor" );
        Console.WriteLine( new string( '─', 76 ) );
        foreach ( var check in checks )
        {
            var marker = check.Passed ? "OK" : check.Skipped ? "SKIP" : check.Required ? "FAIL" : "WARN";
            Console.WriteLine( $"{marker,-5} {check.Name,-22} {check.Detail}" );
            if ( !check.Passed && check.Remediation is not null )
            {
                Console.WriteLine( $"{string.Empty,-28} Fix: {check.Remediation}" );
            }
        }

        Console.WriteLine( new string( '─', 76 ) );
        var failed = checks.Any( static check => check.Required && !check.Passed );
        Console.WriteLine( failed
            ? "Required checks failed."
            : "All required checks passed." );
        return failed ? 1 : 0;
    }

    private static async ValueTask AddAssistantChecksAsync(
        CommandDeckRuntime runtime,
        List<Check> checks,
        CancellationToken cancellationToken )
    {
        var agent = runtime.Options.Agent;
        try
        {
            var codex = await CodexBinaryLocator.LocateAsync( agent, cancellationToken )
                .ConfigureAwait( false );
            checks.Add( Check.Value(
                "Codex CLI",
                true,
                $"codex-cli {codex.Version} at {codex.CanonicalPath}" +
                    (codex.Reviewed ? string.Empty : "; unreviewed App Server version admitted by agent.maximumCodex*Version"),
                required: false ) );
        }
        catch ( AiOperationException exception )
        {
            checks.Add( Check.Value( "Codex CLI", false, exception.SafeSummary ?? "unavailable", required: false ) );
        }
        foreach ( var profile in CliAgentProfile.All )
        {
            try
            {
                var binary = CliAgentBinaryLocator.Locate( profile, profile.BackendId switch
                {
                    AgentBackendIds.ClaudeCode => agent.ClaudeBinaryPath,
                    AgentBackendIds.Cursor => agent.CursorBinaryPath,
                    AgentBackendIds.Grok => agent.GrokBinaryPath,
                    _ => null
                } );
                checks.Add( Check.Value( profile.DisplayName + " CLI", true, binary.CanonicalPath, required: false ) );
            }
            catch ( AiOperationException exception )
            {
                checks.Add( Check.Value( profile.DisplayName + " CLI", false, exception.SafeSummary ?? "unavailable", required: false ) );
            }
        }
        checks.Add( Check.Value( "Bubblewrap", File.Exists( agent.BubblewrapPath ), agent.BubblewrapPath, required: false ) );
        checks.Add( Check.Value( "systemd-run", File.Exists( agent.SystemdRunPath ), agent.SystemdRunPath, required: false ) );
        var credential = new CodexCredentialStore( agent.StateDirectory ).Exists;
        checks.Add( Check.Value(
            "Assistant credential",
            credential,
            credential ? "CommandDeck-owned Codex login present" : "not signed in",
            required: false ) );
        try
        {
            var status = await runtime.GetAgentStatusAsync( cancellationToken ).ConfigureAwait( false );
            checks.Add( Check.Value(
                "Package helper",
                status.Helper.Installed,
                status.Helper.Installed
                    ? $"installed, protocol {status.Helper.Version ?? "?"}"
                    : status.Helper.Message ?? "not installed",
                required: false ) );
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or IOException )
        {
            checks.Add( Check.Value( "Package helper", false, "status unavailable", required: false ) );
        }
        var agentProcess = FindPolkitAgentProcess( );
        checks.Add( Check.Value(
            "Polkit agent",
            agentProcess is not null,
            agentProcess ?? "no known authentication agent process found",
            required: false ) );
    }

    private static string? FindPolkitAgentProcess( )
    {
        string[] known = ["quickshell", "hyprpolkitagent", "polkit-gnome-au", "lxqt-policykit-", "polkit-kde-authe"];
        try
        {
            var scanned = 0;
            foreach ( var directory in Directory.EnumerateDirectories( "/proc" ) )
            {
                if ( ++scanned > 4096 )
                {
                    break;
                }
                var commPath = Path.Combine( directory, "comm" );
                if ( !File.Exists( commPath ) )
                {
                    continue;
                }
                var name = File.ReadAllText( commPath ).Trim( );
                if ( known.Any( candidate => name.StartsWith( candidate, StringComparison.Ordinal ) ) )
                {
                    return name;
                }
            }
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
        }
        return null;
    }

    // A root the watch limit covers only partly is a warning, like an
    // unwatched one, with the reason and the remedy.
    internal static (bool Passed, string Detail) DescribeIndexRoot( IndexRootStatus root )
    {
        var fullyWatched = root.Watched && root.UnwatchedDirectoryCount == 0 && string.IsNullOrEmpty( root.Remediation );
        var state = root.Paused
            ? "paused"
            : fullyWatched
                ? (root.WatchedDirectoryCount > 0 ? $"watched; {root.WatchedDirectoryCount} directories; " : "watched; ") + $"{root.IndexedItemCount} indexed item(s)"
                : string.Join(
                    " ",
                    new[] { root.LastError, root.Remediation }
                        .Where( static value => !string.IsNullOrWhiteSpace( value ) ) );

        return (fullyWatched || root.Paused, $"{root.Path}: {state}");
    }

    private sealed class Check
    {
        public required string Name { get; init; }
        public required bool Passed { get; init; }
        public required bool Required { get; init; }
        public required string Detail { get; init; }
        public bool Skipped { get; init; }
        public string? Remediation { get; init; }

        // Desktop wiring is never required: a skip or warning keeps the exit code.
        public static Check FromDesktop( DesktopCheck check ) =>
            new( )
            {
                Name = check.Name,
                Passed = check.State == DesktopCheckState.Passed,
                Skipped = check.State == DesktopCheckState.Skipped,
                Required = false,
                Detail = check.Detail,
                Remediation = check.Remediation
            };

        public static Check Value(
            string name,
            bool passed,
            string detail,
            bool required = true ) =>
            new( )
            {
                Name = name,
                Passed = passed,
                Detail = detail,
                Required = required
            };

        public static Check Command( string command, bool required )
        {
            var found = ProcessRunner.ExistsOnPath( command );
            return new( )
            {
                Name = command,
                Passed = found,
                Required = required,
                Detail = OptionalToolCatalog.DoctorDetail( command, found )
            };
        }
    }
}
