using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task InlineCalculatorChoiceOverridesConfigurationAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-inline-choice-" ).FullName;
        foreach ( var configured in new[] { false, true } )
        {
            var files = Directory.CreateDirectory( Path.Combine( root, configured.ToString( ) ) ).FullName;
            var calculationPath = Path.Combine( files, "100 * 5.md" );
            var dateTitlePath = Path.Combine( files, "days until 11 sep.md" );
            var dateBodyPath = Path.Combine( files, "Notes.md" );
            await File.WriteAllTextAsync( calculationPath, "Arithmetic notes" );
            await File.WriteAllTextAsync( dateTitlePath, "Date notes" );
            await File.WriteAllTextAsync( dateBodyPath, "11 sep: days until launch\n100 * 5" );
            var calculationId = StableId.Create( ResultKinds.File, calculationPath );
            string[] dateIds = [StableId.Create( ResultKinds.File, dateTitlePath ), StableId.Create( ResultKinds.File, dateBodyPath )];

            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, $"{configured}.db" ) },
                Search = new SearchOptions { Calculator = new CalculatorOptions { Inline = configured, UseQalc = "never" } },
                Indexing = new IndexingOptions
                {
                    Roots = [new IndexRootOptions { Path = files, IncludeContentInSearch = true }],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await using SqliteCatalog catalog = new( options.Storage, searchOptions: options.Search );
            var config = Path.Combine( root, "config.json" );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config,
                new RuntimeConstructionOptions { StartIndexer = false }, default );
            await runtime.Indexer.ReindexAsync( null, default );

            async Task AssertSearchAsync( bool enabled )
            {
                foreach ( var topMatches in new[] { true, false } )
                {
                    var current = await catalog.LauncherPreferences.GetPreferencesAsync( default );
                    await runtime.UpdatePreferencesAsync( new LauncherPreferences
                    {
                        Sources = current.Sources,
                        InlineCalculator = current.InlineCalculator,
                        TopMatches = topMatches
                    }, default );
                    var rows = await runtime.SearchAsync( new SearchRequest { Query = "100*5" }, default );
                    Equal( enabled, rows.Any( static row => row.Kind == ResultKinds.Calculator ) );
                    var calculation = rows.Single( row => row.Id == calculationId );
                    True( calculation.Ranking.Contributions.Any( static value => value.Label == "Catalog lexical match" ),
                        "Arithmetic catalog results must come through FTS." );
                    var dates = await runtime.SearchAsync( new SearchRequest { Query = "days until 11 sep" }, default );
                    foreach ( var id in dateIds )
                    {
                        var date = dates.Single( row => row.Id == id );
                        True( date.Ranking.Contributions.Any( static value => value.Label == "Catalog lexical match" ),
                            "Date title and body matches must come through FTS." );
                    }
                    Equal( enabled, dates[0].Kind == ResultKinds.Calculator );
                    if ( enabled )
                    {
                        Equal( "500", rows[0].Title );
                        Equal( "500", rows[0].Actions[0].Arguments["text"] );
                    }
                }
                var explicitRows = await runtime.SearchAsync( new SearchRequest { Query = "=100 * 5" }, default );
                Equal( "500", explicitRows[0].Title );
                var dateRows = await runtime.SearchAsync( new SearchRequest { Query = "days until tomorrow" }, default );
                Equal( enabled, dateRows.Any( static row => row.Title == "1 day" ) );
            }

            var snapshot = await runtime.GetPreferencesAsync( false, default );
            True( snapshot.Preferences.InlineCalculator is null );
            Equal( configured, snapshot.Defaults!.InlineCalculator );
            await AssertSearchAsync( configured );
            foreach ( var chosen in new[] { false, true, false } )
            {
                await runtime.UpdatePreferencesAsync( new LauncherPreferences
                {
                    Sources = snapshot.Preferences.Sources,
                    InlineCalculator = chosen
                }, default );
                Equal( ( bool? ) chosen, (await catalog.LauncherPreferences.GetPreferencesAsync( default )).InlineCalculator );
                await AssertSearchAsync( chosen );
                True( PreferencesTextFormatter.Format( await runtime.GetPreferencesAsync( false, default ) )
                    .Contains( $"Inline calculator: {(chosen ? "on" : "off")}" ) );
            }
            await catalog.LauncherPreferences.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = snapshot.Preferences.Sources,
                InlineCalculator = !configured
            }, default );
            await AssertSearchAsync( !configured );
            var reset = await runtime.ResetCustomizationsAsync( default );
            True( reset.InlineCalculator is null );
            True( (await catalog.LauncherPreferences.GetPreferencesAsync( default )).InlineCalculator is null );
            await AssertSearchAsync( configured );
        }
    }
}
