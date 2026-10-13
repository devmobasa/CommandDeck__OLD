using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public sealed partial class ExtensionPackageRegistry
{

    public ValueTask ConfigureAsync( string key, Dictionary<string, JsonElement> values, long expectedRevision, CancellationToken cancellationToken ) =>
        MutateAsync( key, expectedRevision, installation =>
        {
            if ( installation.Removed || values.Count > 16 || values.Keys.Except( installation.Manifest.Preferences.Select( field => field.Id ), StringComparer.Ordinal ).Any( ) )
            {
                throw new InvalidOperationException( "Settings must belong to the installed package." );
            }

            var updated = installation with { Settings = values.ToDictionary( pair => pair.Key, pair => pair.Value.Clone( ), StringComparer.Ordinal ) };
            _ = ExtensionSettingsResolver.Resolve( updated );
            return updated;
        }, cancellationToken );

    // Caller holds the execution lease, which orders this transaction with revocation.
    public ValueTask<CommandDataResult> AccessDataAsync( ExtensionInstallation installation, CommandDataRequest request, CancellationToken cancellationToken )
    {
        installation = Find( installation.Key );
        if ( request.Operation == ExtensionDataOperations.Settings )
        {
            return ValueTask.FromResult( new CommandDataResult( installation.GrantRevision, ExtensionSettingsResolver.Resolve( installation ) ) );
        }
        return DataStore.AccessAsync( installation.Key, request, cancellationToken );
    }

    private async ValueTask MigrateStorageAsync( CancellationToken cancellationToken )
    {
        var legacy = Document.Installations.Where( installation => installation.StorageRevision != 0 || installation.Storage.Count != 0 ).ToArray( );
        if ( legacy.Length == 0 )
        {
            return;
        }
        var rows = legacy.Select( installation => new ExtensionStoredDataRow( installation.Key, installation.StorageRevision,
            JsonSerializer.Serialize( installation.Storage, ExtensionRegistryJsonContext.Default.DictionaryStringJsonElement ) ) ).ToArray( );
        var document = Document with
        {
            Revision = checked(Document.Revision + 1),
            Installations = Document.Installations.Select( installation => installation with { StorageRevision = 0, Storage = [] } ).ToArray( )
        };
        await _catalog.CommitExtensionRegistryAsync( document, ContributionSnapshotBuilder.Build( document ), Document.Revision, null, rows, cancellationToken ).ConfigureAwait( false );
        _document = document;
    }
}
