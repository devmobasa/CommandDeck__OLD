namespace CommandDeck;

public readonly record struct FileIndexPolicy(
    bool IncludeFileName,
    bool IncludeMetadata,
    bool IncludeContent,
    long? MaximumFileBytes,
    int? MaximumExtractedBytes,
    int? ExtractorTimeoutMilliseconds,
    long? ExtractorMemoryLimitBytes,
    IReadOnlyList<IndexFieldPolicy> Provenance );

public static class FileIndexPolicyResolver
{
    public static FileIndexPolicy Resolve( IndexRootOptions root, string path )
    {
        ArgumentNullException.ThrowIfNull( root );
        var fileName = Path.GetFileName( path );
        var fileNameSearch = root.IncludeFileNameInSearch;
        var metadataSearch = root.IncludeMetadataInSearch;
        var contentSearch = root.IncludeContentInSearch;
        long? maximumFileBytes = null;
        int? maximumExtractedBytes = null;
        int? extractorTimeoutMilliseconds = null;
        long? extractorMemoryLimitBytes = null;
        string? fileNameRule = null;
        string? metadataRule = null;
        string? contentRule = null;

        foreach ( var rule in root.FileTypePolicies )
        {
            if ( !PathPolicy.MatchesAny( fileName, [rule.Pattern] ) )
            {
                continue;
            }

            if ( rule.IncludeFileNameInSearch is not null )
            {
                fileNameSearch = rule.IncludeFileNameInSearch.Value;
                fileNameRule = rule.Pattern;
            }
            if ( rule.IncludeMetadataInSearch is not null )
            {
                metadataSearch = rule.IncludeMetadataInSearch.Value;
                metadataRule = rule.Pattern;
            }
            if ( rule.IncludeContentInSearch is not null )
            {
                contentSearch = rule.IncludeContentInSearch.Value;
                contentRule = rule.Pattern;
            }
            maximumFileBytes = rule.MaximumFileBytes ?? maximumFileBytes;
            maximumExtractedBytes = rule.MaximumExtractedBytes ?? maximumExtractedBytes;
            extractorTimeoutMilliseconds =
                rule.ExtractorTimeoutMilliseconds ?? extractorTimeoutMilliseconds;
            extractorMemoryLimitBytes =
                rule.ExtractorMemoryLimitBytes ?? extractorMemoryLimitBytes;
        }

        return new FileIndexPolicy(
            fileNameSearch,
            metadataSearch,
            contentSearch,
            maximumFileBytes,
            maximumExtractedBytes,
            extractorTimeoutMilliseconds,
            extractorMemoryLimitBytes,
            [
                Describe(RankingFieldNames.Title, fileNameSearch, fileNameRule),
                Describe(RankingFieldNames.Path, metadataSearch, metadataRule),
                Describe(RankingFieldNames.Metadata, metadataSearch, metadataRule),
                Describe(RankingFieldNames.Content, contentSearch, contentRule),
                Describe(RankingFieldNames.Config, contentSearch, contentRule)
            ] );
    }

    private static IndexFieldPolicy Describe(
        string field,
        bool included,
        string? rule ) => new( )
        {
            Field = field,
            Included = included,
            Reason = rule is null ? "root-default" : "file-type-override",
            Rule = rule
        };
}
