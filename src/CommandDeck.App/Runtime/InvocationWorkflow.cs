namespace CommandDeck;

internal sealed class InvocationWorkflow(
    SearchResultCache cache,
    IActionExecutor actions,
    IUsageHistoryStore usageHistory,
    SearchOptions searchOptions,
    PrivacyOptions privacy )
{
    public async ValueTask<ActionExecutionResult> InvokeAsync(
        string resultId,
        string actionId,
        bool confirmed,
        IReadOnlyDictionary<string, string>? inputValues,
        CancellationToken cancellationToken )
    {
        if ( !cache.TryGet( resultId, out var cached ) || cached is null )
        {
            return ActionExecutionResult.Failure(
                "The result expired. Search again before invoking it." );
        }

        return await InvokeCachedAsync( cached, resultId, actionId, confirmed, inputValues,
            allowPrivateResults: true, cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<ActionExecutionResult> InvokeReferenceAsync(
        string resultId,
        string actionId,
        string reference,
        string owner,
        bool confirmed,
        IReadOnlyDictionary<string, string>? inputValues,
        CancellationToken cancellationToken )
    {
        if ( !cache.TryGetInvocation( reference, owner, resultId, out var cached ) || cached is null )
        {
            return ActionExecutionResult.Failure(
                "The action target changed or expired. Search again before invoking it." );
        }

        var outcome = await InvokeCachedAsync( cached, resultId, actionId, confirmed, inputValues,
            allowPrivateResults: true, cancellationToken ).ConfigureAwait( false );
        var undo = cached.Result.Actions.FirstOrDefault( candidate => candidate.Id == UndoActions.For( actionId ) );
        if ( !outcome.Succeeded || undo is null )
        {
            return outcome;
        }

        var referenceForUndo = cache.StoreDetached( new SearchResult( cached.Result ) { Actions = [undo] },
            cached.Query, cached.PrivateQuery, cached.Owner );
        return new ActionExecutionResult
        {
            Succeeded = outcome.Succeeded,
            Message = outcome.Message,
            DetailMarkdown = outcome.DetailMarkdown,
            ConfirmationToken = outcome.ConfirmationToken,
            UndoInvocationReference = referenceForUndo
        };
    }

    public SearchResult? ResolveAssistantResult( string resultId, string reference, string owner )
    {
        if ( !cache.TryGetInvocation( reference, owner, resultId, out var cached ) ||
            cached is null || cached.PrivateQuery )
        {
            return null;
        }

        return !PrivateSources.IsPrivate( cached.Result ) ? cached.Result : null;
    }

    public ValueTask<ActionExecutionResult> InvokeAssistantAsync(
        string resultId,
        string actionId,
        string reference,
        string owner,
        bool confirmed,
        CancellationToken cancellationToken )
    {
        if ( !cache.TryGetInvocation( reference, owner, resultId, out var cached ) ||
            cached is null || cached.PrivateQuery || PrivateSources.IsPrivate( cached.Result ) )
        {
            return ValueTask.FromResult( ActionExecutionResult.Failure( "This result is unavailable to assistant tools." ) );
        }

        return InvokeCachedAsync( cached, resultId, actionId, confirmed, null,
            allowPrivateResults: false, cancellationToken );
    }

    private async ValueTask<ActionExecutionResult> InvokeCachedAsync(
        CachedSearchResult cached,
        string resultId,
        string actionId,
        bool confirmed,
        IReadOnlyDictionary<string, string>? inputValues,
        bool allowPrivateResults,
        CancellationToken cancellationToken )
    {
        if ( !allowPrivateResults && (cached.PrivateQuery || PrivateSources.IsPrivate( cached.Result )) )
        {
            return ActionExecutionResult.Failure( "This result is unavailable to assistant tools." );
        }

        var action = cached.Result.Actions.FirstOrDefault( candidate =>
            string.Equals( candidate.Id, actionId, StringComparison.Ordinal ) );
        if ( action is null )
        {
            return ActionExecutionResult.Failure(
                $"Action '{actionId}' is not available for '{cached.Result.Title}'." );
        }

        if ( action.RequiresConfirmation && !confirmed )
        {
            return ActionExecutionResult.Failure(
                "This action requires confirmation.",
                "Set `confirmed: true` only after showing a confirmation dialog." );
        }

        action = ActionInputValues.Bind( action, inputValues );
        var outcome = await actions.ExecuteAsync(
            cached.Result,
            action,
            cached.Query,
            cancellationToken ).ConfigureAwait( false );

        if ( outcome.Succeeded &&
            !cached.PrivateQuery &&
            !PrivateSources.IsPrivate( cached.Result ) &&
            ShouldRecordUsage( action ) &&
            ((searchOptions.LearnFromInvocations &&
              searchOptions.Ranking.Adaptive.Enabled &&
              privacy.StoreUsageHistory) ||
             privacy.StoreQueryHistory) )
        {
            var catalogResultId = cached.Result.Metadata.GetValueOrDefault(
                ResultMetadataKeys.CatalogResultId,
                resultId );
            // Acceptance has happened. Dismissing the launcher must not cancel
            // this bounded bookkeeping or turn a successful launch into a retry.
            // SQLite separately caps its synchronous lock wait to one second.
            using CancellationTokenSource completion = new( TimeSpan.FromSeconds( 2 ) );
            try
            {
                await usageHistory.RecordInvocationAsync(
                    catalogResultId,
                    actionId,
                    privacy.StoreQueryHistory ? cached.Query : null,
                    completion.Token ).ConfigureAwait( false );
            }
            catch ( Exception )
            {
                return ActionExecutionResult.Success(
                    string.IsNullOrWhiteSpace( outcome.Message )
                        ? "Action accepted; history could not be saved."
                        : outcome.Message + " History could not be saved.",
                    outcome.DetailMarkdown );
            }
        }

        return outcome;
    }

    private static bool ShouldRecordUsage( CommandAction action ) =>
        action.Kind is not ActionKinds.ClipboardHistory and not ActionKinds.CopyText and not ActionKinds.SnippetCopy and not ActionKinds.SnippetPreview
            and not ActionKinds.HerdrCreateTab and not ActionKinds.HerdrRenameTab and not ActionKinds.BrowseHerdrTabs;
}
