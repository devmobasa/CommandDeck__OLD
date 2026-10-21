using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SavedSearchesPersistTypedQueriesAndRejectConflictsAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-saved-search-spec-{Guid.NewGuid( ):N}.db" );
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

                await VerifySavedAssistantPromptAsync( catalog );
                await VerifySavedLiteralQueriesAsync( catalog );

                ShortcutInventory inventory = new( )
                {
                    Available = true,
                    Chords = ["SUPER+R"]
                };
                Throws<InvalidOperationException>( ( ) =>
                    SavedSearchValidator.EnsureShortcutAvailable(
                        "ctrl+n", string.Empty, searches, inventory ) );
                foreach ( var reserved in new[]
                {
                    "ctrl+home", "ctrl+end", "ctrl+enter", "shift+ctrl+enter", "ctrl+1", "ctrl+9", "ctrl+u",
                    "ctrl+up", "ctrl+down", "ctrl+k", "ctrl+d", "ctrl+shift+c", "alt+shift+a", "shift+alt+d",
                    "ctrl+0", "ctrl+i", "alt+shift+r", "ctrl+alt+up", "alt+ctrl+down", "shift+escape", "shift+esc",
                    "ctrl+return", "ctrl+shift+return"
                } )
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

            // A shortcut saved before CommandDeck reserved it loads as reserved,
            // so the launcher never registers it over the built-in.
            using ( SqliteConnection connection = new( new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString( ) ) )
            {
                connection.Open( );
                using var command = connection.CreateCommand( );
                command.CommandText = "UPDATE saved_searches SET shortcut = 'CTRL+D' WHERE id = $id;";
                command.Parameters.AddWithValue( "$id", id );
                command.ExecuteNonQuery( );
            }
            var stale = (await reopened.GetSavedSearchesAsync( CancellationToken.None )).Single( );
            True( stale.Shortcut is null && stale.ReservedShortcut == "CTRL+D" );

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

    private static async Task VerifySavedAssistantPromptAsync( SqliteCatalog catalog )
    {
        const string prompt = "Is -1 negative? Keep  two spaces and \"quotes\".";
        var searches = await catalog.SaveSavedSearchAsync( new SavedSearch
        {
            Id = string.Empty,
            Name = "Assistant prompt",
            Expression = new QueryExpression
            {
                IncludeFilterIds = ["ai"],
                Clauses = [new QueryClause { Text = prompt }]
            }
        }, CancellationToken.None );
        var saved = searches.Single( value => value.Name == "Assistant prompt" );

        Equal( prompt, saved.Expression.Clauses.SingleOrDefault( )?.Text );

        await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = catalog.DatabasePath } );
        await reopened.InitializeAsync( CancellationToken.None );
        var persisted = (await reopened.GetSavedSearchesAsync( CancellationToken.None ))
            .Single( value => value.Id == saved.Id );
        var updated = (await reopened.SaveSavedSearchAsync( new SavedSearch
        {
            Id = persisted.Id,
            Name = "Renamed assistant prompt",
            Expression = persisted.Expression
        }, CancellationToken.None )).Single( value => value.Id == saved.Id );

        Equal( prompt, updated.Expression.Clauses.SingleOrDefault( )?.Text );

        await reopened.DeleteSavedSearchAsync( saved.Id, CancellationToken.None );
    }

    private static async Task VerifySavedLiteralQueriesAsync( SqliteCatalog catalog )
    {
        var prompt = "Is -1 negative? Keep  two spaces and \"quotes\". " + new string( 'x', 600 );
        foreach ( var original in new[] { prompt, "claude: " + prompt, "= -1" } )
        {
            var searches = await catalog.SaveSavedSearchAsync( new SavedSearch
            {
                Id = string.Empty,
                Name = "Literal query",
                Expression = new QueryExpression
                {
                    LiteralQuery = original,
                    IncludeFilterIds = original == prompt ? ["ai"] : []
                }
            }, CancellationToken.None );
            var saved = searches.Single( value => value.Name == "Literal query" );

            await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = catalog.DatabasePath } );
            await reopened.InitializeAsync( CancellationToken.None );
            var persisted = (await reopened.GetSavedSearchesAsync( CancellationToken.None ))
                .Single( value => value.Id == saved.Id );
            var updated = (await reopened.SaveSavedSearchAsync( new SavedSearch
            {
                Id = persisted.Id,
                Name = "Renamed literal query",
                Expression = persisted.Expression
            }, CancellationToken.None )).Single( value => value.Id == saved.Id );

            Equal( original, updated.Expression.LiteralQuery );
            var replay = QueryParser.Parse( null, updated.Expression );
            Equal( original, replay.Original );
            Equal( original == "= -1" ? "-1" : prompt, replay.Text );
            Equal( original.StartsWith( "claude:", StringComparison.Ordinal ) ? AgentBackendIds.ClaudeCode : null,
                replay.ExplicitBackendId );
            Equal( original == "= -1", replay.IsCalculator );

            await reopened.DeleteSavedSearchAsync( saved.Id, CancellationToken.None );
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
