namespace CommandDeck;

public sealed class SnippetProvider( IReadOnlyList<SnippetOptions> snippets ) : ICommandProvider
{
    public string Id => ProviderIds.Snippets;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Snippets ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( snippets.Where( snippet =>
                    snippet.Title.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) ||
                    snippet.Text.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) )
                    .Take( Math.Clamp( request.Limit, 1, 100 ) ).Select( Build ).ToArray( ) );
    }

    private static SearchResult Build( SnippetOptions snippet ) => new( )
    {
        Id = StableId.Create( "snippet", snippet.Id ),
        Title = snippet.Title,
        Subtitle = "Snippet · copy or preview with inputs",
        Icon = "edit-paste",
        Kind = ResultKinds.CustomCommand,
        ProviderId = ProviderIds.Snippets,
        SourceId = SourceIds.Snippets,
        DetailMarkdown = snippet.Text,
        Score = 2,
        Section = new ResultSection { Id = "snippets", Title = "Snippets", Order = 6 },
        Actions = [Action( snippet, ActionKinds.SnippetCopy, "Copy snippet" ), Action( snippet, ActionKinds.SnippetPreview, "Preview expanded snippet" )]
    };

    private static CommandAction Action( SnippetOptions snippet, string kind, string title ) => new( )
    {
        Id = kind,
        Kind = kind,
        Title = title,
        Inputs = SnippetTemplate.Inputs( snippet.Text ),
        Arguments = new SnippetActionPayload( snippet.Text ).ToArguments( )
    };
}
