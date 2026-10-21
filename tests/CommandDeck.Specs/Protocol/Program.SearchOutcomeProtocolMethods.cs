namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SearchSocketReportsIncompleteWithoutDiagnosticsAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-search-outcome-" ).FullName;
        using CancellationTokenSource stop = new( );
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await catalog.LauncherPreferences.SetFilterAliasAsync( "inboxq", SourceIds.Hey, default );
            SearchCoordinator search = new( [new FailingSearchProvider( )], options.Search,
                catalog, catalog.LauncherPreferences );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory,
                new AcceptanceExecutor( ), search );
            var socket = SpecTemporaryFiles.SocketPath( root, "daemon.sock" );
            UnixProtocolServer server = new( runtime, socket );
            _ = server.RunAsync( stop.Token );
            try
            {
                await using var client = await ConnectWithRetryAsync( socket );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.Search,
                    Query = "ordinary"
                }, default );
                var messages = await CollectUntilDoneAsync( client, 1 );
                var completion = messages.Single( static message => message.Type == ProtocolMessageTypes.Done )
                    .SearchCompletion;

                True( completion is { Complete: false, IncompleteProviderCount: 1 },
                    "the socket must distinguish a failed provider from an empty complete search" );
                True( messages.All( static message =>
                    !ProtocolJson.SerializeMessage( message ).Contains( "private synthetic error", StringComparison.Ordinal ) ),
                    "search completion must not expose provider exception text" );

                await VerifySearchCompletionPrivacyAsync( client );
            }
            finally
            {
                await stop.CancelAsync( );
                await server.Completion;
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task VerifySearchCompletionPrivacyAsync( UnixProtocolClient client )
    {
        foreach ( var (query, isPrivate) in new[]
        {
            ("ordinary", false),
            ("@hey private-token", true),
            ("notes hey:private-token", true),
            ("notes inboxq:private-token", true),
            ("file: -clip: notes", true)
        } )
        {
            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 2,
                Operation = ProtocolOperations.Search,
                Query = query
            }, default );
            var completed = (await CollectUntilDoneAsync( client, 2 ))
                .Single( static message => message.Type == ProtocolMessageTypes.Done );
            using var json = System.Text.Json.JsonDocument.Parse( ProtocolJson.SerializeMessage( completed ) );
            True( json.RootElement.GetProperty( "searchCompletion" )
                .TryGetProperty( "containsPrivateQuery", out var privacy ),
                "search completion must classify the query before the frontend may retain it" );
            Equal( isPrivate, privacy.GetBoolean( ) );
        }
    }
}
