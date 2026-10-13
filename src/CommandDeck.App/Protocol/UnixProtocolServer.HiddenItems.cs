namespace CommandDeck;

public sealed partial class UnixProtocolServer
{

    // Hidden items follow the bookmark shape: the target is the daemon-cached
    // result, the reply is the authoritative customization state, and restore
    // works by id so an unavailable entry can still be removed.
    private async ValueTask<ProtocolMessage> SetHiddenAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        var resultId = RequireValue( request.ResultId, "hidden.set requires resultId." );
        var enabled = request.Enabled;
        if ( enabled is null )
        {
            throw new InvalidOperationException( "hidden.set requires enabled." );
        }

        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.CustomizationResult,
            Customization = await _runtime.SetHiddenAsync(
                resultId,
                enabled.Value,
                cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> RestoreHiddenAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        var id = RequireValue( request.CustomizationId, "hidden.restore requires customizationId." );

        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.CustomizationResult,
            Customization = await _runtime.RestoreHiddenAsync( id, cancellationToken )
                .ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> ListHiddenAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken ) => new( )
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.HiddenItemsResult,
            HiddenItems = await _runtime.ListHiddenAsync(
                request.Query,
                request.Limit,
                request.Offset,
                cancellationToken ).ConfigureAwait( false )
        };
}
