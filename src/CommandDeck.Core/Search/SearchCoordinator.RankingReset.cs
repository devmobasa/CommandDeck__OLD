namespace CommandDeck;

public sealed partial class SearchCoordinator
{
    public const string ResetRankingActionId = "reset-ranking";
    private const string RecentFrequentSectionId = "recent-frequent";

    // A result ranked by accepted use offers "Reset Ranking", which forgets that
    // use for this result alone. Assistant and suggestion searches never offer
    // it, and private sources record no use to reset.
    private static SearchResult[] OfferRankingReset( IEnumerable<SearchResult> results, SearchPurpose purpose ) =>
        purpose == SearchPurpose.Interactive ? [.. results.Select( OfferRankingReset )] : [.. results];

    private static SearchResult OfferRankingReset( SearchResult result )
    {
        var used = result.Section?.Id == RecentFrequentSectionId || result.Ranking.Contributions.Any( static contribution =>
            contribution.Kind == RankingContributionKinds.Frecency && contribution.Value > 0 );
        if ( !used || result.Actions.Count == 0 || PrivateSources.IsPrivate( result ) ||
            result.Actions.Any( static action => action.Kind == ActionKinds.ResetRanking ) )
        {
            return result;
        }

        return new SearchResult( result )
        {
            Actions =
            [
                .. result.Actions,
                new CommandAction { Id = ResetRankingActionId, Title = "Reset Ranking", Kind = ActionKinds.ResetRanking }
            ]
        };
    }
}
