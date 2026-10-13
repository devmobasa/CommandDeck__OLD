using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal sealed class ExtensionSessionPublisher( ExtensionCommandSession session, IExtensionSessionHost host )
{
    public async ValueTask PublishAsync( string status, string? error, CancellationToken cancellationToken )
    {
        using var delivery = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        delivery.CancelAfter( TimeSpan.FromSeconds( 2 ) );
        var view = session.CurrentView;
        await host.PublishAsync( new ExtensionSessionState
        {
            SessionId = session.Id,
            PackageKey = session.Installation.Key,
            Title = session.Installation.Manifest.Name,
            Status = status,
            Epoch = ExtensionSessionProtocol.ExecutionEpoch,
            Revision = view?.Revision ?? 0,
            ViewId = view?.ViewId,
            Content = status == ExtensionSessionStatuses.View ? view?.Content : null,
            Error = error,
            Debug = session.DebugState,
            Confirmation = session.Confirmation
        }, delivery.Token ).ConfigureAwait( false );
    }
}
