namespace CommandDeck;

public static class StdioBridge
{
    public static async Task<int> RunAsync(
        string? socketPath,
        CancellationToken cancellationToken )
    {
        await using var client =
            await UnixProtocolClient.ConnectAsync( socketPath, cancellationToken )
                .ConfigureAwait( false );
        using var lifetime =
            CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        using var standardInput = Console.OpenStandardInput( );
        BoundedLineReader input = new(
            standardInput,
            ProtocolLimits.MaximumRequestBytes );
        BridgeState state = new( );

        var responses = PumpResponsesAsync( client, state, lifetime.Token );
        var requests = PumpRequestsAsync( client, input, state, lifetime.Token );
        try
        {
            var completed = await Task.WhenAny(
                requests,
                responses,
                state.AllRequestsCompleted ).ConfigureAwait( false );
            if ( completed == requests )
            {
                await requests.ConfigureAwait( false );
                completed = await Task.WhenAny( responses, state.AllRequestsCompleted )
                    .ConfigureAwait( false );
            }

            if ( completed == responses )
            {
                await responses.ConfigureAwait( false );
            }
        }
        finally
        {
            lifetime.Cancel( );
            try
            {
                await Task.WhenAll( requests, responses ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( lifetime.IsCancellationRequested )
            {
            }
        }

        return 0;
    }

    private static async Task PumpRequestsAsync(
        UnixProtocolClient client,
        BoundedLineReader input,
        BridgeState state,
        CancellationToken cancellationToken )
    {
        try
        {
            while ( !cancellationToken.IsCancellationRequested )
            {
                string? line;
                try
                {
                    line = await input.ReadLineAsync( cancellationToken ).ConfigureAwait( false );
                }
                catch ( Exception exception ) when (
                    exception is InvalidDataException or PayloadTooLargeException )
                {
                    await WriteErrorAsync( exception.Message, cancellationToken ).ConfigureAwait( false );
                    continue;
                }

                if ( line is null )
                {
                    break;
                }

                ProtocolRequest? request;
                try
                {
                    request = ProtocolJson.DeserializeRequest( line );
                    if ( request is not null )
                    {
                        ProtocolRequestValidator.Validate( request, deferFilterResolution: true );
                    }
                }
                catch ( Exception exception ) when (
                    exception is System.Text.Json.JsonException or InvalidOperationException )
                {
                    await WriteErrorAsync( exception.Message, cancellationToken ).ConfigureAwait( false );
                    continue;
                }

                if ( request is null )
                {
                    continue;
                }

                state.RequestStarted( );
                try
                {
                    await client.SendAsync( request, cancellationToken ).ConfigureAwait( false );
                }
                catch
                {
                    state.RequestFinished( );
                    throw;
                }
            }
        }
        finally
        {
            state.InputCompleted( );
        }
    }

    private static async Task PumpResponsesAsync(
        UnixProtocolClient client,
        BridgeState state,
        CancellationToken cancellationToken )
    {
        await foreach ( var response in client.ReadRawMessagesAsync(
            cancellationToken ).ConfigureAwait( false ) )
        {
            await Console.Out.WriteLineAsync(
                response.AsMemory( ),
                cancellationToken ).ConfigureAwait( false );
            await Console.Out.FlushAsync( cancellationToken ).ConfigureAwait( false );

            try
            {
                var message = System.Text.Json.JsonSerializer.Deserialize(
                    response,
                    CommandDeckJsonContext.Default.ProtocolMessage );
                if ( message?.Type == ProtocolMessageTypes.Done )
                {
                    state.RequestFinished( );
                }
            }
            catch ( System.Text.Json.JsonException )
            {
            }
        }
    }

    private static async ValueTask WriteErrorAsync(
        string message,
        CancellationToken cancellationToken )
    {
        var response = ProtocolJson.SerializeMessage( new ProtocolMessage
        {
            RequestId = 0,
            Type = ProtocolMessageTypes.Error,
            Error = message
        } );
        await Console.Out.WriteLineAsync(
            response.AsMemory( ),
            cancellationToken ).ConfigureAwait( false );
    }

    private sealed class BridgeState
    {
        private readonly TaskCompletionSource _completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously );
        private int _outstanding;
        private int _inputCompleted;

        public Task AllRequestsCompleted => _completed.Task;

        public void RequestStarted( ) => Interlocked.Increment( ref _outstanding );

        public void RequestFinished( )
        {
            var remaining = Interlocked.Decrement( ref _outstanding );
            if ( remaining < 0 )
            {
                Interlocked.Exchange( ref _outstanding, 0 );
                return;
            }

            TryComplete( remaining );
        }

        public void InputCompleted( )
        {
            Interlocked.Exchange( ref _inputCompleted, 1 );
            TryComplete( Volatile.Read( ref _outstanding ) );
        }

        private void TryComplete( int outstanding )
        {
            if ( outstanding == 0 && Volatile.Read( ref _inputCompleted ) == 1 )
            {
                _completed.TrySetResult( );
            }
        }
    }
}
