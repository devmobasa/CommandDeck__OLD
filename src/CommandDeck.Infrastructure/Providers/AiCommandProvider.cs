namespace CommandDeck;

public sealed class AiCommandProvider : ICommandProvider
{
    private readonly Func<AiOptions> _options;
    private readonly Func<string?> _selectedBackend;

    public AiCommandProvider( AiOptions options, Func<string?>? selectedBackend = null )
        : this( ( ) => options, selectedBackend )
    {
        ArgumentNullException.ThrowIfNull( options );
    }

    /// <summary>
    /// Reads the effective provider set on every search so a live settings
    /// mutation is reflected without rebuilding the provider list.
    /// </summary>
    public AiCommandProvider( Func<AiOptions> options, Func<string?>? selectedBackend = null )
    {
        if ( options is null )
        {
            throw new ArgumentNullException( nameof( options ) );
        }

        _options = options;
        _selectedBackend = selectedBackend ?? (( ) => null);
    }

    public string Id => ProviderIds.Assistant;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );

        var options = _options( );
        // An explicit backend prefix (codex:, claude:, cursor:, grok:) names an
        // assistant backend, never a direct provider.
        if ( !options.Enabled || !query.IsAiMode || query.ExplicitBackendId is not null )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        List<SearchResult> results = [];
        var selected = _selectedBackend( );
        foreach ( var provider in options.Providers.Where( provider =>
            provider.Enabled && selected is not null &&
            string.Equals( provider.Id, selected, StringComparison.OrdinalIgnoreCase ) ) )
        {
            var title = provider.Kind == "ollama"
                ? $"Ask local AI ({provider.Model})"
                : $"Ask cloud AI ({provider.Model})";

            var score = query.IsAiMode
                ? 1
                : Math.Max(
                    FuzzyMatcher.Score( query.Text, title ),
                    FuzzyMatcher.Score( query.Text, provider.Id ) );

            if ( !query.IsAiMode && !string.IsNullOrWhiteSpace( query.Text ) && score <= 0 )
            {
                continue;
            }

            results.Add( new SearchResult
            {
                Id = $"ai:{provider.Id}",
                Kind = ResultKinds.AiCommand,
                Title = title,
                Subtitle = provider.Kind == "ollama"
                    ? "Explicit local prompt; no cloud transfer"
                    : $"Explicit request to {new Uri( provider.BaseUrl ).Host}",
                Icon = provider.Kind == "ollama" ? "computer" : "network-server",
                ProviderId = Id,
                SourceId = SourceIds.Ai,
                Score = score,
                DetailMarkdown =
                    "The prompt is sent only after you explicitly invoke this action.",
                Details =
                [
                    new DetailField("Provider", provider.Id, Monospace: true),
                    new DetailField("Model", provider.Model, Monospace: true)
                ],
                Actions =
                [
                    new CommandAction
                    {
                        Id = "ask",
                        Title = "Ask",
                        Kind = ActionKinds.AiPrompt,
                        Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["providerId"] = provider.Id,
                            ["modelId"] = provider.Model,
                            ["prompt"] = query.Text
                        }
                    }
                ],
                Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
                {
                    ["providerId"] = provider.Id,
                    ["prompt"] = query.Text
                }
            } );
        }

        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( results );
    }
}
