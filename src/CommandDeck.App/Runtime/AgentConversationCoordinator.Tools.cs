using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandDeck;

public sealed partial class AgentConversationCoordinator
{
    private sealed class TurnHost( AgentConversationCoordinator owner, Conversation conversation, long turnId ) : IAgentTurnHost
    {
        private readonly Stopwatch _active = Stopwatch.StartNew( );

        public TimeSpan ActiveElapsed => _active.Elapsed;

        public ValueTask OnStatusAsync( string status, CancellationToken cancellationToken )
        {
            if ( !owner.IsCurrentTurn( conversation, turnId ) )
            {
                return ValueTask.CompletedTask;
            }
            lock ( owner._sync )
            {
                conversation.ActiveStatus = status;
            }
            owner.Publish( conversation, AgentEventKinds.Status, status: status );
            return ValueTask.CompletedTask;
        }

        public ValueTask OnTextAsync( string delta, CancellationToken cancellationToken )
        {
            if ( string.IsNullOrEmpty( delta ) )
            {
                return ValueTask.CompletedTask;
            }
            var bytes = Encoding.UTF8.GetByteCount( delta );
            lock ( owner._sync )
            {
                if ( !owner.IsCurrentTurnUnderLock( conversation, turnId ) )
                {
                    return ValueTask.CompletedTask;
                }
                if ( conversation.ActiveTextBytes + bytes > AgentLimits.MaximumAnswerBytes )
                {
                    throw new AiOperationException( AiErrorCategories.ResponseTooLarge, safeSummary: "The answer exceeded the supported size." );
                }
                conversation.ActiveText.Append( delta );
                conversation.ActiveTextBytes += bytes;
            }
            owner.PublishTextChunks( conversation, AgentEventKinds.Text, delta );
            return ValueTask.CompletedTask;
        }

        public ValueTask OnReasoningAsync( string delta, CancellationToken cancellationToken )
        {
            if ( string.IsNullOrEmpty( delta ) )
            {
                return ValueTask.CompletedTask;
            }
            string accepted;
            lock ( owner._sync )
            {
                if ( !owner.IsCurrentTurnUnderLock( conversation, turnId ) )
                {
                    return ValueTask.CompletedTask;
                }
                var remaining = AgentLimits.MaximumReasoningBytes - conversation.ActiveReasoningBytes;
                accepted = remaining <= 0 ? string.Empty : TakeUtf8Prefix( delta, remaining );
                conversation.ActiveReasoning.Append( accepted );
                conversation.ActiveReasoningBytes += Encoding.UTF8.GetByteCount( accepted );
            }
            owner.PublishTextChunks( conversation, AgentEventKinds.Reasoning, accepted );
            return ValueTask.CompletedTask;
        }

        public ValueTask OnUsageAsync( AiUsage usage, CancellationToken cancellationToken )
        {
            lock ( owner._sync )
            {
                if ( !owner.IsCurrentTurnUnderLock( conversation, turnId ) )
                {
                    return ValueTask.CompletedTask;
                }
                conversation.Usage = usage;
            }
            owner.Publish( conversation, AgentEventKinds.Usage, usage: usage );
            return ValueTask.CompletedTask;
        }

        public async ValueTask<AgentToolResult> OnToolCallAsync( AgentToolCall toolCall, CancellationToken cancellationToken )
        {
            lock ( owner._sync )
            {
                if ( !owner.IsCurrentTurnUnderLock( conversation, turnId ) )
                {
                    return AgentToolResult.Denied( "This conversation generation is no longer active." );
                }
                if ( ++conversation.ToolCalls > AgentLimits.MaximumToolCallsPerTurn )
                {
                    return AgentToolResult.Denied( "Too many tool calls in this turn." );
                }
            }
            var activity = owner.StartActivity( conversation, toolCall.Name, turnId );
            _active.Stop( );
            try
            {
                var result = await owner.DispatchToolAsync( conversation, toolCall, turnId, cancellationToken )
                    .ConfigureAwait( false );
                owner.FinishActivity( conversation, activity, result.Success ? AgentToolActivityStates.Completed : AgentToolActivityStates.Denied );
                return result;
            }
            catch ( OperationCanceledException )
            {
                owner.FinishActivity( conversation, activity, AgentToolActivityStates.Failed );
                throw;
            }
            catch ( Exception exception ) when ( exception is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException )
            {
                owner.FinishActivity( conversation, activity, AgentToolActivityStates.Failed );
                return AgentToolResult.Denied( "The tool call failed: " + Sanitize( exception.Message ) );
            }
            catch ( Exception exception ) when ( exception is not OutOfMemoryException )
            {
                owner.FinishActivity( conversation, activity, AgentToolActivityStates.Failed );
                return AgentToolResult.Denied( "The tool call failed." );
            }
            finally
            {
                _active.Start( );
            }
        }

        public async ValueTask<AgentUserInputResult> OnUserInputAsync( AgentUserInputRequest request, CancellationToken cancellationToken )
        {
            AgentPendingRequest pending = new( )
            {
                Id = NewId( ),
                Kind = AgentPendingRequestKinds.Question,
                TurnId = turnId,
                Prompt = request.Questions.Count == 1 ? request.Questions[0].Prompt : "The assistant has questions for you.",
                Questions = [.. request.Questions],
                Decisions = [AgentRequestDecisions.Deny, AgentRequestDecisions.CancelTurn],
            };
            _active.Stop( );
            try
            {
                var decision = await owner.ParkAsync( conversation, pending, cancellationToken ).ConfigureAwait( false );
                if ( decision.Decision is AgentRequestDecisions.Deny or AgentRequestDecisions.CancelTurn || decision.Answers is null )
                {
                    return new AgentUserInputResult { Declined = true };
                }
                Dictionary<string, string> answers = new( StringComparer.Ordinal );
                foreach ( (var key, var value) in decision.Answers )
                {
                    answers[key] = value.Length > AgentLimits.MaximumAnswerCharacters ? value[..AgentLimits.MaximumAnswerCharacters] : value;
                }
                return new AgentUserInputResult { Answers = answers };
            }
            finally
            {
                _active.Start( );
            }
        }
    }

    private async ValueTask<PendingDecision> ParkAsync(
        Conversation conversation,
        AgentPendingRequest request,
        CancellationToken cancellationToken )
    {
        var expires = _clock.GetUtcNow( ).AddSeconds( AgentLimits.MaximumPendingRequestSeconds );
        AgentPendingRequest stamped = new( )
        {
            Id = request.Id,
            Kind = request.Kind,
            TurnId = request.TurnId,
            Prompt = request.Prompt,
            Detail = request.Detail,
            Questions = request.Questions,
            Context = request.Context,
            Action = request.Action,
            Decisions = request.Decisions,
            ExpiresUnixMilliseconds = expires.ToUnixTimeMilliseconds( )
        };
        PendingRequest pending = new( stamped, expires, conversation.ClientOwnerId );
        lock ( _sync )
        {
            if ( conversation.Pending is not null )
            {
                throw new InvalidOperationException( "Another request is already waiting for the user." );
            }
            conversation.Pending = pending;
            conversation.State = AgentConversationStates.Waiting;
        }
        Publish( conversation, AgentEventKinds.Request, request: stamped, status: "Waiting for you" );
        try
        {
            using var expiry = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            expiry.CancelAfter( expires - _clock.GetUtcNow( ) + TimeSpan.FromSeconds( 1 ) );
            try
            {
                return await pending.Completion.Task.WaitAsync( expiry.Token ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
            {
                return new PendingDecision( AgentRequestDecisions.Deny, null );
            }
        }
        finally
        {
            lock ( _sync )
            {
                if ( ReferenceEquals( conversation.Pending, pending ) )
                {
                    conversation.Pending = null;
                    conversation.State = AgentConversationStates.Running;
                }
            }
            Publish( conversation, AgentEventKinds.Status, status: "Continuing" );
        }
    }

    private AgentToolActivity StartActivity( Conversation conversation, string name, long turnId )
    {
        AgentToolActivity activity = new( )
        {
            Id = NewId( ),
            Name = name,
            Label = DescribeTool( name ),
            State = AgentToolActivityStates.Running,
            TurnId = turnId
        };
        lock ( _sync )
        {
            conversation.Tools.Add( activity );
            if ( conversation.Tools.Count > 64 )
            {
                conversation.Tools.RemoveAt( 0 );
            }
        }
        Publish( conversation, AgentEventKinds.Tool, tool: activity );
        return activity;
    }

    private void FinishActivity( Conversation conversation, AgentToolActivity activity, string state )
    {
        AgentToolActivity finished = new( )
        {
            Id = activity.Id,
            Name = activity.Name,
            Label = activity.Label,
            State = state,
            TurnId = activity.TurnId
        };
        lock ( _sync )
        {
            var index = conversation.Tools.FindIndex( value => value.Id == activity.Id );
            if ( index >= 0 )
            {
                conversation.Tools[index] = finished;
            }
        }
        Publish( conversation, AgentEventKinds.Tool, tool: finished );
    }

    private ValueTask<AgentToolResult> DispatchToolAsync(
        Conversation conversation, AgentToolCall toolCall, long turnId, CancellationToken cancellationToken )
    {
        AdmittedAgentTools tools = new( )
        {
            TryAdmit = minimum =>
            {
                lock ( _sync )
                {
                    return IsCurrentTurnUnderLock( conversation, turnId ) && conversation.TrustLevel >= minimum &&
                        (minimum != AgentTrustLevels.Packages || _packages is not null);
                }
            },
            Search = async ( arguments, token ) => AgentToolResult.Ok( await AgentLauncherTools.SearchAsync(
                _launcher, ReadString( arguments, "query" ), ReadInt( arguments, "limit", 8 ), token ).ConfigureAwait( false ) ),
            Invoke = ( arguments, token ) => InvokeToolAsync( conversation, arguments, token ),
            Context = ( name, arguments, token ) => ContextToolAsync( conversation, name, arguments, token ),
            Command = ( arguments, token ) => CommandToolAsync( conversation, arguments, token ),
            PackageSearch = PackageSearchToolAsync,
            PackageChange = ( arguments, token ) => PackageChangeToolAsync( conversation, arguments, token )
        };

        return AgentToolRouter.DispatchAsync( toolCall.Name, ParseArguments( toolCall.ArgumentsJson ), tools, cancellationToken );
    }

    private async ValueTask<AgentToolResult> InvokeToolAsync(
        Conversation conversation,
        JsonObject arguments,
        CancellationToken cancellationToken )
    {
        var resultId = ReadString( arguments, "resultId" );
        var actionId = ReadString( arguments, "actionId" );
        var reference = ReadString( arguments, "invocationReference" );
        var result = _launcher.ResolveCachedResult( resultId, reference );
        var action = result?.Actions.FirstOrDefault( value => value.Id == actionId );
        if ( result is null || action is null )
        {
            return AgentToolResult.Denied( "The result or action is no longer available; search again." );
        }
        var risk = AgentLauncherTools.ClassifyRisk( result, action );
        if ( risk == AgentActionRisks.Denied )
        {
            return AgentToolResult.Denied( "That action cannot be invoked by the assistant." );
        }
        if ( risk == AgentActionRisks.Confirm )
        {
            var card = AgentLauncherTools.BuildCard( NewId( ), result, action, risk, ReadOptional( arguments, "rationale" ) );
            var decision = await ParkAsync( conversation, new AgentPendingRequest
            {
                Id = card.Id,
                Kind = AgentPendingRequestKinds.Action,
                TurnId = conversation.ActiveTurnId,
                Prompt = card.Title,
                Action = card,
                Decisions = [AgentRequestDecisions.Allow, AgentRequestDecisions.Deny, AgentRequestDecisions.CancelTurn]
            }, cancellationToken ).ConfigureAwait( false );
            if ( decision.Decision != AgentRequestDecisions.Allow )
            {
                return AgentToolResult.Denied( "The user declined the action." );
            }
        }
        var outcome = await _launcher.InvokeAsync( resultId, actionId, reference, string.Empty, confirmed: true, cancellationToken )
            .ConfigureAwait( false );
        JsonObject payload = new( )
        {
            ["succeeded"] = outcome.Succeeded,
            ["message"] = outcome.Message ?? (outcome.Succeeded ? "Done." : "Failed.")
        };
        return outcome.Succeeded ? AgentToolResult.Ok( payload.ToJsonString( ) ) : AgentToolResult.Denied( payload.ToJsonString( ) );
    }

    private void RecordOperation( string id, string state, string title, string? message )
    {
        _operations[id] = new AgentOperationSummary
        {
            Id = id,
            Kind = "package",
            State = state,
            Title = title,
            Message = message,
            UpdatedUnixMilliseconds = _clock.GetUtcNow( ).ToUnixTimeMilliseconds( ),
            Progress = state == PackageOperationStates.Completed ? 100 : 0
        };
        if ( _operations.Count > 16 )
        {
            var oldest = _operations.Values.OrderBy( static value => value.UpdatedUnixMilliseconds ).FirstOrDefault( )?.Id;
            if ( oldest is not null )
            {
                _operations.TryRemove( oldest, out _ );
            }
        }
    }

    private static JsonObject ParseArguments( string json )
    {
        try
        {
            return JsonNode.Parse( json ) as JsonObject ?? [];
        }
        catch ( JsonException )
        {
            return [];
        }
    }

    private static string ReadString( JsonObject arguments, string name )
    {
        var value = ( string? ) arguments[name];
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            throw new InvalidOperationException( $"The tool argument {name} is required." );
        }
        return value.Length > 4096 ? value[..4096] : value;
    }

    private static string? ReadOptional( JsonObject arguments, string name )
    {
        var value = ( string? ) arguments[name];
        return string.IsNullOrWhiteSpace( value ) ? null : value.Length > 512 ? value[..512] : value;
    }

    private static int ReadInt( JsonObject arguments, string name, int fallback ) =>
        JsonNodeReader.TryReadInt32( arguments[name], out var parsed ) ? parsed : fallback;

    private static string DescribeTool( string name ) => name switch
    {
        AgentToolNames.Search => "Searching the launcher",
        AgentToolNames.Invoke => "Invoking a launcher action",
        AgentToolNames.ListDirectory => "Listing a folder",
        AgentToolNames.ReadFile => "Reading a file",
        AgentToolNames.SearchText => "Searching files",
        AgentToolNames.RunCommand => "Running a command",
        AgentToolNames.SearchPackages => "Searching packages",
        AgentToolNames.ProposePackageChange => "Proposing a package change",
        _ => "Using " + name
    };

    private static string DescribeOperation( string operation ) => operation switch
    {
        AgentContextOperations.ListDirectory => "list this folder",
        AgentContextOperations.ReadFile => "read this file",
        _ => "search this folder"
    };

    private static string NewId( ) => Convert.ToHexString( RandomNumberGenerator.GetBytes( 12 ) ).ToLowerInvariant( );

    private static string Sanitize( string message )
    {
        var trimmed = message.Length > 200 ? message[..200] : message;
        return new string( trimmed.Where( static character => !char.IsControl( character ) ).ToArray( ) );
    }
}
