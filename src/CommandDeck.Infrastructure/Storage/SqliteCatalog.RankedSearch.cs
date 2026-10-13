using System.Buffers;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // A one-letter prefix matches most of the catalog. The single statement this
    // replaces sought every match's catalog item (a text-key lookup per row)
    // before LIMIT kept the best few. Ranking now happens inside the FTS scan: the
    // only per-match lookups left decide order or eligibility (last use for the
    // tie-break, hidden items, and the item only while Home-only search is on).
    // Item columns, favorites and aliases are read for the ranked rows alone, and
    // highlights for the rows the ranking keeps.
    //
    // The order is the old one: bm25 rank, then last use, then FTS rowid, which is
    // the order in which the old statement kept tied rows. Both statements run in
    // one read transaction, so they read one snapshot.
    private async ValueTask<List<SearchResult>> ReadRankedMatchesAsync(
        SqliteConnection connection,
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int limit,
        CancellationToken cancellationToken )
    {
        var rowLimit = Math.Clamp( limit, 1, 500 );
        await using var snapshot = connection.BeginTransaction( deferred: true );

        for ( var candidateLimit = rowLimit; ; candidateLimit *= 4 )
        {
            var matches = await ReadRankedMatchRowsAsync(
                connection,
                snapshot,
                query,
                sourceIds,
                candidateLimit,
                cancellationToken ).ConfigureAwait( false );
            var (results, joinedRows) = await ReadRankedItemsAsync(
                connection,
                snapshot,
                query,
                matches,
                rowLimit,
                cancellationToken ).ConfigureAwait( false );

            // An FTS row whose item is missing (a projection not yet repaired) takes
            // a candidate the old join skipped; widen until the page is full.
            if ( joinedRows >= rowLimit || matches.Count < candidateLimit )
            {
                return results;
            }
        }
    }

    private async ValueTask<List<RankedMatch>> ReadRankedMatchRowsAsync(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int candidateLimit,
        CancellationToken cancellationToken )
    {
        await using var command = CreateRankedMatchCommand( connection, query, sourceIds, candidateLimit );
        command.Transaction = snapshot;

        List<RankedMatch> matches = [];
        await using var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        var lexical = new CatalogLexicalRowReader( reader );
        var stableId = reader.GetOrdinal( "stable_id" );
        var lastUsed = reader.GetOrdinal( "last_used_unix_ms" );
        var useCount = reader.GetOrdinal( "use_count" );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            matches.Add( new RankedMatch(
                reader.GetString( stableId ), lexical.Rank,
                reader.GetInt64( lastUsed ), reader.GetInt64( useCount ),
                [lexical.TitleMatch, lexical.MetadataMatch, lexical.PathMatch, lexical.ContentMatch, lexical.ConfigMatch] ) );
        }

        return matches;
    }

    // Specs read this command's query plan to prove catalog items are sought only
    // under the Home-only scope.
    internal SqliteCommand CreateRankedMatchCommand(
        SqliteConnection connection,
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int candidateLimit )
    {
        var command = connection.CreateCommand( );
        command.CommandText = $"""
            SELECT catalog_fts.stable_id,
                   bm25(catalog_fts, 0.0, 0.0, 0.0,
                        $title_weight, $metadata_weight, $path_weight,
                        $content_weight, $config_weight) AS rank,
                   COALESCE(u.last_used_unix_ms, 0) AS last_used_unix_ms,
                   COALESCE(u.use_count, 0) AS use_count,
                   highlight(catalog_fts, 3, char(1), char(2)) AS title_match,
                   highlight(catalog_fts, 4, char(1), char(2)) AS metadata_match,
                   highlight(catalog_fts, 5, char(1), char(2)) AS path_match,
                   snippet(catalog_fts, 6, char(1), char(2), ' … ', 18) AS content_match,
                   snippet(catalog_fts, 7, char(1), char(2), ' … ', 18) AS config_match
            FROM catalog_fts
            LEFT JOIN usage u ON u.stable_id = catalog_fts.stable_id
            WHERE catalog_fts MATCH $fts_query
              {RankedMatchScopeClause( )}
            ORDER BY rank, last_used_unix_ms DESC, catalog_fts.rowid
            LIMIT $candidate_limit;
            """;
        command.Parameters.AddWithValue( "$fts_query", BuildFtsQuery( query, sourceIds ) );
        command.Parameters.AddWithValue( "$title_weight", _rankingOptions.TitleWeight );
        command.Parameters.AddWithValue( "$metadata_weight", _rankingOptions.MetadataWeight );
        command.Parameters.AddWithValue( "$path_weight", _rankingOptions.PathWeight );
        command.Parameters.AddWithValue( "$content_weight", _rankingOptions.ContentWeight );
        command.Parameters.AddWithValue( "$config_weight", _rankingOptions.ConfigWeight );
        AddHomeSearchParameters( command );
        command.Parameters.AddWithValue( "$candidate_limit", candidateLimit );

        return command;
    }

    // Reads the matches' items in rank order and builds results for the first
    // rowLimit matches that still have an item.
    private async ValueTask<(List<SearchResult> Results, int JoinedRows)> ReadRankedItemsAsync(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        ParsedQuery query,
        List<RankedMatch> matches,
        int rowLimit,
        CancellationToken cancellationToken )
    {
        List<SearchResult> results = [];
        if ( matches.Count == 0 )
        {
            return (results, 0);
        }

        await using var command = connection.CreateCommand( );
        command.Transaction = snapshot;
        command.CommandText = """
            SELECT matched.key AS match_index, i.stable_id, i.kind, i.title, i.subtitle, i.path, i.icon,
                   i.payload_json, i.provider_id, i.source_id,
                   EXISTS(SELECT 1 FROM favorites f WHERE f.stable_id = i.stable_id) AS is_favorite,
                   COALESCE((SELECT group_concat(ordered_alias.alias, char(31))
                             FROM (SELECT a.alias FROM aliases a
                                   WHERE a.stable_id = i.stable_id
                                   ORDER BY a.created_unix_ms, a.alias COLLATE NOCASE)
                                  ordered_alias), '') AS aliases,
                   i.index_policy_json, 0 AS use_count, 0 AS last_used_unix_ms
            FROM json_each($stable_ids) matched
            JOIN catalog_items i ON i.stable_id = matched.value
            ORDER BY matched.key;
            """;
        command.Parameters.AddWithValue( "$stable_ids", EncodeStableIds( matches ) );

        var joinedRows = 0;
        await using var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        var row = new CatalogSearchRowReader( reader );
        var matchIndex = reader.GetOrdinal( "match_index" );
        while ( joinedRows < rowLimit && await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            joinedRows++;
            var result = CreateRankedSearchResult( matches[reader.GetInt32( matchIndex )], row );
            if ( MatchesFilterExpression( query, result ) )
            {
                results.Add( result );
            }
        }

        return (results, joinedRows);
    }

    private static string EncodeStableIds( List<RankedMatch> matches )
    {
        ArrayBufferWriter<byte> buffer = new( matches.Sum( static match => match.StableId.Length + 3 ) + 2 );
        using ( Utf8JsonWriter writer = new( buffer ) )
        {
            writer.WriteStartArray( );
            foreach ( var match in matches )
            {
                writer.WriteStringValue( match.StableId );
            }
            writer.WriteEndArray( );
        }

        return System.Text.Encoding.UTF8.GetString( buffer.WrittenSpan );
    }

    private SearchResult CreateRankedSearchResult( RankedMatch match, CatalogSearchRowReader item )
    {
        var kind = item.Kind;
        var lexicalScore = RankToScore( match.Rank );
        var frecencyBoost = AdaptiveScore( kind, match.UseCount, match.LastUsedUnixMilliseconds );
        var score = Math.Clamp( lexicalScore + frecencyBoost, 0, 1.2 );
        var ranking = BuildCatalogRanking(
            match.Marks,
            kind,
            lexicalScore,
            frecencyBoost,
            score,
            _rankingOptions.TimeAware );
        ranking.Policies.AddRange( ReadIndexPolicies( item ) );

        return CreateCatalogSearchResult(
            match.StableId,
            kind,
            item.Title,
            item.Subtitle,
            item.Path,
            item.Icon,
            item.Payload,
            item.ProviderId,
            item.SourceId,
            score,
            item.IsFavorite,
            SplitAliases( item.Aliases ),
            ranking: ranking );
    }

    // Marks are the highlighted title, subtitle and path and the body and
    // configuration snippets, in that order.
    private sealed record RankedMatch(
        string StableId,
        double Rank,
        long LastUsedUnixMilliseconds,
        long UseCount,
        string[] Marks );
}
