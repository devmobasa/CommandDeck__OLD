using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal interface IExtensionSessionHost
{
    ValueTask PublishAsync( ExtensionSessionState state, CancellationToken cancellationToken );
    ValueTask CopyAsync( ExtensionCommandSession session, CopyTextRequest request, CancellationToken cancellationToken );
    ValueTask<CommandDataResult> DataAsync( ExtensionCommandSession session, CommandDataRequest request, CancellationToken cancellationToken );
    ValueTask<BrokerResult> BrokerAsync( ExtensionCommandSession session, BrokerRequest request, CancellationToken cancellationToken );
    void RecordDiagnostic( ExtensionDiagnostic diagnostic );
}

public sealed partial class ExtensionSessionService
{
    private sealed class SessionHost( ExtensionSessionService service, string installationKey,
        Func<ExtensionSessionState, CancellationToken, ValueTask> publish ) : IExtensionSessionHost
    {
        public ValueTask PublishAsync( ExtensionSessionState state, CancellationToken cancellationToken ) => publish( state, cancellationToken );
        public ValueTask CopyAsync( ExtensionCommandSession session, CopyTextRequest request, CancellationToken cancellationToken ) => service.CopyAsync( session, request, cancellationToken );
        public ValueTask<CommandDataResult> DataAsync( ExtensionCommandSession session, CommandDataRequest request, CancellationToken cancellationToken ) => service.DataAsync( session, request, cancellationToken );
        public ValueTask<BrokerResult> BrokerAsync( ExtensionCommandSession session, BrokerRequest request, CancellationToken cancellationToken ) => service.BrokerAsync( session, request, cancellationToken );
        public void RecordDiagnostic( ExtensionDiagnostic diagnostic ) => service._development.Record( installationKey, diagnostic );
    }
}
