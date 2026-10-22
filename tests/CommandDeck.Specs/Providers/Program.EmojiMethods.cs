namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task EmojiProviderMatchesByNameAndKeywordAndCopiesGlyphAsync( )
    {
        EmojiProvider provider = new( );

        // Filter-gated: without the emoji filter there is no result and no work.
        var bare = QueryParser.Parse( "smile" );
        Equal( 0, (await provider.SearchAsync(
            bare, new SearchRequest { Query = "smile", Limit = 20 }, default )).Count );

        // Name match, on the emoji source, with a copy-glyph action.
        var byName = QueryParser.Parse( "emoji: grinning" );
        True( byName.IncludedFilters.Any( filter => filter.SourceId == SourceIds.Emoji ),
            "emoji: targets the emoji source" );
        var named = await provider.SearchAsync(
            byName, new SearchRequest { Query = "emoji: grinning", Limit = 20 }, default );
        True( named.Count > 0, "a name query returns emoji" );
        True( named.All( result => result.SourceId == SourceIds.Emoji ),
            "results carry the emoji source" );
        Equal( ActionKinds.CopyText, named[0].Actions[0].Kind );
        True( named[0].Actions[0].Arguments["text"].Length > 0,
            "the copy action carries the glyph" );

        // The icon is the glyph itself so the launcher can draw it large.
        True( named.All( result => result.Icon == result.Actions[0].Arguments["text"] ),
            "the icon is the emoji glyph" );

        // Keyword match: 'lit' finds the fire emoji even though the name is 'fire'.
        var byKeyword = QueryParser.Parse( "emoji: lit" );
        var keyworded = await provider.SearchAsync(
            byKeyword, new SearchRequest { Query = "emoji: lit", Limit = 20 }, default );
        True( keyworded.Any( result => result.Actions[0].Arguments["text"] == "\U0001F525" ),
            "keyword 'lit' finds the fire emoji" );

        // An empty emoji query browses, honoring the request limit.
        var browse = QueryParser.Parse( "emoji:" );
        var browsed = await provider.SearchAsync(
            browse, new SearchRequest { Query = "emoji:", Limit = 5 }, default );
        Equal( 5, browsed.Count );

        // Cancellation is honored.
        using System.Threading.CancellationTokenSource cancelled = new( );
        cancelled.Cancel( );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await provider.SearchAsync(
            byName, new SearchRequest { Query = "emoji: grinning", Limit = 20 }, cancelled.Token ) );
    }
}
