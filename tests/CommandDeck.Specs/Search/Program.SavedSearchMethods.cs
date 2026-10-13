namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SavedSearchesPersistTypedQueriesAndRejectConflictsAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ), $"commanddeck-saved-search-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            string id;
            await using ( SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } ) )
            {
                await catalog.InitializeAsync( CancellationToken.None );
                var expression = QueryParser.Parse(
                    "file: title:roadmap -path:private" ).Expression;
                var searches = await catalog.SaveSavedSearchAsync(
                    new SavedSearch
                    {
                        Id = string.Empty,
                        Name = "Public roadmap",
                        Expression = expression,
                        Shortcut = "ctrl+alt+r",
                        ShowOnHome = true
                    }, CancellationToken.None );
                var saved = searches.Single( );
                id = saved.Id;
                Equal( "CTRL+ALT+R", saved.Shortcut );
                Equal( "files", saved.Expression.IncludeFilterIds.Single( ) );
                Equal( 2, saved.Expression.Clauses.Count );
                True( saved.ShowOnHome );

                ShortcutInventory inventory = new( )
                {
                    Available = true,
                    Chords = ["SUPER+R"]
                };
                Throws<InvalidOperationException>( ( ) =>
                    SavedSearchValidator.EnsureShortcutAvailable(
                        "ctrl+n", string.Empty, searches, inventory ) );
                foreach ( var reserved in new[] { "ctrl+home", "ctrl+end", "ctrl+enter", "ctrl+1", "ctrl+9" } )
                {
                    Throws<InvalidOperationException>( ( ) =>
                        SavedSearchValidator.EnsureShortcutAvailable(
                            reserved, string.Empty, searches, inventory ) );
                }
                Throws<InvalidOperationException>( ( ) =>
                    SavedSearchValidator.EnsureShortcutAvailable(
                        "super+r", string.Empty, searches, inventory ) );
                Throws<InvalidOperationException>( ( ) =>
                    SavedSearchValidator.EnsureShortcutAvailable(
                        "ctrl+alt+r", "saved-search:other", searches, inventory ) );
            }

            await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = database } );
            await reopened.InitializeAsync( CancellationToken.None );
            var persisted = (await reopened.GetSavedSearchesAsync(
                CancellationToken.None )).Single( );
            Equal( id, persisted.Id );
            Equal( "Public roadmap", persisted.Name );
            Equal( 1, (await reopened.ExportAsync( CancellationToken.None )).SavedSearches.Count );

            SearchCoordinator coordinator = new(
                [], new SearchOptions( ), reopened, reopened, reopened.UsageHistory );
            var home = (await coordinator.SearchAsync(
                new SearchRequest { Limit = 20 }, CancellationToken.None ))
                .Single( value => value.Kind == ResultKinds.SavedSearch );
            Equal( id, home.Id );
            Equal( ActionKinds.ApplySavedSearch, home.Actions.Single( ).Kind );
            Equal( "files", home.SavedSearch?.Expression.IncludeFilterIds.Single( ) );

            await reopened.DeleteSavedSearchAsync( id, CancellationToken.None );
            Equal( 0, (await reopened.GetSavedSearchesAsync( CancellationToken.None )).Count );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task VerifySavedSearchProtocolAsync(
        UnixProtocolClient client,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 194,
            Operation = ProtocolOperations.SavedSearchSave,
            SavedSearch = new SavedSearch
            {
                Id = string.Empty,
                Name = "Protocol files",
                Expression = QueryParser.Parse( "file: protocol" ).Expression,
                ShowOnHome = true
            }
        }, CancellationToken.None );
        var saved = await ReadMessageAsync(
            client, 194, ProtocolMessageTypes.SavedSearchResult, cancellationToken );
        var search = saved?.SavedSearches?.Searches.Single( );
        if ( search is null )
        {
            throw new InvalidOperationException( "Saved search response missing." );
        }

        Equal( "files", search.Expression.IncludeFilterIds.Single( ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 195,
            Operation = ProtocolOperations.SavedSearchDelete,
            SavedSearchId = search.Id,
            Confirmed = false
        }, CancellationToken.None );
        var rejected = await ReadMessageAsync(
            client, 195, ProtocolMessageTypes.Error, cancellationToken );
        True( rejected?.Error?.Contains( "confirmed", StringComparison.Ordinal ) == true );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 196,
            Operation = ProtocolOperations.SavedSearchDelete,
            SavedSearchId = search.Id,
            Confirmed = true
        }, CancellationToken.None );
        var deleted = await ReadMessageAsync(
            client, 196, ProtocolMessageTypes.SavedSearchResult, cancellationToken );
        Equal( 0, deleted?.SavedSearches?.Searches.Count ?? -1 );
    }
}
