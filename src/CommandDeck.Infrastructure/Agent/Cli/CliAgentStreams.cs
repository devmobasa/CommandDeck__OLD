using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandDeck;

/// <summary>Outcome of one NDJSON line from a CLI agent turn.</summary>
public sealed class CliAgentStreamEvent
{
    public string? Text { get; init; }
    public string? Reasoning { get; init; }
    public string? Status { get; init; }
    public string? SessionId { get; init; }
    public AiUsage? Usage { get; init; }
    public bool Terminal { get; init; }
    public bool IsError { get; init; }
    public string? ErrorCategory { get; init; }
    public string? ErrorSummary { get; init; }
    public string? FinalText { get; init; }
}

/// <summary>
/// Maps the two supported CLI stream framings onto CommandDeck's turn events.
/// The readers are tolerant: unknown line types are ignored, and a message is
/// never rendered twice when a CLI emits both deltas and the whole message.
/// </summary>
public sealed class CliAgentStreamReader
{
    private readonly string _format;
    private bool _streamedText;
    private bool _sawResult;

    public CliAgentStreamReader( string format )
    {
        _format = format;
    }

    public bool SawResult => _sawResult;

    public CliAgentStreamEvent? Read( string line )
    {
        if ( string.IsNullOrWhiteSpace( line ) )
        {
            return null;
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse( line );
        }
        catch ( JsonException )
        {
            return null;
        }
        if ( node is not JsonObject frame )
        {
            return null;
        }
        return _format == CliAgentStreamFormats.Cursor ? ReadCursor( frame ) : ReadMessages( frame );
    }

    private CliAgentStreamEvent? ReadMessages( JsonObject frame )
    {
        switch ( ( string? ) frame["type"] )
        {
            case "system":
                var subtype = ( string? ) frame["subtype"];
                return subtype switch
                {
                    "init" => new CliAgentStreamEvent { SessionId = ( string? ) frame["session_id"], Status = "Starting" },
                    "api_retry" => new CliAgentStreamEvent { Status = "Retrying" },
                    _ => null
                };
            case "stream_event":
                return ReadStreamEvent( frame["event"] as JsonObject );
            case "assistant":
                if ( _streamedText )
                {
                    return null;
                }
                var text = ReadTextBlocks( frame["message"]?["content"] as JsonArray, "text" );
                if ( text.Length == 0 )
                {
                    return null;
                }
                _streamedText = true;
                return new CliAgentStreamEvent { Text = text, SessionId = ( string? ) frame["session_id"] };
            case "result":
                _sawResult = true;
                var isError = ( bool? ) frame["is_error"] ?? false;
                var resultSubtype = ( string? ) frame["subtype"] ?? string.Empty;
                var resultText = ( string? ) frame["result"] ?? string.Empty;
                return new CliAgentStreamEvent
                {
                    Terminal = true,
                    IsError = isError || resultSubtype.StartsWith( "error", StringComparison.Ordinal ),
                    ErrorCategory = isError || resultSubtype.StartsWith( "error", StringComparison.Ordinal )
                        ? ClassifyResult( resultSubtype, resultText )
                        : null,
                    ErrorSummary = isError || resultSubtype.StartsWith( "error", StringComparison.Ordinal )
                        ? SummarizeResult( resultSubtype, resultText )
                        : null,
                    FinalText = _streamedText || isError ? null : resultText,
                    SessionId = ( string? ) frame["session_id"],
                    Usage = ReadUsage( frame["usage"] as JsonObject )
                };
            default:
                return null;
        }
    }

    private CliAgentStreamEvent? ReadStreamEvent( JsonObject? streamEvent )
    {
        if ( streamEvent is null )
        {
            return null;
        }
        switch ( ( string? ) streamEvent["type"] )
        {
            case "content_block_delta":
                var delta = streamEvent["delta"] as JsonObject;
                switch ( ( string? ) delta?["type"] )
                {
                    case "text_delta":
                        var text = ( string? ) delta!["text"] ?? string.Empty;
                        if ( text.Length == 0 )
                        {
                            return null;
                        }
                        _streamedText = true;
                        return new CliAgentStreamEvent { Text = text };
                    case "thinking_delta":
                        var thinking = ( string? ) delta!["thinking"] ?? string.Empty;
                        return thinking.Length == 0 ? null : new CliAgentStreamEvent { Reasoning = thinking };
                    default:
                        return null;
                }
            case "content_block_start":
                var block = streamEvent["content_block"] as JsonObject;
                return ( string? ) block?["type"] switch
                {
                    "thinking" => new CliAgentStreamEvent { Status = "Thinking" },
                    "text" => new CliAgentStreamEvent { Status = "Answering" },
                    "tool_use" => new CliAgentStreamEvent { Status = "Tool call declined" },
                    _ => null
                };
            case "message_delta":
                return new CliAgentStreamEvent { Usage = ReadUsage( streamEvent["usage"] as JsonObject ) };
            default:
                return null;
        }
    }

    private CliAgentStreamEvent? ReadCursor( JsonObject frame )
    {
        switch ( ( string? ) frame["type"] )
        {
            case "system":
                return ( string? ) frame["subtype"] == "init"
                    ? new CliAgentStreamEvent { SessionId = ( string? ) frame["session_id"], Status = "Starting" }
                    : null;
            case "assistant":
                // With --stream-partial-output only frames that carry timestamp_ms and
                // no model_call_id contain new text; the rest are buffered flushes.
                var partial = frame["timestamp_ms"] is not null && frame["model_call_id"] is null;
                var text = ReadTextBlocks( frame["message"]?["content"] as JsonArray, "text" );
                if ( text.Length == 0 )
                {
                    return null;
                }
                if ( partial )
                {
                    _streamedText = true;
                    return new CliAgentStreamEvent { Text = text, SessionId = ( string? ) frame["session_id"] };
                }
                if ( _streamedText )
                {
                    return null;
                }
                _streamedText = true;
                return new CliAgentStreamEvent { Text = text, SessionId = ( string? ) frame["session_id"] };
            case "tool_call":
                return new CliAgentStreamEvent { Status = "Tool call declined" };
            case "result":
                _sawResult = true;
                var isError = ( bool? ) frame["is_error"] ?? false;
                var resultText = ( string? ) frame["result"] ?? string.Empty;
                return new CliAgentStreamEvent
                {
                    Terminal = true,
                    IsError = isError,
                    ErrorCategory = isError ? ClassifyResult( ( string? ) frame["subtype"] ?? string.Empty, resultText ) : null,
                    ErrorSummary = isError ? SummarizeResult( ( string? ) frame["subtype"] ?? string.Empty, resultText ) : null,
                    FinalText = _streamedText || isError ? null : resultText,
                    SessionId = ( string? ) frame["session_id"]
                };
            default:
                return null;
        }
    }

    private static string ReadTextBlocks( JsonArray? content, string blockType )
    {
        if ( content is null )
        {
            return string.Empty;
        }
        System.Text.StringBuilder text = new( );
        foreach ( var block in content )
        {
            if ( block is JsonObject item && ( string? ) item["type"] == blockType )
            {
                text.Append( ( string? ) item["text"] );
            }
        }
        return text.ToString( );
    }

    private static AiUsage? ReadUsage( JsonObject? usage )
    {
        if ( usage is null )
        {
            return null;
        }
        var input = ( long? ) usage["input_tokens"];
        var output = ( long? ) usage["output_tokens"];
        if ( input is null && output is null )
        {
            return null;
        }
        return new AiUsage
        {
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input is null || output is null ? null : input + output
        };
    }

    public static string ClassifyResult( string subtype, string text )
    {
        if ( subtype.Contains( "max_turns", StringComparison.Ordinal ) )
        {
            return AiErrorCategories.Unavailable;
        }
        var lowered = text.ToLowerInvariant( );
        if ( lowered.Contains( "not logged in", StringComparison.Ordinal ) ||
            lowered.Contains( "authentication", StringComparison.Ordinal ) ||
            lowered.Contains( "unauthorized", StringComparison.Ordinal ) ||
            lowered.Contains( "log in", StringComparison.Ordinal ) ||
            lowered.Contains( "login", StringComparison.Ordinal ) ||
            lowered.Contains( "api key", StringComparison.Ordinal ) )
        {
            return AiErrorCategories.Authentication;
        }
        if ( lowered.Contains( "rate limit", StringComparison.Ordinal ) || lowered.Contains( "usage limit", StringComparison.Ordinal ) )
        {
            return AiErrorCategories.RateLimited;
        }
        if ( lowered.Contains( "context", StringComparison.Ordinal ) && lowered.Contains( "too long", StringComparison.Ordinal ) )
        {
            return AiErrorCategories.ContextLimit;
        }
        return AiErrorCategories.Unavailable;
    }

    private static string SummarizeResult( string subtype, string text )
    {
        var category = ClassifyResult( subtype, text );
        return category switch
        {
            AiErrorCategories.Authentication => "The agent is not signed in. Sign in and try again.",
            AiErrorCategories.RateLimited => "The agent reported a usage or rate limit.",
            AiErrorCategories.ContextLimit => "The conversation exceeded the model's context.",
            _ => subtype.Contains( "max_turns", StringComparison.Ordinal )
                ? "The agent stopped at its turn limit before answering."
                : "The agent turn failed."
        };
    }
}
