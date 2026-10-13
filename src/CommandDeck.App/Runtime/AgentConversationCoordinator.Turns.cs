using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public sealed partial class AgentConversationCoordinator
{

    private async ValueTask<Conversation> PrepareConversationAsync(
        IAgentBackend backend,
        AgentAuthoritySnapshot authority,
        string? modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken )
    {
        // The gate is held across the spawn/thread-start so two concurrent
        // callers (a prewarm racing the first real turn) resolve to one
        // Conversation. No lock (_sync) is held across an await here.
        await _prepareGate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await PrepareConversationCoreAsync( backend, authority, modelId, reasoningEffort, cancellationToken )
                .ConfigureAwait( false );
        }
        finally
        {
            _prepareGate.Release( );
        }
    }

    private async ValueTask<Conversation> PrepareConversationCoreAsync(
        IAgentBackend backend,
        AgentAuthoritySnapshot authority,
        string? modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken )
    {
        var preferences = authority.Preferences;
        var epoch = authority.SessionEpoch;
        // A preparer whose epoch is already stale must not clear a newer
        // conversation or start a process for authority that was revoked.
        if ( !_authority.IsCurrent( epoch ) )
        {
            throw new AgentAuthorityChangedException( );
        }
        Conversation? existing;
        lock ( _sync )
        {
            existing = _conversation;
        }
        // A conversation is bound to one backend and one authority epoch;
        // switching either starts fresh.
        var sameTrust = existing is not null && existing.AuthorityEpoch == epoch &&
            existing.TrustLevel == preferences.TrustLevel &&
            string.Equals( existing.BackendId, backend.Id, StringComparison.Ordinal );
        if ( sameTrust && existing!.Session is { IsFaulted: false } )
        {
            return existing;
        }
        var packagesAvailable = _packages is not null;
        var tools = AgentToolCatalog.ForTrustLevel( preferences.TrustLevel, packagesAvailable );
        AgentSessionOptions options = new( )
        {
            ModelId = modelId ?? preferences.ModelId,
            ReasoningEffort = reasoningEffort ?? preferences.ReasoningEffort,
            TrustLevel = preferences.TrustLevel,
            WebSearchEnabled = preferences.WebSearchEnabled,
            DeveloperInstructions = AgentToolCatalog.DeveloperInstructions( preferences.TrustLevel, tools ),
            Tools = [.. tools],
            TurnTimeoutMinutes = preferences.TurnTimeoutMinutes,
            Admission = new AgentAdmission( epoch, _authority )
        };
        if ( sameTrust && existing!.Session is { IsFaulted: true } )
        {
            // The sandboxed process exited under a live conversation. Replace
            // only the dead backend session and keep the conversation, its
            // transcript, generation, and any subscribers waiting on the next
            // turn; a heavier clear would tear those subscribers down.
            var replacement = await StartAdmittedSessionAsync( backend, options, epoch, cancellationToken ).ConfigureAwait( false );
            IAgentSession? dead = null;
            bool installed;
            lock ( _sync )
            {
                installed = _authority.IsCurrent( epoch ) && ReferenceEquals( _conversation, existing );
                if ( installed )
                {
                    dead = existing.Session;
                    existing.Session = replacement;
                }
            }
            if ( !installed )
            {
                await DiscardSessionAsync( backend, replacement ).ConfigureAwait( false );
                throw new AgentAuthorityChangedException( );
            }
            if ( dead is not null )
            {
                await dead.DisposeAsync( ).ConfigureAwait( false );
            }
            return existing;
        }
        if ( existing is not null )
        {
            // A trust or epoch change needs a fresh session and a fresh generation.
            await ClearAsync( cancellationToken ).ConfigureAwait( false );
        }
        var session = await StartAdmittedSessionAsync( backend, options, epoch, cancellationToken ).ConfigureAwait( false );
        Conversation conversation = new( )
        {
            Id = Convert.ToHexString( RandomNumberGenerator.GetBytes( 12 ) ).ToLowerInvariant( ),
            BackendId = backend.Id,
            ModelId = session.ModelId ?? options.ModelId,
            ReasoningEffort = options.ReasoningEffort,
            TrustLevel = preferences.TrustLevel,
            AuthorityEpoch = epoch,
            TtlMinutes = preferences.ConversationTtlMinutes,
            TurnTimeoutMinutes = preferences.TurnTimeoutMinutes,
            Session = session,
            Generation = Interlocked.Increment( ref _generation ),
            UpdatedAt = _clock.GetUtcNow( ),
            State = AgentConversationStates.Idle
        };
        bool admitted;
        lock ( _sync )
        {
            // Install admission: a revocation that follows an epoch change takes
            // this lock afterwards, so it finds a conversation installed before it.
            admitted = _authority.IsCurrent( epoch );
            if ( admitted )
            {
                _conversation = conversation;
            }
        }
        if ( !admitted )
        {
            await DiscardSessionAsync( backend, session ).ConfigureAwait( false );
            throw new AgentAuthorityChangedException( );
        }
        return conversation;
    }

    // A revocation can stop the process while its session is still starting,
    // which surfaces as a closed connection; when the epoch moved, report that
    // as the authority change it is.
    private async ValueTask<IAgentSession> StartAdmittedSessionAsync(
        IAgentBackend backend,
        AgentSessionOptions options,
        long epoch,
        CancellationToken cancellationToken )
    {
        try
        {
            return await backend.StartSessionAsync( options, cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when (
            exception is OperationCanceledException or InvalidOperationException or IOException or ObjectDisposedException &&
            !cancellationToken.IsCancellationRequested && !_authority.IsCurrent( epoch ) )
        {
            throw new AgentAuthorityChangedException( );
        }
    }

    private async Task RunTurnAsync(
        Conversation conversation,
        long turnId,
        string text,
        string? modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken )
    {
        await Task.Yield( );
        TurnHost host = new( this, conversation, turnId );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        using Timer watchdog = new( _ => CheckTurnTimeout( conversation, host, timeout ), null, TimeSpan.FromSeconds( 5 ), TimeSpan.FromSeconds( 5 ) );
        AgentTurnResult result;
        try
        {
            await conversation.Session!.RunTurnAsync(
                new AgentTurnInput { Text = text, ModelId = modelId, ReasoningEffort = reasoningEffort },
                host,
                timeout.Token ).ConfigureAwait( false );
            if ( !IsCurrentTurn( conversation, turnId ) )
            {
                return;
            }
            result = CompleteTurn( conversation, turnId );
        }
        catch ( OperationCanceledException )
        {
            if ( !IsCurrentTurn( conversation, turnId ) )
            {
                return;
            }
            result = FinishTurn( conversation, turnId, AgentTurnOutcomes.Cancelled, new AiError
            {
                Category = AiErrorCategories.Cancelled,
                Summary = conversation.CancelReason ?? (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                    ? "The turn exceeded its time limit."
                    : "The turn was cancelled.")
            } );
        }
        catch ( AiOperationException exception )
        {
            if ( !IsCurrentTurn( conversation, turnId ) )
            {
                return;
            }
            result = FinishTurn( conversation, turnId, AgentTurnOutcomes.Failed, new AiError
            {
                Category = exception.Category,
                Summary = exception.SafeSummary ?? "The turn failed."
            } );
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or IOException or JsonRpcException )
        {
            if ( !IsCurrentTurn( conversation, turnId ) )
            {
                return;
            }
            result = FinishTurn( conversation, turnId, AgentTurnOutcomes.Failed, new AiError
            {
                Category = AiErrorCategories.Internal,
                Summary = "The turn failed unexpectedly."
            } );
        }
        PublishDone( conversation, result );
    }

    private static void CheckTurnTimeout( Conversation conversation, TurnHost host, CancellationTokenSource timeout )
    {
        var limit = TimeSpan.FromMinutes( conversation.TurnTimeoutMinutes );
        if ( host.ActiveElapsed > limit )
        {
            timeout.Cancel( );
        }
    }

    private AgentTurnResult CompleteTurn( Conversation conversation, long turnId )
    {
        string answer;
        lock ( _sync )
        {
            answer = conversation.ActiveText.ToString( );
            AppendEntry( conversation, AgentTranscriptRoles.Assistant, answer, turnId );
            conversation.ActiveText.Clear( );
        }
        long outputGeneration = 0;
        lock ( _sync )
        {
            if ( answer.Length > 0 && IsCurrentTurnUnderLock( conversation, turnId ) )
            {
                outputGeneration = Interlocked.Increment( ref _outputGeneration );
                _outputs.Store( outputGeneration, OutputOwnerId, answer );
            }
        }
        AgentTurnResult result = new( )
        {
            TurnId = turnId,
            Outcome = AgentTurnOutcomes.Completed,
            FinalOutputBytes = Encoding.UTF8.GetByteCount( answer ),
            OutputActions = outputGeneration > 0 ? OutputActions( ) : [],
            OutputGeneration = outputGeneration
        };
        lock ( _sync )
        {
            conversation.OutputGeneration = outputGeneration;
            conversation.State = AgentConversationStates.Completed;
            conversation.ActiveStatus = "Completed";
            MarkIdle( conversation );
        }
        return result;
    }

    private AgentTurnResult FinishTurn( Conversation conversation, long turnId, string outcome, AiError error )
    {
        lock ( _sync )
        {
            if ( conversation.ActiveText.Length > 0 )
            {
                AppendEntry( conversation, AgentTranscriptRoles.Assistant, conversation.ActiveText.ToString( ), turnId );
                conversation.ActiveText.Clear( );
            }
            conversation.LastError = error;
            conversation.State = outcome == AgentTurnOutcomes.Cancelled ? AgentConversationStates.Cancelled : AgentConversationStates.Failed;
            conversation.ActiveStatus = outcome == AgentTurnOutcomes.Cancelled ? "Cancelled" : "Failed";
            MarkIdle( conversation );
        }
        return new AgentTurnResult { TurnId = turnId, Outcome = outcome, Error = error };
    }

    private void MarkIdle( Conversation conversation )
    {
        conversation.UpdatedAt = _clock.GetUtcNow( );
        conversation.ExpiresAt = conversation.TtlMinutes == 0
            ? conversation.UpdatedAt
            : conversation.UpdatedAt.AddMinutes( conversation.TtlMinutes );
        conversation.Pending = null;
    }
}
