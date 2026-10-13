namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SearchSocketReportsIncompleteWithoutDiagnosticsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-search-outcome-" ).FullName;
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
            SearchCoordinator search = new( [new FailingSearchProvider( )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory,
                new AcceptanceExecutor( ), search );
            var socket = Path.Combine( root, "daemon.sock" );
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
}
