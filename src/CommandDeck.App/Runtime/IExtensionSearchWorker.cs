using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal sealed record ExtensionSearchQuery( ExtensionInstallation Installation, ExtensionProtocol.ExtensionSearchProvider Provider,
    ExtensionSearchRequest Request, bool Global );

internal interface IExtensionSearchWorker
{
    Task<ExtensionSearchResponse> QueryAsync( ExtensionSearchQuery query, CancellationToken cancellationToken );
}
