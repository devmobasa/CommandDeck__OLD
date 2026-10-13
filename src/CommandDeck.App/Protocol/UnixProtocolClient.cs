using System.Net.Sockets;
using System.Text;

namespace CommandDeck;

public sealed class UnixProtocolClient : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly BoundedLineReader _boundedReader;
    private readonly StreamWriter _writer;

    private UnixProtocolClient( Socket socket )
    {
        _socket = socket;
        _stream = new NetworkStream( socket, ownsSocket: false );
        _boundedReader = new BoundedLineReader(
            _stream,
            ProtocolLimits.MaximumResponseBytes );
        _writer = new StreamWriter(
            _stream,
            new UTF8Encoding( false ),
            bufferSize: 16_384,
            leaveOpen: true )
        {
            AutoFlush = true
        };
    }

    public static async ValueTask<UnixProtocolClient> ConnectAsync(
        string? socketPath,
        CancellationToken cancellationToken )
    {
        var resolved = string.IsNullOrWhiteSpace( socketPath )
            ? PathExpansion.GetDefaultSocketPath( )
            : PathExpansion.Expand( socketPath );

        Socket socket = new(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified );
        try
        {
            await socket.ConnectAsync(
                new UnixDomainSocketEndPoint( resolved ),
                cancellationToken ).ConfigureAwait( false );
            return new UnixProtocolClient( socket );
        }
        catch
        {
            socket.Dispose( );
            throw;
        }
    }

    public async ValueTask SendAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        var line = ProtocolJson.SerializeRequest( request );
        await _writer.WriteLineAsync( line.AsMemory( ), cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask SendRawAsync(
        string line,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( line );
        await _writer.WriteLineAsync( line.AsMemory( ), cancellationToken ).ConfigureAwait( false );
    }

    public async IAsyncEnumerable<string> ReadRawMessagesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken )
    {
        while ( true )
        {
            var line = await _boundedReader.ReadLineAsync( cancellationToken ).ConfigureAwait( false );
            if ( line is null )
            {
                yield break;
            }

            yield return line;
        }
    }

    public async ValueTask DisposeAsync( )
    {
        await _writer.DisposeAsync( ).ConfigureAwait( false );
        await _stream.DisposeAsync( ).ConfigureAwait( false );
        _socket.Dispose( );
    }
}
