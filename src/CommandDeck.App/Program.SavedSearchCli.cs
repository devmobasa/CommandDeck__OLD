using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunSavedSearchAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var values = arguments.PositionalsAfterCommand( );
        var action = values.Count == 0 ? "list" : values[0].ToLowerInvariant( );
        var searches = await runtime.Preferences.GetSavedSearchesAsync(
            cancellationToken ).ConfigureAwait( false );
        if ( action == "list" )
        {
            WriteSavedSearches( searches, arguments.HasFlag( CliOptionNames.Json ) );
            return 0;
        }
        if ( action == "save" && values.Count >= 2 )
        {
            var query = arguments.GetOption( "--query" );
            if ( query is null )
            {
                throw new InvalidOperationException( "saved-search save requires --query." );
            }

            var aliases = await runtime.Preferences.GetFilterAliasesAsync(
                cancellationToken ).ConfigureAwait( false );
            var aliasMap = aliases.ToDictionary(
                static value => value.Alias,
                static value => value.FilterId,
                StringComparer.OrdinalIgnoreCase );
            var parsed = QueryParser.Parse( query, aliases: aliasMap );
            var result = await runtime.SaveSavedSearchAsync( new SavedSearch
            {
                Id = arguments.GetOption( "--id" ) ?? string.Empty,
                Name = values[1],
                Expression = parsed.Expression,
                Shortcut = arguments.GetOption( "--shortcut" ),
                ShowOnHome = arguments.HasFlag( "--home" )
            }, cancellationToken ).ConfigureAwait( false );
            WriteSavedSearches( result.Searches, arguments.HasFlag( CliOptionNames.Json ) );
            return 0;
        }
        if ( action == "rename" && values.Count == 3 )
        {
            var current = FindSavedSearch( searches, values[1] );
            var result = await runtime.SaveSavedSearchAsync( new SavedSearch
            {
                Id = current.Id,
                Name = values[2],
                Expression = current.Expression,
                Shortcut = current.Shortcut,
                ShowOnHome = current.ShowOnHome,
                Position = current.Position
            }, cancellationToken ).ConfigureAwait( false );
            WriteSavedSearches( result.Searches, arguments.HasFlag( CliOptionNames.Json ) );
            return 0;
        }
        if ( action == "run" && values.Count == 2 )
        {
            var current = FindSavedSearch( searches, values[1] );
            var results = await runtime.SearchAsync( new SearchRequest
            {
                Expression = current.Expression,
                Limit = ParseInteger( arguments.GetOption( CliOptionNames.Limit ), 20, 1, 200 )
            }, cancellationToken ).ConfigureAwait( false );
            if ( arguments.HasFlag( CliOptionNames.Json ) )
            {
                Console.WriteLine( JsonSerializer.Serialize(
                    results.ToList( ), CommandDeckJsonContext.Default.ListSearchResult ) );
            }
            else
            {
                if ( results.Count == 0 )
                {
                    Console.WriteLine( "No results." );
                }
                for ( var index = 0; index < results.Count; index++ )
                {
                    var result = results[index];
                    Console.WriteLine( $"{index + 1,2}. [{result.Kind}] {result.Title}" );
                }
            }
            return 0;
        }
        if ( action == "delete" && values.Count == 2 )
        {
            if ( !arguments.HasFlag( CliOptionNames.Yes ) )
            {
                throw new InvalidOperationException( "saved-search delete requires --yes." );
            }
            var result = await runtime.DeleteSavedSearchAsync(
                values[1], cancellationToken ).ConfigureAwait( false );
            WriteSavedSearches( result.Searches, arguments.HasFlag( CliOptionNames.Json ) );
            return 0;
        }
        Console.Error.WriteLine(
            "Use saved-search list, save NAME --query QUERY, rename ID NAME, run ID, or delete ID --yes." );
        return 2;
    }

    private static SavedSearch FindSavedSearch(
        IReadOnlyList<SavedSearch> searches,
        string id )
    {
        var result2 = searches.FirstOrDefault( value => string.Equals(
            value.Id, id, StringComparison.Ordinal ) );
        if ( result2 is null )
        {
            throw new InvalidOperationException( $"Saved search '{id}' was not found." );
        }

        return result2;
    }

    private static void WriteSavedSearches( IReadOnlyList<SavedSearch> searches, bool json )
    {
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                searches.ToList( ), CommandDeckJsonContext.Default.ListSavedSearch ) );
            return;
        }
        if ( searches.Count == 0 )
        {
            Console.WriteLine( "No saved searches." );
            return;
        }
        foreach ( var search in searches )
        {
            var shortcut = search.Shortcut is null ? string.Empty : $" [{search.Shortcut}]";
            Console.WriteLine(
                $"{search.Id}  {PreferencesTextFormatter.EscapeTerminalText( search.Name )}{shortcut}" );
        }
    }
}
