using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // Scope candidates before their LIMIT, including fuzzy and Home candidates.
    // Applications and other non-file sources remain available outside home.
    // Hidden items are excluded here too, so they never consume a candidate
    // slot on any catalog path; the probe is a primary-key lookup per row.
    internal static string HomeSearchClause( string itemAlias, bool? enabled = null )
    {
        var homeOnly = enabled is null
            ? HomeOnlyPreference
            : enabled.Value ? "1" : "0";
        return $"""
            AND ({homeOnly} = 0
                 OR {InsideHomeScope( itemAlias )})
            AND NOT EXISTS(SELECT 1 FROM hidden_items hidden WHERE hidden.stable_id = {itemAlias}.stable_id)
            """;
    }

    // The same scope for a ranked FTS match whose item has not been read: the
    // item is sought only while Home-only search is on.
    private static string RankedMatchScopeClause( ) => $"""
        AND ({HomeOnlyPreference} = 0
             OR EXISTS(SELECT 1 FROM catalog_items scoped
                       WHERE scoped.stable_id = catalog_fts.stable_id
                         AND {InsideHomeScope( "scoped" )}))
        AND NOT EXISTS(SELECT 1 FROM hidden_items hidden WHERE hidden.stable_id = catalog_fts.stable_id)
        """;

    private const string HomeOnlyPreference =
        "EXISTS(SELECT 1 FROM launcher_preferences WHERE key = 'search_home_only' AND value = 'true')";

    private static string InsideHomeScope( string itemAlias ) => $"""
        ({itemAlias}.kind NOT IN ('file', 'configuration')
         OR {itemAlias}.path = $search_home
         OR substr({itemAlias}.path, 1, length($search_home_prefix)) = $search_home_prefix)
        """;

    internal static void AddHomeSearchParameters( SqliteCommand command )
    {
        var home = PathExpansion.Expand( "~" );
        if ( CanonicalPathBoundary.TryCreate( home, out var boundary ) )
        {
            home = boundary!.CanonicalRoot;
        }
        command.Parameters.AddWithValue( "$search_home", home );
        command.Parameters.AddWithValue( "$search_home_prefix",
            Path.EndsInDirectorySeparator( home ) ? home : home + Path.DirectorySeparatorChar );
    }
}
