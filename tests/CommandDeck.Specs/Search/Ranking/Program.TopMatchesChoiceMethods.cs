using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task TopMatchesChoiceOverridesTheConfigurationAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-top-matches-choice-" ).FullName;
        try
        {
            foreach ( var configured in new[] { false, true } )
            {
                CommandDeckOptions options = new( )
                {
                    Storage = new StorageOptions { DatabasePath = Path.Combine( root, $"{configured}.db" ) },
                    Search = new SearchOptions
                    {
                        Ranking = new RankingOptions { TopMatches = new TopMatchesOptions { Enabled = configured } }
                    },
                    Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
                    Ai = new AiOptions { Enabled = false, Providers = [] }
                };
                await using SqliteCatalog catalog = new( options.Storage, searchOptions: options.Search );
                var config = Path.Combine( root, "config.json" );
                await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
                await using var runtime = await CommandDeckRuntime.CreateAsync( config,
                    new RuntimeConstructionOptions { StartIndexer = false }, default );
                await catalog.ReplaceAllAsync( SignalPromotionItems( ), null, default );
                await catalog.UsageHistory.RecordInvocationAsync( "app:signal.desktop", "launch", null, default );
                await catalog.UsageHistory.RecordInvocationAsync( "app:signal.desktop", "launch", null, default );
                async Task AssertSearchAsync( bool expected )
                {
                    foreach ( var query in new[] { "app: sig", "app: Signal" } )
                    {
                        var rows = await runtime.SearchAsync( new SearchRequest { Query = query }, default );
                        var app = rows.Single( value => value.Id == "app:signal.desktop" );
                        Equal( expected, app.Section?.Id == "top-match" );
                    }
                }

                var snapshot = await runtime.GetPreferencesAsync( false, default );
                True( snapshot.Preferences.TopMatches is null );
                Equal( configured, snapshot.Defaults!.TopMatches );
                True( PreferencesTextFormatter.Format( snapshot )
                    .Contains( $"Top Matches: {(configured ? "on" : "off")} (configuration)" ) );
                await AssertSearchAsync( configured );

                await runtime.UpdatePreferencesAsync( new LauncherPreferences
                {
                    Sources = snapshot.Preferences.Sources,
                    ReduceMotion = true
                }, default );
                True( (await catalog.LauncherPreferences.GetPreferencesAsync( default )).TopMatches is null );
                await AssertSearchAsync( configured );

                foreach ( var chosen in new[] { false, true, false } )
                {
                    await runtime.UpdatePreferencesAsync( new LauncherPreferences
                    {
                        Sources = snapshot.Preferences.Sources,
                        TopMatches = chosen
                    }, default );
                    Equal( ( bool? ) chosen, (await catalog.LauncherPreferences.GetPreferencesAsync( default )).TopMatches );
                    await AssertSearchAsync( chosen );
                    True( PreferencesTextFormatter.Format( await runtime.GetPreferencesAsync( false, default ) )
                        .Contains( $"Top Matches: {(chosen ? "on" : "off")}" ) );
                }

                // A choice saved by another client is applied by the next search.
                await catalog.LauncherPreferences.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = snapshot.Preferences.Sources,
                    TopMatches = !configured
                }, default );
                await AssertSearchAsync( !configured );

                var reset = await runtime.ResetCustomizationsAsync( default );
                True( reset.TopMatches is null );
                True( (await catalog.LauncherPreferences.GetPreferencesAsync( default )).TopMatches is null );
                await AssertSearchAsync( configured );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
