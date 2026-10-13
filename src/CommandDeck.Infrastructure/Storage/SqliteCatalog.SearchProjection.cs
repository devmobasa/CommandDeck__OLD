using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // These rules decide what an item's title, subtitle and path expose to
    // full-text search. Indexing applies them to each IndexedItem and stores
    // the inclusion flags on its catalog row; the integrity check and repair
    // apply the same rules to those stored flags through SQL functions, so
    // they never expect or restore a field the search policy excluded.
    private static string SearchTitle( string title, bool titleInSearch ) =>
        titleInSearch ? title : string.Empty;

    private static string SearchMetadata( string? value, bool metadataInSearch ) =>
        metadataInSearch ? value ?? string.Empty : string.Empty;

    private static void RegisterSearchProjectionFunctions( SqliteConnection connection )
    {
        connection.CreateFunction<string, long, string>(
            "commanddeck_search_title",
            static ( title, titleInSearch ) => SearchTitle( title, titleInSearch != 0 ),
            isDeterministic: true );
        connection.CreateFunction<string?, long, string>(
            "commanddeck_search_metadata",
            static ( value, metadataInSearch ) => SearchMetadata( value, metadataInSearch != 0 ),
            isDeterministic: true );
    }
}
