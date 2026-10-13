namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    private async ValueTask HandleAiManagementRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        ProtocolMessage message;
        try
        {
            message = request.Operation switch
            {
                ProtocolOperations.AiSettingsGet => new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.AiSettingsResult,
                    AiSettings = await _runtime.GetAiSettingsAsync( cancellationToken ).ConfigureAwait( false )
                },
                ProtocolOperations.AiSettingsUpdate => new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.AiSettingsResult,
                    AiSettings = await _runtime.UpdateAiEnabledAsync(
                        request.Enabled == true, request.ExpectedRevision, cancellationToken ).ConfigureAwait( false )
                },
                ProtocolOperations.AiProvidersSave => new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.AiProviderSaveResult,
                    AiProviderSave = await _runtime.SaveAiProviderAsync(
                        request.AiProvider!, request.Create, request.ExpectedRevision,
                        request.SelectAfterSave, cancellationToken ).ConfigureAwait( false )
                },
                ProtocolOperations.AiProvidersDelete => new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.AiSettingsResult,
                    AiSettings = await _runtime.DeleteAiProviderAsync(
                        request.ProviderId!, request.ExpectedRevision, cancellationToken ).ConfigureAwait( false )
                },
                ProtocolOperations.AiProvidersTest => new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.AiProviderTestResult,
                    AiProviderTest = await _runtime.TestAiProviderDraftAsync(
                        request.AiProvider!, request.DraftRevision, request.ExpectedRevision,
                        cancellationToken ).ConfigureAwait( false )
                },
                _ => throw UnknownOperation( request )
            };
        }
        catch ( AiOperationException exception )
        {
            // Management failures carry their category so clients can distinguish
            // conflict, not-found, invalid-config, busy, and persistence failures.
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.Error,
                Error = $"{exception.Category}: {SafeProtocolError( exception )}"
            } ).ConfigureAwait( false );
            await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
            return;
        }
        await writer.WriteAsync( message ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask HandleAiRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var clientOwnerId = context.OwnerId;
        var cancellationToken = context.CancellationToken;

        switch ( request.Operation )
        {
            case ProtocolOperations.AiClear:
                _runtime.ClearDirectAiClient( clientOwnerId );
                await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
                return;
            case ProtocolOperations.AiStart:
                await StreamAiAsync( writer, request, clientOwnerId, cancellationToken ).ConfigureAwait( false );
                return;
        }

        var message = request.Operation switch
        {
            ProtocolOperations.AiProvidersGet => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.AiProvidersResult,
                AiProviders = _runtime.GetAiProviders( )
            },
            ProtocolOperations.AiModelsGet => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.AiModelsResult,
                AiModels = await _runtime.DiscoverAiModelsAsync(
                    request.ProviderId!, cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AiSelectionGet or ProtocolOperations.AiSelectionUpdate =>
                new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.AiSelectionResult,
                    AiSelection = request.Operation == ProtocolOperations.AiSelectionGet
                        ? await _runtime.GetAiSelectionAsync( cancellationToken ).ConfigureAwait( false )
                        : await _runtime.SetAiSelectionAsync(
                            request.ProviderId, request.ModelId, cancellationToken ).ConfigureAwait( false )
                },
            ProtocolOperations.AiTemplatesGet or ProtocolOperations.AiTemplatesSave or
                ProtocolOperations.AiTemplatesDelete => new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.AiTemplatesResult,
                    AiTemplates = new AiTemplateResult
                    {
                        Templates = [.. await HandleAiTemplatesAsync( request, cancellationToken ).ConfigureAwait( false )]
                    }
                },
            ProtocolOperations.AiPreflight => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.AiPreflightResult,
                AiPreflight = await _runtime.CreateAiPreflightAsync(
                    clientOwnerId, request.AiDraft!, cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.AiOutput => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.ActionResult,
                ActionResult = await _runtime.ExecuteAiOutputAsync(
                    clientOwnerId, request.Generation, request.OutputAction!, request.Path,
                    request.ConfirmationToken, cancellationToken ).ConfigureAwait( false )
            },
            _ => throw UnknownOperation( request )
        };
        await writer.WriteAsync( message ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private ValueTask<IReadOnlyList<AiTemplate>> HandleAiTemplatesAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken ) => request.Operation switch
        {
            ProtocolOperations.AiTemplatesGet => _runtime.GetAiTemplatesAsync( cancellationToken ),
            ProtocolOperations.AiTemplatesSave => _runtime.SaveAiTemplateAsync( request.AiTemplate!, cancellationToken ),
            ProtocolOperations.AiTemplatesDelete => _runtime.DeleteAiTemplateAsync( request.AiTemplateId!, cancellationToken ),
            _ => throw UnknownOperation( request )
        };

    private async ValueTask StreamAiAsync(
        ClientWriter writer,
        ProtocolRequest request,
        string clientOwnerId,
        CancellationToken cancellationToken )
    {
        await _runtime.StreamAiAsync(
            clientOwnerId, request.PreflightId!, request.PayloadDigest!, request.Generation,
            chunk => writer.WriteAsync( new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.AiChunk,
                SessionId = request.SessionId,
                AiChunk = chunk
            } ),
            done => writer.WriteAsync( new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.AiDone,
                SessionId = request.SessionId,
                AiDone = done
            } ),
            cancellationToken ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }
}
