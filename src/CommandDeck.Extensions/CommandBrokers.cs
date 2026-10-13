using System.Collections.Immutable;
using System.Net;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions;

/// <summary>Bounded, installation-scoped file and HTTP services. No host credentials or paths are exposed.</summary>
public sealed class CommandBrokers
{
    private readonly HostChannel _channel;
    internal CommandBrokers( HostChannel channel ) => _channel = channel;

    public async ValueTask<FileListing> ListAsync( string connection, string? directory = null, int offset = 0, CancellationToken cancellationToken = default )
    {
        var result = await SendAsync( ExtensionBrokerOperations.ListFiles, connection, directory, offset, null, cancellationToken ).ConfigureAwait( false );
        RequireCompleted( result );
        if ( result.Entries is null )
        {
            throw new InvalidDataException( "Missing file listing." );
        }
        return new( result.Entries.Select( entry => new FileEntry( entry.Name, entry.Reference, entry.Directory, entry.Size ) ).ToImmutableArray( ), result.NextOffset );
    }
    public async ValueTask<FileContent> ReadAsync( string connection, string reference, CancellationToken cancellationToken = default )
    {
        var result = await SendAsync( ExtensionBrokerOperations.ReadFile, connection, reference, 0, null, cancellationToken ).ConfigureAwait( false );
        RequireCompleted( result );
        return new( DecodeBody( result ) );
    }
    public async ValueTask<HttpResult> SendHttpAsync( string connection, BrokerHttpRequest request, CancellationToken cancellationToken = default )
    {
        var result = await SendAsync( ExtensionBrokerOperations.SendHttp, connection, null, 0, request, cancellationToken ).ConfigureAwait( false );
        RequireCompleted( result );
        return new( result.StatusCode, result.ContentType, DecodeBody( result ) );
    }

    private static void RequireCompleted( BrokerResult result )
    {
        if ( result.Outcome != ExtensionBrokerOutcomes.Completed )
        {
            throw new BrokerOutcomeException( result.Outcome );
        }
    }
    private static byte[] DecodeBody( BrokerResult result )
    {
        if ( result.Body is null )
        {
            throw new InvalidDataException( "Missing broker body." );
        }
        try
        { return Convert.FromBase64String( result.Body ); }
        catch ( FormatException exception ) { throw new InvalidDataException( "Malformed broker body.", exception ); }
    }

    public HttpClient CreateHttpClient( string connection ) => new( new BrokerHandler( this, connection ) ) { BaseAddress = new Uri( "https://connection.invalid/" ), Timeout = Timeout.InfiniteTimeSpan };

    private ValueTask<BrokerResult> SendAsync( string operation, string connection, string? reference, int offset,
        BrokerHttpRequest? http, CancellationToken cancellationToken )
    {
        var stamp = _channel.Stamp;
        var request = new BrokerRequest( stamp.SessionId, stamp.Epoch, stamp.Generation, stamp.InvocationId, operation, connection, reference, offset, http );
        return _channel.CallAsync( ExtensionMethods.CommandBroker, ExtensionFeatures.Brokers, request,
            BrokerJsonContext.Default.BrokerRequest, BrokerJsonContext.Default.BrokerResult, cancellationToken );
    }

    private sealed class BrokerHandler( CommandBrokers broker, string connection ) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
        {
            if ( request.RequestUri is null || request.RequestUri.GetLeftPart( UriPartial.Authority ) != "https://connection.invalid" ||
                request.Headers.Any( header => header.Key != ExtensionHttpLimits.IdempotencyKeyHeader ) )
            {
                throw new InvalidOperationException( "Only connection-relative requests and Idempotency-Key are supported." );
            }
            byte[]? bytes = null;
            if ( request.Content is not null )
            {
                await request.Content.LoadIntoBufferAsync( ExtensionHttpLimits.MaximumRequestBytes, cancellationToken ).ConfigureAwait( false );
                bytes = await request.Content.ReadAsByteArrayAsync( cancellationToken ).ConfigureAwait( false );
            }
            var key = request.Headers.TryGetValues( ExtensionHttpLimits.IdempotencyKeyHeader, out var keys ) ? keys.Single( ) : null;
            HttpResult result;
            try
            {
                result = await broker.SendHttpAsync( connection, new( request.Method.Method, request.RequestUri.PathAndQuery,
                    bytes is null ? null : Convert.ToBase64String( bytes ), request.Content?.Headers.ContentType?.ToString( ), key ), cancellationToken ).ConfigureAwait( false );
            }
            catch ( BrokerOutcomeException exception ) { throw new HttpRequestException( exception.Message, exception ); }
            var response = new HttpResponseMessage( ( HttpStatusCode ) result.StatusCode ) { Content = new ByteArrayContent( result.Body.ToArray( ) ), RequestMessage = request };
            if ( result.ContentType is not null )
            {
                response.Content.Headers.TryAddWithoutValidation( "Content-Type", result.ContentType );
            }
            return response;
        }
    }
}
