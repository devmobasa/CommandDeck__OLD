using System.Text.Json;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    public IContributionRegistry? Contributions { get; set; }
    public ContributionRegistrySnapshot ContributionSnapshot => Contributions?.Snapshot ?? ContributionRegistrySnapshot.BuiltIns;

    public async ValueTask<ExtensionRegistryDocument> ReadExtensionRegistryAsync( CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT document_json FROM extension_registry WHERE id = 1;";
        var json = ( string? ) await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
        if ( json is null )
        {
            return new ExtensionRegistryDocument( );
        }
        if ( json.Length > 16 * 1024 * 1024 )
        {
            throw new InvalidDataException( "The extension registry exceeds its limit." );
        }
        var result = JsonSerializer.Deserialize( json, ExtensionRegistryJsonContext.Default.ExtensionRegistryDocument );
        if ( result is null )
        {
            throw new InvalidDataException( "The extension registry is invalid." );
        }

        return result;
    }

    public async ValueTask<long> ReadExtensionRegistryRevisionAsync( CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT revision FROM extension_registry WHERE id = 1;";
        var revision = ( long? ) await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );

        return revision ?? 0;
    }

    public ValueTask CommitExtensionRegistryAsync(
        ExtensionRegistryDocument document, ContributionRegistrySnapshot snapshot, long expectedRevision, CancellationToken cancellationToken ) =>
        CommitExtensionRegistryAsync( document, snapshot, expectedRevision, null, null, cancellationToken );

    public async ValueTask CommitExtensionRegistryAsync(
        ExtensionRegistryDocument document, ContributionRegistrySnapshot snapshot, long expectedRevision,
        string? eraseStorageKey, IReadOnlyList<ExtensionStoredDataRow>? migratedStorage, CancellationToken cancellationToken )
    {
        var json = JsonSerializer.Serialize( document, ExtensionRegistryJsonContext.Default.ExtensionRegistryDocument );
        if ( json.Length > 16 * 1024 * 1024 )
        {
            throw new InvalidDataException( "The extension registry exceeds its limit." );
        }
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        // Registry commits include grant revocation. Do not acknowledge them
        // with the catalog's less durable indexing-oriented NORMAL setting.
        await using ( var durability = connection.CreateCommand( ) )
        {
            durability.CommandText = "PRAGMA synchronous = FULL;";
            await durability.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        await using var transaction = connection.BeginTransaction( deferred: false );
        await using var revision = connection.CreateCommand( );
        revision.Transaction = transaction;
        revision.CommandText = "SELECT revision FROM extension_registry WHERE id = 1;";
        var stored = ( long? ) await revision.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ) ?? 0;
        if ( stored != expectedRevision )
        {
            throw new InvalidOperationException( "The extension registry changed. Refresh and try again." );
        }
        await using var save = connection.CreateCommand( );
        save.Transaction = transaction;
        save.CommandText = """
            INSERT INTO extension_registry(id, revision, document_json) VALUES(1, $revision, $document)
            ON CONFLICT(id) DO UPDATE SET revision = excluded.revision, document_json = excluded.document_json;
            DELETE FROM extension_commands;
            """;
        save.Parameters.AddWithValue( "$revision", document.Revision );
        save.Parameters.AddWithValue( "$document", json );
        await save.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        if ( eraseStorageKey is not null )
        {
            await using var erase = connection.CreateCommand( );
            erase.Transaction = transaction;
            erase.CommandText = """
                INSERT INTO extension_storage(installation_key, revision, values_json) VALUES($key, 1, '{}')
                ON CONFLICT(installation_key) DO UPDATE SET revision = extension_storage.revision + 1, values_json = '{}';
                """;
            erase.Parameters.AddWithValue( "$key", eraseStorageKey );
            await erase.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        foreach ( var row in migratedStorage ?? [] )
        {
            await using var migrate = connection.CreateCommand( );
            migrate.Transaction = transaction;
            migrate.CommandText = "INSERT INTO extension_storage(installation_key, revision, values_json) VALUES($key, $revision, $values) ON CONFLICT(installation_key) DO NOTHING;";
            migrate.Parameters.AddWithValue( "$key", row.InstallationKey );
            migrate.Parameters.AddWithValue( "$revision", row.Revision );
            migrate.Parameters.AddWithValue( "$values", row.ValuesJson );
            await migrate.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        foreach ( var contribution in snapshot.Commands.Where( command => command.Available ) )
        {
            await using var insert = connection.CreateCommand( );
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO extension_commands(stable_id, kind, title, subtitle, icon, provider_id, source_id)
                VALUES($id, $kind, $title, $description, $icon, $provider, $source);
                """;
            insert.Parameters.AddWithValue( "$id", contribution.StableId );
            insert.Parameters.AddWithValue( "$kind", ResultKinds.ExtensionCommand );
            insert.Parameters.AddWithValue( "$icon", ExtensionIcons.Addon );
            insert.Parameters.AddWithValue( "$provider", ProviderIds.Extensions );
            insert.Parameters.AddWithValue( "$title", contribution.Title );
            insert.Parameters.AddWithValue( "$description", contribution.Description );
            insert.Parameters.AddWithValue( "$source", contribution.SourceId );
            await insert.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
    }
}
