namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CatalogSearchPreservesProjectionAcrossQueryShapesAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-search-projection-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database },
                searchOptions: new SearchOptions { LearnFromInvocations = false } );
            await catalog.ReplaceAllAsync( CatalogSearchProjectionItems( ), null, CancellationToken.None );
            var initial = (await catalog.SearchAsync(
                QueryParser.Parse( "alpha" ), 10, CancellationToken.None )).Single( );
            await catalog.SetFavoriteAsync( initial, true, CancellationToken.None );
            await catalog.SetAliasAsync( initial, "alpha-shortcut", CancellationToken.None );

            foreach ( var query in new[] { "alpha", "", "alhpa" } )
            {
                var results = await catalog.SearchAsync(
                    QueryParser.Parse( query ), 10, CancellationToken.None );
                var result = results.Single( value => value.Id == "configuration:projection" );
                Equal( ResultKinds.Configuration, result.Kind );
                Equal( "Alpha configuration", result.Title );
                Equal( "Metadata alpha marker", result.Subtitle );
                Equal( "projection-icon", result.Icon );
                Equal( "projection-provider", result.ProviderId );
                Equal( SourceIds.Configuration, result.SourceId );
                Equal( "/tmp/alpha-projection.conf", result.Metadata["path"] );
                Equal( "projection-value", result.Metadata["value"] );
                True( result.IsFavorite );
                True( result.Aliases.SequenceEqual( ["alpha-shortcut"] ) );
                Equal( "37:4", result.Details.Single( value => value.Label == "Key location" ).Value );
                var edit = result.Actions.Single( value => value.Kind == ActionKinds.EditAtLine );
                Equal( "37", edit.Arguments["line"] );
                Equal( "4", edit.Arguments["column"] );

                Equal( 2, result.Ranking.Policies.Count );
                var titlePolicy = result.Ranking.Policies.Single( value => value.Field == RankingFieldNames.Title );
                True( titlePolicy.Included );
                Equal( "file-type-override", titlePolicy.Reason );
                Equal( "*.conf", titlePolicy.Rule );
                var contentPolicy = result.Ranking.Policies.Single( value => value.Field == RankingFieldNames.Content );
                True( contentPolicy.Included );
                Equal( "root-default", contentPolicy.Reason );
                True( contentPolicy.Rule is null );
                Equal( result.Score, result.Ranking.FinalScore );

                if ( query.Length == 0 )
                {
                    Equal( 0.25, result.Score );
                    Equal( 0, result.Ranking.Matches.Count );
                }
                else if ( query == "alpha" )
                {
                    AssertCatalogProjectionMatch( result, RankingFieldNames.Title, "Alpha configuration", 0 );
                    AssertCatalogProjectionMatch( result, RankingFieldNames.Metadata, "Metadata alpha marker", 9 );
                    AssertCatalogProjectionMatch( result, RankingFieldNames.Path, "/tmp/alpha-projection.conf", 5 );
                    AssertCatalogProjectionMatch( result, RankingFieldNames.Content, "Body alpha marker", 5 );
                    AssertCatalogProjectionMatch( result, RankingFieldNames.Config, "Key alpha marker", 4 );
                }
                else
                {
                    Equal( "Bounded fuzzy fallback", result.Ranking.Contributions.Single( ).Label );
                    True( result.MatchedRanges.Count > 0 );
                }
            }

            foreach ( var query in new[] { "bravo", "", "brvao" } )
            {
                var results = await catalog.SearchAsync(
                    QueryParser.Parse( query ), 10, CancellationToken.None );
                var result = results.Single( value => value.Id == "file:nullable-projection" );
                True( result.Subtitle is null && result.Icon is null );
                True( !result.Metadata.ContainsKey( "path" ) );
                Equal( 0, result.Actions.Count );
                True( !result.IsFavorite );
                Equal( 0, result.Aliases.Count );
                Equal( 0, result.Ranking.Policies.Count );
            }
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static void AssertCatalogProjectionMatch(
        SearchResult result,
        string field,
        string value,
        int start )
    {
        var match = result.Ranking.Matches.Single( candidate => candidate.Field == field );
        Equal( value, match.Value );
        True( match.Ranges.SequenceEqual( [new MatchedRange( start, 5 )] ) );
    }

    private static async IAsyncEnumerable<IndexedItem> CatalogSearchProjectionItems( )
    {
        yield return new IndexedItem
        {
            StableId = "configuration:projection",
            Kind = ResultKinds.Configuration,
            Title = "Alpha configuration",
            Subtitle = "Metadata alpha marker",
            Path = "/tmp/alpha-projection.conf",
            Icon = "projection-icon",
            Body = "Body alpha marker",
            ConfigKeys = "Key alpha marker",
            PayloadJson = PayloadWriter.Create(
                ("key", "projectionKey"), ("value", "projection-value"),
                ("keyLine", "37"), ("keyColumn", "4") ),
            ProviderId = "projection-provider",
            SourceId = SourceIds.Configuration,
            IndexPolicies =
            [
                new IndexFieldPolicy
                {
                    Field = RankingFieldNames.Title,
                    Included = true,
                    Reason = "file-type-override",
                    Rule = "*.conf"
                },
                new IndexFieldPolicy
                {
                    Field = RankingFieldNames.Content,
                    Included = true,
                    Reason = "root-default"
                }
            ]
        };
        yield return new IndexedItem
        {
            StableId = "file:nullable-projection",
            Kind = ResultKinds.File,
            Title = "Bravo",
            ProviderId = "nullable-provider",
            SourceId = SourceIds.Files
        };
        await Task.CompletedTask;
    }
}
