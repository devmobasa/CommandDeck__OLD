namespace CommandDeck;

public sealed class SnippetProvider( IReadOnlyList<SnippetOptions> snippets ) : ICommandProvider
{
    // Configuration validation already compiled each template; keep the result per snippet.
    private readonly PreparedSnippet[] _snippets = [.. snippets.Select( static snippet =>
        new PreparedSnippet( snippet, SnippetTemplate.Compile( snippet.Text ) ) )];

    public string Id => ProviderIds.Snippets;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Snippets ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( _snippets.Where( prepared =>
                    prepared.Options.Title.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) ||
                    prepared.Options.Text.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) )
                    .Take( Math.Clamp( request.Limit, 1, 100 ) ).Select( Build ).ToArray( ) );
    }

    private static SearchResult Build( PreparedSnippet snippet ) => new( )
    {
        Id = StableId.Create( "snippet", snippet.Options.Id ),
        Title = snippet.Options.Title,
        Subtitle = "Snippet · copy or preview with inputs",
        Icon = "edit-paste",
        Kind = ResultKinds.CustomCommand,
        ProviderId = ProviderIds.Snippets,
        SourceId = SourceIds.Snippets,
        DetailMarkdown = snippet.Options.Text,
        Score = 2,
        Section = new ResultSection { Id = "snippets", Title = "Snippets", Order = 6 },
        Actions = [Action( snippet, ActionKinds.SnippetCopy, "Copy snippet" ), Action( snippet, ActionKinds.SnippetPreview, "Preview expanded snippet" )]
    };

    private static CommandAction Action( PreparedSnippet snippet, string kind, string title ) => new( )
    {
        Id = kind,
        Kind = kind,
        Title = title,
        Inputs = [.. snippet.Template.Inputs],
        Arguments = new SnippetActionPayload( snippet.Options.Text, snippet.Template.ReadsClipboardHistory ).ToArguments( )
    };

    private sealed record PreparedSnippet( SnippetOptions Options, CompiledSnippet Template );
}
