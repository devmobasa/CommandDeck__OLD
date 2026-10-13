using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // Resolve the common projection once, before reading lexical, empty, or fuzzy rows.
    private readonly struct CatalogSearchRowReader( SqliteDataReader reader )
    {
        private readonly int _stableId = reader.GetOrdinal( "stable_id" );
        private readonly int _kind = reader.GetOrdinal( "kind" );
        private readonly int _title = reader.GetOrdinal( "title" );
        private readonly int _subtitle = reader.GetOrdinal( "subtitle" );
        private readonly int _path = reader.GetOrdinal( "path" );
        private readonly int _icon = reader.GetOrdinal( "icon" );
        private readonly int _payload = reader.GetOrdinal( "payload_json" );
        private readonly int _providerId = reader.GetOrdinal( "provider_id" );
        private readonly int _sourceId = reader.GetOrdinal( "source_id" );
        private readonly int _useCount = reader.GetOrdinal( "use_count" );
        private readonly int _lastUsed = reader.GetOrdinal( "last_used_unix_ms" );
        private readonly int _isFavorite = reader.GetOrdinal( "is_favorite" );
        private readonly int _aliases = reader.GetOrdinal( "aliases" );
        private readonly int _indexPolicy = reader.GetOrdinal( "index_policy_json" );
        private readonly int _metadataInSearch = TrySearchOrdinal( reader, "metadata_in_search" );

        public string StableId => reader.GetString( _stableId );
        public string Kind => reader.GetString( _kind );
        public string Title => reader.GetString( _title );
        public string? Subtitle => ReadNullableSearchString( reader, _subtitle );
        public string? Path => ReadNullableSearchString( reader, _path );
        public string? Icon => ReadNullableSearchString( reader, _icon );
        public string? Payload => ReadNullableSearchString( reader, _payload );
        public string ProviderId => reader.GetString( _providerId );
        public string SourceId => reader.GetString( _sourceId );
        public long UseCount => reader.GetInt64( _useCount );
        public long LastUsedUnixMilliseconds => reader.GetInt64( _lastUsed );
        public bool IsFavorite => reader.GetInt64( _isFavorite ) != 0;
        public string Aliases => reader.GetString( _aliases );
        public bool MetadataInSearch => _metadataInSearch < 0 || reader.GetInt64( _metadataInSearch ) != 0;
        public string? IndexPolicyJson => ReadNullableSearchString( reader, _indexPolicy );
    }

    // Only the FTS shape contains a lexical rank and marked field values.
    private readonly struct CatalogLexicalRowReader( SqliteDataReader reader )
    {
        private readonly int _rank = reader.GetOrdinal( "rank" );
        private readonly int _titleMatch = reader.GetOrdinal( "title_match" );
        private readonly int _metadataMatch = reader.GetOrdinal( "metadata_match" );
        private readonly int _pathMatch = reader.GetOrdinal( "path_match" );
        private readonly int _contentMatch = reader.GetOrdinal( "content_match" );
        private readonly int _configMatch = reader.GetOrdinal( "config_match" );

        public double Rank => reader.GetDouble( _rank );
        public string TitleMatch => ReadNullableSearchString( reader, _titleMatch ) ?? string.Empty;
        public string MetadataMatch => ReadNullableSearchString( reader, _metadataMatch ) ?? string.Empty;
        public string PathMatch => ReadNullableSearchString( reader, _pathMatch ) ?? string.Empty;
        public string ContentMatch => ReadNullableSearchString( reader, _contentMatch ) ?? string.Empty;
        public string ConfigMatch => ReadNullableSearchString( reader, _configMatch ) ?? string.Empty;
    }

    private static int TrySearchOrdinal( SqliteDataReader reader, string name )
    {
        for ( var index = 0; index < reader.FieldCount; index++ )
        {
            if ( reader.GetName( index ) == name )
            {
                return index;
            }
        }

        return -1;
    }

    private static string? ReadNullableSearchString( SqliteDataReader reader, int ordinal ) =>
        reader.IsDBNull( ordinal ) ? null : reader.GetString( ordinal );

    private static RankingExplanation BuildCatalogRanking(
        CatalogLexicalRowReader row,
        string resultKind,
        double lexicalScore,
        double frecencyBoost,
        double finalScore,
        TimeAwareRankingOptions timeAware )
    {
        List<RankingFieldMatch> matches = [];
        AddMarkedMatch( matches, RankingFieldNames.Title, row.TitleMatch );
        AddMarkedMatch( matches, RankingFieldNames.Metadata, row.MetadataMatch );
        AddMarkedMatch( matches, RankingFieldNames.Path, row.PathMatch );
        AddMarkedMatch( matches, RankingFieldNames.Content, row.ContentMatch );
        AddMarkedMatch( matches, RankingFieldNames.Config, row.ConfigMatch );

        List<RankingContribution> contributions =
        [
            new(RankingContributionKinds.Lexical, "Catalog lexical match", lexicalScore)
        ];
        if ( frecencyBoost > 0 )
        {
            var multiplier = TimeAwareRankingPolicy.Multiplier( resultKind, timeAware );
            contributions.Add( new(
                RankingContributionKinds.Frecency,
                $"Recent {TimeAwareRankingPolicy.Category( resultKind )} use ×{multiplier:0.##}",
                frecencyBoost ) );
            contributions.Add( new(
                RankingContributionKinds.Adaptive,
                "Adaptive ranking policy",
                0 ) );
        }

        return new RankingExplanation
        {
            FinalScore = finalScore,
            Contributions = contributions,
            Matches = matches
        };
    }

    private static RankingExplanation BuildCatalogRanking(
        string[] marks,
        string resultKind,
        double lexicalScore,
        double frecencyBoost,
        double finalScore,
        TimeAwareRankingOptions timeAware )
    {
        List<RankingFieldMatch> matches = [];
        AddMarkedMatch( matches, RankingFieldNames.Title, marks[0] );
        AddMarkedMatch( matches, RankingFieldNames.Metadata, marks[1] );
        AddMarkedMatch( matches, RankingFieldNames.Path, marks[2] );
        AddMarkedMatch( matches, RankingFieldNames.Content, marks[3] );
        AddMarkedMatch( matches, RankingFieldNames.Config, marks[4] );
        List<RankingContribution> contributions =
        [
            new(RankingContributionKinds.Lexical, "Catalog lexical match", lexicalScore)
        ];
        if ( frecencyBoost > 0 )
        {
            var multiplier = TimeAwareRankingPolicy.Multiplier( resultKind, timeAware );
            contributions.Add( new(
                RankingContributionKinds.Frecency,
                $"Recent {TimeAwareRankingPolicy.Category( resultKind )} use ×{multiplier:0.##}",
                frecencyBoost ) );
            contributions.Add( new(
                RankingContributionKinds.Adaptive,
                "Adaptive ranking policy",
                0 ) );
        }

        return new RankingExplanation
        {
            FinalScore = finalScore,
            Contributions = contributions,
            Matches = matches
        };
    }

    private static void AddMarkedMatch(
        List<RankingFieldMatch> matches,
        string field,
        string marked )
    {
        const char startMarker = '\u0001';
        const char endMarker = '\u0002';
        List<MatchedRange> ranges = [];
        System.Text.StringBuilder plain = new( marked.Length );
        int? rangeStart = null;
        foreach ( var character in marked )
        {
            if ( character == startMarker )
            {
                rangeStart = plain.Length;
            }
            else if ( character == endMarker )
            {
                if ( rangeStart is int start && plain.Length > start )
                {
                    ranges.Add( new MatchedRange( start, plain.Length - start ) );
                }

                rangeStart = null;
            }
            else
            {
                plain.Append( character );
            }
        }

        if ( ranges.Count > 0 )
        {
            matches.Add( new RankingFieldMatch
            {
                Field = field,
                Value = plain.ToString( ),
                Ranges = ranges
            } );
        }
    }

    private static List<IndexFieldPolicy> ReadIndexPolicies( CatalogSearchRowReader row )
    {
        var json = row.IndexPolicyJson;
        if ( json is null )
        {
            return [];
        }

        return System.Text.Json.JsonSerializer.Deserialize(
            json,
            CommandDeckJsonContext.Default.ListIndexFieldPolicy ) ?? [];
    }
}
