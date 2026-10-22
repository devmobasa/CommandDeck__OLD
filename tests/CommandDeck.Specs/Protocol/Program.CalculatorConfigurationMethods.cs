using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CalculatorDeprecatedOptionAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, _) = await CreateCliCatalogAsync( root, "Calculator notes" );
            var document = JsonNode.Parse( await File.ReadAllTextAsync( config ) );
            if ( document is null )
            {
                throw new InvalidOperationException( "Missing fixture configuration." );
            }

            var calculator = document["search"]?["calculator"];
            if ( calculator is null )
            {
                throw new InvalidOperationException( "Missing fixture calculator options." );
            }

            calculator["exchangeRates"] = false;
            int? exitCode = null;
            foreach ( var mode in new string?[] { null, "auto", "never", "always", "unknown", "" } )
            {
                if ( mode is null )
                {
                    calculator.AsObject( ).Remove( "useQalc" );
                }
                else
                {
                    calculator["useQalc"] = mode;
                }

                await File.WriteAllTextAsync( config, document.ToJsonString( ) );
                var loaded = await ConfigurationLoader.LoadAsync( config, default );

                Equal( mode, loaded.Search.Calculator.UseQalc );
                var doctor = await RunCommandDeckWithEnvironmentAsync( new Dictionary<string, string?>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine( root, "config" ),
                    ["XDG_STATE_HOME"] = Path.Combine( root, "state" ),
                    ["HYPRLAND_INSTANCE_SIGNATURE"] = null
                }, "doctor", "--config", config );
                var notes = doctor.StandardOutput.Split( '\n' )
                    .Where( line => line.Contains( "search.calculator.useQalc is deprecated and ignored; remove it", StringComparison.Ordinal ) ).ToArray( );

                Equal( mode is null ? 0 : 1, notes.Length );
                if ( mode is not null )
                {
                    True( notes[0].StartsWith( "WARN", StringComparison.Ordinal ), notes[0] );
                }

                // The deprecation warning must not change Doctor's exit code.
                exitCode ??= doctor.ExitCode;
                Equal( exitCode, doctor.ExitCode );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task CalculatorRuntimeUsesStateCacheAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "calculator-runtime-" ).FullName;
        var state = Directory.CreateDirectory( Path.Combine( root, "state" ) ).FullName;
        var database = Path.Combine( state, "catalog.db" );
        var cache = database + ".exchange-rates";
        var config = Path.Combine( root, "config.json" );
        CommandDeckOptions options = new( )
        {
            Storage = new( ) { DatabasePath = database },
            Search = new( ) { Calculator = new( ) { ExchangeRates = false } },
            Indexing = new( ) { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
            Ai = new( ) { Enabled = false, Providers = [] }
        };
        await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );

        await using ( var missing = await CommandDeckRuntime.CreateAsync( config, new RuntimeConstructionOptions { StartIndexer = false }, default ) )
        {
            var rows = await missing.SearchAsync( new( ) { Query = "100 usd to eur" }, default );
            // A fetch that the setting failed to stop would finish before this returns.
            await missing.ExchangeRates!.WhenIdleAsync( );

            True( rows.All( row => row.Kind != ResultKinds.Calculator ) );
            True( !File.Exists( cache ) );
        }

        // An old cache must still be used when fetching is disabled.
        await File.WriteAllTextAsync( cache, "2026-10-21\n2026-10-21T12:00:00.0000000+00:00\nUSD 1.1234\n" );
        await using var runtime = await CommandDeckRuntime.CreateAsync( config, new RuntimeConstructionOptions { StartIndexer = false }, default );

        foreach ( var prefix in new[] { "", "=" } )
        {
            var rows = await runtime.SearchAsync( new( ) { Query = prefix + "$100 to €" }, default );

            Equal( "€89.02", rows[0].Title );
            Equal( "ECB reference rates, 2026-10-21", rows[0].Details.Single( field => field.Label == "Exchange rates" ).Value );
        }
    }
}
