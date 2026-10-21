using System.Diagnostics;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunCliRegistrationsAsync( )
    {
        await RunAsync( "indexed folder CLI persists roots and routes live changes to daemon", IndexedFolderCliRoutesToDaemonAsync );
        await RunAsync( "quicklinks CLI adds edits lists validates and removes durable links", QuicklinkCliManagesDurableStateAsync );
        await RunAsync( "picker emits only selected values and honors cancellation", PickerReturnsOnlyTheSelectedValueAsync );
        await RunAsync(
            "picker build and installer deliver every relative QML dependency",
            PickerDeliveryCarriesEveryRelativeDependencyAsync );
        await RunAsync( "catalog inspection CLI labels unchecked exclusion totals as approximate",
            CatalogInspectionCliLabelsApproximateExclusionsAsync );
        await RunAsync(
            "catalog maintenance CLI is inspectable confirmed and customization-safe",
            CatalogMaintenanceCliIsSafeAsync );
        await RunAsync(
            "doctor fails clearly on a catalog written by a newer schema",
            DoctorReportsNewerCatalogSchemaAsync );
        await RunAsync(
            "preferences reset requires process-level confirmation and preserves state",
            PreferencesResetRequiresProcessConfirmationAsync );
        await RunAsync(
            "preferences export rejects oversized UTF-8 output at the CLI boundary",
            PreferencesExportRejectsOversizedUtf8Async );
        await RunAsync(
            "ranking CLI inspects exports and confirms reset",
            RankingCliInspectsExportsAndConfirmsResetAsync );
        await RunAsync(
            "query history CLI escapes inspects deletes exports and confirms reset",
            QueryHistoryCliIsBoundedAndReversibleAsync );
        await RunAsync(
            "filter alias CLI lists sets rejects and removes persisted tokens",
            FilterAliasCliPersistsAndValidatesAsync );
        Run( "hotkey install writes its own Lua module and one require line", HotkeyInstallWritesItsOwnModuleAndOneRequire );
        Run( "hotkey install rerun changes and backs up nothing", HotkeyInstallIsIdempotent );
        Run( "hotkey install migrates the earlier bindings.lua block", HotkeyInstallMigratesTheEarlierBindingsBlock );
        Run( "hotkey install binds a custom chord and rejects invalid chords", HotkeyInstallBindsACustomChord );
        Run( "hotkey remove deletes the generated module and its require line", HotkeyRemoveDeletesTheModuleAndRequire );
        Run( "hotkey install keeps a user CommandDeck binding and notes other chord users", HotkeyInstallKeepsTheExistingConflictChecks );
        Run( "hotkey install requires Omarchy's Lua Hyprland configuration", HotkeyInstallRequiresOmarchyLuaConfiguration );
        await RunAsync( "hotkey CLI installs and removes through XDG paths", HotkeyCliInstallsAndRemovesThroughXdgPathsAsync );
        Run( "Omarchy plugin global shortcut matches the generated Hyprland binding", OmarchyShortcutMatchesTheGeneratedBinding );
        await RunAsync( "doctor desktop checks pass for a wired plugin hotkey and live binding", DoctorDesktopChecksPassWhenWiredAsync );
        await RunAsync( "doctor desktop checks explain a missing installation and skip without Hyprland", DoctorDesktopChecksExplainAMissingInstallationAsync );
        await RunAsync( "doctor desktop checks report stale wiring and unavailable hyprctl", DoctorDesktopChecksReportStaleWiringAsync );
        await RunAsync( "doctor desktop checks accept a user's own hotkey binding", DoctorDesktopChecksAcceptAUserBindingAsync );
        await RunAsync( "doctor prints desktop wiring warnings skips and remediation", DoctorPrintsDesktopRemediationAsync );
    }

    private static async Task PreferencesResetRequiresProcessConfirmationAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, var database) = await CreateCliCatalogAsync( root, "Notes" );

            var rejected = await RunCommandDeckProcessAsync(
                "preferences", "reset", "--config", config );
            Equal( 2, rejected.ExitCode );
            True( rejected.StandardError.Contains( "requires --yes", StringComparison.Ordinal ) );
            await AssertCustomizationCountAsync( database, 1 );

            var accepted = await RunCommandDeckProcessAsync(
                "preferences", "reset", "--yes", "--config", config );
            Equal( 0, accepted.ExitCode );
            True( accepted.StandardOutput.Contains( "reset", StringComparison.OrdinalIgnoreCase ) );
            await AssertCustomizationCountAsync( database, 0 );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task PreferencesExportRejectsOversizedUtf8Async( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, _) = await CreateCliCatalogAsync(
                root,
                new string( '\u00e9', 140_000 ) );
            var outcome = await RunCommandDeckProcessAsync(
                "preferences", "export", "--config", config );

            Equal( 1, outcome.ExitCode );
            Equal( string.Empty, outcome.StandardOutput );
            True( outcome.StandardError.Contains( "256 KiB", StringComparison.Ordinal ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static string CreateCliSpecRoot( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-cli-spec-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        return root;
    }

    private static async Task<(string Config, string Database)> CreateCliCatalogAsync(
        string root,
        string title )
    {
        var database = Path.Combine( root, "catalog.db" );
        var config = Path.Combine( root, "config.json" );
        CommandDeckOptions options = new( )
        {
            Storage = new StorageOptions { DatabasePath = database },
            Indexing = new IndexingOptions
            {
                Roots = [],
                IndexDesktopApplications = false,
                IndexOmarchyCommands = false
            },
            Privacy = new PrivacyOptions
            {
                StoreUsageHistory = false,
                NeverIndex = []
            },
            Ai = new AiOptions { Enabled = false, Providers = [] }
        };
        await File.WriteAllTextAsync(
            config,
            JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );

        await using SqliteCatalog catalog = new( options.Storage );
        await catalog.InitializeAsync( CancellationToken.None );
        SearchResult favorite = new( )
        {
            Id = "file:cli-notes",
            Kind = ResultKinds.File,
            Title = title,
            ProviderId = "files",
            SourceId = SourceIds.Files,
            CanCustomize = true
        };
        await catalog.SetFavoriteAsync( favorite, true, CancellationToken.None );
        return (config, database);
    }

    private static async Task AssertCustomizationCountAsync( string database, int expected )
    {
        await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
        var snapshot = await catalog.GetSnapshotAsync(
            includeShortcuts: false,
            CancellationToken.None );
        Equal( expected, snapshot.Customizations.Count );
    }

    private static Task<ProcessOutcome> RunCommandDeckProcessAsync( params string[] arguments ) =>
        RunCommandDeckProcessInDirectoryAsync( Environment.CurrentDirectory, arguments );

    private static async Task<ProcessOutcome> RunCommandDeckProcessInDirectoryAsync( string workingDirectory, params string[] arguments )
    {
        ProcessStartInfo start = new( )
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add( typeof( CommandDeck.Program ).Assembly.Location );
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }

        using Process process = new( )
        {
            StartInfo = start
        };
        True( process.Start( ) );
        var output = process.StandardOutput.ReadToEndAsync( );
        var error = process.StandardError.ReadToEndAsync( );
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 15 ) );
        await process.WaitForExitAsync( timeout.Token );
        return new ProcessOutcome(
            process.ExitCode,
            (await output).Trim( ),
            (await error).Trim( ) );
    }

    private sealed record ProcessOutcome(
        int ExitCode,
        string StandardOutput,
        string StandardError );
}
