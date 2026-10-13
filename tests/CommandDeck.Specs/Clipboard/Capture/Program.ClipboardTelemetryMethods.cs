using System.Collections.Concurrent;
using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task VerifyClipboardSearchTelemetryAsync( UnixProtocolClient client, CancellationToken cancellationToken )
    {
        ConcurrentQueue<string> lines = new( );
        var oldSink = AppLog.Sink;
        var oldVerbose = AppLog.Verbose;
        AppLog.Sink = ( _, message ) => lines.Enqueue( message );
        AppLog.Verbose = true;
        try
        {
            long id = 700;
            foreach ( var query in new[] { "clip: sentinel", "clip: app: sentinel", "-clip: sentinel", "@clipboard sentinel", "cb: sentinel", "-cb: sentinel" } )
            {
                await client.SendAsync( new( )
                {
                    RequestId = ++id,
                    Operation = ProtocolOperations.Search,
                    Query = query
                }, cancellationToken );
                await ReadMessageAsync( client, id, ProtocolMessageTypes.Done, cancellationToken );
                True( !lines.Any( static line => line.StartsWith( "event=search.", StringComparison.Ordinal ) ||
                    line.StartsWith( "event=provider.", StringComparison.Ordinal ) ), "private socket searches emit no timing/count telemetry" );
            }

            await client.SendAsync( new( )
            {
                RequestId = ++id,
                Operation = ProtocolOperations.Search,
                Query = "app: ordinary"
            }, cancellationToken );
            await ReadMessageAsync( client, id, ProtocolMessageTypes.Done, cancellationToken );
            True( lines.Any( line => line.StartsWith( $"event=search.complete request_id={id} ", StringComparison.Ordinal ) ),
                "ordinary search diagnostics remain available" );
        }
        finally
        {
            AppLog.Sink = oldSink;
            AppLog.Verbose = oldVerbose;
        }
    }

    private static async Task ClipboardSearchTelemetryRejectsPrivateAndUnclassifiedAsync( )
    {
        List<SearchProviderDiagnostic> diagnostics = [];
        SearchCoordinator search = new( [new TelemetryFailureProvider( )], new SearchOptions( ),
            providerDiagnostic: diagnostic => diagnostics.Add( diagnostic ) );
        var ordinary = new SearchRequest { Query = "ordinary" };
        await search.SearchAsync( ordinary, default );
        Equal( 1, diagnostics.Count );
        Equal( SearchProviderOutcomes.Failed, diagnostics[0].Outcome );
        diagnostics.Clear( );

        var privateRequest = new SearchRequest { Query = "-clip: sentinel" };
        await search.SearchAsync( privateRequest, default );
        True( privateRequest.IsPrivacyClassified && privateRequest.ContainsPrivateQuery );
        Equal( 0, diagnostics.Count );
        List<string> lines = [];
        var oldSink = AppLog.Sink;
        var oldVerbose = AppLog.Verbose;
        AppLog.Sink = ( _, message ) => lines.Add( message );
        AppLog.Verbose = true;
        try
        {
            foreach ( var status in new[] { SearchLogging.CompletedStatus, SearchLogging.CancelledStatus, SearchLogging.FailedStatus } )
            {
                SearchLogging.LogSearchCompletion( privateRequest, 1, Stopwatch.GetTimestamp( ), 20, status );
                SearchLogging.LogSearchCompletion( new SearchRequest { Query = "unresolved-alias: sentinel" },
                    2, Stopwatch.GetTimestamp( ), 20, status );
            }

            Equal( 0, lines.Count );
            SearchLogging.LogSearchCompletion( ordinary, 3, Stopwatch.GetTimestamp( ), 0, SearchLogging.CompletedStatus );
            Equal( 1, lines.Count );
        }
        finally
        {
            AppLog.Sink = oldSink;
            AppLog.Verbose = oldVerbose;
        }
    }

    private sealed class TelemetryFailureProvider : ICommandProvider
    {
        public string Id => "catalog";
        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
        {
            throw new IOException( "synthetic provider failure" );
        }
    }
}
