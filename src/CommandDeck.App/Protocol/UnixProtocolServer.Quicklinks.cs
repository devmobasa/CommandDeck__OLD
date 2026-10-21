namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    private async ValueTask<ProtocolMessage> HandleQuicklinksAsync( ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        if ( request.Operation == ProtocolOperations.QuicklinksRemove && !request.Confirmed )
        {
            throw new InvalidOperationException( "quicklinks.remove requires confirmed: true." );
        }
        var result = request.Operation == ProtocolOperations.QuicklinksGet
            ? await _runtime.GetQuicklinksAsync( cancellationToken ).ConfigureAwait( false )
            : await _runtime.ChangeQuicklinkAsync( request.Operation["quicklinks.".Length..],
                request.Quicklink, request.QuicklinkId, cancellationToken ).ConfigureAwait( false );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.QuicklinksResult,
            Quicklinks = result
        };
    }
}
