using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        int limit,
        CancellationToken cancellationToken )
    {
        var sources = query.IncludedFilters
            .Where( static filter => string.Equals(
                filter.ProviderId,
                "catalog",
                StringComparison.Ordinal ) )
            .Select( static filter => filter.SourceId )
            .Where( static source => !string.IsNullOrWhiteSpace( source ) )
            .Select( static source => source! )
            .Distinct( StringComparer.Ordinal )
            .ToArray( );
        return await SearchSourcesAsync( query, sources, limit, cancellationToken )
            .ConfigureAwait( false );
    }

    public async ValueTask<IReadOnlyList<SearchResult>> SearchSourcesAsync(
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int limit,
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        using var interrupt = InterruptOnCancellation( connection, cancellationToken );

        try
        {
            var results = await ReadCatalogMatchesAsync( connection, query, sourceIds, limit, cancellationToken )
                .ConfigureAwait( false );
            if ( query.Terms.Count > 0 && query.Text.Length <= 64 && results.Count < limit )
            {
                await AddFuzzyCandidatesAsync(
                    connection,
                    query,
                    sourceIds,
                    limit,
                    results,
                    cancellationToken ).ConfigureAwait( false );
            }

            // Open With actions are added by the runtime for the rows it shows.
            return results;
        }
        catch ( SqliteException exception ) when ( WasInterrupted( exception, cancellationToken ) )
        {
            throw new OperationCanceledException( "The catalog search was cancelled.", exception, cancellationToken );
        }
    }

    private ValueTask<List<SearchResult>> ReadCatalogMatchesAsync(
        SqliteConnection connection,
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int limit,
        CancellationToken cancellationToken ) =>
        query.Terms.Count == 0
            ? ReadBrowseMatchesAsync( connection, query, sourceIds, limit, cancellationToken )
            : ReadRankedMatchesAsync( connection, query, sourceIds, limit, cancellationToken );

    private async ValueTask<List<SearchResult>> ReadBrowseMatchesAsync(
        SqliteConnection connection,
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int limit,
        CancellationToken cancellationToken )
    {
        await using var command = CreateBrowseCommand( connection, sourceIds, limit );

        List<SearchResult> results = [];
        await using var reader =
            await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        var row = new CatalogSearchRowReader( reader );

        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var lexicalScore = 0.25;
            var frecencyBoost = AdaptiveScore(
                row.Kind,
                row.UseCount,
                row.LastUsedUnixMilliseconds );

            var score = Math.Clamp( lexicalScore + frecencyBoost, 0, 1.2 );
            var ranking = new RankingExplanation { FinalScore = score };
            ranking.Policies.AddRange( ReadIndexPolicies( row ) );

            var result = CreateCatalogSearchResult(
                row.StableId,
                row.Kind,
                row.Title,
                row.Subtitle,
                row.Path,
                row.Icon,
                row.Payload,
                row.ProviderId,
                row.SourceId,
                score,
                row.IsFavorite,
                SplitAliases( row.Aliases ),
                ranking: ranking );
            if ( MatchesFilterExpression( query, result ) )
            {
                results.Add( result );
            }
        }

        return results;
    }

    private static SqliteCommand CreateBrowseCommand(
        SqliteConnection connection,
        IReadOnlyList<string> sourceIds,
        int limit )
    {
        var command = connection.CreateCommand( );
        var enabledSourceParameters = string.Join(
            ", ",
            sourceIds.Select( ( _, index ) => $"$enabled_source_{index}" ) );
        var enabledSourceClause = sourceIds.Count == 0
            ? string.Empty
            : $" AND i.source_id IN ({enabledSourceParameters})";
        command.CommandText = $"""
            SELECT i.stable_id, i.kind, i.title, i.subtitle, i.path, i.icon,
                   i.payload_json, i.provider_id, i.source_id,
                   COALESCE(u.use_count, 0) AS use_count,
                   COALESCE(u.last_used_unix_ms, 0) AS last_used_unix_ms,
                   EXISTS(SELECT 1 FROM favorites f WHERE f.stable_id = i.stable_id) AS is_favorite,
                   COALESCE((SELECT group_concat(ordered_alias.alias, char(31))
                             FROM (SELECT a.alias FROM aliases a
                                   WHERE a.stable_id = i.stable_id
                                   ORDER BY a.created_unix_ms, a.alias COLLATE NOCASE)
                                  ordered_alias), '') AS aliases,
                   i.index_policy_json
            FROM catalog_items i
            LEFT JOIN usage u ON u.stable_id = i.stable_id
            WHERE 1 = 1
              {enabledSourceClause}
              {HomeSearchClause( "i" )}
            ORDER BY COALESCE(u.last_used_unix_ms, 0) DESC, i.title COLLATE NOCASE
            LIMIT $limit;
            """;
        for ( var index = 0; index < sourceIds.Count; index++ )
        {
            command.Parameters.AddWithValue( $"$enabled_source_{index}", sourceIds[index] );
        }

        AddHomeSearchParameters( command );
        command.Parameters.AddWithValue( "$limit", Math.Clamp( limit, 1, 500 ) );

        return command;
    }

    internal static string BuildFtsQuery(
        ParsedQuery query,
        IReadOnlyList<string>? sourceIds = null )
    {
        List<string> fragments = [];
        foreach ( var clause in query.Expression.Clauses.Where( static value =>
            !value.Excluded ) )
        {
            var clean = clause.Text
                .Replace( "\"", "\"\"", StringComparison.Ordinal )
                .Trim( );

            if ( clean.Length == 0 )
            {
                continue;
            }

            var field = clause.Field switch
            {
                QueryFieldIds.Title => "title : ",
                QueryFieldIds.Metadata => "subtitle : ",
                QueryFieldIds.Path => "path : ",
                QueryFieldIds.Content => "body : ",
                QueryFieldIds.Config => "config_keys : ",
                _ => string.Empty
            };
            fragments.Add( $"{field}\"{clean}\"*" );
        }

        var lexical = fragments.Count == 0 ? "\"\"" : string.Join( " AND ", fragments );
        foreach ( var clause in query.Expression.Clauses.Where( static value =>
            value.Excluded ) )
        {
            var clean = clause.Text
                .Replace( "\"", "\"\"", StringComparison.Ordinal )
                .Trim( );
            var field = clause.Field switch
            {
                QueryFieldIds.Title => "title : ",
                QueryFieldIds.Metadata => "subtitle : ",
                QueryFieldIds.Path => "path : ",
                QueryFieldIds.Content => "body : ",
                QueryFieldIds.Config => "config_keys : ",
                _ => string.Empty
            };
            lexical += $" NOT {field}\"{clean}\"*";
        }
        var sources = sourceIds ?? [];
        if ( sources.Count == 0 )
        {
            return lexical;
        }

        var sourceQuery = string.Join(
            " OR ",
            sources.Select( static source =>
                $"source_id : \"{source.Replace( "\"", "\"\"", StringComparison.Ordinal )}\"" ) );
        return $"{lexical} AND ({sourceQuery})";
    }

    private static bool MatchesFilterExpression( ParsedQuery query, SearchResult result ) =>
        (query.IncludedFilters.Count == 0 || query.IncludedFilters.Any( filter =>
            SearchFilterRegistry.Matches( filter, result ) )) &&
        !query.ExcludedFilters.Any( filter => SearchFilterRegistry.Matches( filter, result ) );

    private static double RankToScore( double rank )
    {
        var magnitude = Math.Abs( rank );
        return Math.Clamp( magnitude / (1.0 + magnitude), 0.2, 1.0 );
    }

    private double AdaptiveScore(
        string resultKind,
        long useCount,
        long lastUsedUnixMilliseconds )
    {
        if ( !_learnFromInvocations || !_privacyOptions.StoreUsageHistory || !_rankingOptions.Adaptive.Enabled ||
            lastUsedUnixMilliseconds < AdaptiveCutoffUnixMilliseconds( ) )
        {
            return 0;
        }
        return TimeAwareRankingPolicy.Score(
            resultKind,
            useCount,
            lastUsedUnixMilliseconds,
            _timeProvider.GetUtcNow( ),
            _rankingOptions.TimeAware );
    }

    private long AdaptiveCutoffUnixMilliseconds( ) =>
        SqliteUsageHistoryStore.AdaptiveCutoffUnixMilliseconds(
            _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ), _rankingOptions );
}
