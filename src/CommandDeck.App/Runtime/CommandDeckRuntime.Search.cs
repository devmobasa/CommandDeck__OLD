namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken ) => SearchAsync( request, SearchPurpose.Interactive, cancellationToken );

    public ValueTask<SearchOutcome> SearchWithOutcomeAsync(
        SearchRequest request,
        CancellationToken cancellationToken ) =>
        SearchWithOutcomeAsync( request, SearchPurpose.Interactive, cancellationToken );

    ValueTask<IReadOnlyList<SearchResult>> IAgentLauncherHost.SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        SearchRequest owned = new( )
        {
            Query = request.Query,
            Limit = request.Limit,
            Expression = request.Expression,
            ActiveApplication = request.ActiveApplication,
            ActiveWorkspace = request.ActiveWorkspace,
            CurrentDirectory = request.CurrentDirectory,
            SelectedProject = request.SelectedProject,
            HerdrWorkspaceId = request.HerdrWorkspaceId,
            OmarchyMenuId = request.OmarchyMenuId,
            BrowseDirectory = request.BrowseDirectory,
            BrowseOffset = request.BrowseOffset,
            EnabledSearchSources = request.EnabledSearchSources,
            ContributionSnapshot = request.ContributionSnapshot,
            SearchOwner = AssistantSearchOwner
        };
        return SearchAsync( owned, SearchPurpose.AssistantTool, cancellationToken );
    }

    private async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        SearchPurpose purpose,
        CancellationToken cancellationToken )
    {
        var outcome = await SearchWithOutcomeAsync( request, purpose, cancellationToken )
            .ConfigureAwait( false );
        return outcome.Results;
    }

    private async ValueTask<SearchOutcome> SearchWithOutcomeAsync(
        SearchRequest request,
        SearchPurpose purpose,
        CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        request.SearchGeneration = _extensionSearch?.Begin( request.SearchOwner ) ?? 0;
        var search = await _search.SearchWithOutcomeAsync( request, purpose, cancellationToken )
            .ConfigureAwait( false );
        var results = await AddOpenWithActionsAsync( search.Results, cancellationToken )
            .ConfigureAwait( false );

        // A trailing row ("Search HEY for …") is not a match: the web fallback
        // still appears when nothing else can act, ahead of the trailing rows.
        if ( !results.Any( static result => SearchResultCapabilities.IsExecutable( result ) && !ResultPlacements.IsTrailing( result ) ) )
        {
            var fallback = await BuildWebFallbackAsync( request, cancellationToken )
                .ConfigureAwait( false );
            if ( fallback is not null )
            {
                results = [.. results.Where( static result => !ResultPlacements.IsTrailing( result ) ), fallback,
                    .. results.Where( ResultPlacements.IsTrailing )];
            }
        }

        results = await AddAiWorkflowActionsAsync( results, cancellationToken )
            .ConfigureAwait( false );

        _extensionSearch?.RetainDisplayed( request, results );
        return new SearchOutcome(
            _cache.Store( results, request.Query, request.ContainsPrivateQuery, request.SearchOwner ),
            search.IncompleteProviderCount );
    }

    // Open With needs each file's MIME type: a stat per path and a `file` process
    // for cache misses. Resolving it after ranking and section budgets, for the
    // indexed files the launcher shows, keeps that cost off discarded candidates
    // and outside the provider deadline. The search waits a bounded time for it
    // and otherwise returns those rows with their own actions; see
    // OpenWithDetection. A catalog without desktop handlers adds none.
    private async ValueTask<IReadOnlyList<SearchResult>> AddOpenWithActionsAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken cancellationToken )
    {
        if ( _openWith is null )
        {
            return results;
        }

        var paths = results
            .Where( IsIndexedFile )
            .Select( static result => result.Metadata["path"] )
            .Distinct( StringComparer.Ordinal )
            .ToArray( );
        if ( paths.Length == 0 )
        {
            return results;
        }

        var openWith = await _openWith.DetectAsync( paths, cancellationToken )
            .ConfigureAwait( false );
        if ( openWith is null )
        {
            return results;
        }

        return [.. results.Select( result => WithOpenWithActions( result, openWith ) )];
    }

    private static bool IsIndexedFile( SearchResult result ) =>
        result.Kind == ResultKinds.File &&
        result.SourceId == SourceIds.Files &&
        !string.IsNullOrWhiteSpace( result.Metadata.GetValueOrDefault( "path" ) );

    private static SearchResult WithOpenWithActions(
        SearchResult result,
        IReadOnlyDictionary<string, IReadOnlyList<CommandAction>> openWith )
    {
        if ( !IsIndexedFile( result ) ||
            !openWith.TryGetValue( result.Metadata["path"], out var actions ) || actions.Count == 0 )
        {
            return result;
        }

        var existing = result.Actions
            .Select( static action => action.Id )
            .ToHashSet( StringComparer.Ordinal );
        return new SearchResult( result )
        {
            Actions = [.. result.Actions, .. actions.Where( action => existing.Add( action.Id ) )]
        };
    }

    private async ValueTask<IReadOnlyList<SearchResult>> AddAiWorkflowActionsAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken cancellationToken )
    {
        if ( !EffectiveAiSettings.Enabled )
        {
            return results;
        }

        var selection = await GetAiSelectionAsync( cancellationToken ).ConfigureAwait( false );
        if ( selection.State != AiSelectionStates.Ready || selection.ProviderId is null ||
            selection.ModelId is null )
        {
            return results;
        }
        return [.. results.Select( result => DecorateAiWorkflows( result, selection ) )];
    }

    private static SearchResult DecorateAiWorkflows( SearchResult result, AiSelection selection )
    {
        if ( PrivateSources.IsPrivate( result ) || result.Kind is ResultKinds.AiCommand or ResultKinds.Status ||
            string.Equals( result.ProviderId, ProviderIds.Fallback, StringComparison.Ordinal ) )
        {
            return result;
        }
        List<CommandAction> actions = [.. result.Actions];
        var attachmentKind = result.Kind == ResultKinds.File
            ? AiAttachmentKinds.File
            : AiAttachmentKinds.Result;
        actions.AddRange( new[]
        {
            AiWorkflowAction("ai-explain", "Explain with AI", "explain",
                "Explain the selected item.", attachmentKind, result.Id, selection),
            AiWorkflowAction("ai-summarize", "Summarize with AI", "summarize",
                "Summarize the attached content.", attachmentKind, result.Id, selection),
            AiWorkflowAction("ai-propose-text", "Propose Text with AI", "propose-text",
                "Propose inert text for the selected item.", attachmentKind, result.Id, selection)
        } );
        return new SearchResult( result ) { Actions = actions };
    }

    private static CommandAction AiWorkflowAction(
        string id,
        string title,
        string workflowId,
        string prompt,
        string attachmentKind,
        string resultId,
        AiSelection selection ) => new( )
        {
            Id = id,
            Title = title,
            Kind = ActionKinds.AiPrompt,
            Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                ["providerId"] = selection.ProviderId!,
                ["modelId"] = selection.ModelId!,
                ["workflowId"] = workflowId,
                ["prompt"] = prompt,
                ["attachmentKind"] = attachmentKind,
                ["resultId"] = resultId
            }
        };

    public async ValueTask<SearchSuggestionSnapshot> SuggestAsync(
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        var query = request.Query.Trim( );
        if ( query.Length == 0 )
        {
            return new SearchSuggestionSnapshot( );
        }

        var limit = Math.Clamp( request.Limit, 1, 8 );
        var filterAliases = await _preferences.GetFilterAliasesAsync(
            cancellationToken ).ConfigureAwait( false );
        var aliasMap = filterAliases.ToDictionary(
            static value => value.Alias,
            static value => value.FilterId,
            StringComparer.OrdinalIgnoreCase );
        var parsed = QueryParser.Parse( query, request.Expression, aliasMap, Contributions );
        if ( parsed.IsAiMode || PrivateSources.ReferencesPrivateSource( parsed ) )
        {
            return new SearchSuggestionSnapshot( );
        }
        List<SearchSuggestion> suggestions = [];
        var history = await _usageHistory.GetQueryHistoryAsync(
            64,
            cancellationToken ).ConfigureAwait( false );
        if ( history.Enabled )
        {
            foreach ( var entry in history.Entries.Where( entry =>
                entry.Query.Contains( query, StringComparison.OrdinalIgnoreCase ) ) )
            {
                ParsedQuery historical;
                try
                {
                    historical = QueryParser.Parse(
                        entry.Query,
                        request.Expression,
                        aliasMap, Contributions );
                }
                catch ( InvalidOperationException )
                {
                    continue;
                }
                if ( PrivateSources.ReferencesPrivateSource( historical ) )
                {
                    continue;
                }

                AddSuggestion( suggestions, new SearchSuggestion
                {
                    Text = entry.Query,
                    Source = "history",
                    Expression = historical.Expression
                }, limit );
            }
        }

        var completionToken = parsed.Text.Trim( );
        var completable = completionToken.Length > 0 && !completionToken.Any( char.IsWhiteSpace );
        if ( completable && suggestions.Count < limit )
        {
            var results = await _search.SearchAsync(
                new SearchRequest
                {
                    Query = query,
                    Expression = request.Expression,
                    Limit = limit,
                    ActiveApplication = request.ActiveApplication,
                    ActiveWorkspace = request.ActiveWorkspace,
                    CurrentDirectory = request.CurrentDirectory,
                    SelectedProject = request.SelectedProject
                },
                SearchPurpose.Suggestion, cancellationToken ).ConfigureAwait( false );
            foreach ( var result in results.Where( result =>
                !PrivateSources.IsPrivate( result ) &&
                result.Title.StartsWith( completionToken, StringComparison.OrdinalIgnoreCase ) &&
                !string.Equals( result.Title, completionToken, StringComparison.OrdinalIgnoreCase ) ) )
            {
                AddSuggestion( suggestions, new SearchSuggestion
                {
                    Text = result.Title,
                    Source = "catalog",
                    Expression = new QueryExpression
                    {
                        IncludeFilterIds = [.. parsed.Expression.IncludeFilterIds],
                        ExcludeFilterIds = [.. parsed.Expression.ExcludeFilterIds],
                        Clauses = [new QueryClause { Text = result.Title }]
                    }
                }, limit );
            }
        }
        return new SearchSuggestionSnapshot { Suggestions = suggestions };
    }

    private static void AddSuggestion(
        List<SearchSuggestion> suggestions,
        SearchSuggestion candidate,
        int limit )
    {
        if ( suggestions.Count >= limit || suggestions.Any( value => string.Equals(
            value.Text,
            candidate.Text,
            StringComparison.OrdinalIgnoreCase ) ) )
        {
            return;
        }
        suggestions.Add( candidate );
    }
}
