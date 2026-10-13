namespace CommandDeck;

public sealed partial class ExtensionPackageRegistry
{
    private async ValueTask MutateAsync( string key, long expectedRevision,
        Func<ExtensionInstallation, ExtensionInstallation> change, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            EnsureRevision( expectedRevision );
            var current = Find( key );
            var updated = change( current ) with { GrantRevision = checked(current.GrantRevision + 1) };
            await ReplaceAsync( updated, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    private async ValueTask MutateDocumentAsync( long expectedRevision, string? revokedKey,
        Func<ExtensionRegistryDocument, ExtensionRegistryDocument> change, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            EnsureRevision( expectedRevision );
            await CommitAsync( change( Document ), revokedKey, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }
}
