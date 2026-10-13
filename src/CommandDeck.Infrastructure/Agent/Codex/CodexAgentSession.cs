using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace CommandDeck;

public sealed class CodexAgentSession : IAgentSession
{
    private readonly CodexAppServerClient _client;
    private readonly Channel<(string Method, JsonNode? Parameters)> _events = Channel.CreateBounded<(string, JsonNode?)>(
        new BoundedChannelOptions( 4096 ) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true } );
    private IAgentTurnHost? _host;
    private string? _activeTurnId;
    private int _hostCallsInFlight;
    private int _closed;

    internal CodexAgentSession( CodexAppServerClient client, string threadId, string? modelId, string? reasoningEffort )
    {
        _client = client;
        SessionId = threadId;
        ModelId = modelId;
        ReasoningEffort = reasoningEffort;
    }

    public string SessionId { get; }

    public string? ModelId { get; private set; }

    public string? ReasoningEffort { get; private set; }

    public bool IsFaulted => _closed != 0 || _client.IsFaulted;

    public string ProcessGeneration { get; internal set; } = string.Empty;

    public async ValueTask RunTurnAsync(
        AgentTurnInput input,
        IAgentTurnHost host,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( input );
        ArgumentNullException.ThrowIfNull( host );
        if ( _closed != 0 )
        {
            throw new AiOperationException( AiErrorCategories.Unavailable, safeSummary: "The agent session is closed." );
        }
        if ( Interlocked.CompareExchange( ref _host, host, null ) is not null )
        {
            throw new AiOperationException( AiErrorCategories.Busy, safeSummary: "The agent session already has an active turn." );
        }
        try
        {
            ModelId = input.ModelId ?? ModelId;
            ReasoningEffort = input.ReasoningEffort ?? ReasoningEffort;
            await host.OnStatusAsync( "Sending", cancellationToken ).ConfigureAwait( false );
            var turnId = await _client.StartTurnAsync( SessionId, input.Text, input.ModelId, input.ReasoningEffort, cancellationToken )
                .ConfigureAwait( false );
            _activeTurnId = turnId;
            await ConsumeTurnAsync( turnId, host, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _activeTurnId = null;
            _host = null;
        }
    }

    public async ValueTask InterruptAsync( CancellationToken cancellationToken )
    {
        var turnId = _activeTurnId;
        if ( turnId is null || _closed != 0 )
        {
            return;
        }
        try
        {
            await _client.InterruptTurnAsync( SessionId, turnId, cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is JsonRpcException or OperationCanceledException or InvalidOperationException )
        {
        }
    }

    internal ValueTask EnqueueAsync( string method, JsonNode? parameters, CancellationToken cancellationToken ) =>
        _closed != 0 ? ValueTask.CompletedTask : _events.Writer.WriteAsync( (method, parameters), cancellationToken );

    internal void MarkClosed( )
    {
        if ( Interlocked.Exchange( ref _closed, 1 ) == 0 )
        {
            _events.Writer.TryComplete( );
        }
    }

    internal async ValueTask<JsonNode?> HandleRequestAsync( string method, JsonNode parameters, CancellationToken cancellationToken )
    {
        var host = _host;
        if ( host is null )
        {
            throw new JsonRpcException( -32000, "No active CommandDeck turn." );
        }

        Interlocked.Increment( ref _hostCallsInFlight );
        try
        {
            return method == "item/tool/call"
                ? await HandleToolCallAsync( host, parameters, cancellationToken ).ConfigureAwait( false )
                : await HandleUserInputAsync( host, parameters, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            Interlocked.Decrement( ref _hostCallsInFlight );
        }
    }

    private static async ValueTask<JsonNode?> HandleToolCallAsync( IAgentTurnHost host, JsonNode parameters, CancellationToken cancellationToken )
    {
        AgentToolCall toolCall = new( )
        {
            CallId = ( string? ) parameters["callId"] ?? string.Empty,
            Name = ( string? ) parameters["tool"] ?? string.Empty,
            ArgumentsJson = parameters["arguments"]?.ToJsonString( ) ?? "{}"
        };
        var result = await host.OnToolCallAsync( toolCall, cancellationToken ).ConfigureAwait( false );
        return new JsonObject
        {
            ["contentItems"] = new JsonArray( new JsonObject { ["type"] = "inputText", ["text"] = result.ContentText } ),
            ["success"] = result.Success
        };
    }

    private static async ValueTask<JsonNode?> HandleUserInputAsync( IAgentTurnHost host, JsonNode parameters, CancellationToken cancellationToken )
    {
        List<AgentQuestion> questions = [];
        if ( parameters["questions"] is JsonArray array )
        {
            foreach ( var entry in array )
            {
                var question = ReadQuestion( entry );
                if ( question is not null )
                {
                    questions.Add( question );
                }
            }
        }
        var answer = await host.OnUserInputAsync(
            new AgentUserInputRequest { Questions = questions },
            cancellationToken ).ConfigureAwait( false );
        JsonObject answers = new( );
        foreach ( var question in questions )
        {
            var value = answer.Declined
                ? string.Empty
                : answer.Answers.GetValueOrDefault( question.Id, string.Empty );
            answers[question.Id] = new JsonObject { ["answers"] = new JsonArray( value ) };
        }
        return new JsonObject { ["answers"] = answers };
    }

    private static AgentQuestion? ReadQuestion( JsonNode? entry )
    {
        var id = ( string? ) entry?["id"];
        var prompt = ( string? ) entry?["question"];
        if ( id is null || prompt is null )
        {
            return null;
        }
        List<string> options = [];
        if ( entry?["options"] is JsonArray optionArray )
        {
            foreach ( var option in optionArray.Take( AgentLimits.MaximumQuestionOptions ) )
            {
                var label = ( string? ) option?["label"];
                if ( !string.IsNullOrWhiteSpace( label ) )
                {
                    options.Add( label );
                }
            }
        }
        var allowOther = ( bool? ) entry?["isOther"] ?? false;
        return new AgentQuestion
        {
            Id = id,
            Header = ( string? ) entry?["header"],
            Prompt = prompt,
            Options = options,
            AllowFreeText = options.Count == 0 || allowOther
        };
    }

    private async ValueTask ConsumeTurnAsync( string turnId, IAgentTurnHost host, CancellationToken cancellationToken )
    {
        string? failureMessage = null;
        string? failureCategory = null;
        while ( true )
        {
            (string Method, JsonNode? Parameters) item;
            try
            {
                item = await ReadEventAsync( cancellationToken ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
            {
                await InterruptAsync( CancellationToken.None ).ConfigureAwait( false );
                throw;
            }
            var completesTurn = item.Method == "turn/completed" &&
                                string.Equals( ( string? ) item.Parameters?["turn"]?["id"], turnId, StringComparison.Ordinal );
            switch ( item.Method )
            {
                case "item/agentMessage/delta":
                    await host.OnTextAsync( ReadDelta( item.Parameters ), cancellationToken ).ConfigureAwait( false );
                    break;
                case "item/reasoning/summaryTextDelta":
                    await host.OnReasoningAsync( ReadDelta( item.Parameters ), cancellationToken ).ConfigureAwait( false );
                    break;
                case "item/started":
                    await host.OnStatusAsync( DescribeItem( item.Parameters ), cancellationToken ).ConfigureAwait( false );
                    break;
                case "thread/tokenUsage/updated":
                    await host.OnUsageAsync( ReadUsage( item.Parameters ), cancellationToken ).ConfigureAwait( false );
                    break;
                case "error":
                    failureMessage = ( string? ) item.Parameters?["error"]?["message"];
                    failureCategory = MapErrorCategory( item.Parameters?["error"]?["codexErrorInfo"] );
                    break;
                case "turn/completed" when completesTurn:
                    CompleteTurn( item.Parameters, failureMessage, failureCategory );
                    return;
                case "__closed":
                    throw new AiOperationException( AiErrorCategories.Unavailable, safeSummary: "The agent process stopped during the turn." );
            }
        }
    }

    private async ValueTask<(string Method, JsonNode? Parameters)> ReadEventAsync( CancellationToken cancellationToken )
    {
        while ( true )
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            if ( Volatile.Read( ref _hostCallsInFlight ) == 0 )
            {
                idle.CancelAfter( TimeSpan.FromSeconds( AgentLimits.StreamIdleSeconds ) );
            }
            try
            {
                if ( !await _events.Reader.WaitToReadAsync( idle.Token ).ConfigureAwait( false ) )
                {
                    return ("__closed", null);
                }
                if ( _events.Reader.TryRead( out var item ) )
                {
                    return item;
                }
            }
            catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
            {
                if ( Volatile.Read( ref _hostCallsInFlight ) > 0 )
                {
                    continue;
                }
                await InterruptAsync( CancellationToken.None ).ConfigureAwait( false );
                throw new AiOperationException( AiErrorCategories.Timeout, safeSummary: "The agent produced no progress for 30 seconds." );
            }
        }
    }

    private static void CompleteTurn( JsonNode? parameters, string? failureMessage, string? failureCategory )
    {
        var status = ( string? ) parameters?["turn"]?["status"];
        switch ( status )
        {
            case "completed":
                return;
            case "interrupted":
                throw new OperationCanceledException( "The turn was interrupted." );
            default:
                var message = ( string? ) parameters?["turn"]?["error"]?["message"] ?? failureMessage ?? "The agent turn failed.";
                var category = MapErrorCategory( parameters?["turn"]?["error"]?["codexErrorInfo"] ) ?? failureCategory ?? AiErrorCategories.Unavailable;
                throw new AiOperationException( category, safeSummary: Sanitize( message ) );
        }
    }

    private static string ReadDelta( JsonNode? parameters ) => ( string? ) parameters?["delta"] ?? string.Empty;

    private static string DescribeItem( JsonNode? parameters )
    {
        var type = ( string? ) parameters?["item"]?["type"];
        return type switch
        {
            "agentMessage" => "Answering",
            "reasoning" => "Thinking",
            "dynamicToolCall" => "Using " + (( string? ) parameters?["item"]?["tool"] ?? "a tool"),
            "commandExecution" => "Running a command",
            _ => "Working"
        };
    }

    private static AiUsage ReadUsage( JsonNode? parameters )
    {
        var total = parameters?["tokenUsage"]?["total"];
        return new AiUsage
        {
            InputTokens = ( long? ) total?["inputTokens"],
            OutputTokens = ( long? ) total?["outputTokens"],
            TotalTokens = ( long? ) total?["totalTokens"],
            ContextLimit = ( long? ) parameters?["tokenUsage"]?["modelContextWindow"]
        };
    }

    private static string? MapErrorCategory( JsonNode? info )
    {
        var code = info switch
        {
            JsonValue value => value.TryGetValue( out string? text ) ? text : null,
            JsonObject obj => ( string? ) obj["type"],
            _ => null
        };
        return code switch
        {
            "contextWindowExceeded" => AiErrorCategories.ContextLimit,
            "usageLimitExceeded" or "rateLimitExceeded" => AiErrorCategories.RateLimited,
            "serverOverloaded" => AiErrorCategories.Unavailable,
            "unauthorized" => AiErrorCategories.Authentication,
            null => null,
            _ => AiErrorCategories.Unavailable
        };
    }

    private static string Sanitize( string message )
    {
        var trimmed = message.Length > 240 ? message[..240] : message;
        return new string( trimmed.Where( static character => !char.IsControl( character ) ).ToArray( ) );
    }

    public ValueTask DisposeAsync( )
    {
        MarkClosed( );
        _client.RemoveSession( SessionId );
        return ValueTask.CompletedTask;
    }
}
