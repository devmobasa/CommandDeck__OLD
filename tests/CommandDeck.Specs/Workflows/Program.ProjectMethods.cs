namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ProjectCheckoutsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-projects-" ).FullName;
        try
        {
            var first = Directory.CreateDirectory( Path.Combine( root, "one checkout" ) ).FullName;
            var second = Directory.CreateDirectory( Path.Combine( root, "two" ) ).FullName;
            await ProcessRunner.RunAsync( "git", ["init", "-b", "feature-one", first], null, null, TimeSpan.FromSeconds( 2 ), default );
            ProjectProvider provider = new( [
                new() { Title = "Vision", Path = first },
                new() { Title = "Vision", Path = second },
                new() { Title = "Missing", Path = Path.Combine(root, "missing") }
            ] );
            SearchRequest request = new( )
            {
                Query = "proj: vision",
                Limit = 10
            };
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "vision" ), request, default )).Count );
            var rows = await provider.SearchAsync( QueryParser.Parse( request.Query ), request, default );
            Equal( 2, rows.Count );
            True( rows[0].Id != rows[1].Id, "each checkout has its own identity" );
            True( rows[0].DetailMarkdown!.Contains( "feature-one", StringComparison.Ordinal ), "branch appears in details" );
            Equal( first, rows[0].Actions[0].Arguments["workingDirectory"] );
            Equal( "rider\u001f" + first, rows[0].Actions[0].Arguments["argv"] );
            Equal( first, rows[0].Actions.Single( action => action.Id == "copy-path" ).Arguments["text"] );
            var missing = await provider.SearchAsync( QueryParser.Parse( "proj: missing" ), request, default );
            Equal( 1, missing[0].Actions.Count );
            Equal( ActionKinds.CopyText, missing[0].Actions[0].Kind );
            SearchCoordinator coordinator = new( [provider], new SearchOptions { ProviderTimeoutMilliseconds = 2000 } );
            Equal( 2, (await coordinator.SearchAsync( request, default )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
