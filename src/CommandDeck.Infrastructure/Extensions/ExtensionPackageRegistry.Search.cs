namespace CommandDeck;

public sealed partial class ExtensionPackageRegistry
{
    public ValueTask SetGlobalSearchAsync( string key, bool enabled, long expectedRevision, CancellationToken cancellationToken ) =>
        MutateAsync( key, expectedRevision, installation =>
        {
            if ( installation.Removed || installation.Manifest.SearchProviders.Length == 0 || enabled && installation.Origin == ExtensionOrigins.Development )
            {
                throw new InvalidOperationException( "Global search requires an installed release search provider." );
            }

            return installation with { GlobalSearchEnabled = enabled };
        }, cancellationToken );
}
