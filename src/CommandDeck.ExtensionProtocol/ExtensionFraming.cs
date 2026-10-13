using System.Buffers;
using System.Text.Json;

namespace CommandDeck.ExtensionProtocol;

/// <summary>Strict, bounded JSONL framing. Overflow fails immediately without draining hostile input.</summary>
public sealed class ExtensionFraming( Stream input )
{
    // JSONL is carried only over the private RPC pipe, never embedded in HTML.
    // Keep base64 '+' characters literal so 128 KiB responses fit the frame budget.
    private static readonly ExtensionJsonContext WireJson = new( new JsonSerializerOptions( ExtensionJsonContext.Default.Options )
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    } );
    private readonly byte[] _buffer = new byte[4096];
    private int _position;
    private int _length;
    public int LastFrameBytes { get; private set; }

    public async ValueTask<ExtensionFrame?> ReadAsync( CancellationToken cancellationToken )
    {
        ArrayBufferWriter<byte> line = new( 4096 );
        while ( true )
        {
            if ( _position == _length )
            {
                _length = await input.ReadAsync( _buffer, cancellationToken ).ConfigureAwait( false );
                _position = 0;
                if ( _length == 0 )
                {
                    if ( line.WrittenCount != 0 )
                    {
                        throw new InvalidDataException( "An extension frame ended without a newline." );
                    }
                    return null;
                }
            }

            var newline = Array.IndexOf( _buffer, ( byte ) '\n', _position, _length - _position );
            var count = (newline < 0 ? _length : newline) - _position;
            if ( line.WrittenCount + count > ExtensionLimits.MaximumFrameBytes )
            {
                throw new InvalidDataException( "The extension frame exceeds its byte limit." );
            }
            line.Write( _buffer.AsSpan( _position, count ) );
            _position += count;
            if ( newline >= 0 )
            {
                _position++;
                LastFrameBytes = line.WrittenCount + 1;
                return Parse( line.WrittenMemory );
            }
        }
    }

    public static ExtensionFrame Parse( ReadOnlyMemory<byte> bytes )
    {
        if ( bytes.Length > ExtensionLimits.MaximumFrameBytes )
        {
            throw new InvalidDataException( "The extension frame exceeds its byte limit." );
        }
        // JsonDocument validates token syntax; strict decoding also rejects invalid UTF-8 inside strings.
        using var document = StrictJson.Parse( bytes, ExtensionLimits.MaximumDepth );
        var root = document.RootElement;
        if ( root.ValueKind != JsonValueKind.Object )
        {
            throw new InvalidDataException( "An extension frame must be an object." );
        }
        var frame = root.Deserialize( ExtensionJsonContext.Default.ExtensionFrame );
        if ( frame is null )
        {
            throw new InvalidDataException( "An extension frame is missing." );
        }

        if ( !root.TryGetProperty( "jsonrpc", out _ ) || frame.Jsonrpc != "2.0" )
        {
            throw new InvalidDataException( "The extension JSON-RPC version is unsupported." );
        }
        ValidateEnvelope( root, frame );
        if ( root.TryGetProperty( "error", out var error ) && error.TryGetProperty( "data", out var errorData ) && errorData.ValueKind != JsonValueKind.Object )
        {
            throw new InvalidDataException( "Extension error data must be an object." );
        }
        if ( frame.Error?.Data is { } data && (data.Code is not { Length: > 0 and <= 48 } || data.Code[0] is < 'a' or > 'z' ||
            data.Code.Any( character => character is not (>= 'a' and <= 'z') and not '_' )) )
        {
            throw new InvalidDataException( "Invalid extension error data." );
        }
        return frame;
    }

    private static void ValidateEnvelope( JsonElement root, ExtensionFrame frame )
    {
        var hasId = root.TryGetProperty( "id", out _ );
        if ( hasId && (frame.Id is not { Length: > 2 and <= 64 } ||
            !(frame.Id.StartsWith( "h:", StringComparison.Ordinal ) || frame.Id.StartsWith( "e:", StringComparison.Ordinal )) ||
            frame.Id.Any( c => !char.IsAsciiLetterOrDigit( c ) && c is not ':' and not '-' and not '_' )) )
        {
            throw new InvalidDataException( "An extension request ID is invalid." );
        }
        var hasMethod = root.TryGetProperty( "method", out _ );
        var hasResult = root.TryGetProperty( "result", out _ );
        var hasError = root.TryGetProperty( "error", out _ );
        var hasParams = root.TryGetProperty( "params", out _ );
        if ( hasMethod )
        {
            if ( frame.Method is not { Length: > 0 and <= 64 } || hasResult || hasError ||
                (hasParams && frame.Params?.ValueKind != JsonValueKind.Object) )
            {
                throw new InvalidDataException( "An extension request envelope is invalid." );
            }
        }
        else if ( !hasId || hasParams || hasResult == hasError ||
                  (hasError && (frame.Error is null || frame.Error.Message is not { Length: <= 512 })) )
        {
            throw new InvalidDataException( "An extension response envelope is invalid." );
        }
    }

    public static byte[] Serialize( ExtensionFrame frame )
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes( frame, WireJson.ExtensionFrame );
        _ = Parse( bytes );
        var line = new byte[bytes.Length + 1];
        bytes.CopyTo( line, 0 );
        line[^1] = ( byte ) '\n';
        return line;
    }
}
