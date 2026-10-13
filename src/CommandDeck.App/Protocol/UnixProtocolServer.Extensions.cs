namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    private async ValueTask HandleExtensionSessionAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var owner = context.OwnerId;
        var cancellationToken = context.CancellationToken;

        var sessions = _runtime.ExtensionSessions;
        var input = request.ExtensionInput!;
        switch ( request.Operation )
        {
            case ProtocolOperations.ExtensionOpen:
                var search = request.ResultId!.StartsWith( "extension-item:", StringComparison.Ordinal ) ? _runtime.ExtensionSearch.Consume( owner, request.ResultId, input.SearchLease ) : null;
                await sessions.OpenAsync( owner, search?.CommandId ?? request.ResultId!, input.Features, ( state, token ) => writer.WriteCancellableAsync( new ProtocolMessage
                {
                    RequestId = request.RequestId,
                    Type = ProtocolMessageTypes.ExtensionSession,
                    ExtensionSession = state
                }, token ), cancellationToken, search ).ConfigureAwait( false );
                break;
            case ProtocolOperations.ExtensionPresent:
                sessions.Present( owner, request.SessionId!, input );
                break;
            case ProtocolOperations.ExtensionAction:
                await sessions.ActAsync( owner, request.SessionId!, input, cancellationToken ).ConfigureAwait( false );
                break;
            case ProtocolOperations.ExtensionClose:
                await sessions.CloseAsync( owner, request.SessionId ).ConfigureAwait( false );
                break;
            default:
                throw UnknownOperation( request );
        }
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }
}
