namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FileTypePoliciesSeparateSearchFieldsAsync( )
    {
        var temporary = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-index-policy-{Guid.NewGuid( ):N}" );
        var root = Path.Combine( temporary, "root" );
        var metadataDirectory = Path.Combine( root, "UniqueMetadataMarker" );
        Directory.CreateDirectory( metadataDirectory );
        await File.WriteAllTextAsync(
            Path.Combine( root, "UniqueFilenameMarker.name.txt" ),
            "ContentMustNotLeak" );
        await File.WriteAllTextAsync(
            Path.Combine( metadataDirectory, "opaque.meta.txt" ),
            "MetadataBodyMustNotLeak" );
        await File.WriteAllTextAsync(
            Path.Combine( root, "opaque.body.txt" ),
            "UniqueBodyMarker" );
        await File.WriteAllTextAsync( Path.Combine( root, "glob-private.txt" ), "glob" );
        await File.WriteAllTextAsync( Path.Combine( root, ".hidden.txt" ), "hidden" );
        await File.WriteAllTextAsync(
            Path.Combine( root, "privacy-marker.secret.txt" ),
            "PrivateContentMarker" );
        await File.WriteAllTextAsync(
            Path.Combine( root, "oversized.txt" ),
            "This content is over the configured limit" );
        await File.WriteAllTextAsync(
            Path.Combine( root, "settings.conf" ),
            "theme = nord\n" );

        IndexRootOptions rootOptions = new( )
        {
            Path = root,
            IncludeFileNameInSearch = false,
            IncludeMetadataInSearch = false,
            IncludeContentInSearch = false,
            ExcludeGlobs = ["glob-*"],
            FileTypePolicies =
            [
                new FileTypeIndexPolicyOptions
                {
                    Pattern = "*.name.txt",
                    IncludeFileNameInSearch = true
                },
                new FileTypeIndexPolicyOptions
                {
                    Pattern = "*.meta.txt",
                    IncludeMetadataInSearch = true
                },
                new FileTypeIndexPolicyOptions
                {
                    Pattern = "*.body.txt",
                    IncludeContentInSearch = true
                },
                new FileTypeIndexPolicyOptions
                {
                    Pattern = "oversized.txt",
                    IncludeContentInSearch = true
                },
                new FileTypeIndexPolicyOptions
                {
                    Pattern = "*.conf",
                    IncludeFileNameInSearch = true,
                    IncludeMetadataInSearch = true,
                    IncludeContentInSearch = true
                }
            ]
        };
        IndexingOptions indexing = new( )
        {
            Roots = [rootOptions],
            MaximumTextFileBytes = 32,
            IndexDesktopApplications = false,
            IndexOmarchyCommands = false
        };
        FileIndexSource source = new( indexing, new PrivacyOptions
        {
            NeverIndex = ["**/*privacy-marker*"]
        } );
        SqliteCatalog catalog = new( new StorageOptions
        {
            DatabasePath = Path.Combine( temporary, "catalog.db" )
        } );

        try
        {
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceGenerationAsync(
                source.ScanAsync( progress: null, CancellationToken.None ),
                source.SnapshotDiagnostics,
                progress: null,
                CancellationToken.None );

            var filename = await catalog.SearchAsync(
                QueryParser.Parse( "UniqueFilenameMarker" ),
                10,
                CancellationToken.None );
            var metadata = await catalog.SearchAsync(
                QueryParser.Parse( "UniqueMetadataMarker" ),
                10,
                CancellationToken.None );
            var content = await catalog.SearchAsync(
                QueryParser.Parse( "UniqueBodyMarker" ),
                10,
                CancellationToken.None );
            var blockedContent = await catalog.SearchAsync(
                QueryParser.Parse( "ContentMustNotLeak" ),
                10,
                CancellationToken.None );

            Equal( "UniqueFilenameMarker.name.txt", filename.Single( ).Title );
            Equal( "opaque.meta.txt", metadata.Single( ).Title );
            Equal( "opaque.body.txt", content.Single( ).Title );
            Equal( 0, blockedContent.Count );
            var filenameResult = filename.Single( );
            Equal( 5, filenameResult.Ranking.Policies.Count );
            var titlePolicy = filenameResult.Ranking.Policies.Single( value =>
                value.Field == RankingFieldNames.Title );
            True( titlePolicy.Included );
            Equal( "file-type-override", titlePolicy.Reason );
            Equal( "*.name.txt", titlePolicy.Rule );
            var contentPolicy = filenameResult.Ranking.Policies.Single( value =>
                value.Field == RankingFieldNames.Content );
            True( !contentPolicy.Included );
            Equal( "root-default", contentPolicy.Reason );
            var pathPolicy = filenameResult.Ranking.Policies.Single( value =>
                value.Field == RankingFieldNames.Path );
            True( !pathPolicy.Included );
            var configPolicy = filenameResult.Ranking.Policies.Single( value =>
                value.Field == RankingFieldNames.Config );
            True( !configPolicy.Included );

            var configuration = (await catalog.SearchAsync(
                QueryParser.Parse( "cfg: theme" ),
                10,
                CancellationToken.None )).Single( );
            Equal( ResultKinds.Configuration, configuration.Kind );
            Equal( 5, configuration.Ranking.Policies.Count );
            True( configuration.Ranking.Policies.Single( value =>
                value.Field == RankingFieldNames.Path ).Included );
            True( configuration.Ranking.Policies.Single( value =>
                value.Field == RankingFieldNames.Config ).Included );
            True( !System.Text.Json.JsonSerializer.Serialize(
                    filenameResult,
                    CommandDeckJsonContext.Default.SearchResult )
                .Contains( "PrivateContentMarker", StringComparison.Ordinal ) );

            var inspection = await catalog.InspectAsync( CancellationToken.None );
            True( inspection.ExclusionDiagnostics.Any( value =>
                value.Reason == IndexExclusionReasons.RootGlob && value.Rule == "glob-*" ) );
            True( inspection.ExclusionDiagnostics.Any( value =>
                value.Reason == IndexExclusionReasons.Hidden ) );
            True( inspection.ExclusionDiagnostics.Any( value =>
                value.Reason == IndexExclusionReasons.SizeLimit &&
                value.Rule == "maximumBytes=32" ) );
            var privacy = inspection.ExclusionDiagnostics.Single( value =>
                value.Reason == IndexExclusionReasons.PrivacyRule );
            Equal( "[redacted]", privacy.RelativePath );
            True( !privacy.RelativePath.Contains( "privacy-marker", StringComparison.Ordinal ) );
            var audit = await catalog.AuditPrivacyAsync( CancellationToken.None );
            True( audit.Succeeded );
            Equal( 0L, audit.PolicyViolationCount );
            Equal( 0L, audit.UnsafeDiagnosticCount );
            True( !System.Text.Json.JsonSerializer.Serialize(
                    audit,
                    CommandDeckJsonContext.Default.PrivacyAuditResult )
                .Contains( "PrivateContentMarker", StringComparison.Ordinal ) );
        }
        finally
        {
            source.Dispose( );
            await catalog.DisposeAsync( );
            Directory.Delete( temporary, recursive: true );
        }
    }
}
