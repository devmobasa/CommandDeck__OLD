using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class FakeExtensionSearchWorker(
        Func<ExtensionInstallation, ExtensionProtocol.ExtensionSearchProvider, ExtensionSearchRequest, bool, CancellationToken,
            Task<ExtensionSearchResponse>> query ) : IExtensionSearchWorker
    {
        public Task<ExtensionSearchResponse> QueryAsync( ExtensionSearchQuery input, CancellationToken cancellationToken ) =>
            query( input.Installation, input.Provider, input.Request, input.Global, cancellationToken );
    }
}
