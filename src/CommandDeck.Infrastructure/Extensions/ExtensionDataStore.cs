using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck;

/// <summary>Durable per-installation data. Callers order changes with the registry's execution lease.</summary>
public sealed class ExtensionDataStore : IDisposable
{
    private readonly SqliteCatalog _catalog;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writes = new( StringComparer.Ordinal );
    private ImmutableDictionary<string, CommandDataResult> _snapshot = ImmutableDictionary<string, CommandDataResult>.Empty;

    private ExtensionDataStore( SqliteCatalog catalog ) => _catalog = catalog;
    internal static ExtensionDataStore Empty( SqliteCatalog catalog ) => new( catalog );

    public static async ValueTask<ExtensionDataStore> OpenAsync( SqliteCatalog catalog, CancellationToken cancellationToken )
    {
        var entries = ImmutableDictionary.CreateBuilder<string, CommandDataResult>( StringComparer.Ordinal );
        foreach ( var row in await catalog.ReadExtensionStorageAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            if ( row.Revision < 0 || row.ValuesJson.Length > 1024 * 1024 )
            {
                throw new InvalidDataException( "The extension storage is invalid." );
            }
            var values = JsonSerializer.Deserialize( row.ValuesJson, ExtensionRegistryJsonContext.Default.DictionaryStringJsonElement );
            if ( values is null )
            {
                throw new InvalidDataException( "The extension storage is missing." );
            }
            Validate( values );
            entries.Add( row.InstallationKey, new( row.Revision, values ) );
        }
        return new ExtensionDataStore( catalog ) { _snapshot = entries.ToImmutable( ) };
    }

    public CommandDataResult Read( string key )
    {
        var current = Volatile.Read( ref _snapshot ).GetValueOrDefault( key );
        return current is null ? new( 0, [] ) : new( current.Revision, current.Values.ToDictionary( pair => pair.Key, pair => pair.Value.Clone( ), StringComparer.Ordinal ) );
    }

    public (long Revision, JsonElement? Value) CacheValue( string key, string cacheKey )
    {
        var current = Volatile.Read( ref _snapshot ).GetValueOrDefault( key );
        return (current?.Revision ?? 0, current is not null && current.Values.TryGetValue( cacheKey, out var value ) ? value.Clone( ) : null);
    }

    public async ValueTask<CommandDataResult> AccessAsync( string key, CommandDataRequest request, CancellationToken cancellationToken )
    {
        if ( request.Operation == ExtensionDataOperations.Read )
        {
            return Read( key );
        }
        var gate = _writes.GetOrAdd( key, _ => new( 1, 1 ) );
        await gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            var current = Read( key );
            if ( request.Operation is not (ExtensionDataOperations.Write or ExtensionDataOperations.Delete) )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidRequest );
            }
            if ( request.ExpectedRevision != current.Revision )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.RevisionConflict );
            }
            try
            { ViewValidation.ValidateId( request.Key ); }
            catch ( InvalidDataException ) { throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidRequest ); }
            if ( request.Operation == ExtensionDataOperations.Delete )
            {
                current.Values.Remove( request.Key! );
            }
            else
            {
                if ( request.Value is not { } value || value.ValueKind == JsonValueKind.Undefined )
                {
                    throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidRequest );
                }
                if ( Encoding.UTF8.GetByteCount( value.GetRawText( ) ) > 16 * 1024 )
                {
                    throw new ExtensionApplicationException( ExtensionErrorCodes.QuotaExceeded );
                }
                current.Values[request.Key!] = value.Clone( );
            }
            try
            { Validate( current.Values ); }
            catch ( InvalidDataException ) { throw new ExtensionApplicationException( ExtensionErrorCodes.QuotaExceeded ); }
            var revision = checked(current.Revision + 1);
            var json = JsonSerializer.Serialize( current.Values, ExtensionRegistryJsonContext.Default.DictionaryStringJsonElement );
            await _catalog.CommitExtensionStorageAsync( new( key, revision, json ), current.Revision, cancellationToken ).ConfigureAwait( false );
            ImmutableInterlocked.Update( ref _snapshot, snapshot => snapshot.SetItem( key, new( revision, current.Values ) ) );
            return new( revision, [] );
        }
        finally
        {
            gate.Release( );
        }
    }

    // Called after the registry commits its settings and data erase in one transaction.
    internal void PublishErase( string key ) => ImmutableInterlocked.Update( ref _snapshot,
        snapshot => snapshot.SetItem( key, new( checked((snapshot.GetValueOrDefault( key )?.Revision ?? 0) + 1), [] ) ) );

    public void Dispose( )
    {
        foreach ( var gate in _writes.Values )
        {
            gate.Dispose( );
        }
    }

    internal static void Validate( Dictionary<string, JsonElement>? values )
    {
        if ( values is null || values.Count > 64 )
        {
            throw new InvalidDataException( "An installation can store at most 64 keys." );
        }
        var bytes = 0;
        foreach ( var (key, value) in values )
        {
            ViewValidation.ValidateId( key );
            if ( value.ValueKind == JsonValueKind.Undefined )
            {
                throw new InvalidDataException( "A stored value is missing." );
            }
            var size = Encoding.UTF8.GetByteCount( value.GetRawText( ) );
            if ( size > 16 * 1024 )
            {
                throw new InvalidDataException( "A stored value must be at most 16 KiB." );
            }
            bytes += key.Length + size;
        }
        if ( bytes > 64 * 1024 )
        {
            throw new InvalidDataException( "An installation can store at most 64 KiB." );
        }
    }
}
