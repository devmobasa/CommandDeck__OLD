namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class BlockingProtocolProvider : ICommandProvider
    {
        public string Id => "blocking-protocol";
        public TaskCompletionSource Entered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            Entered.TrySetResult( );
            await Release.Task.WaitAsync( cancellationToken );
            return [];
        }
    }

    private static async Task DuplicateActiveRequestClosesSocketAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-duplicate-request-" ).FullName;
        using CancellationTokenSource serverCancellation = new( );
        BlockingProtocolProvider provider = new( );
        try
        {
            var socket = Path.Combine( root, "daemon.sock" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            SearchCoordinator search = new( [provider], options.Search );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory,
                new RecordingInvocationExecutor( ), search );
            UnixProtocolServer server = new( runtime, socket );
            var serverTask = server.RunAsync( serverCancellation.Token );
            try
            {
                await using var client = await ConnectWithRetryAsync( socket );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 51,
                    Operation = ProtocolOperations.Search,
                    Query = "first"
                }, default );
                await provider.Entered.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 51,
                    Operation = ProtocolOperations.Search,
                    Query = "second"
                }, default );
                using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
                List<ProtocolMessage> frames = [];
                await foreach ( var line in client.ReadRawMessagesAsync( timeout.Token ) )
                {
                    var message = DeserializeMessage( line );
                    if ( message is not null )
                    {
                        frames.Add( message );
                    }
                }

                Equal( 1, frames.Count( frame => frame.Type == ProtocolMessageTypes.Error && frame.RequestId == 0 ) );
                True( frames.Count( frame => frame.Type == ProtocolMessageTypes.Done && frame.RequestId == 51 ) <= 1,
                    "a duplicate must not emit a second terminal response under the original ID" );
            }
            finally
            {
                provider.Release.TrySetResult( );
                serverCancellation.Cancel( );
                await serverTask;
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
