namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    private const string AssistantSearchOwner = "assistant-tool";

    public void RevokeInvocationClient( string owner ) => _cache.RemoveOwner( owner );

    private SearchResult GetCustomizableCachedResult( string resultId )
    {
        if ( !_cache.TryGet( resultId, out var cached ) || cached is null )
        {
            throw new InvalidOperationException(
                "The result expired. Search again before customizing it." );
        }

        if ( !cached.Result.CanCustomize )
        {
            throw new InvalidOperationException( "This result cannot be customized." );
        }

        return cached.Result;
    }

    private SearchResult? ResolveOwnedCachedResult( string owner, string resultId ) =>
        _cache.TryGetOwned( resultId, owner, out var cached ) ? cached?.Result : null;

    SearchResult? IAgentLauncherHost.ResolveCachedResult( string resultId, string invocationReference ) =>
        _invocations.ResolveAssistantResult( resultId, invocationReference, AssistantSearchOwner );

    ValueTask<ActionExecutionResult> IAgentLauncherHost.InvokeAsync(
        string resultId,
        string actionId,
        string invocationReference,
        string query,
        bool confirmed,
        CancellationToken cancellationToken ) =>
        _invocations.InvokeAssistantAsync( resultId, actionId, invocationReference, AssistantSearchOwner,
            confirmed, cancellationToken );

    private SearchResult CacheSyntheticResult( SearchResult result, string owner ) =>
        _cache.Store( [result], string.Empty, owner: owner )[0];

    private async ValueTask<SearchResult?> BuildWebFallbackAsync(
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        var parsed = QueryParser.Parse( request.Query, request.Expression, registry: Contributions );
        if ( parsed.Text.Length == 0 || parsed.HasExplicitFilter || parsed.IsCalculator ||
            parsed.IsAiMode || parsed.IsCommandMode )
        {
            return null;
        }

        var preferences = await _preferences.GetPreferencesAsync( cancellationToken )
            .ConfigureAwait( false );
        var quicklink = _options.Quicklinks.FirstOrDefault( value =>
            string.Equals( value.Id, preferences.WebFallbackQuicklinkId, StringComparison.Ordinal ) );
        if ( quicklink is null )
        {
            return null;
        }

        return new SearchResult
        {
            Id = StableId.Create( "web-fallback", $"{quicklink.Id}\n{parsed.Text}" ),
            Kind = ResultKinds.Quicklink,
            Title = $"Search the web for “{parsed.Text}”",
            Subtitle = quicklink.Title,
            Icon = quicklink.Icon ?? "internet-web-browser",
            ProviderId = ProviderIds.Fallback,
            SourceId = SourceIds.CommandsQuicklinks,
            Section = new ResultSection { Id = "fallback", Title = "Search the Web", Order = 100 },
            Score = 0.1,
            CanCustomize = false,
            Actions =
            [
                new CommandAction
                {
                    Id = "open",
                    Title = "Search the Web",
                    Kind = ActionKinds.OpenUrl,
                    Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["urlTemplate"] = quicklink.UrlTemplate.Replace(
                            "{query}",
                            Uri.EscapeDataString(parsed.Text),
                            StringComparison.OrdinalIgnoreCase)
                    }
                }
            ],
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                ["fallback"] = "web",
                ["quicklinkId"] = quicklink.Id
            }
        };
    }

}
