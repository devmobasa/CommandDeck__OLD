namespace CommandDeck.Specs;

public static partial class Program
{
    private static SearchResult HeyPrivateRow( ) => new( )
    {
        Id = "hey:topic:9001",
        Title = "private mail sentinel",
        Kind = ResultKinds.Mail,
        SourceId = SourceIds.Hey,
        ProviderId = ProviderIds.Hey,
        Actions = [new( ) { Id = "open", Title = "Open in HEY", Kind = ActionKinds.OpenUrl }]
    };

    private static void PrivateSourcesCoverClipboardAndMail( )
    {
        var mail = HeyPrivateRow( );
        True( PrivateSources.IsPrivate( mail ) && PrivateSources.IsMail( mail ) );

        // Any one of the source, provider, or stable-id prefix marks a row as mail.
        True( PrivateSources.IsMail( new SearchResult { Id = "x", Title = "t", Kind = ResultKinds.Mail, SourceId = SourceIds.Hey } ) );
        True( PrivateSources.IsMail( new SearchResult { Id = "x", Title = "t", Kind = ResultKinds.Mail, SourceId = "other", ProviderId = ProviderIds.Hey } ) );
        True( PrivateSources.IsMail( new SearchResult { Id = "hey:fallback", Title = "t", Kind = ResultKinds.Mail, SourceId = "other" } ) );

        SearchResult clipboard = new( )
        {
            Id = "clipboard:1",
            Title = "t",
            Kind = ResultKinds.File,
            SourceId = SourceIds.Clipboard
        };
        True( PrivateSources.IsPrivate( clipboard ) && !PrivateSources.IsMail( clipboard ) );
        True( !PrivateSources.IsPrivate( new SearchResult { Id = "app:x", Title = "t", Kind = ResultKinds.Application, SourceId = SourceIds.Applications } ) );

        True( PrivateSources.ReferencesPrivateSource( new QueryExpression { ExcludeFilterIds = [SourceIds.Hey] } ) );
        True( PrivateSources.ReferencesPrivateSource( new QueryExpression { IncludeFilterIds = ["clipboard"] } ) );
        True( !PrivateSources.ReferencesPrivateSource( new QueryExpression { IncludeFilterIds = ["files"] } ) );
        True( PrivateSources.ReadsPrivateContent( ProviderIds.Hey ) && PrivateSources.ReadsPrivateContent( ProviderIds.Clipboard ) );
        True( !PrivateSources.ReadsPrivateContent( ProviderIds.Catalog ) );
    }

    // Suggestions and the assistant's search tool never start the HEY provider,
    // and a mail row from any provider is dropped before it can reach them.
    private static async Task HeyNoninteractiveSearchNeverRunsAsync( )
    {
        CountingSearchProvider hey = new( ProviderIds.Hey );
        var privateRow = HeyPrivateRow( );
        SearchCoordinator search = new( [hey, new StaticSearchProvider( [privateRow] )], new SearchOptions( ) );
        foreach ( var purpose in new[] { SearchPurpose.AssistantTool, SearchPurpose.Suggestion } )
        {
            foreach ( var query in new[] { "@hey invoice", "invoice", "private mail" } )
            {
                var results = await search.SearchAsync( new SearchRequest { Query = query }, purpose, default );
                Equal( 0, results.Count );
                Equal( 0, hey.Calls );
            }
        }

        Equal( AgentActionRisks.Denied, AgentLauncherTools.ClassifyRisk( privateRow, privateRow.Actions[0] ) );
    }

    private static async Task HeyAiAttachmentsAreRejectedAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-hey-ai-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await using AiProviderRegistry providers = new( OneLocalAiProvider( ), new TestAiTransportFactory( ) );
            var result = HeyPrivateRow( );
            using AiPreflightService service = new( providers,
                new FilePreviewService( new IndexingOptions { Roots = [] }, new PrivacyOptions { NeverIndex = [] } ),
                catalog.AiPreferences, ( _, _ ) => result );

            var exception = await CaptureInvalidOperationAsync( async ( ) => await service.CreateAsync( "owner", new AiDraft
            {
                ProviderId = "ollama",
                ModelId = "qwen",
                UserText = "Summarize",
                Attachments = [new( ) { Kind = AiAttachmentKinds.Result, ResultId = result.Id }]
            }, default ) );

            Equal( "HEY mail cannot be attached to AI requests.", exception?.Message );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task<Exception?> CaptureInvalidOperationAsync( Func<Task> action )
    {
        try
        {
            await action( );
            return null;
        }
        catch ( InvalidOperationException exception )
        {
            return exception;
        }
    }
}
