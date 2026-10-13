using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ScriptCommandsRespectInputsAndBoundsAsync( )
    {
        var directory = Directory.CreateTempSubdirectory( "commanddeck-scripts-" ).FullName;
        try
        {
            var script = Path.Combine( directory, "echo.sh" );
            await File.WriteAllTextAsync( script, "printf '%s' \"$1\"\n" );
            var manifestPath = Path.Combine( directory, "echo.commanddeck.json" );
            var manifest = new ScriptCommandManifest
            {
                Title = "Echo literal input",
                Executable = "/bin/sh",
                Arguments = [script, "{value}"],
                Inputs = [new( ) { Name = "value" }],
                TimeoutSeconds = 1
            };
            await File.WriteAllTextAsync( manifestPath, JsonSerializer.Serialize( manifest, CommandDeckJsonContext.Default.ScriptCommandManifest ) );
            await File.WriteAllTextAsync( Path.Combine( directory, "broken.commanddeck.json" ), "{" );
            await File.WriteAllTextAsync( Path.Combine( directory, "null-description.commanddeck.json" ),
                "{\"title\":\"Broken\",\"description\":null,\"executable\":\"/bin/true\"}" );
            ScriptCommandProvider provider = new( [directory] );
            SearchRequest request = new( )
            {
                Query = "script: echo"
            };
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "echo" ), request, default )).Count );
            var rows = await provider.SearchAsync( QueryParser.Parse( request.Query ), request, default );
            Equal( 1, rows.Count );
            True( rows[0].Actions[0].RequiresConfirmation, "scripts require explicit invocation confirmation" );
            var literal = "a b ' ; $(touch " + Path.Combine( directory, "never" ) + ")";
            var action = ActionInputValues.Bind( rows[0].Actions[0], new Dictionary<string, string> { ["value"] = literal } );
            var outcome = await ScriptCommandProvider.ExecuteAsync( action, default );
            True( outcome.Succeeded, outcome.Message ?? "script should succeed" );
            Equal( literal, outcome.DetailMarkdown );
            True( !File.Exists( Path.Combine( directory, "never" ) ), "inputs must not become shell source" );
            await File.AppendAllTextAsync( manifestPath, " " );
            True( !(await ScriptCommandProvider.ExecuteAsync( action, default )).Succeeded, "a changed manifest requires another search" );
            await File.WriteAllTextAsync( manifestPath, JsonSerializer.Serialize( manifest, CommandDeckJsonContext.Default.ScriptCommandManifest ) );
            var relativeManifest = new ScriptCommandManifest
            {
                Title = "Working directory",
                Executable = "/bin/pwd",
                WorkingDirectory = "child"
            };
            Directory.CreateDirectory( Path.Combine( directory, "child" ) );
            await File.WriteAllTextAsync( manifestPath, JsonSerializer.Serialize( relativeManifest, CommandDeckJsonContext.Default.ScriptCommandManifest ) );
            var relativeRows = await provider.SearchAsync( QueryParser.Parse( "script: Working" ), request, default );
            var relativeResult = await ScriptCommandProvider.ExecuteAsync( relativeRows[0].Actions[0], default );
            True( relativeResult.Succeeded, "relative working directory succeeds" );
            Equal( Path.Combine( directory, "child" ), relativeResult.DetailMarkdown!.Trim( ) );
            await File.WriteAllTextAsync( manifestPath, JsonSerializer.Serialize( manifest, CommandDeckJsonContext.Default.ScriptCommandManifest ) );
            await File.WriteAllTextAsync( script, "sleep 5\n" );
            var timed = await ScriptCommandProvider.ExecuteAsync( action, default );
            True( !timed.Succeeded && timed.Message!.Contains( "timed out", StringComparison.Ordinal ), "long scripts are stopped" );
            await File.WriteAllTextAsync( script, "while true; do printf 'abcdefghijklmnopqrstuvwxyz0123456789'; done\n" );
            var bounded = await ScriptCommandProvider.ExecuteAsync( action, default );
            True( !bounded.Succeeded && bounded.DetailMarkdown!.Length <= 90 * 1024, "script output is bounded" );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }
}
