namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FolderBrowsingAndSelectionAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-browse-" ).FullName;
        try
        {
            var child = Directory.CreateDirectory( Path.Combine( directory, "nested folder" ) ).FullName;
            for ( var i = 0; i < 8; i++ )
            {
                await File.WriteAllTextAsync( Path.Combine( directory, "file " + i ), "test" );
            }
            FolderBrowserProvider provider = new( );
            var query = QueryParser.Parse( "browse:" );
            var first = await provider.SearchAsync( query, new SearchRequest { Query = "browse:", BrowseDirectory = directory, Limit = 5 }, default );
            Equal( 4, first.Count );
            var nested = first.Single( row => row.Title == "nested folder" );
            Equal( ActionKinds.BrowseFolder, nested.Actions[0].Kind );
            Equal( child, nested.Actions[0].Arguments["path"] );
            var next = first.Single( row => row.Title == "Next page" );
            var second = await provider.SearchAsync( query, new SearchRequest
            {
                Query = "browse:",
                BrowseDirectory = directory,
                Limit = 5,
                BrowseOffset = int.Parse( next.Actions[0].Arguments["offset"] )
            }, default );
            True( !second.Any( row => row.Id == nested.Id ), "the next page advances through the directory" );
            var matches = await provider.SearchAsync( QueryParser.Parse( "browse: file 7" ), new SearchRequest { Query = "browse: file 7", BrowseDirectory = directory }, default );
            True( matches.Any( row => row.Title == "file 7" ), "filter searches entries outside the first page" );
            SearchCoordinator coordinator = new( [provider], new SearchOptions { Ranking = new RankingOptions { Context = new ContextRankingOptions { Enabled = false } } } );
            True( (await coordinator.SearchAsync( new SearchRequest { Query = "browse: file 7", BrowseDirectory = directory }, default )).Any( row => row.Title == "file 7" ), "browsing does not depend on contextual ranking" );
            var all = await provider.SearchAsync( query, new SearchRequest { Query = "browse:", BrowseDirectory = directory }, default );
            var file = all.Single( row => row.Title == "file 0" );
            Dictionary<string, SearchResult> cached = all.ToDictionary( static row => row.Id, StringComparer.Ordinal );
            RecordingActionExecutor executor = new( );
            FileSelectionService selection = new( executor, ( _, id ) => cached.GetValueOrDefault( id ) );
            Equal( 1, selection.Select( "one", file.Id, true ).Count );
            Equal( 1, selection.Select( "one", file.Id, true ).Count );
            Equal( 2, selection.Select( "one", nested.Id, true ).Count );
            cached.Clear( );
            True( (await selection.ExecuteAsync( "one", "copy-paths", default )).Succeeded, "selection survives search-cache expiry" );
            Equal( file.Metadata["path"] + "\n" + child, executor.Actions.Single( ).Arguments["text"] );
            True( !(await selection.ExecuteAsync( "other", "open", default )).Succeeded, "selections belong to the client" );
            Throws<InvalidOperationException>( ( ) => selection.Select( "other", file.Id, true ) );
            executor.Actions.Clear( );
            True( (await selection.ExecuteAsync( "one", "open", default )).Succeeded, "all selected paths open" );
            Equal( 2, executor.Actions.Count );
            Equal( ActionKinds.OpenPath, executor.Actions[0].Kind );
            File.Delete( file.Metadata["path"] );
            executor.Actions.Clear( );
            True( !(await selection.ExecuteAsync( "one", "open", default )).Succeeded, "missing paths fail before any opening" );
            Equal( 0, executor.Actions.Count );
            selection.Clear( "one" );
            True( !(await selection.ExecuteAsync( "one", "copy-paths", default )).Succeeded, "clear removes selection" );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }
}
