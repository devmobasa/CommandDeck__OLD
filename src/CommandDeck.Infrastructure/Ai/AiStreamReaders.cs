using System.Text;
using System.Text.Json;

namespace CommandDeck;

public sealed class AiIdleTimeoutStream : Stream
{
    private readonly Stream _inner;
    private readonly TimeSpan _idleTimeout;

    public AiIdleTimeoutStream( Stream inner, TimeSpan idleTimeout )
    {
        ArgumentNullException.ThrowIfNull( inner );
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual( idleTimeout, TimeSpan.Zero );
        _inner = inner;
        _idleTimeout = idleTimeout;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException( );
    public override long Position
    {
        get => throw new NotSupportedException( );
        set => throw new NotSupportedException( );
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default )
    {
        using var idle =
            CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        idle.CancelAfter( _idleTimeout );
        try
        {
            return await _inner.ReadAsync( buffer, idle.Token ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
        {
            throw new TimeoutException( "The AI provider stream exceeded its idle timeout." );
        }
    }

    public override int Read( byte[] buffer, int offset, int count ) =>
        throw new NotSupportedException( );
    public override void Flush( ) => throw new NotSupportedException( );
    public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
    public override void SetLength( long value ) => throw new NotSupportedException( );
    public override void Write( byte[] buffer, int offset, int count ) =>
        throw new NotSupportedException( );
    protected override void Dispose( bool disposing )
    {
        if ( disposing )
        {
            _inner.Dispose( );
        }
        base.Dispose( disposing );
    }
    public override async ValueTask DisposeAsync( )
    {
        await _inner.DisposeAsync( ).ConfigureAwait( false );
        await base.DisposeAsync( ).ConfigureAwait( false );
        GC.SuppressFinalize( this );
    }
}

public static class OllamaNdjsonReader
{
    public static async IAsyncEnumerable<AiProviderStreamEvent> ReadAsync(
        Stream stream,
        string expectedModel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken )
    {
        BoundedLineReader lines = new( stream, 64 * 1024 );
        var terminal = false;
        while ( await lines.ReadLineAsync( cancellationToken ).ConfigureAwait( false ) is string line )
        {
            if ( string.IsNullOrWhiteSpace( line ) )
            {
                continue;
            }

            var frame = ParseFrame( line, expectedModel );
            if ( !string.IsNullOrEmpty( frame.Text ) )
            {
                yield return new AiProviderStreamEvent { Text = frame.Text };
            }
            if ( frame.Done )
            {
                terminal = true;
                yield return new AiProviderStreamEvent
                {
                    Terminal = true,
                    FinishCategory = frame.Finish,
                    Usage = frame.Usage
                };
                break;
            }
        }
        if ( !terminal )
        {
            throw new EndOfStreamException( "Ollama stream ended before its terminal object." );
        }
    }

    private static OllamaStreamFrame ParseFrame( string line, string expectedModel )
    {
        using var document = JsonDocument.Parse( line );
        var root = document.RootElement;
        if ( root.ValueKind != JsonValueKind.Object )
        {
            throw new InvalidDataException( "Ollama stream event must be a JSON object." );
        }
        if ( AiProviderOutputPolicy.ContainsUnsupportedField( root ) )
        {
            throw AiProviderOutputPolicy.Unsupported( );
        }

        ValidateModel( root, expectedModel );
        var text = ReadMessageContent( root );
        var done = ReadBoolean( root, "done" );
        var finish = ReadOptionalString( root, "done_reason" ) ?? "stop";
        if ( AiProviderOutputPolicy.IsUnsupportedFinishReason( finish ) )
        {
            throw AiProviderOutputPolicy.Unsupported( );
        }
        var input = ReadLong( root, "prompt_eval_count" );
        var output = ReadLong( root, "eval_count" );
        return new OllamaStreamFrame(
            text,
            done,
            finish,
            new AiUsage
            {
                InputTokens = input,
                OutputTokens = output,
                TotalTokens = Sum( input, output )
            } );
    }

    private static void ValidateModel( JsonElement root, string expectedModel )
    {
        if ( !root.TryGetProperty( "model", out var model ) )
        {
            return;
        }
        if ( model.ValueKind != JsonValueKind.String )
        {
            throw new InvalidDataException( "Ollama stream model must be text." );
        }
        if ( !string.Equals( model.GetString( ), expectedModel, StringComparison.Ordinal ) )
        {
            throw new InvalidDataException( "AI provider returned a different model." );
        }
    }

    private static string? ReadMessageContent( JsonElement root )
    {
        if ( !root.TryGetProperty( "message", out var message ) )
        {
            return null;
        }
        if ( message.ValueKind != JsonValueKind.Object )
        {
            throw new InvalidDataException( "AI provider message must be an object." );
        }
        if ( !message.TryGetProperty( "content", out var content ) )
        {
            return null;
        }
        if ( content.ValueKind is not JsonValueKind.String and not JsonValueKind.Null )
        {
            throw new InvalidDataException( "Ollama stream content must be text or null." );
        }
        return content.ValueKind == JsonValueKind.String ? content.GetString( ) : null;
    }

    private static bool ReadBoolean( JsonElement root, string name )
    {
        if ( !root.TryGetProperty( name, out var value ) )
        {
            return false;
        }
        if ( value.ValueKind is not JsonValueKind.True and not JsonValueKind.False )
        {
            throw new InvalidDataException( $"Ollama stream {name} must be a boolean." );
        }
        return value.ValueKind == JsonValueKind.True;
    }

    private static string? ReadOptionalString( JsonElement root, string name )
    {
        if ( !root.TryGetProperty( name, out var value ) )
        {
            return null;
        }
        if ( value.ValueKind is not JsonValueKind.String and not JsonValueKind.Null )
        {
            throw new InvalidDataException( $"Ollama stream {name} must be text or null." );
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString( ) : null;
    }

    private static long? ReadLong( JsonElement root, string name ) =>
        root.TryGetProperty( name, out var value ) && value.TryGetInt64( out var number ) && number >= 0
            ? number : null;
    private static long? Sum( long? left, long? right ) =>
        left is null || right is null ? null : checked(left + right);

    private readonly record struct OllamaStreamFrame(
        string? Text,
        bool Done,
        string Finish,
        AiUsage Usage );

}

public static class OpenAiSseReader
{
    public static async IAsyncEnumerable<AiProviderStreamEvent> ReadAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken )
    {
        BoundedLineReader lines = new( stream, 64 * 1024 );
        StringBuilder data = new( );
        string? eventName = null;
        var terminal = false;
        while ( await lines.ReadLineAsync( cancellationToken ).ConfigureAwait( false ) is string line )
        {
            if ( line.Length == 0 )
            {
                if ( data.Length == 0 )
                {
                    continue;
                }

                var payload = data.ToString( );
                data.Clear( );
                if ( string.Equals( eventName, "error", StringComparison.OrdinalIgnoreCase ) )
                {
                    throw new AiProviderException( AiErrorCategories.Unavailable );
                }
                eventName = null;
                if ( payload == "[DONE]" )
                {
                    terminal = true;
                    yield return new AiProviderStreamEvent
                    {
                        Terminal = true
                    };
                    break;
                }
                var parsed = ParseEvent( payload );
                yield return parsed;
                continue;
            }
            if ( line.StartsWith( ':' ) )
            {
                continue;
            }

            if ( line.StartsWith( "event:", StringComparison.Ordinal ) )
            {
                eventName = line[6..].Trim( );
                continue;
            }
            if ( line.StartsWith( "data:", StringComparison.Ordinal ) )
            {
                if ( data.Length > 0 )
                {
                    data.Append( '\n' );
                }

                data.Append( line.AsSpan( 5 ).TrimStart( ) );
                if ( Encoding.UTF8.GetByteCount( data.ToString( ) ) > 64 * 1024 )
                {
                    throw new PayloadTooLargeException( "SSE event exceeds its byte limit." );
                }
            }
        }
        if ( !terminal )
        {
            throw new EndOfStreamException( "OpenAI stream ended before [DONE]." );
        }
    }

    private static AiProviderStreamEvent ParseEvent( string payload )
    {
        using var document = JsonDocument.Parse( payload );
        var root = document.RootElement;
        if ( root.ValueKind != JsonValueKind.Object )
        {
            throw new InvalidDataException( "OpenAI stream event must be a JSON object." );
        }
        if ( AiProviderOutputPolicy.ContainsUnsupportedField( root ) )
        {
            throw AiProviderOutputPolicy.Unsupported( );
        }
        if ( root.TryGetProperty( "error", out var error ) &&
            error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined )
        {
            throw new AiProviderException(
                AiErrorCategories.Unavailable,
                safeSummary: "The AI provider returned a structured error event." );
        }
        var hasChoices = root.TryGetProperty( "choices", out var choices );
        string? text = null;
        string? finish = null;
        var meaningful = false;
        if ( hasChoices )
        {
            if ( choices.ValueKind != JsonValueKind.Array )
            {
                throw new InvalidDataException( "OpenAI stream choices must be an array." );
            }
            if ( choices.GetArrayLength( ) > 0 )
            {
                (text, finish) = ParseChoice( choices[0] );
                for ( var index = 1; index < choices.GetArrayLength( ); index++ )
                {
                    _ = ParseChoice( choices[index] );
                }
                if ( choices.GetArrayLength( ) > 1 )
                {
                    throw new InvalidDataException(
                        "OpenAI stream returned multiple choices, which are unsupported." );
                }
                meaningful = true;
            }
        }
        var hasUsage = root.TryGetProperty( "usage", out var usageValue );
        if ( hasUsage && usageValue.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null )
        {
            throw new InvalidDataException( "OpenAI stream usage must be an object or null." );
        }
        var usage = hasUsage && usageValue.ValueKind == JsonValueKind.Object
            ? new AiUsage
            {
                InputTokens = ReadLong( usageValue, "prompt_tokens" ),
                OutputTokens = ReadLong( usageValue, "completion_tokens" ),
                TotalTokens = ReadLong( usageValue, "total_tokens" )
            }
            : null;
        meaningful |= usage is not null;
        if ( !meaningful )
        {
            throw new InvalidDataException( "OpenAI stream event contains no supported data." );
        }
        return new AiProviderStreamEvent { Text = text, FinishCategory = finish, Usage = usage };
    }

    private static (string? Text, string? Finish) ParseChoice( JsonElement choice )
    {
        if ( choice.ValueKind != JsonValueKind.Object )
        {
            throw new InvalidDataException( "OpenAI stream choice must be an object." );
        }
        var hasDelta = choice.TryGetProperty( "delta", out var delta );
        var hasFinish = choice.TryGetProperty( "finish_reason", out var reason );
        if ( !hasDelta && !hasFinish )
        {
            throw new InvalidDataException( "OpenAI stream choice has no delta or finish reason." );
        }

        var text = hasDelta ? ParseDelta( delta ) : null;
        if ( hasFinish && reason.ValueKind is not JsonValueKind.String and not JsonValueKind.Null )
        {
            throw new InvalidDataException( "OpenAI stream finish reason must be text or null." );
        }
        var finish = hasFinish && reason.ValueKind == JsonValueKind.String
            ? reason.GetString( )
            : null;
        if ( AiProviderOutputPolicy.IsUnsupportedFinishReason( finish ) )
        {
            throw AiProviderOutputPolicy.Unsupported( );
        }
        return (text, finish);
    }

    private static string? ParseDelta( JsonElement delta )
    {
        if ( delta.ValueKind != JsonValueKind.Object )
        {
            throw new InvalidDataException( "OpenAI stream delta must be an object." );
        }
        if ( !delta.TryGetProperty( "content", out var content ) )
        {
            return null;
        }
        if ( content.ValueKind is not JsonValueKind.String and not JsonValueKind.Null )
        {
            throw new InvalidDataException( "OpenAI stream content must be text or null." );
        }
        return content.ValueKind == JsonValueKind.String ? content.GetString( ) : null;
    }

    private static long? ReadLong( JsonElement root, string name ) =>
        root.TryGetProperty( name, out var value ) && value.TryGetInt64( out var number ) && number >= 0
            ? number : null;
}

internal static class AiProviderOutputPolicy
{
    public static bool ContainsUnsupportedField( JsonElement value )
    {
        switch ( value.ValueKind )
        {
            case JsonValueKind.Object:
                foreach ( var property in value.EnumerateObject( ) )
                {
                    if ( property.NameEquals( "tool_calls" ) || property.NameEquals( "function_call" ) )
                    {
                        return true;
                    }
                    if ( ContainsUnsupportedField( property.Value ) )
                    {
                        return true;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach ( var item in value.EnumerateArray( ) )
                {
                    if ( ContainsUnsupportedField( item ) )
                    {
                        return true;
                    }
                }
                break;
        }

        return false;
    }

    public static bool IsUnsupportedFinishReason( string? value ) =>
        string.Equals( value, "tool_calls", StringComparison.OrdinalIgnoreCase ) ||
        string.Equals( value, "function_call", StringComparison.OrdinalIgnoreCase );

    public static AiProviderException Unsupported( ) => new(
        AiErrorCategories.UnsupportedProviderOutput,
        safeSummary: "The AI provider returned unsupported tool or function output." );

}
