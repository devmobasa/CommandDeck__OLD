using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task LocalSuggestionsAreBoundedPrivateAndNetworkSilentAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-suggestion-spec-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        TcpListener listener = new( IPAddress.Loopback, 0 );
        listener.Start( );
        try
        {
            var files = Path.Combine( root, "files" );
            Directory.CreateDirectory( files );
            for ( var index = 0; index < 12; index++ )
            {
                await File.WriteAllTextAsync(
                    Path.Combine( files, $"roadmap-{index:D2}.md" ),
                    $"roadmap content {index}" );
            }
            var port = (( IPEndPoint ) listener.LocalEndpoint).Port;
            var config = Path.Combine( root, "config.json" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Indexing = new IndexingOptions
                {
                    Roots = [new IndexRootOptions { Path = files }],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Privacy = new PrivacyOptions
                {
                    StoreUsageHistory = true,
                    StoreQueryHistory = true,
                    NeverIndex = []
                },
                Quicklinks =
                [
                    new QuicklinkOptions
                    {
                        Id = "network-probe",
                        Title = "Network probe",
                        UrlTemplate = $"http://127.0.0.1:{port}/?q={{query}}"
                    }
                ],
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync(
                config,
                CancellationToken.None );
            await runtime.Indexer.ReindexAsync( progress: null, CancellationToken.None );
            await UsageHistoryOf( runtime ).RecordInvocationAsync(
                "file:history",
                "open",
                "private roadmap query",
                CancellationToken.None );

            var snapshot = await runtime.SuggestAsync(
                new SearchRequest
                {
                    Query = "road",
                    Expression = new QueryExpression { IncludeFilterIds = ["files"] },
                    Limit = 50
                },
                CancellationToken.None );
            True( snapshot.Suggestions.Count is > 0 and <= 8 );
            Equal( "history", snapshot.Suggestions[0].Source );
            True( snapshot.Suggestions.All( value =>
                value.Expression.IncludeFilterIds.Contains( "files", StringComparer.Ordinal ) ) );
            True( snapshot.Suggestions.Where( static value => value.Source == "catalog" ).All( static value =>
                value.Text.StartsWith( "road", StringComparison.OrdinalIgnoreCase ) ),
                "catalog suggestions complete the typed word by prefix" );
            var sentence = await runtime.SuggestAsync(
                new SearchRequest { Query = "a what is this", Limit = 8 },
                CancellationToken.None );
            True( sentence.Suggestions.All( static value => value.Source != "catalog" ),
                "a multi-word query never receives result titles as completions" );
            var prompt = await runtime.SuggestAsync(
                new SearchRequest { Query = "ai: what is this", Limit = 8 },
                CancellationToken.None );
            Equal( 0, prompt.Suggestions.Count );
            True( !listener.Pending( ) );

            var disabledConfig = Path.Combine( root, "disabled-config.json" );
            CommandDeckOptions disabledOptions = new( )
            {
                Storage = new StorageOptions
                {
                    DatabasePath = Path.Combine( root, "disabled-catalog.db" )
                },
                Indexing = options.Indexing,
                Privacy = new PrivacyOptions
                {
                    StoreUsageHistory = true,
                    StoreQueryHistory = false,
                    NeverIndex = []
                },
                Quicklinks = options.Quicklinks,
                Ai = options.Ai
            };
            await File.WriteAllTextAsync(
                disabledConfig,
                JsonSerializer.Serialize(
                    disabledOptions,
                    CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var disabledRuntime = await CommandDeckRuntime.CreateAsync(
                disabledConfig,
                CancellationToken.None );
            await UsageHistoryOf( disabledRuntime ).RecordInvocationAsync(
                "file:hidden-history",
                "open",
                "private-token-unique",
                CancellationToken.None );
            var disabled = await disabledRuntime.SuggestAsync(
                new SearchRequest { Query = "private-token-unique", Limit = 8 },
                CancellationToken.None );
            True( disabled.Suggestions.All( value => value.Source != "history" ) );
            True( !listener.Pending( ) );

            using CancellationTokenSource cancelled = new( );
            cancelled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) =>
                await runtime.SuggestAsync(
                    new SearchRequest { Query = "road", Limit = 8 },
                    cancelled.Token ) );
        }
        finally
        {
            listener.Stop( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task VerifySuggestionsProtocolAsync(
        UnixProtocolClient client,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 194,
            Operation = ProtocolOperations.Suggest,
            Query = "preview",
            Expression = new QueryExpression { IncludeFilterIds = ["files"] },
            Limit = 8
        }, CancellationToken.None );
        var message = await ReadMessageAsync(
            client,
            194,
            ProtocolMessageTypes.SuggestionsResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( message?.Suggestions );
        True( message.Suggestions.Suggestions.Count is > 0 and <= 8 );
        True( message.Suggestions.Suggestions.All( value => value.Source is "catalog" or "history" ) );
    }
}
