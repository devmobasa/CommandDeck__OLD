using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task JsoncLocationsAndDuplicateKeysAreExactAsync( )
    {
        const string jsonc = """
            {
              // duplicate keys are legal input and must remain independently addressable
              "café": {
                "dup": "first",
                "dup": "second",
              },
            }
            """;
        var entries = StructuredConfigExtractor.Extract(
            "/tmp/settings.jsonc",
            jsonc );
        var duplicates = entries
            .Where( static entry => entry.Key == "café.dup" )
            .ToArray( );

        Equal( 2, duplicates.Length );
        Equal( "first", duplicates[0].Value );
        Equal( 4, duplicates[0].KeyLine );
        Equal( 5, duplicates[0].KeyColumn );
        Equal( 4, duplicates[0].ValueLine );
        Equal( 12, duplicates[0].ValueColumn );
        Equal( "second", duplicates[1].Value );
        Equal( 5, duplicates[1].KeyLine );
        Equal( 5, duplicates[1].KeyColumn );
        Equal( 12, duplicates[1].ValueColumn );

        var sameLineUnicode = StructuredConfigExtractor.Extract(
            "/tmp/inline.json",
            "{ \"é\": { \"dup\": 1 } }" ).Single( );
        Equal( 10, sameLineUnicode.KeyColumn );
        Equal( 17, sameLineUnicode.ValueColumn );

        var temporary = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-json-location-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( temporary );
        var path = Path.Combine( temporary, "settings.jsonc" );
        await File.WriteAllTextAsync( path, jsonc );
        FileIndexSource source = new( new IndexingOptions
        {
            Roots =
            [
                new IndexRootOptions
                {
                    Path = temporary,
                    IncludeContentInSearch = true
                }
            ],
            IndexDesktopApplications = false,
            IndexOmarchyCommands = false
        }, new PrivacyOptions( ) );

        try
        {
            var indexed = await source.IndexPathAsync(
                path,
                CancellationToken.None );
            var duplicateItems = indexed
                .Where( static item => item.Kind == ResultKinds.Configuration &&
                    item.Title == "café.dup" )
                .ToArray( );
            Equal( 2, duplicateItems.Length );
            Equal( 2, duplicateItems.Select( static item => item.StableId ).Distinct( ).Count( ) );

            using var firstPayload = JsonDocument.Parse( duplicateItems[0].PayloadJson! );
            Equal( "4", firstPayload.RootElement.GetProperty( "keyLine" ).GetString( ) );
            Equal( "5", firstPayload.RootElement.GetProperty( "keyColumn" ).GetString( ) );
            Equal( "12", firstPayload.RootElement.GetProperty( "valueColumn" ).GetString( ) );

            await using SqliteCatalog catalog = new( new StorageOptions
            {
                DatabasePath = Path.Combine( temporary, "catalog.db" )
            } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync(
                AsAsync( indexed ),
                progress: null,
                CancellationToken.None );
            var result = (await catalog.SearchAsync(
                QueryParser.Parse( "cfg: first" ),
                10,
                CancellationToken.None )).Single( );
            Equal( "4:5", result.Details.Single( static detail =>
                detail.Label == "Key location" ).Value );
            Equal( "4:12", result.Details.Single( static detail =>
                detail.Label == "Value location" ).Value );
            var edit = result.Actions.Single( static action => action.Id == "edit" );
            Equal( "4", edit.Arguments["line"] );
            Equal( "5", edit.Arguments["column"] );
        }
        finally
        {
            source.Dispose( );
            Directory.Delete( temporary, recursive: true );
        }
    }

    private static void TypedStructuredConfigParsersRespectTheirGrammars( )
    {
        var toml = StructuredConfigExtractor.Extract(
            "/tmp/settings.toml",
            """"
            [server]
            title = """first
            second"""
            """" );
        var tomlTitle = toml.Single( );
        Equal( "server", tomlTitle.Section );
        Equal( "first\nsecond", tomlTitle.Value );
        Equal( 2, tomlTitle.KeyLine );
        Equal( 1, tomlTitle.KeyColumn );
        Equal( 9, tomlTitle.ValueColumn );

        var yaml = StructuredConfigExtractor.Extract(
            "/tmp/settings.yaml",
            """
            service:
              command: >
                run
                now
            """ );
        var yamlCommand = yaml.Single( );
        Equal( "service", yamlCommand.Section );
        Equal( "command", yamlCommand.Key );
        Equal( "run now", yamlCommand.Value );
        Equal( 2, yamlCommand.KeyLine );
        Equal( 3, yamlCommand.KeyColumn );

        var systemd = StructuredConfigExtractor.Extract(
            "/tmp/example.service",
            """
            [Service]
            Environment="A=1" \
              "B=2"
            Environment=C=3
            """ );
        Equal( 2, systemd.Count );
        True( systemd.All( static entry => entry.Section == "Service" ) );
        Equal( "\"A=1\" \"B=2\"", systemd[0].Value );
        Equal( "C=3", systemd[1].Value );

        var lua = StructuredConfigExtractor.Extract(
            "/tmp/settings.lua",
            """
            local app.title = [[hello
            world]]
            ignored: not_an_assignment
            """ );
        var luaTitle = lua.Single( );
        Equal( "app", luaTitle.Section );
        Equal( "app.title", luaTitle.Key );
        Equal( "hello\nworld", luaTitle.Value );

        var luaTable = StructuredConfigExtractor.Extract(
            "/tmp/looknfeel.lua",
            """
            hl.config({
              general = {
                gaps_in = 1,
                layout = "dwindle"; -- tiling
              },
            })
            """ );
        Equal( "1", luaTable.Single( static entry => entry.Key == "gaps_in" ).Value );
        Equal( "dwindle", luaTable.Single( static entry => entry.Key == "layout" ).Value );

        var hyprland = StructuredConfigExtractor.Extract(
            "/tmp/hyprland.conf",
            """
            decoration {
              blur {
                enabled = true # effective setting
              }
            }
            """ );
        var enabled = hyprland.Single( );
        Equal( "decoration.blur", enabled.Section );
        Equal( "enabled", enabled.Key );
        Equal( "true", enabled.Value );
        Equal( 3, enabled.KeyLine );
        Equal( 5, enabled.KeyColumn );
        Equal( 15, enabled.ValueColumn );
    }

    private static async IAsyncEnumerable<IndexedItem> AsAsync(
        IEnumerable<IndexedItem> items )
    {
        foreach ( var item in items )
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
