namespace CommandDeck.Specs;

public static partial class Program
{
    private static void QueryAlgebraParsesDeterministically( )
    {
        var parsed = QueryParser.Parse(
            "file: app: \"release notes\" -workspaces: -path:\"/private area\" title:roadmap" );

        True( parsed.IncludedFilters.Select( static value => value.Id ).SequenceEqual(
            ["files", "applications"] ) );
        Equal( "workspaces", parsed.ExcludedFilters.Single( ).Id );
        Equal( 3, parsed.Expression.Clauses.Count );
        Equal( "release notes", parsed.Expression.Clauses[0].Text );
        Equal( QueryFieldIds.Path, parsed.Expression.Clauses[1].Field );
        Equal( "/private area", parsed.Expression.Clauses[1].Text );
        True( parsed.Expression.Clauses[1].Excluded );
        Equal( QueryFieldIds.Title, parsed.Expression.Clauses[2].Field );
        Equal( "roadmap", parsed.Expression.Clauses[2].Text );

        var negativeOnly = QueryParser.Parse( "-secret" );
        Equal( string.Empty, negativeOnly.Text );
        True( negativeOnly.HasExplicitFilter );
        Equal( "secret", negativeOnly.Expression.Clauses.Single( ).Text );
        True( negativeOnly.Expression.Clauses.Single( ).Excluded );

        var excludedTerm = QueryParser.Parse( "roadmap -secret" );
        Equal( "roadmap", excludedTerm.Text );
        True( excludedTerm.Expression.Clauses[1].Excluded );
        Equal( "workspaces", QueryParser.Parse( "roadmap -workspaces:" ).ExcludedFilters.Single( ).Id );
        var applicationSearch = QueryParser.Parse( "app: roadmap -path:/private" );
        Equal( QueryFieldIds.Path, applicationSearch.Expression.Clauses[1].Field );
        True( applicationSearch.Expression.Clauses[1].Excluded );

        var merged = QueryParser.Parse( "metadata:alpha", new QueryExpression
        {
            IncludeFilterIds = ["commands"],
            Clauses = [new QueryClause { Field = QueryFieldIds.Title, Text = "deploy" }]
        } );
        Equal( "commands", merged.IncludedFilters.Single( ).Id );
        Equal( 2, merged.Expression.Clauses.Count );
    }

    private static void QueryAlgebraRejectsMalformedExpressions( )
    {
        Throws<InvalidOperationException>( ( ) => QueryParser.Parse( "\"unterminated" ) );
        var unknownPrefix = QueryParser.Parse( "unknown:" );
        True( unknownPrefix.Expression.Clauses.Count == 1 && unknownPrefix.Expression.Clauses[0].Text == "unknown",
            "an unknown prefix is ordinary text while typing" );
        True( QueryParser.Parse( "title:" ).Expression.Clauses.Count == 0, "an incomplete field clause is ignored while typing" );
        Throws<InvalidOperationException>( ( ) => QueryParser.Parse( string.Empty, new QueryExpression
        {
            IncludeFilterIds = ["files", "FILES"]
        } ) );
        Throws<InvalidOperationException>( ( ) => QueryParser.Parse( string.Empty, new QueryExpression
        {
            IncludeFilterIds = ["files"],
            ExcludeFilterIds = ["files"]
        } ) );
        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.Search,
            Expression = null!
        } ) );
        Throws<InvalidOperationException>( ( ) => QueryParser.Parse( string.Empty, new QueryExpression
        {
            Clauses = null!
        } ) );
    }

    private static async Task QueryAlgebraEvaluatesAcrossProvidersAsync( )
    {
        var application = AlgebraResult(
            "app:editor", ResultKinds.Application, "Roadmap Editor", SourceIds.Applications );
        var file = AlgebraResult(
            "file:roadmap", ResultKinds.File, "Roadmap Notes", SourceIds.Files,
            new Dictionary<string, string>( StringComparer.Ordinal ) { ["path"] = "/docs/roadmap.md" } );
        var privateFile = AlgebraResult(
            "file:private", ResultKinds.File, "Roadmap Draft", SourceIds.Files,
            new Dictionary<string, string>( StringComparer.Ordinal ) { ["path"] = "/private/draft.md" } );
        var window = AlgebraResult(
            "window:1", ResultKinds.Window, "Roadmap", SourceIds.WindowsWorkspaces );
        var workspace = AlgebraResult(
            "workspace:1", ResultKinds.Workspace, "Roadmap", SourceIds.WindowsWorkspaces );

        SearchCoordinator coordinator = new(
            [
                new AlgebraSearchProvider("catalog", [application, file, privateFile]),
                new AlgebraSearchProvider("hyprland", [window, workspace])
            ],
            new SearchOptions( ) );

        var union = await coordinator.SearchAsync( new SearchRequest
        {
            Query = "roadmap",
            Expression = new QueryExpression { IncludeFilterIds = ["applications", "files"] },
            Limit = 20
        }, CancellationToken.None );
        True( union.Select( static value => value.Id ).ToHashSet( StringComparer.Ordinal )
            .SetEquals( [application.Id, file.Id, privateFile.Id] ) );

        var intersected = await coordinator.SearchAsync( new SearchRequest
        {
            Query = string.Empty,
            Expression = new QueryExpression
            {
                IncludeFilterIds = ["files"],
                Clauses =
                [
                    new QueryClause { Field = QueryFieldIds.Title, Text = "roadmap" },
                    new QueryClause { Field = QueryFieldIds.Path, Text = "/private", Excluded = true }
                ]
            },
            Limit = 20
        }, CancellationToken.None );
        Equal( file.Id, intersected.Single( ).Id );

        var sharedProviderExclusion = await coordinator.SearchAsync(
            new SearchRequest
            {
                Query = "roadmap",
                Expression = new QueryExpression { ExcludeFilterIds = ["windows"] },
                Limit = 20
            },
            CancellationToken.None );
        True( sharedProviderExclusion.Any( value => value.Id == workspace.Id ) );
        True( sharedProviderExclusion.All( value => value.Id != window.Id ) );
    }

    private static SearchResult AlgebraResult(
        string id,
        string kind,
        string title,
        string source,
        Dictionary<string, string>? metadata = null ) => new( )
        {
            Id = id,
            Kind = kind,
            Title = title,
            SourceId = source,
            ProviderId = source,
            Score = 0.8,
            Metadata = metadata ?? new Dictionary<string, string>( StringComparer.Ordinal )
        };

    private sealed class AlgebraSearchProvider(
        string id,
        IReadOnlyList<SearchResult> results ) : ICommandProvider
    {
        public string Id { get; } = id;

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken ) => ValueTask.FromResult( results );
    }
}
