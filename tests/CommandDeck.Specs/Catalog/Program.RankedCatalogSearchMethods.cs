using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    // The ranked catalog query must return exactly what the single statement it
    // replaced returned: the same rows in the same order, with the same
    // highlights, favorites and aliases. The fixture repeats token shapes so bm25
    // ties are common, gives many rows one of a few last-use times, hides rows
    // near the top, and runs every case with the Home-only scope off and on.
    private static async Task RankedCatalogSearchMatchesTheSingleStatementQueryAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-ranked-equivalence-{Guid.NewGuid( ):N}.db" );
        var home = PathExpansion.Expand( "~" );
        if ( CanonicalPathBoundary.TryCreate( home, out var boundary ) )
        {
            home = boundary!.CanonicalRoot;
        }

        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( RankedEquivalenceItems( home ), null, default );
            await SeedRankedEquivalenceStateAsync( database );
            var favorite = (await catalog.SearchAsync( QueryParser.Parse( "readme 3" ), 1, default )).Single( );
            await catalog.SetFavoriteAsync( favorite, true, default );
            await catalog.SetAliasAsync( favorite, "rdm", default );

            var defaults = await catalog.GetPreferencesAsync( default );
            var enabledSources = defaults.Sources
                .Where( static source => source.EnabledInSearch )
                .Select( static source => source.SourceId )
                .Where( static source => source is not SourceIds.WindowsWorkspaces and not SourceIds.Herdr )
                .ToArray( );
            string[][] sourceSets = [[], [SourceIds.Files], [SourceIds.Applications, SourceIds.Configuration], enabledSources];
            string[] queries =
            [
                "s", "se", "sa", "st", "r", "re", "rea", "readme", "no", "notes 1", "sy", "system",
                "title:report", "path:share", "-report s", "src", "script sh", "zz", "a"
            ];
            var compared = 0;
            foreach ( var homeOnly in new[] { false, true } )
            {
                await catalog.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = defaults.Sources,
                    SearchHomeOnly = homeOnly
                }, default );
                foreach ( var query in queries )
                {
                    var parsed = QueryParser.Parse( query );
                    foreach ( var sources in sourceSets )
                    {
                        foreach ( var limit in new[] { 10, 50, 210 } )
                        {
                            var expected = await ReadSingleStatementRowsAsync( database, parsed, sources, limit );
                            var actual = await catalog.SearchSourcesAsync( parsed, sources, limit, default );
                            CompareRankedRows( $"{query} [{string.Join( ',', sources )}] limit {limit} homeOnly={homeOnly}", expected, actual, limit );
                            compared++;
                        }
                    }
                }
            }

            True( compared == 2 * queries.Length * sourceSets.Length * 3 );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }

    // While FTS ranks every match, a match's catalog item may be sought only by
    // the Home-only scope; the ranked page's items are read afterwards.
    private static async Task RankedCatalogQuerySeeksItemsOnlyForTheHomeScopeAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-ranked-plan-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( default );
            await using var connection = await catalog.OpenAsync( default );
            await using var command = catalog.CreateRankedMatchCommand( connection, QueryParser.Parse( "s" ), [], 50 );

            var plan = await ExplainQueryPlanAsync( command );

            True( plan.Any( static step => step.Contains( "SCAN catalog_fts VIRTUAL TABLE", StringComparison.Ordinal ) ),
                $"ranking scans FTS: {string.Join( " | ", plan )}" );
            True( plan.Any( static step => step.Contains( "SEARCH scoped USING PRIMARY KEY", StringComparison.Ordinal ) ),
                $"the Home-only scope seeks the item: {string.Join( " | ", plan )}" );
            True( !plan.Any( static step =>
                    step.Contains( "SEARCH i ", StringComparison.Ordinal ) ||
                    step.Contains( "SCAN i", StringComparison.Ordinal ) ||
                    step.Contains( "catalog_items", StringComparison.Ordinal ) ),
                $"no other catalog item access while ranking: {string.Join( " | ", plan )}" );
            True( plan.Any( static step => step.Contains( "SEARCH u USING PRIMARY KEY", StringComparison.Ordinal ) ) );
            True( plan.Any( static step => step.Contains( "SEARCH hidden USING PRIMARY KEY", StringComparison.Ordinal ) ) );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }

    private sealed record SingleStatementRow( string Id, bool IsFavorite, string Aliases, string[] Marks );

    private static void CompareRankedRows(
        string label,
        IReadOnlyList<SingleStatementRow> expected,
        IReadOnlyList<SearchResult> actual,
        int limit )
    {
        // The fuzzy fallback may append rows only after an incomplete ranked page.
        True( actual.Count >= expected.Count && (expected.Count < limit || actual.Count == expected.Count),
            $"{label}: {actual.Count} rows, the single statement returned {expected.Count}" );
        for ( var index = 0; index < expected.Count; index++ )
        {
            var row = expected[index];
            var result = actual[index];
            if ( row.Id != result.Id )
            {
                throw new InvalidOperationException(
                    $"{label}: row {index} is {result.Id}; the single statement returned {row.Id}\n" +
                    $"expected: {string.Join( ", ", expected.Select( static value => value.Id ) )}\n" +
                    $"actual:   {string.Join( ", ", actual.Take( expected.Count ).Select( static value => value.Id ) )}" );
            }

            Equal( row.IsFavorite, result.IsFavorite );
            Equal( row.Aliases, string.Join( '\u001f', result.Aliases ) );
            Equal( DescribeMarkedFields( row.Marks ), DescribeMatchedFields( result ) );
        }
    }

    private static string DescribeMatchedFields( SearchResult result ) => string.Join( " | ", result.Ranking.Matches
        .Select( static match => $"{match.Field}:{match.Value}:{string.Join( ',', match.Ranges.Select( static range => $"{range.Start}+{range.Length}" ) )}" ) );

    private static string DescribeMarkedFields( string[] marks )
    {
        string[] fields = [RankingFieldNames.Title, RankingFieldNames.Metadata, RankingFieldNames.Path, RankingFieldNames.Content, RankingFieldNames.Config];
        List<string> described = [];
        for ( var index = 0; index < fields.Length; index++ )
        {
            System.Text.StringBuilder plain = new( );
            List<string> ranges = [];
            var start = -1;
            foreach ( var character in marks[index] )
            {
                if ( character == '\u0001' )
                {
                    start = plain.Length;
                }
                else if ( character == '\u0002' )
                {
                    if ( start >= 0 && plain.Length > start )
                    {
                        ranges.Add( $"{start}+{plain.Length - start}" );
                    }
                    start = -1;
                }
                else
                {
                    plain.Append( character );
                }
            }

            if ( ranges.Count > 0 )
            {
                described.Add( $"{fields[index]}:{plain}:{string.Join( ',', ranges )}" );
            }
        }

        return string.Join( " | ", described );
    }

    // The statement SqliteCatalog ran before ranking moved inside the FTS scan,
    // kept verbatim as the reference for the equivalence spec.
    private static async Task<List<SingleStatementRow>> ReadSingleStatementRowsAsync(
        string database,
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int limit )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT i.stable_id, i.kind, i.title, i.subtitle, i.path, i.icon,
                   i.payload_json, i.provider_id, i.source_id,
                   COALESCE(u.use_count, 0), COALESCE(u.last_used_unix_ms, 0),
                   bm25(catalog_fts, 0.0, 0.0, 0.0,
                        $title_weight, $metadata_weight, $path_weight,
                        $content_weight, $config_weight) AS rank,
                   EXISTS(SELECT 1 FROM favorites f WHERE f.stable_id = i.stable_id),
                   COALESCE((SELECT group_concat(ordered_alias.alias, char(31))
                             FROM (SELECT a.alias FROM aliases a
                                   WHERE a.stable_id = i.stable_id
                                   ORDER BY a.created_unix_ms, a.alias COLLATE NOCASE)
                                  ordered_alias), ''),
                   highlight(catalog_fts, 3, char(1), char(2)),
                   highlight(catalog_fts, 4, char(1), char(2)),
                   highlight(catalog_fts, 5, char(1), char(2)),
                   snippet(catalog_fts, 6, char(1), char(2), ' … ', 18),
                   snippet(catalog_fts, 7, char(1), char(2), ' … ', 18),
                   i.index_policy_json
            FROM catalog_fts
            JOIN catalog_items i ON i.stable_id = catalog_fts.stable_id
            LEFT JOIN usage u ON u.stable_id = i.stable_id
            WHERE catalog_fts MATCH $fts_query
              AND (EXISTS(SELECT 1 FROM launcher_preferences WHERE key = 'search_home_only' AND value = 'true') = 0
                   OR i.kind NOT IN ('file', 'configuration')
                   OR i.path = $search_home
                   OR substr(i.path, 1, length($search_home_prefix)) = $search_home_prefix)
              AND NOT EXISTS(SELECT 1 FROM hidden_items hidden WHERE hidden.stable_id = i.stable_id)
            ORDER BY rank, COALESCE(u.last_used_unix_ms, 0) DESC
            LIMIT $limit;
            """;
        RankingOptions weights = new( );
        command.Parameters.AddWithValue( "$fts_query", SqliteCatalog.BuildFtsQuery( query, sourceIds ) );
        command.Parameters.AddWithValue( "$title_weight", weights.TitleWeight );
        command.Parameters.AddWithValue( "$metadata_weight", weights.MetadataWeight );
        command.Parameters.AddWithValue( "$path_weight", weights.PathWeight );
        command.Parameters.AddWithValue( "$content_weight", weights.ContentWeight );
        command.Parameters.AddWithValue( "$config_weight", weights.ConfigWeight );
        SqliteCatalog.AddHomeSearchParameters( command );
        command.Parameters.AddWithValue( "$limit", Math.Clamp( limit, 1, 500 ) );

        List<SingleStatementRow> rows = [];
        await using var reader = await command.ExecuteReaderAsync( );
        while ( await reader.ReadAsync( ) )
        {
            rows.Add( new SingleStatementRow(
                reader.GetString( 0 ),
                reader.GetInt64( 12 ) != 0,
                reader.GetString( 13 ),
                [.. Enumerable.Range( 14, 5 ).Select( ordinal => reader.IsDBNull( ordinal ) ? string.Empty : reader.GetString( ordinal ) )] ) );
        }

        return rows;
    }

    // Hidden rows sit among the best matches; many rows share one of four
    // last-use times, so the recency tie-break has ties of its own. Some FTS rows
    // lose their catalog item, as in a projection that repair has not reached.
    private static async Task SeedRankedEquivalenceStateAsync( string database )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var seed = connection.CreateCommand( );
        seed.CommandText = """
            INSERT INTO usage(stable_id, use_count, last_used_unix_ms)
            SELECT stable_id, (length(stable_id) % 5) + 1, 1790000000000 - (length(title) % 4) * 86400000
            FROM catalog_items WHERE (unicode(substr(stable_id, -1)) + length(title)) % 7 = 0;
            INSERT INTO hidden_items(stable_id, title_snapshot, source_id, hidden_unix_ms)
            SELECT stable_id, title, source_id, 1
            FROM catalog_items WHERE (unicode(substr(stable_id, -2, 1)) + length(stable_id)) % 29 = 0;
            DELETE FROM catalog_items
            WHERE (unicode(substr(stable_id, -3, 1)) + length(title)) % 31 = 0;
            """;
        await seed.ExecuteNonQueryAsync( );
    }

    private static async IAsyncEnumerable<IndexedItem> RankedEquivalenceItems( string home )
    {
        string[] words = ["report", "readme", "setup", "notes", "sample", "script", "style", "source", "system", "summary"];
        string[] extensions = ["txt", "md", "sh", "conf"];
        string[] folders = ["src", "share", "docs", "scripts", "settings"];
        for ( var index = 0; index < 6_000; index++ )
        {
            var folder = folders[index % folders.Length];
            var root = index % 2 == 0 ? Path.Combine( home, "Projects" ) : "/srv/data";
            var title = $"{words[index % words.Length]} {index % 50}.{extensions[index % extensions.Length]}";
            yield return new IndexedItem
            {
                StableId = $"file:equivalence-{index}",
                Kind = ResultKinds.File,
                Title = title,
                Subtitle = $"{folder}/{words[(index / 7) % words.Length]}",
                Path = Path.Combine( root, folder, title ),
                Body = index % 3 == 0 ? $"{words[(index / 3) % words.Length]} section {index % 11} of the {folders[(index / 5) % folders.Length]} set" : null,
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files
            };
        }
        for ( var index = 0; index < 40; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"app:equivalence-{index}",
                Kind = ResultKinds.Application,
                Title = $"{words[index % words.Length]} {(index % 2 == 0 ? "studio" : "viewer")}",
                Subtitle = "Application",
                ProviderId = ProviderIds.Applications,
                SourceId = SourceIds.Applications
            };
        }
        for ( var index = 0; index < 60; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"config:equivalence-{index}",
                Kind = ResultKinds.Configuration,
                Title = $"{words[index % words.Length]}_{index % 6}",
                Path = Path.Combine( index % 2 == 0 ? home : "/etc", $"{words[index % words.Length]}.conf" ),
                ConfigKeys = $"{words[(index + 3) % words.Length]} {folders[index % folders.Length]}",
                ProviderId = ProviderIds.Config,
                SourceId = SourceIds.Configuration
            };
        }
        await Task.CompletedTask;
    }
}
