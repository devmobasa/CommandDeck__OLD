using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardQueriesNeverPersistAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-private-query-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                privacy: new PrivacyOptions { StoreQueryHistory = true, StoreUsageHistory = true } );
            await catalog.InitializeAsync( default );
            await catalog.SetFilterAliasAsync( "cb", "clipboard", default );
            foreach ( var query in new[] { "clip: sentinel", "-clip: sentinel", "app: cb: sentinel", "@clipboard sentinel" } )
            {
                await catalog.UsageHistory.RecordInvocationAsync( "app:synthetic", "launch", query, default );
            }

            await catalog.UsageHistory.RecordInvocationAsync( "clipboard:synthetic", "open", "ordinary text", default );
            Equal( 0, (await catalog.UsageHistory.GetQueryHistoryAsync( 100, default )).Entries.Count );
            await catalog.UsageHistory.RecordInvocationAsync( "app:synthetic", "launch", "ordinary query", default );
            Equal( "ordinary query", (await catalog.UsageHistory.GetQueryHistoryAsync( 100, default )).Entries.Single( ).Query );

            SearchCoordinator coordinator = new( [], new SearchOptions( ), preferences: catalog );
            foreach ( var expression in new[]
                {
                    new QueryExpression { IncludeFilterIds = ["clipboard", "applications"] },
                    new QueryExpression { ExcludeFilterIds = ["clipboard"] }
                } )
            {
                SearchRequest request = new( )
                {
                    Query = "synthetic private query",
                    Expression = expression
                };
                await coordinator.SearchAsync( request, default );
                True( request.ContainsPrivateQuery, "typed clipboard scope follows the canonical result into cache bookkeeping" );
                var serialized = JsonSerializer.Serialize( request, CommandDeckJsonContext.Default.SearchRequest );
                True( !serialized.Contains( "containsPrivateQuery", StringComparison.Ordinal ), "privacy decision never crosses the wire" );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardSavedSearchesAreQuarantinedAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-private-saved-" ).FullName;
        var path = Path.Combine( root, "catalog.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = path } );
            await catalog.InitializeAsync( default );
            var expression = new QueryExpression
            {
                IncludeFilterIds = ["clipboard"],
                Clauses = [new( ) { Text = "private sentinel" }]
            };
            SavedSearch saved = new( )
            {
                Id = "saved-search:private",
                Name = "private sentinel",
                Expression = expression,
                Shortcut = "CTRL+ALT+P",
                ShowOnHome = true,
                Position = 1
            };
            await ThrowsAsync<InvalidOperationException>( async ( ) => await catalog.SaveSavedSearchAsync( saved, default ) );
            Equal( 0, (await catalog.GetSavedSearchesAsync( default )).Count );

            using ( SqliteConnection database = new( $"Data Source={path};Pooling=False" ) )
            {
                database.Open( );
                using var command = database.CreateCommand( );
                command.CommandText = "INSERT INTO saved_searches VALUES($id,$name,$expression,'CTRL+ALT+P',1,1,1,1)";
                command.Parameters.AddWithValue( "$id", saved.Id );
                command.Parameters.AddWithValue( "$name", saved.Name );
                command.Parameters.AddWithValue( "$expression", JsonSerializer.Serialize( expression, CommandDeckJsonContext.Default.QueryExpression ) );
                command.ExecuteNonQuery( );
            }

            var rows = await catalog.GetSavedSearchesAsync( default );
            var quarantined = rows.Single( );
            True( quarantined.Unavailable, "legacy data is quarantined" );
            Equal( "Unsupported clipboard saved search", quarantined.Name );
            True( quarantined.Shortcut is null && !quarantined.ShowOnHome, "no shortcut or Home execution" );
            Equal( 0, quarantined.Expression.Clauses.Count );
            var export = await catalog.ExportAsync( default );
            Equal( 0, export.SavedSearches.Count );
            Equal( 1, export.OmittedClipboardSearches );
            True( !JsonSerializer.Serialize( export, CommandDeckJsonContext.Default.CustomizationExport )
                .Contains( "private sentinel", StringComparison.Ordinal ), "export omits private name and query" );
            SearchCoordinator coordinator = new( [], new SearchOptions( ), catalog, catalog );
            True( (await coordinator.SearchAsync( new SearchRequest( ), default )).All( result => result.Id != saved.Id ), "quarantine never appears on Home" );
            await catalog.DeleteSavedSearchAsync( saved.Id, default );
            Equal( 0, (await catalog.GetSavedSearchesAsync( default )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
