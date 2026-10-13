using System.Text;

namespace CommandDeck;

public sealed partial class AgentConversationCoordinator
{

    private void CompleteSubscribers( long generation )
    {
        foreach ( (var key, var subscriber) in _subscribers )
        {
            if ( subscriber.Generation == generation && _subscribers.TryRemove( key, out _ ) )
            {
                subscriber.Channel.Writer.TryComplete( );
            }
        }
    }

    private void Publish(
        Conversation conversation,
        string kind,
        string? status = null,
        string? text = null,
        AgentToolActivity? tool = null,
        AgentPendingRequest? request = null,
        AiUsage? usage = null,
        AgentTurnResult? done = null )
    {
        AgentStreamEvent streamEvent;
        lock ( _sync )
        {
            streamEvent = new AgentStreamEvent
            {
                ConversationId = conversation.Id,
                TurnId = conversation.ActiveTurnId,
                Generation = conversation.Generation,
                Sequence = ++conversation.Sequence,
                Kind = kind,
                Status = status,
                Text = text,
                Tool = tool,
                Request = request,
                Usage = usage,
                Done = done
            };
        }
        foreach ( (var key, var subscriber) in _subscribers )
        {
            if ( subscriber.Generation != conversation.Generation )
            {
                continue;
            }
            if ( !subscriber.Channel.Writer.TryWrite( streamEvent ) &&
                _subscribers.TryRemove( key, out _ ) )
            {
                subscriber.Channel.Writer.TryComplete(
                    new InvalidOperationException( "The agent event subscriber could not keep up." ) );
            }
        }
    }

    /// <summary>
    /// Ends the turn for its subscribers: each one present now gets the done event
    /// and completes. The turn stops counting as running in the same step, so a
    /// client that sends its next prompt on done is not refused as busy, and the
    /// subscription it opens for that prompt is not completed by this turn.
    /// </summary>
    private void PublishDone( Conversation conversation, AgentTurnResult result )
    {
        AgentStreamEvent streamEvent;
        List<Subscriber> finished = [];
        lock ( _sync )
        {
            streamEvent = new AgentStreamEvent
            {
                ConversationId = conversation.Id,
                TurnId = conversation.ActiveTurnId,
                Generation = conversation.Generation,
                Sequence = ++conversation.Sequence,
                Kind = AgentEventKinds.Done,
                Done = result
            };
            conversation.TurnFinished = true;
            foreach ( (var key, var subscriber) in _subscribers )
            {
                if ( subscriber.Generation == conversation.Generation && _subscribers.TryRemove( key, out _ ) )
                {
                    finished.Add( subscriber );
                }
            }
        }

        foreach ( var subscriber in finished )
        {
            if ( subscriber.Channel.Writer.TryWrite( streamEvent ) )
            {
                subscriber.Channel.Writer.TryComplete( );
            }
            else
            {
                subscriber.Channel.Writer.TryComplete(
                    new InvalidOperationException( "The agent event subscriber could not keep up." ) );
            }
        }
    }

    private bool IsCurrentTurn( Conversation conversation, long turnId )
    {
        lock ( _sync )
        {
            return IsCurrentTurnUnderLock( conversation, turnId );
        }
    }

    private bool IsCurrentTurnUnderLock( Conversation conversation, long turnId ) =>
        ReferenceEquals( _conversation, conversation ) &&
        conversation.Generation == _generation &&
        conversation.ActiveTurnId == turnId;

    private void PublishTextChunks( Conversation conversation, string kind, string text )
    {
        var remaining = text;
        while ( remaining.Length > 0 )
        {
            var chunk = TakeUtf8Prefix( remaining, AgentLimits.MaximumEventTextBytes );
            if ( chunk.Length == 0 )
            {
                break;
            }
            Publish( conversation, kind, text: chunk );
            remaining = remaining[chunk.Length..];
        }
    }

    private static string TakeUtf8Prefix( string value, int maximumBytes )
    {
        if ( value.Length == 0 || maximumBytes <= 0 )
        {
            return string.Empty;
        }
        if ( Encoding.UTF8.GetByteCount( value ) <= maximumBytes )
        {
            return value;
        }
        var low = 0;
        var high = value.Length;
        while ( low < high )
        {
            var middle = (low + high + 1) / 2;
            if ( middle < value.Length && char.IsHighSurrogate( value[middle - 1] ) )
            {
                middle--;
            }
            if ( middle <= low )
            {
                break;
            }
            if ( Encoding.UTF8.GetByteCount( value.AsSpan( 0, middle ) ) <= maximumBytes )
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        while ( low > 0 && char.IsHighSurrogate( value[low - 1] ) )
        {
            low--;
        }
        return value[..low];
    }

    private static void AppendEntry( Conversation conversation, string role, string text, long turnId )
    {
        var truncated = Encoding.UTF8.GetByteCount( text ) > AgentLimits.MaximumAnswerBytes;
        var bounded = truncated ? TakeUtf8Prefix( text, AgentLimits.MaximumAnswerBytes ) : text;
        conversation.Entries.Add( new AgentTranscriptEntry
        {
            Role = role,
            Text = bounded,
            TurnId = turnId,
            Truncated = truncated
        } );
        while ( conversation.Entries.Count > AgentLimits.MaximumTranscriptEntries )
        {
            conversation.Entries.RemoveAt( 0 );
        }
    }

    private static List<CommandAction> OutputActions( ) =>
    [
        new CommandAction { Id = "copy", Title = "Copy", Kind = ActionKinds.CopyText },
        new CommandAction { Id = "save", Title = "Save", Kind = ActionKinds.OpenPath },
        new CommandAction { Id = "open", Title = "Open", Kind = ActionKinds.OpenPath }
    ];

    private static AgentConversationSummary Summarize(
        Conversation conversation,
        string? requesterOwnerId ) => new( )
        {
            ConversationId = conversation.Id,
            BackendId = conversation.BackendId,
            ModelId = conversation.ModelId,
            ReasoningEffort = conversation.ReasoningEffort,
            State = conversation.Pending is not null ? AgentConversationStates.Waiting : conversation.State,
            TurnCount = conversation.TurnCount,
            UpdatedUnixMilliseconds = conversation.UpdatedAt.ToUnixTimeMilliseconds( ),
            ExpiresUnixMilliseconds = conversation.ExpiresAt?.ToUnixTimeMilliseconds( ),
            PendingRequestId = CanResolve( conversation.Pending, requesterOwnerId )
            ? conversation.Pending?.Request.Id
            : null,
            TrustLevel = conversation.TrustLevel
        };
}
