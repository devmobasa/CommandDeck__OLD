namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task VerifyDisabledContextProtocolAsync(
        UnixProtocolClient client,
        string selectedProject,
        CancellationToken cancellationToken )
    {
        System.Collections.Concurrent.ConcurrentQueue<string> logLines = [];
        var verbose = AppLog.Verbose;
        AppLog.Sink = ( _, message ) => logLines.Enqueue( message );
        AppLog.Verbose = true;

        string[] withoutContext;
        try
        {
            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 185,
                Operation = ProtocolOperations.Search,
                Query = "context report",
                Limit = 20
            }, CancellationToken.None );
            withoutContext = await ReadResultIdsAsync( client, 185, cancellationToken );

            // The server logs after Done reaches the socket, so wait for the line.
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( logLines.Any( static line =>
                    line.StartsWith( "event=search.complete request_id=185 ", StringComparison.Ordinal ) ) ),
                TimeSpan.FromSeconds( 2 ) ) );
        }
        finally
        {
            AppLog.Sink = null;
            AppLog.Verbose = verbose;
        }

        var completion = logLines.Single( static line =>
            line.StartsWith( "event=search.complete request_id=185 ", StringComparison.Ordinal ) );
        True( completion.Contains( " query_chars=14 ", StringComparison.Ordinal ) );
        True( completion.Contains( $" result_count={withoutContext.Length} ", StringComparison.Ordinal ) );
        True( completion.EndsWith( " status=completed", StringComparison.Ordinal ) );
        True( !completion.Contains( "context report", StringComparison.Ordinal ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 186,
            Operation = ProtocolOperations.Search,
            Query = "context report",
            Limit = 20,
            ActiveApplication = "untrusted-app",
            ActiveWorkspace = "99",
            CurrentDirectory = selectedProject,
            SelectedProject = selectedProject
        }, CancellationToken.None );
        var ignoredContext = await ReadResultIdsAsync( client, 186, cancellationToken );

        True( withoutContext.Length >= 2 );
        Equal( string.Join( '|', withoutContext ), string.Join( '|', ignoredContext ) );
    }

    private static async Task<string[]> ReadResultIdsAsync(
        UnixProtocolClient client,
        long requestId,
        CancellationToken cancellationToken )
    {
        List<string> ids = [];
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId != requestId )
            {
                continue;
            }
            if ( message.Type == ProtocolMessageTypes.Result && message.Result is not null )
            {
                ids.Add( message.Result.Id );
            }
            if ( message.Type == ProtocolMessageTypes.Error )
            {
                throw new InvalidOperationException( message.Error ?? "Context search failed." );
            }
            if ( message.Type == ProtocolMessageTypes.Done )
            {
                break;
            }
        }
        return ids.ToArray( );
    }
}
