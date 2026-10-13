using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal static class ExtensionHttpTransport
{
    internal const int MaximumRequest = ExtensionHttpLimits.MaximumRequestBytes, MaximumResponse = 128 * 1024;
    internal static Uri Origin( string value, bool privateNetwork )
    {
        if ( value is not { Length: > 0 and <= 2048 } || !Uri.TryCreate( value, UriKind.Absolute, out var uri ) || uri.UserInfo.Length != 0 ||
            uri.Fragment.Length != 0 || uri.Query.Length != 0 || uri.AbsolutePath != "/" || uri.Scheme is not ("https" or "http") ||
            uri.Scheme != "https" && !privateNetwork || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6) )
        {
            throw new InvalidOperationException( "Use an HTTPS origin without a path, query, or credentials." );
        }
        if ( privateNetwork && (!IPAddress.TryParse( uri.Host.Trim( '[', ']' ), out var address ) || !ExtensionAddressPolicy.PrivateAddress( address )) )
        {
            throw new InvalidOperationException( "Private connections require an explicit private IP literal." );
        }
        return new Uri( uri.GetLeftPart( UriPartial.Authority ) + "/" );
    }

    internal static bool PublicAddress( IPAddress address ) => ExtensionAddressPolicy.PublicAddress( address );

    internal static string PathValue( string path )
    {
        if ( path is not { Length: > 0 and <= 2048 } || !path.StartsWith( '/' ) || path.StartsWith( "//", StringComparison.Ordinal ) ||
            path.Any( character => character > 127 || char.IsControl( character ) || character is '\\' or '#' or '%' ) ||
            path.Split( '?' )[0].Split( '/' ).Any( part => part is "." or ".." ) )
        {
            throw new InvalidOperationException( "Use a plain connection-relative route without escapes or traversal." );
        }
        return path;
    }
    internal static Uri RequestUri( ExtensionConnectionSetup setup, BrokerHttpRequest request )
    {
        if ( !setup.Methods.Contains( request.Method, StringComparer.Ordinal ) )
        {
            throw new InvalidOperationException( "The connection does not allow this HTTP method." );
        }
        var path = PathValue( request.Path );
        var route = setup.Route.TrimEnd( '/' );
        var origin = Origin( setup.Origin!, setup.PrivateNetwork );
        var uri = new Uri( origin, path );
        // System.Uri canonicalizes before transport (including trailing whitespace).
        // Authorize that destination, never the caller's pre-normalized spelling.
        var target = uri.AbsolutePath;
        if ( uri.GetLeftPart( UriPartial.Authority ) != origin.GetLeftPart( UriPartial.Authority ) )
        {
            throw new InvalidOperationException( "The destination is outside the connection scope." );
        }
        if ( route.Length > 0 && target != route && !target.StartsWith( route + "/", StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "The route is outside the connection scope." );
        }
        return uri;
    }
    internal static byte[] Body( BrokerHttpRequest request )
    {
        if ( request.IdempotencyKey is not null && (request.IdempotencyKey.Length is < 16 or > 128 || request.IdempotencyKey.Any( character => !char.IsAsciiLetterOrDigit( character ) && character is not ('-' or '_') )) )
        {
            throw new InvalidOperationException( "Invalid idempotency key." );
        }
        if ( request.Body?.Length > ExtensionHttpLimits.Base64Length( ExtensionHttpLimits.MaximumRequestBytes ) )
        {
            throw new InvalidOperationException( "Request body exceeds 64 KiB." );
        }
        byte[] body;
        try
        {
            body = Convert.FromBase64String( request.Body ?? "" );
        }
        catch ( FormatException )
        {
            throw new InvalidOperationException( "The request body is not base64." );
        }
        if ( body.Length > MaximumRequest || request.ContentType?.Length > 128 ||
            request.ContentType is not null && !MediaTypeHeaderValue.TryParse( request.ContentType, out _ ) )
        {
            throw new InvalidOperationException( "Invalid request body or content type." );
        }
        return body;
    }

    internal static HttpClient Create( Uri origin, bool privateNetwork )
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxResponseHeadersLength = 16,
            ConnectTimeout = TimeSpan.FromSeconds( 5 ),
            ConnectCallback = async ( context, token ) =>
            {
                if ( context.DnsEndPoint.Host != origin.IdnHost || context.DnsEndPoint.Port != origin.Port )
                {
                    throw new HttpRequestException( "Destination changed." );
                }
                var addresses = await Dns.GetHostAddressesAsync( context.DnsEndPoint.Host, token ).ConfigureAwait( false );
                if ( addresses.Length is < 1 or > 32 || addresses.Any( address => privateNetwork ? !ExtensionAddressPolicy.PrivateAddress( address ) : !PublicAddress( address ) ) )
                {
                    throw new HttpRequestException( "Destination address is outside the connection policy." );
                }
                var address = addresses[0];
                if ( privateNetwork && !address.Equals( IPAddress.Parse( origin.Host.Trim( '[', ']' ) ) ) )
                {
                    throw new HttpRequestException( "Private destination changed." );
                }
                var socket = new Socket( address.AddressFamily, SocketType.Stream, ProtocolType.Tcp );
                try
                {
                    await socket.ConnectAsync( new IPEndPoint( address, origin.Port ), token ).ConfigureAwait( false );
                    return new NetworkStream( socket, true );
                }
                catch
                {
                    socket.Dispose( );
                    throw;
                }
            }
        };
        return new HttpClient( handler ) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal static async ValueTask<BrokerResult> SendAsync( HttpClient client, Uri uri, BrokerHttpRequest request, string? bearer, CancellationToken token )
    {
        using var message = new HttpRequestMessage( new HttpMethod( request.Method ), uri ) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var body = Body( request );
        if ( request.Body is not null )
        {
            message.Content = new ByteArrayContent( body );
            message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse( request.ContentType ?? "application/octet-stream" );
        }
        if ( bearer is not null )
        {
            message.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", bearer );
        }
        if ( request.IdempotencyKey is not null )
        {
            message.Headers.Add( ExtensionHttpLimits.IdempotencyKeyHeader, request.IdempotencyKey );
        }
        using var response = await client.SendAsync( message, HttpCompletionOption.ResponseHeadersRead, token ).ConfigureAwait( false );
        if ( ( int ) response.StatusCode is >= 300 and < 400 )
        {
            return new( ExtensionBrokerOutcomes.RedirectDenied );
        }
        var bytes = await ReadResponseAsync( response, token ).ConfigureAwait( false );
        if ( bearer is not null && System.Text.Encoding.UTF8.GetString( bytes ).Contains( bearer, StringComparison.Ordinal ) )
        {
            return new( ExtensionBrokerOutcomes.CredentialEchoDenied );
        }
        var contentType = response.Content.Headers.ContentType?.ToString( );
        if ( contentType?.Length > 128 )
        {
            throw new InvalidDataException( "Response content type exceeds the limit." );
        }
        return new( ExtensionBrokerOutcomes.Completed, Body: Convert.ToBase64String( bytes ), StatusCode: ( int ) response.StatusCode, ContentType: contentType );
    }
    internal static async ValueTask<byte[]> ReadResponseAsync( HttpResponseMessage response, CancellationToken token )
    {
        var encodings = response.Content.Headers.ContentEncoding.ToArray( );
        if ( encodings.Length > 1 || encodings.Length == 1 && encodings[0] != "gzip" )
        {
            throw new InvalidDataException( "Unsupported response encoding." );
        }
        await using var stream = await response.Content.ReadAsStreamAsync( token ).ConfigureAwait( false );
        await using var decompressed = encodings.Length == 1 ? new GZipStream( stream, CompressionMode.Decompress, true ) : null;
        var input = ( Stream? ) decompressed ?? stream;
        var buffer = new byte[MaximumResponse + 1];
        var offset = 0;
        while ( offset < buffer.Length )
        {
            var count = await input.ReadAsync( buffer.AsMemory( offset ), token ).ConfigureAwait( false );
            if ( count == 0 )
            {
                break;
            }
            offset += count;
        }
        if ( offset > MaximumResponse )
        {
            throw new InvalidDataException( "The response exceeds 128 KiB." );
        }
        return buffer[..offset];
    }
}
