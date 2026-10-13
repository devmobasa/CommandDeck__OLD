namespace CommandDeck.Specs;

public static partial class Program
{
    private static SearchRequest HeyChipRequest( string query ) => new( )
    {
        Query = query,
        Expression = new QueryExpression { IncludeFilterIds = [HeySearch.FilterId] },
        EnabledSearchSources = [SourceIds.Hey]
    };

    private static async Task<IReadOnlyList<SearchResult>> HeyCompleteAsync( HeyProvider provider, SearchRequest request ) =>
        await provider.SearchAsync( QueryParser.Parse( request.Query, request.Expression ), request, default );

    // A refinement still being typed lists its values instead of searching;
    // a finished value, or a space after it, searches.
    private static async Task HeyCompletesRefinementValuesAsync( )
    {
        HeyFake fake = new( );
        var provider = HeyProviderFor( fake );

        var boxes = await HeyCompleteAsync( provider, HeyChipRequest( "invoice in:" ) );
        Equal( "in:imbox,in:feed,in:papertrail,in:trash", string.Join( ',', boxes.Select( static row => row.Title ) ) );
        Equal( "invoice in:imbox ", boxes[0].Metadata[ResultMetadataKeys.CompletionText] );
        var use = boxes[0].Actions.Single( );
        Equal( ActionKinds.ApplyQuery, use.Kind );
        Equal( "invoice in:imbox ", use.Arguments["query"] );
        Equal( HeySearch.FilterId, use.Arguments["filterId"] );
        True( boxes.All( static row => PrivateSources.IsPrivate( row ) && !row.CanCustomize ) );

        Equal( "has:pdfs,has:presentations",
            string.Join( ',', (await HeyCompleteAsync( provider, HeyChipRequest( "invoice HAS:p" ) )).Select( static row => row.Title ) ) );
        Equal( 4, (await HeyCompleteAsync( provider, HeyChipRequest( "date:" ) )).Count );
        Equal( "box:reply-later", (await HeyCompleteAsync( provider, HeyChipRequest( "box:re" ) )).Single( ).Title );
        Equal( 0, fake.Calls.Count );

        // Without a chip the words keep their own prefix.
        SearchRequest typed = new( )
        {
            Query = "@hey lunch in:im",
            EnabledSearchSources = [SourceIds.Hey]
        };
        var plain = (await provider.SearchAsync( QueryParser.Parse( typed.Query ), typed, default )).Single( );
        Equal( "@hey lunch in:imbox ", plain.Actions.Single( ).Arguments["query"] );
        Equal( "", plain.Actions[0].Arguments["filterId"] );

        // Finished values and a trailing space search; an unknown one is explained.
        await HeyCompleteAsync( provider, HeyChipRequest( "invoice has:pdf" ) );
        await HeyCompleteAsync( provider, HeyChipRequest( "invoice in:imbox " ) );
        Equal( 2, fake.Calls.Count );
        Equal( "hey:state:refinement", (await HeyCompleteAsync( provider, HeyChipRequest( "invoice in:outbox" ) )).Single( ).Id );
        Equal( 2, fake.Calls.Count );
    }
}
