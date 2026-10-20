using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private const string CatalogCommand = "catalog";

    public static async Task<int> Main( string[] args )
    {
        CliArguments arguments = new( args );
        AppLog.Verbose = arguments.HasFlag( "--verbose" );

        using CancellationTokenSource shutdown = new( );
        var terminateRequested = 0;
        using var terminateSignal = OperatingSystem.IsLinux( )
            ? PosixSignalRegistration.Create( PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                Interlocked.Exchange( ref terminateRequested, 1 );
                shutdown.Cancel( );
            } ) : null;
        Console.CancelKeyPress += ( _, eventArgs ) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel( );
        };

        try
        {
            return arguments.Command.ToLowerInvariant( ) switch
            {
                "help" or "--help" or "-h" => PrintHelp( ),
                "version" or "--version" => PrintVersion( ),
                "extensions" => await RunExtensionsCliAsync( arguments, shutdown.Token ).ConfigureAwait( false ),
                "extension-probe" => await RunExtensionProbeAsync( arguments, shutdown.Token ).ConfigureAwait( false ),
                "extension-worker" => await ExtensionSandbox.RunWorkerAsync(
                    arguments.GetOption( "--artifact" ) ?? "", arguments.GetOption( "--entrypoint" ) ?? "",
                    int.Parse( arguments.GetOption( "--owner-pid" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ),
                    long.Parse( arguments.GetOption( "--owner-start" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ),
                    arguments.GetOption( "--debug-socket" ), shutdown.Token ).ConfigureAwait( false ),
                "extension-debug-connect" => await ExtensionDebugTransport.ConnectAsync( arguments.GetOption( "--socket" ) ?? "", shutdown.Token ).ConfigureAwait( false ),
                "clipboard-frame" => await ClipboardCapture.WriteFrameAsync( Console.OpenStandardInput( ), Console.Out,
                    Environment.GetEnvironmentVariable( "CLIPBOARD_STATE" ), shutdown.Token ).ConfigureAwait( false ),
                "clipboard-host" => RunClipboardHost( arguments, shutdown.Token ),
                "clipboard-html-project" => RunClipboardHtmlProjection( ),
                "clipboard-action-worker" => ClipboardActionWorker.RunChild( ),
                "pick" => await RunPickerAsync( arguments, shutdown.Token ).ConfigureAwait( false ),
                "config" => await RunConfigAsync( arguments, shutdown.Token ).ConfigureAwait( false ),
                "hotkey" => RunHotkey( arguments ),
                "bridge" => await StdioBridge.RunAsync(
                    arguments.GetOption( "--socket" ),
                    shutdown.Token ).ConfigureAwait( false ),
                "agent" when AgentActionUsesDaemon( arguments ) =>
                    await RunAgentDaemonAsync( arguments, shutdown.Token ).ConfigureAwait( false ),
                "ai" when AiActionUsesDaemon( arguments ) =>
                    await RunAiDaemonAsync( arguments, shutdown.Token ).ConfigureAwait( false ),
                _ => await RunWithRuntimeAsync( arguments, shutdown.Token ).ConfigureAwait( false )
            };
        }
        catch ( OperationCanceledException ) when ( shutdown.IsCancellationRequested )
        {
            return Volatile.Read( ref terminateRequested ) == 1 ? 0 : 130;
        }
        catch ( Exception exception )
        {
            AppLog.Error( exception.Message, AppLog.Verbose ? exception : null );
            return 1;
        }
    }

    private static async Task<int> RunWithRuntimeAsync(
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var configPath = arguments.GetOption( "--config" );
        var command = arguments.Command.ToLowerInvariant( );
        await using var runtime =
            await CommandDeckRuntime.CreateAsync(
                configPath,
                new RuntimeConstructionOptions { StartIndexer = command != CatalogCommand },
                includeClipboardHistory: command is "daemon" or "serve",
                cancellationToken )
                .ConfigureAwait( false );

        if ( command is not ("reindex" or CatalogCommand or "index") &&
            await runtime.Catalog.RequiresReindexAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            AppLog.Info( "Catalog schema changed; rebuilding the index before continuing." );
            await runtime.Indexer.ReindexAsync(
                progress: null,
                cancellationToken ).ConfigureAwait( false );
        }

        return command switch
        {
            "daemon" or "serve" => await RunDaemonAsync(
                runtime,
                arguments,
                cancellationToken ).ConfigureAwait( false ),
            "search" => await RunSearchAsync(
                runtime,
                arguments,
                cancellationToken ).ConfigureAwait( false ),
            "suggest" => await RunSuggestAsync(
                runtime,
                arguments,
                cancellationToken ).ConfigureAwait( false ),
            "reindex" => await RunReindexAsync(
                runtime,
                arguments,
                cancellationToken ).ConfigureAwait( false ),
            "index" => await RunIndexControlAsync(
                runtime,
                arguments,
                cancellationToken ).ConfigureAwait( false ),
            "agent" => await RunAgentAsync( arguments, runtime, cancellationToken )
                .ConfigureAwait( false ),
            "doctor" => await Doctor.RunAsync(
                runtime,
                configPath,
                cancellationToken ).ConfigureAwait( false ),
            "status" => await RunStatusAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            CatalogCommand => await RunCatalogAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            "ai" => await RunAiAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            "preferences" => await RunPreferencesAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            "ranking" => await RunRankingAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            "history" => await RunQueryHistoryAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            "filters" => await RunFilterAliasesAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            "saved-search" => await RunSavedSearchAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            _ => UnknownCommand( arguments.Command )
        };
    }

    private static async Task<int> RunDaemonAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var executable = Environment.ProcessPath;
        if ( executable is null )
        {
            throw new InvalidOperationException( "Cannot locate CommandDeck executable." );
        }

        runtime.StartClipboardCapture( executable, Path.GetFileNameWithoutExtension( executable ) == "dotnet"
            ? [Environment.GetCommandLineArgs( )[0]] : [] );
        UnixProtocolServer server = new( runtime, arguments.GetOption( "--socket" ) );
        await server.RunAsync( cancellationToken ).ConfigureAwait( false );
        return 0;
    }

    private static async Task<int> RunSearchAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var query = arguments.GetOption( "--query" )
                    ?? arguments.JoinPositionalsAfterCommand( );
        var limit = ParseInteger( arguments.GetOption( CliOptionNames.Limit ), 20, 1, 200 );

        var results = await runtime.SearchAsync(
            new SearchRequest { Query = query, Limit = limit },
            cancellationToken ).ConfigureAwait( false );

        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            var json = JsonSerializer.Serialize(
                results.ToList( ),
                CommandDeckJsonContext.Default.ListSearchResult );
            Console.WriteLine( json );
            return 0;
        }

        if ( results.Count == 0 )
        {
            Console.WriteLine( "No results." );
            return 0;
        }

        // Titles and subtitles come from file names, windows and other sources a
        // hostile author controls, so terminal controls are printed escaped.
        var index = 1;
        foreach ( var result in results )
        {
            Console.WriteLine(
                $"{index,2}. [{result.Kind}] {PreferencesTextFormatter.EscapeTerminalText( result.Title )}" );
            if ( !string.IsNullOrWhiteSpace( result.Subtitle ) )
            {
                Console.WriteLine( $"    {PreferencesTextFormatter.EscapeTerminalText( result.Subtitle )}" );
            }

            index++;
        }

        return 0;
    }

    private static async Task<int> RunReindexAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var quiet = arguments.HasFlag( "--quiet" );
        var progress = quiet
            ? null
            : new Progress<IndexProgress>( value =>
            {
                if ( value.Stage == "scan-files" || value.Stage == "write" )
                {
                    Console.Error.Write(
                        $"\r{value.Stage,-12} indexed={value.Indexed,8} " +
                        $"skipped={value.Skipped,8} failed={value.Failed,6}" );
                }
            } );

        var scopeKind = arguments.GetOption( "--scope" ) ??
                        (arguments.GetOption( "--root" ) is null
                            ? arguments.GetOption( "--path" ) is null
                                ? IndexReindexScopeKinds.Full
                                : Directory.Exists( PathExpansion.Expand( arguments.GetOption( "--path" )! ) )
                                    ? IndexReindexScopeKinds.Directory
                                    : IndexReindexScopeKinds.File
                            : IndexReindexScopeKinds.Root);
        var report = await runtime.Indexer.ReindexAsync(
            new IndexReindexScope
            {
                Kind = scopeKind,
                RootId = arguments.GetOption( "--root" ),
                Path = arguments.GetOption( "--path" )
            },
            progress,
            cancellationToken ).ConfigureAwait( false );

        if ( !quiet )
        {
            Console.Error.WriteLine( );
        }

        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( ProtocolJson.SerializeReport( report ) );
        }
        else
        {
            var elapsed = report.CompletedAt - report.StartedAt;
            Console.WriteLine(
                $"Indexed {report.Indexed} items; skipped {report.Skipped}; " +
                $"failed {report.Failed}; elapsed {elapsed.TotalSeconds:F2}s." );
        }

        return report.Failed > 0 ? 2 : 0;
    }

    private static async Task<int> RunIndexControlAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count == 0 ? "status" : positionals[0].ToLowerInvariant( );
        var rootId = arguments.GetOption( "--root" );
        switch ( action )
        {
            case "pause":
                await runtime.Indexer.PauseAsync( rootId, cancellationToken ).ConfigureAwait( false );
                break;
            case "resume":
                await runtime.Indexer.ResumeAsync( rootId, cancellationToken ).ConfigureAwait( false );
                break;
            case "status":
                break;
            default:
                Console.Error.WriteLine( "Use 'index status', 'index pause', or 'index resume'." );
                return 2;
        }

        var status = await runtime.Indexer.GetIncrementalStatusAsync(
            cancellationToken ).ConfigureAwait( false );
        foreach ( var root in status.Roots )
        {
            Console.WriteLine(
                $"{root.Id}  paused={root.Paused} watched={root.Watched} " +
                $"pending={root.PendingEventCount} indexed={root.IndexedItemCount}  {root.Path}" );
            if ( !string.IsNullOrWhiteSpace( root.LastError ) )
            {
                Console.WriteLine( $"  Error: {root.LastError}" );
                Console.WriteLine( $"  Next: {root.Remediation}" );
            }
        }

        var maintenance = status.Maintenance;
        Console.WriteLine(
            $"maintenance enabled={maintenance.Enabled} running={maintenance.Running} " +
            $"next={maintenance.NextRunAt?.ToString( "O", System.Globalization.CultureInfo.InvariantCulture ) ?? "none"}" );
        if ( !string.IsNullOrWhiteSpace( maintenance.LastError ) )
        {
            Console.WriteLine( $"  Maintenance error: {maintenance.LastError}" );
        }

        return 0;
    }

    private static async Task<int> RunStatusAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var status = await runtime.GetRuntimeStatusAsync( cancellationToken )
            .ConfigureAwait( false );

        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                status,
                CommandDeckJsonContext.Default.RuntimeStatusSnapshot ) );
            return 0;
        }

        var stats = status.Catalog;
        Console.WriteLine( $"Items:        {stats.ItemCount}" );
        Console.WriteLine( $"Files:        {stats.FileCount}" );
        Console.WriteLine( $"Config keys:  {stats.ConfigCount}" );
        Console.WriteLine( $"Applications: {stats.ApplicationCount}" );
        Console.WriteLine( $"Invocations:  {stats.UsageEventCount}" );
        Console.WriteLine( $"Database:     {stats.DatabaseBytes} bytes" );
        Console.WriteLine( $"Watch roots:  {status.Indexing.WatchedRootCount}" );
        Console.WriteLine( $"Pending:      {status.Indexing.PendingEventCount}" );
        Console.WriteLine( $"Reconcile:    {status.Indexing.ReconciliationRequired}" );
        foreach ( var unwatchedRoot in status.Indexing.UnwatchedRoots )
        {
            Console.WriteLine( $"Unwatched:    {unwatchedRoot}" );
        }
        foreach ( var root in status.Indexing.Roots )
        {
            Console.WriteLine(
                $"Root:         {root.Path} watched={root.Watched} paused={root.Paused} " +
                $"pending={root.PendingEventCount} indexed={root.IndexedItemCount}" );
            if ( !string.IsNullOrWhiteSpace( root.Remediation ) )
            {
                Console.WriteLine( $"  Next: {root.Remediation}" );
            }
        }
        if ( !string.IsNullOrWhiteSpace( status.Indexing.LastError ) )
        {
            Console.WriteLine( $"Index error:  {status.Indexing.LastError}" );
        }
        foreach ( var provider in status.Providers )
        {
            Console.WriteLine( $"{provider.Label}: {provider.State}" );
            if ( !string.IsNullOrWhiteSpace( provider.Detail ) )
            {
                Console.WriteLine( $"  {provider.Detail}" );
            }
            if ( !string.IsNullOrWhiteSpace( provider.Remediation ) )
            {
                Console.WriteLine( $"  Next: {provider.Remediation}" );
            }
        }
        return 0;
    }

    private static async Task<int> RunConfigAsync(
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count > 0 ? positionals[0] : "init";
        if ( !string.Equals( action, "init", StringComparison.OrdinalIgnoreCase ) )
        {
            Console.Error.WriteLine( "Only 'commanddeck config init' is currently supported." );
            return 2;
        }

        var path = await ConfigurationLoader.WriteDefaultAsync(
            arguments.GetOption( "--config" ),
            arguments.HasFlag( "--force" ),
            cancellationToken ).ConfigureAwait( false );
        Console.WriteLine( path );
        return 0;
    }

    private static async Task<int> RunPreferencesAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count > 0 ? positionals[0].ToLowerInvariant( ) : "show";
        switch ( action )
        {
            case "show":
                var snapshot = await runtime.GetPreferencesAsync(
                    includeShortcuts: false,
                    cancellationToken ).ConfigureAwait( false );
                if ( arguments.HasFlag( CliOptionNames.Json ) )
                {
                    Console.WriteLine( JsonSerializer.Serialize(
                        snapshot,
                        CommandDeckJsonContext.Default.PreferencesSnapshot ) );
                    return 0;
                }

                foreach ( var line in PreferencesTextFormatter.Format( snapshot ) )
                {
                    Console.WriteLine( line );
                }
                return 0;

            case "export":
                var export = await runtime.ExportCustomizationsAsync( cancellationToken )
                    .ConfigureAwait( false );
                Console.WriteLine( ProtocolJson.SerializeBoundedCustomizationExport( export ) );
                return 0;

            case "reset":
                if ( !arguments.HasFlag( CliOptionNames.Yes ) )
                {
                    Console.Error.WriteLine( "preferences reset requires --yes." );
                    return 2;
                }

                await runtime.ResetCustomizationsAsync( cancellationToken ).ConfigureAwait( false );
                Console.WriteLine( "Launcher preferences and customizations reset." );
                return 0;

            default:
                Console.Error.WriteLine( "Use 'preferences show', 'preferences export', or 'preferences reset --yes'." );
                return 2;
        }
    }

    private static int PrintVersion( )
    {
        var assembly = typeof( Program ).Assembly;
        var version = assembly
                          .GetCustomAttribute<AssemblyInformationalVersionAttribute>( )?
                          .InformationalVersion
                      ?? assembly.GetName( ).Version?.ToString( )
                      ?? "unknown";
        Console.WriteLine( $"commanddeck {version}" );
        Console.WriteLine( $".NET {Environment.Version}" );
        Console.WriteLine(
            System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
                ? "runtime: JIT"
                : "runtime: Native AOT" );
        return 0;
    }

    private static int UnknownCommand( string command )
    {
        Console.Error.WriteLine( $"Unknown command '{command}'. Run 'commanddeck help'." );
        return 2;
    }

    private static int ParseInteger( string? value, int fallback, int minimum, int maximum ) =>
        int.TryParse(
            value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed )
            ? Math.Clamp( parsed, minimum, maximum )
            : fallback;
}
