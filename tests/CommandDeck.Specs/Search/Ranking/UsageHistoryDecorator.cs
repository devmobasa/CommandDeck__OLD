namespace CommandDeck.Specs;

internal abstract class UsageHistoryDecorator( IUsageHistoryStore inner ) : IUsageHistoryStore
{
    public virtual ValueTask RecordInvocationAsync( string resultId, string actionId, string? query, CancellationToken cancellationToken ) =>
        inner.RecordInvocationAsync( resultId, actionId, query, cancellationToken );
    public ValueTask<RankingLearningSnapshot> GetRankingLearningAsync( int limit, CancellationToken cancellationToken ) =>
        inner.GetRankingLearningAsync( limit, cancellationToken );
    public ValueTask ResetRankingLearningAsync( CancellationToken cancellationToken ) => inner.ResetRankingLearningAsync( cancellationToken );
    public ValueTask<QueryHistorySnapshot> GetQueryHistoryAsync( int limit, CancellationToken cancellationToken ) => inner.GetQueryHistoryAsync( limit, cancellationToken );
    public ValueTask<bool> DeleteQueryHistoryAsync( long id, CancellationToken cancellationToken ) => inner.DeleteQueryHistoryAsync( id, cancellationToken );
    public ValueTask ResetQueryHistoryAsync( CancellationToken cancellationToken ) => inner.ResetQueryHistoryAsync( cancellationToken );
    public virtual ValueTask<IReadOnlyDictionary<string, UsageSignal>> GetUsageSignalsAsync( IReadOnlyList<string> stableIds, CancellationToken cancellationToken ) =>
        inner.GetUsageSignalsAsync( stableIds, cancellationToken );
}
