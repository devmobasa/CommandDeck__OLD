using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions;

/// <summary>Bounded host effects authorized by the current no-view command invocation.</summary>
public sealed class CommandEffects
{
    private readonly HostChannel _channel;
    internal CommandEffects( HostChannel channel ) => _channel = channel;

    public async ValueTask CopyTextAsync( string text, CancellationToken cancellationToken = default )
    {
        _channel.RequireMode( ExtensionCommandModes.NoView );
        Views.ViewValidation.ValidateCopyText( text );

        var stamp = _channel.Stamp;
        var request = new CopyTextRequest( stamp.SessionId, stamp.Epoch, stamp.Generation, stamp.InvocationId, text );
        await _channel.CallAsync( ExtensionMethods.CopyText, ExtensionFeatures.CopyText, request,
            ExtensionViewJsonContext.Default.CopyTextRequest, cancellationToken ).ConfigureAwait( false );
    }
}
