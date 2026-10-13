using System.Threading.Channels;

namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    private async ValueTask HandleAgentRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var clientOwnerId = context.OwnerId;
        var cancellationToken = context.CancellationToken;

        switch ( request.Operation )
        {
            case ProtocolOperations.AgentTurnStart:
                await StreamAgentTurnAsync( writer, request, clientOwnerId, cancellationToken ).ConfigureAwait( false );
                return;
            case ProtocolOperations.AgentConversationGet:
                await StreamAgentConversationAsync( writer, request, clientOwnerId, cancellationToken ).ConfigureAwait( false );
                return;
            case ProtocolOperations.AgentTurnCancel:
                await _runtime.CancelAgentTurnAsync( cancellationToken ).ConfigureAwait( false );
                await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
                return;
            case ProtocolOperations.AgentClear:
                await _runtime.ClearAgentAsync( cancellationToken ).ConfigureAwait( false );
                await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
                return;
            case ProtocolOperations.AgentPreferencesUpdate:
                await UpdateAgentPreferencesAsync( writer, request, cancellationToken ).ConfigureAwait( false );
                return;
            case ProtocolOperations.AgentPrewarm:
                // Best-effort intent prewarm: the daemon spawns and connects the
                // session ahead of Enter and acks with a plain done. It never
                // fails the client, so a disabled or signed-out assistant no-ops.
                await _runtime.PrewarmAgentAsync( cancellationToken ).ConfigureAwait( false );
                await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
                return;
        }
        var message = await BuildAgentMessageAsync( request, clientOwnerId, cancellationToken ).ConfigureAwait( false );
        await writer.WriteAsync( message ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    // A settings save reports its failure category the way AI provider settings
    // do, so a client can tell a stale save (conflict) from an invalid one.
    private async ValueTask UpdateAgentPreferencesAsync(
        ClientWriter writer,
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        ProtocolMessage message;
        try
        {
            message = new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.AgentPreferencesResult,
                AgentPreferences = await _runtime.SetAgentPreferencesAsync( request.AgentPreferences!, cancellationToken )
                    .ConfigureAwait( false )
            };
        }
        catch ( AiOperationException exception )
        {
            message = new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.Error,
                Error = $"{exception.Category}: {SafeProtocolError( exception )}"
            };
        }
        await writer.WriteAsync( message ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask<ProtocolMessage> BuildAgentMessageAsync(
        ProtocolRequest request,
        string clientOwnerId,
        CancellationToken cancellationToken )
    {
        var id = request.RequestId;
        var message = request.Operation switch
        {
            ProtocolOperations.AgentBackendsGet => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentBackendsResult,
                AgentBackends = await _runtime.GetAgentBackendsAsync( cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AgentStatusGet => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentStatusResult,
                AgentStatus = await _runtime.GetAgentStatusAsync( cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AgentModelsGet => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentModelsResult,
                AgentModels = await _runtime.DiscoverAgentModelsAsync( request.BackendId, request.IncludeHidden, cancellationToken )
                    .ConfigureAwait( false )
            },
            ProtocolOperations.AgentPreferencesGet => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentPreferencesResult,
                AgentPreferences = await _runtime.GetAgentPreferencesAsync( cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AgentLoginStart or ProtocolOperations.AgentLoginCancel or
                ProtocolOperations.AgentLoginComplete or ProtocolOperations.AgentLogout =>
                new ProtocolMessage
                {
                    RequestId = id,
                    Type = ProtocolMessageTypes.AgentLoginResult,
                    AgentLogin = await HandleAgentLoginAsync( request, cancellationToken ).ConfigureAwait( false )
                },
            ProtocolOperations.AgentTurnInput or ProtocolOperations.AgentContextResolve => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentConversationResult,
                AgentConversation = await _runtime.ResolveAgentPendingAsync(
                    request.PendingRequestId!,
                    request.Operation == ProtocolOperations.AgentTurnInput ? AgentRequestDecisions.Allow : request.Decision!,
                    request.Answers,
                    clientOwnerId,
                    cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AgentTrustGet or ProtocolOperations.AgentTrustUpdate => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentTrustResult,
                AgentTrust = request.Operation == ProtocolOperations.AgentTrustGet
                    ? await _runtime.GetAgentTrustAsync( cancellationToken ).ConfigureAwait( false )
                    : await _runtime.ApplyAgentTrustAsync(
                        clientOwnerId, request.ChallengeId!, request.TrustLevel!.Value, request.Phrase, request.TrustDuration,
                        cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AgentTrustChallenge => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentTrustChallengeResult,
                AgentTrustChallenge = await _runtime.CreateAgentTrustChallengeAsync(
                    clientOwnerId, request.TrustLevel!.Value, cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AgentGrantsGet or ProtocolOperations.AgentGrantsAdd or
            ProtocolOperations.AgentGrantsRemove or ProtocolOperations.AgentGrantsClear => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentGrantsResult,
                AgentGrants = await HandleAgentGrantsAsync( request, cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AgentOperationsGet => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.AgentOperationsResult,
                AgentOperations = _runtime.GetAgentOperations( )
            },
            ProtocolOperations.AgentOutput => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.ActionResult,
                ActionResult = await _runtime.ExecuteAgentOutputAsync(
                    request.Generation, request.OutputAction!, request.Path, request.ConfirmationToken, cancellationToken )
                    .ConfigureAwait( false )
            },
            _ => throw UnknownOperation( request )
        };

        if ( message.AgentStatus?.Login is { } statusLogin )
        {
            _runtime.PresentLoginResult( statusLogin, clientOwnerId );
        }
        if ( message.AgentLogin is { } login )
        {
            _runtime.PresentLoginResult( login, clientOwnerId );
        }

        return message;
    }

    private async ValueTask HandlePackageRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        var message = await BuildPackageMessageAsync( request, cancellationToken ).ConfigureAwait( false );
        await writer.WriteAsync( message ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask<ProtocolMessage> BuildPackageMessageAsync( ProtocolRequest request, CancellationToken cancellationToken )
    {
        var id = request.RequestId;
        return request.Operation switch
        {
            ProtocolOperations.PackageSearch => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.PackageSearchResult,
                PackageSearch = await _runtime.SearchPackagesAsync( request.Query!, request.Limit, cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.PackagePreview => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.PackagePreviewResult,
                PackagePreview = await _runtime.PreviewPackagesAsync( request.PackageOperation!, request.PackageNames, cancellationToken )
                    .ConfigureAwait( false )
            },
            ProtocolOperations.PackageCommit => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.PackageCommitResult,
                PackageCommit = await _runtime.CommitPackagesAsync( request.PreviewId!, cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.PackageCancel => new ProtocolMessage
            {
                RequestId = id,
                Type = ProtocolMessageTypes.PackageCommitResult,
                PackageCommit = await _runtime.CancelPackageOperationAsync( request.OperationId!, cancellationToken ).ConfigureAwait( false )
            },
            _ => throw UnknownOperation( request )
        };
    }

    private ValueTask<AgentLoginState> HandleAgentLoginAsync( ProtocolRequest request, CancellationToken cancellationToken ) =>
        request.Operation switch
        {
            ProtocolOperations.AgentLoginStart => _runtime.StartAgentLoginAsync(
                request.LoginMethod!, request.Credential, cancellationToken, request.BackendId ),
            ProtocolOperations.AgentLoginCancel => _runtime.CancelAgentLoginAsync( cancellationToken, request.BackendId ),
            ProtocolOperations.AgentLoginComplete => _runtime.CompleteAgentLoginAsync( request.LoginCode!, cancellationToken, request.BackendId ),
            ProtocolOperations.AgentLogout => _runtime.AgentLogoutAsync( cancellationToken, request.BackendId ),
            _ => throw UnknownOperation( request )
        };

    private ValueTask<AgentGrantResult> HandleAgentGrantsAsync( ProtocolRequest request, CancellationToken cancellationToken ) =>
        request.Operation switch
        {
            ProtocolOperations.AgentGrantsAdd => _runtime.AddAgentFolderGrantAsync( request.GrantKind!, request.GrantScope!, cancellationToken ),
            ProtocolOperations.AgentGrantsRemove => _runtime.RemoveAgentGrantAsync( request.GrantId!, cancellationToken ),
            ProtocolOperations.AgentGrantsClear => _runtime.ClearAgentGrantsAsync( cancellationToken ),
            ProtocolOperations.AgentGrantsGet => _runtime.GetAgentGrantsAsync( cancellationToken ),
            _ => throw UnknownOperation( request )
        };

    private async ValueTask StreamAgentTurnAsync(
        ClientWriter writer,
        ProtocolRequest request,
        string clientOwnerId,
        CancellationToken cancellationToken )
    {
        var events = _runtime.SubscribeAgentEvents( completeWhenIdle: false, out var subscription );
        try
        {
            AgentConversationSnapshot snapshot;
            try
            {
                snapshot = await _runtime.StartAgentTurnAsync(
                    request.Query!, request.BackendId, request.ModelId, request.ReasoningEffort, clientOwnerId, cancellationToken )
                    .ConfigureAwait( false );
            }
            catch ( AiOperationException exception )
            {
                await writer.WriteAsync( new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.Error,
                    Error = exception.SafeSummary ?? "The assistant could not start the turn."
                } ).ConfigureAwait( false );
                await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
                return;
            }
            await PumpAgentEventsAsync( writer, request.RequestId, events, snapshot.Generation, 0, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _runtime.UnsubscribeAgentEvents( subscription );
        }
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask StreamAgentConversationAsync(
        ClientWriter writer,
        ProtocolRequest request,
        string clientOwnerId,
        CancellationToken cancellationToken )
    {
        var events = _runtime.SubscribeAgentEvents( completeWhenIdle: true, out var subscription );
        try
        {
            var snapshot = _runtime.GetAgentConversation( clientOwnerId );
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.AgentConversationResult,
                AgentConversation = snapshot
            } ).ConfigureAwait( false );
            if ( snapshot.ActiveTurnId > 0 )
            {
                await PumpAgentEventsAsync( writer, request.RequestId, events, snapshot.Generation, snapshot.LastSequence, cancellationToken )
                    .ConfigureAwait( false );
            }
        }
        finally
        {
            _runtime.UnsubscribeAgentEvents( subscription );
        }
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private static async ValueTask PumpAgentEventsAsync(
        ClientWriter writer,
        long requestId,
        ChannelReader<AgentStreamEvent> events,
        long generation,
        long afterSequence,
        CancellationToken cancellationToken )
    {
        await foreach ( var streamEvent in events.ReadAllAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            if ( streamEvent.Generation != generation || streamEvent.Sequence <= afterSequence )
            {
                continue;
            }
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = requestId,
                Type = ProtocolMessageTypes.AgentEvent,
                AgentEvent = streamEvent
            } ).ConfigureAwait( false );
            if ( streamEvent.Kind == AgentEventKinds.Done )
            {
                return;
            }
        }
    }
}
