using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task VersionedRankingCorpusMeetsQualityThresholdsAsync( )
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "ranking-corpus-v1.json" );
        using var document = JsonDocument.Parse( File.ReadAllText( path ) );
        var root = document.RootElement;
        Equal( 4, root.GetProperty( "version" ).GetInt32( ) );

        var topOneMatches = 0;
        double reciprocalRankTotal = 0;
        var caseCount = 0;
        foreach ( var testCase in root.GetProperty( "cases" ).EnumerateArray( ) )
        {
            caseCount++;
            var query = testCase.GetProperty( "query" ).GetString( );
            if ( query is null )
            {
                throw new InvalidOperationException( "Ranking query is missing." );
            }

            var expectedFirst = testCase.GetProperty( "expectedFirst" ).GetString( );
            if ( expectedFirst is null )
            {
                throw new InvalidOperationException( "Expected result is missing." );
            }

            var ranked = testCase.GetProperty( "candidates" )
                .EnumerateArray( )
                .Select( static value =>
                {
                    var result2 = value.GetString( );
                    if ( result2 is null )
                    {
                        throw new InvalidOperationException( "Ranking candidate is missing." );
                    }

                    return result2;
                } )
                .OrderByDescending( candidate => FuzzyMatcher.Score( query, candidate ) )
                .ThenBy( static candidate => candidate, StringComparer.OrdinalIgnoreCase )
                .ToArray( );

            var expectedIndex = Array.FindIndex(
                ranked,
                candidate => string.Equals(
                    candidate,
                    expectedFirst,
                    StringComparison.Ordinal ) );
            True( expectedIndex >= 0 );
            topOneMatches += expectedIndex == 0 ? 1 : 0;
            reciprocalRankTotal += 1d / (expectedIndex + 1);
        }

        True( caseCount > 0 );
        var topOneAccuracy = topOneMatches / ( double ) caseCount;
        var meanReciprocalRank = reciprocalRankTotal / caseCount;
        True( topOneAccuracy >= root.GetProperty( "minimumTopOneAccuracy" ).GetDouble( ) );
        True( meanReciprocalRank >=
            root.GetProperty( "minimumMeanReciprocalRank" ).GetDouble( ) );

        var pipeline = await MeasurePipelineCorpusAsync(
            root.GetProperty( "pipeline" ) );
        True( pipeline.TopOneAccuracy >=
            root.GetProperty( "pipeline" ).GetProperty( "minimumTopOneAccuracy" ).GetDouble( ) );
        True( pipeline.MeanReciprocalRank >=
            root.GetProperty( "pipeline" ).GetProperty( "minimumMeanReciprocalRank" ).GetDouble( ) );

        var relevance = root.GetProperty( "relevanceCases" );
        Equal( 9, relevance.GetProperty( "visibleWindow" ).GetInt32( ) );
        var relevanceMeasurement = await MeasureRelevanceCorpusAsync(
            relevance,
            4,
            writeReport: true );
        True( relevanceMeasurement.CaseCount >= 30 );
        True( relevanceMeasurement.Passed == relevanceMeasurement.CaseCount );
        True( relevanceMeasurement.TopOneAccuracy >=
            relevance.GetProperty( "minimumTopOneAccuracy" ).GetDouble( ) );
        True( relevanceMeasurement.MeanReciprocalRank >=
            relevance.GetProperty( "minimumMeanReciprocalRank" ).GetDouble( ) );
        True( relevanceMeasurement.VisibleRecall >=
            relevance.GetProperty( "minimumVisibleRecall" ).GetDouble( ) );
    }

    private static async Task<RankingQualityMeasurement> MeasurePipelineCorpusAsync(
        JsonElement pipeline )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-ranking-corpus-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync(
                CorpusItems( pipeline.GetProperty( "items" ) ),
                null,
                CancellationToken.None );
            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog )],
                new SearchOptions( ) );
            var topOneMatches = 0;
            double reciprocalRankTotal = 0;
            var scoredCases = 0;
            foreach ( var testCase in pipeline.GetProperty( "cases" ).EnumerateArray( ) )
            {
                var query = testCase.GetProperty( "query" ).GetString( )!;
                var results = await coordinator.SearchAsync(
                    new SearchRequest { Query = query, Limit = 20 },
                    CancellationToken.None );
                var expectedElement = testCase.GetProperty( "expectedFirst" );
                if ( expectedElement.ValueKind == JsonValueKind.Null )
                {
                    Equal( 0, results.Count );
                    continue;
                }

                var expected = expectedElement.GetString( )!;
                var rank = results.ToList( ).FindIndex( result => result.Id == expected );
                RankingTrue( rank >= 0, $"pipeline corpus missed '{expected}' for '{query}'" );
                scoredCases++;
                topOneMatches += rank == 0 ? 1 : 0;
                reciprocalRankTotal += 1d / (rank + 1);
            }

            True( scoredCases > 0 );
            var topOneAccuracy = topOneMatches / ( double ) scoredCases;
            var meanReciprocalRank = reciprocalRankTotal / scoredCases;
            return new RankingQualityMeasurement(
                scoredCases,
                topOneAccuracy,
                meanReciprocalRank );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task<RankingQualityMeasurement> MeasureRankingQualityForGateAsync( )
    {
        var path = Path.Combine( AppContext.BaseDirectory, "fixtures", "ranking-corpus-v1.json" );
        using var document = JsonDocument.Parse( File.ReadAllText( path ) );
        return await MeasurePipelineCorpusAsync( document.RootElement.GetProperty( "pipeline" ) );
    }

    private static async IAsyncEnumerable<IndexedItem> CorpusItems( JsonElement items )
    {
        foreach ( var item in items.EnumerateArray( ) )
        {
            yield return new IndexedItem
            {
                StableId = item.GetProperty( "id" ).GetString( )!,
                Kind = item.GetProperty( "kind" ).GetString( )!,
                Title = item.GetProperty( "title" ).GetString( )!,
                Subtitle = OptionalString( item, "subtitle" ),
                Path = OptionalString( item, "path" ),
                Body = OptionalString( item, "body" ),
                ConfigKeys = OptionalString( item, "configKeys" ),
                ProviderId = "ranking-corpus",
                SourceId = item.GetProperty( "sourceId" ).GetString( )!
            };
        }
        await Task.CompletedTask;
    }

    private static string? OptionalString( JsonElement element, string property ) =>
        element.TryGetProperty( property, out var value ) ? value.GetString( ) : null;

    private sealed record RankingQualityMeasurement(
        int CaseCount,
        double TopOneAccuracy,
        double MeanReciprocalRank );
}
