namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardNavigationSurvivesBookmarksAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-navigation-" ).FullName;
        try
        {
            List<IndexedItem> items = [];
            ConfiguredItemSource source = new( new CommandDeckOptions( ) );
            await foreach ( var item in source.ScanAsync( default ) )
            {
                items.Add( item );
            }

            var navigation = items.Single( item => item.StableId == ClipboardHistoryNavigation.Id );
            True( navigation.Body is null && navigation.PayloadJson is null, "navigation contains no query or history payload" );
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( source.ScanAsync( default ), null, default );
            var result = (await catalog.SearchAsync( QueryParser.Parse( "Clipboard History" ), 10, default )).Single( );
            Equal( ClipboardHistoryNavigation.Id, result.Id );
            True( result.CanCustomize && !ClipboardPrivacy.IsHistory( result ), "query-free navigation can be bookmarked without treating it as private content" );
            Equal( ActionKinds.OpenClipboardHistory, result.Actions.Single( ).Kind );
            Equal( 0, result.Actions.Single( ).Arguments.Count );
            await catalog.SetFavoriteAsync( result, true, default );
            await catalog.SetAliasAsync( result, "history-browser", default );
            await catalog.ReplaceAllAsync( source.ScanAsync( default ), null, default );
            var alias = await catalog.FindAliasAsync( "history-browser", default );
            Equal( result.Id, alias!.Id );
            Equal( ActionKinds.OpenClipboardHistory, alias.Actions.Single( ).Kind );
            var home = await catalog.GetHomeResultsAsync( LauncherPreferences.CreateDefault( ), 20, default );
            True( home.Any( item => item.Id == result.Id && item.IsFavorite ), "bookmarked navigation is retained on Home" );
            True( home.All( static item => !ClipboardPrivacy.IsHistory( item ) ), "navigation adds no clipboard content to Home" );
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }
}
