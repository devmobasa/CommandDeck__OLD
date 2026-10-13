namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    public ValueTask<ActionExecutionResult> InvokeAsync(
        string resultId,
        string actionId,
        bool confirmed,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? inputValues = null ) =>
        _invocations.InvokeAsync( resultId, actionId, confirmed, inputValues, cancellationToken );

    public ValueTask<ActionExecutionResult> InvokeReferenceAsync(
        string resultId,
        string actionId,
        string reference,
        string owner,
        bool confirmed,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? inputValues = null ) =>
        _invocations.InvokeReferenceAsync( resultId, actionId, reference, owner, confirmed, inputValues, cancellationToken );

    public ValueTask<RankingLearningSnapshot> GetRankingLearningAsync(
        int limit,
        CancellationToken cancellationToken ) =>
        _usageHistory.GetRankingLearningAsync( limit, cancellationToken );

    public ValueTask ResetRankingLearningAsync( CancellationToken cancellationToken ) =>
        _usageHistory.ResetRankingLearningAsync( cancellationToken );

    public ValueTask<FilePreviewOutcome> PreviewAsync(
        string resultId,
        CancellationToken cancellationToken,
        string? query = null,
        QueryExpression? expression = null,
        int matchIndex = 0,
        string? owner = null,
        string? invocationReference = null ) =>
        _filePreviews.PreviewAsync( resultId, query, expression, matchIndex, owner, invocationReference, cancellationToken );
}
