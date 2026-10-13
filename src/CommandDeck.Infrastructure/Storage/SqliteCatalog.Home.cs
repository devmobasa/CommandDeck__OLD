using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private const int MaximumHomeBookmarks = 8;
    private const int MaximumRecentFrequentResults = 6;
    private const int MaximumOmarchyHomeResults = 2;
    private static readonly string[] CustomizableItemTables = ["catalog_items", "extension_commands"];

    public async ValueTask<IReadOnlyList<SearchResult>> GetHomeResultsAsync(
        LauncherPreferences preferences,
        int limit,
        CancellationToken cancellationToken )
    {
        var enabled = preferences.Sources
            .Where( static value => value.EnabledOnHome )
            .Select( static value => value.SourceId )
            .ToHashSet( StringComparer.Ordinal );
        if ( enabled.Count == 0 )
        {
            return [];
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        using var interrupt = InterruptOnCancellation( connection, cancellationToken );

        List<(SearchResult Result, long Uses, long LastUsed, int? FavoritePosition)> candidates;
        try
        {
            candidates = await ReadHomeCandidatesAsync( connection, preferences, enabled, cancellationToken )
                .ConfigureAwait( false );
        }
        catch ( SqliteException exception ) when ( WasInterrupted( exception, cancellationToken ) )
        {
            throw new OperationCanceledException( "The Home query was cancelled.", exception, cancellationToken );
        }

        HashSet<string> selected = new( StringComparer.Ordinal );
        List<SearchResult> home = [];
        foreach ( var candidate in candidates
            .Where( static value => value.FavoritePosition is not null )
            .OrderBy( static value => value.FavoritePosition )
            .Take( MaximumHomeBookmarks ) )
        {
            selected.Add( candidate.Result.Id );
            home.Add( WithSection( candidate.Result, "bookmarks", "Bookmarks", 1 ) );
        }

        foreach ( var candidate in candidates
            .Where( value => value.Uses > 0 &&
                value.Result.SourceId != SourceIds.OmarchyControl &&
                !selected.Contains( value.Result.Id ) )
            .OrderByDescending( static value => value.Result.Score )
            .ThenByDescending( static value => value.LastUsed )
            .ThenBy( static value => value.Result.Title, StringComparer.OrdinalIgnoreCase )
            .Take( MaximumRecentFrequentResults ) )
        {
            selected.Add( candidate.Result.Id );
            home.Add( WithSection( candidate.Result, "recent-frequent", "Recent & Frequent", 2 ) );
        }

        foreach ( var candidate in candidates
            .Where( value => value.Uses > 0 &&
                value.Result.SourceId == SourceIds.OmarchyControl &&
                !selected.Contains( value.Result.Id ) )
            .OrderByDescending( static value => value.Result.Score )
            .ThenByDescending( static value => value.LastUsed )
            .Take( MaximumOmarchyHomeResults ) )
        {
            selected.Add( candidate.Result.Id );
            home.Add( WithSection( candidate.Result, "omarchy", "Omarchy", 4 ) );
        }

        return home.Take( Math.Clamp( limit, 1, 500 ) ).ToArray( );
    }

    private async ValueTask<List<(SearchResult Result, long Uses, long LastUsed, int? FavoritePosition)>>
        ReadHomeCandidatesAsync(
            SqliteConnection connection,
            LauncherPreferences preferences,
            HashSet<string> enabled,
            CancellationToken cancellationToken )
    {
        await using var command = CreateHomeResultsCommand( connection, preferences, enabled );

        List<(SearchResult Result, long Uses, long LastUsed, int? FavoritePosition)> candidates = [];
        await using var reader =
            await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var sourceId = reader.GetString( 8 );
            if ( !enabled.Contains( sourceId ) )
            {
                continue;
            }

            var uses = reader.GetInt64( 9 );
            var lastUsed = reader.GetInt64( 10 );
            int? position = reader.IsDBNull( 11 ) ? null : reader.GetInt32( 11 );
            candidates.Add( (
                ReadCustomizationSearchResult(
                    reader,
                    AdaptiveScore( reader.GetString( 1 ), uses, lastUsed ),
                    exactAlias: null,
                    favoriteIndex: position is null ? null : 11,
                    aliasIndex: 12 ),
                uses,
                lastUsed,
                position) );
        }

        return candidates;
    }

    // Specs run EXPLAIN QUERY PLAN over this command to prove Home never scans the catalog.
    internal SqliteCommand CreateHomeResultsCommand(
        SqliteConnection connection,
        LauncherPreferences preferences,
        IReadOnlyCollection<string> enabledSources )
    {
        var command = connection.CreateCommand( );
        var sourceParameters = string.Join(
            ", ",
            enabledSources.Select( ( _, index ) => $"$home_source_{index}" ) );
        command.CommandText = $"""
            WITH eligible_usage AS MATERIALIZED (
                {EligibleItemRows(
                    "u.stable_id, u.use_count, u.last_used_unix_ms, used_item.source_id, used_item.kind",
                    "usage u",
                    "used_item",
                    sourceParameters,
                    preferences.SearchHomeOnly )}
            ),
            ranked_usage AS (
                SELECT eligible.stable_id, eligible.source_id,
                       commanddeck_frecency(
                           eligible.use_count,
                           eligible.last_used_unix_ms,
                           $current_unix_milliseconds) *
                       CASE
                           WHEN eligible.kind = $application_kind THEN $application_multiplier
                           WHEN eligible.kind IN ($file_kind, $configuration_kind)
                               THEN $file_multiplier
                           WHEN eligible.kind IN ($workspace_kind, $window_kind)
                               THEN $workspace_multiplier
                           WHEN eligible.kind IN ($omarchy_kind, $command_kind, $quicklink_kind)
                               THEN $action_multiplier
                           ELSE 0
                       END AS frecency
                FROM eligible_usage eligible
                WHERE $adaptive_enabled = 1
                  AND eligible.last_used_unix_ms >= $adaptive_cutoff
            ),
            ranked_favorites AS MATERIALIZED (
                {EligibleItemRows(
                    "f.stable_id, f.position",
                    "favorites f",
                    "favorite_item",
                    sourceParameters,
                    preferences.SearchHomeOnly )}
            ),
            candidate_ids AS (
                SELECT f0.stable_id
                FROM (
                    SELECT stable_id, position
                    FROM ranked_favorites
                    ORDER BY position
                    LIMIT $bookmark_candidate_limit
                ) f0
                UNION
                SELECT regular.stable_id
                FROM (
                    SELECT stable_id, frecency
                    FROM ranked_usage
                    WHERE source_id <> $omarchy_source
                    ORDER BY frecency DESC
                    LIMIT $regular_candidate_limit
                ) regular
                UNION
                SELECT omarchy.stable_id
                FROM (
                    SELECT stable_id, frecency
                    FROM ranked_usage
                    WHERE source_id = $omarchy_source
                    ORDER BY frecency DESC
                    LIMIT $omarchy_candidate_limit
                ) omarchy
            )
            SELECT *
            FROM (
                {string.Join( "\nUNION ALL\n", CustomizableItemTables.Select( CandidateRows ) )}
            ) candidate
            ORDER BY CASE WHEN favorite_position IS NULL THEN 1 ELSE 0 END,
                     favorite_position,
                     last_used_unix_ms DESC
            ;
            """;
        AddHomeSearchParameters( command );
        var sourceIndex = 0;
        foreach ( var sourceId in enabledSources )
        {
            command.Parameters.AddWithValue( $"$home_source_{sourceIndex}", sourceId );
            sourceIndex++;
        }
        command.Parameters.AddWithValue(
            "$current_unix_milliseconds",
            _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) );
        command.Parameters.AddWithValue(
            "$adaptive_enabled",
            _rankingOptions.Adaptive.Enabled && _rankingOptions.TimeAware.Enabled ? 1 : 0 );
        command.Parameters.AddWithValue( "$adaptive_cutoff", AdaptiveCutoffUnixMilliseconds( ) );
        command.Parameters.AddWithValue( "$omarchy_source", SourceIds.OmarchyControl );
        command.Parameters.AddWithValue( "$application_kind", ResultKinds.Application );
        command.Parameters.AddWithValue( "$file_kind", ResultKinds.File );
        command.Parameters.AddWithValue( "$configuration_kind", ResultKinds.Configuration );
        command.Parameters.AddWithValue( "$workspace_kind", ResultKinds.Workspace );
        command.Parameters.AddWithValue( "$window_kind", ResultKinds.Window );
        command.Parameters.AddWithValue( "$omarchy_kind", ResultKinds.Omarchy );
        command.Parameters.AddWithValue( "$command_kind", ResultKinds.CustomCommand );
        command.Parameters.AddWithValue( "$quicklink_kind", ResultKinds.Quicklink );
        command.Parameters.AddWithValue(
            "$application_multiplier",
            _rankingOptions.TimeAware.ApplicationMultiplier );
        command.Parameters.AddWithValue(
            "$file_multiplier",
            _rankingOptions.TimeAware.FileMultiplier );
        command.Parameters.AddWithValue(
            "$workspace_multiplier",
            _rankingOptions.TimeAware.WorkspaceMultiplier );
        command.Parameters.AddWithValue(
            "$action_multiplier",
            _rankingOptions.TimeAware.ActionMultiplier );
        command.Parameters.AddWithValue( "$bookmark_candidate_limit", MaximumHomeBookmarks );
        command.Parameters.AddWithValue(
            "$regular_candidate_limit",
            MaximumHomeBookmarks + MaximumRecentFrequentResults );
        command.Parameters.AddWithValue(
            "$omarchy_candidate_limit",
            MaximumHomeBookmarks + MaximumOmarchyHomeResults );

        return command;
    }

    // Column order is fixed by ReadCustomizationSearchResult: 0-8 item fields, 9 use
    // count, 10 last used, 11 favorite position, 12 aliases.
    private static string CandidateRows( string table ) => $"""
        SELECT i.stable_id, i.kind, i.title, i.subtitle, i.path, i.icon,
               i.payload_json, i.provider_id, i.source_id,
               COALESCE(u.use_count, 0) AS use_count,
               COALESCE(u.last_used_unix_ms, 0) AS last_used_unix_ms,
               f.position AS favorite_position,
               COALESCE((SELECT group_concat(ordered_alias.alias, char(31))
                         FROM (SELECT a.alias FROM aliases a
                               WHERE a.stable_id = i.stable_id
                               ORDER BY a.created_unix_ms, a.alias COLLATE NOCASE)
                              ordered_alias), '') AS alias_list
        FROM candidate_ids c
        JOIN {table} i ON i.stable_id = c.stable_id
        LEFT JOIN usage u ON u.stable_id = i.stable_id
        LEFT JOIN favorites f ON f.stable_id = i.stable_id
        WHERE f.stable_id IS NOT NULL OR COALESCE(u.use_count, 0) > 0
        """;

    // customizable_items is a UNION ALL view; joining it materializes both member
    // tables (plus an automatic index) on every Home open. Probing each member by
    // primary key keeps the Home query proportional to usage and favorites, not to
    // the catalog.
    private static string EligibleItemRows(
        string columns,
        string sourceTable,
        string itemAlias,
        string sourceParameters,
        bool homeOnly )
    {
        var sourceAlias = sourceTable[(sourceTable.LastIndexOf( ' ' ) + 1)..];

        return string.Join(
            "\nUNION ALL\n",
            CustomizableItemTables.Select( table => $"""
                SELECT {columns}
                FROM {sourceTable}
                CROSS JOIN {table} {itemAlias}
                WHERE {itemAlias}.stable_id = {sourceAlias}.stable_id
                  AND {itemAlias}.source_id IN ({sourceParameters})
                  {HomeSearchClause( itemAlias, homeOnly )}
                """ ) );
    }

    public ValueTask<CustomizationExport> ExportAsync( CancellationToken cancellationToken ) =>
        _launcherPreferences.ExportAsync( cancellationToken );

    public ValueTask<LauncherPreferences> ResetAsync( CancellationToken cancellationToken ) =>
        _launcherPreferences.ResetAsync( cancellationToken );

    internal static SearchResult ReadCustomizationSearchResult(
        SqliteDataReader reader,
        double score,
        string? exactAlias,
        int? favoriteIndex = 9,
        int aliasIndex = 10 )
    {
        var favorite = favoriteIndex is not null && !reader.IsDBNull( favoriteIndex.Value ) &&
                       reader.GetInt64( favoriteIndex.Value ) != 0;
        return CreateCatalogSearchResult(
            reader.GetString( 0 ),
            reader.GetString( 1 ),
            reader.GetString( 2 ),
            reader.IsDBNull( 3 ) ? null : reader.GetString( 3 ),
            reader.IsDBNull( 4 ) ? null : reader.GetString( 4 ),
            reader.IsDBNull( 5 ) ? null : reader.GetString( 5 ),
            reader.IsDBNull( 6 ) ? null : reader.GetString( 6 ),
            reader.GetString( 7 ),
            reader.GetString( 8 ),
            score,
            favorite,
            SplitAliases( reader.GetString( aliasIndex ) ),
            exactAlias );
    }

    private static SearchResult WithSection( SearchResult result, string id, string title, int order ) => new( result )
    {
        Section = new ResultSection { Id = id, Title = title, Order = order },
    };

}
