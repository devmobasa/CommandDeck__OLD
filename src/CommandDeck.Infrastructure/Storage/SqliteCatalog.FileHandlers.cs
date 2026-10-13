using System.Text.Json;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    public async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<CommandAction>>>
        GetOpenWithActionsAsync(
            IReadOnlyList<string> paths,
            int maximumHandlersPerFile,
            CancellationToken cancellationToken )
    {
        if ( paths.Count == 0 )
        {
            return new Dictionary<string, IReadOnlyList<CommandAction>>( StringComparer.Ordinal );
        }
        var handlerLimit = Math.Clamp( maximumHandlersPerFile, 1, 12 );
        var handlers = await ReadFileHandlersAsync( cancellationToken )
            .ConfigureAwait( false );
        var mimeTypes =
            await _mimeTypeResolver.ResolveManyAsync(
                paths.Distinct( StringComparer.Ordinal ).ToArray( ), cancellationToken )
                .ConfigureAwait( false );
        Dictionary<string, IReadOnlyList<CommandAction>> result = new( StringComparer.Ordinal );
        foreach ( var path in paths.Distinct( StringComparer.Ordinal ) )
        {
            var mimeType = mimeTypes.GetValueOrDefault( path );
            if ( mimeType is null )
            {
                result[path] = [];
                continue;
            }
            result[path] = ActionsForMimeType( handlers, mimeType, path, handlerLimit );
        }
        return result;
    }

    private static CommandAction[] ActionsForMimeType(
        IReadOnlyList<HandlerRecord> handlers,
        string mimeType,
        string path,
        int handlerLimit ) => handlers
        .Where( value => value.MimeTypes.Contains( mimeType, StringComparer.OrdinalIgnoreCase ) )
        .OrderBy( static value => value.Title, StringComparer.OrdinalIgnoreCase )
        .Take( handlerLimit )
        .Select( value => BuildOpenWithAction( value, path ) )
        .ToArray( );

    private async ValueTask<IReadOnlyList<HandlerRecord>> ReadFileHandlersAsync(
        CancellationToken cancellationToken )
    {
        if ( _fileHandlerCache is not null )
        {
            return _fileHandlerCache;
        }
        await _fileHandlerCacheLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _fileHandlerCache is not null )
            {
                return _fileHandlerCache;
            }
            await using var connection = await OpenAsync( cancellationToken )
                .ConfigureAwait( false );
            await using var command = connection.CreateCommand( );
            command.CommandText = """
                SELECT title, payload_json
                FROM catalog_items
                WHERE kind = $kind AND payload_json IS NOT NULL
                ORDER BY title COLLATE NOCASE, stable_id;
                """;
            command.Parameters.AddWithValue( "$kind", ResultKinds.Application );
            await using var reader = await command.ExecuteReaderAsync( cancellationToken )
                .ConfigureAwait( false );
            List<HandlerRecord> handlers = [];
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                var payload = ReadHandlerPayload( reader.GetString( 1 ) );
                var mimeTypes = payload.GetValueOrDefault( "mimeTypes", string.Empty ).Split(
                    ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
                if ( mimeTypes.Length > 0 && payload.ContainsKey( "exec" ) &&
                    payload.ContainsKey( "desktopId" ) )
                {
                    handlers.Add( new HandlerRecord( reader.GetString( 0 ), payload, mimeTypes ) );
                }
            }
            _fileHandlerCache = handlers;
            return handlers;
        }
        finally
        {
            _fileHandlerCacheLock.Release( );
        }
    }

    private static Dictionary<string, string> ReadHandlerPayload( string json )
    {
        Dictionary<string, string> values = new( StringComparer.Ordinal );
        try
        {
            using var document = JsonDocument.Parse( json );
            foreach ( var property in document.RootElement.EnumerateObject( ) )
            {
                if ( property.Value.ValueKind == JsonValueKind.String )
                {
                    values[property.Name] = property.Value.GetString( ) ?? string.Empty;
                }
            }
        }
        catch ( JsonException )
        {
        }
        return values;
    }

    private static CommandAction BuildOpenWithAction( HandlerRecord handler, string path )
    {
        Dictionary<string, string> arguments = new( handler.Payload, StringComparer.Ordinal )
        {
            ["applicationName"] = handler.Title,
            ["launchArguments"] = LaunchArgumentCodec.Encode( [path] )
        };
        return new CommandAction
        {
            Id = $"open-with:{handler.Payload["desktopId"]}",
            Title = $"Open With {handler.Title}",
            Kind = ActionKinds.LaunchDesktop,
            Arguments = arguments
        };
    }

    private sealed record HandlerRecord(
        string Title,
        Dictionary<string, string> Payload,
        IReadOnlyList<string> MimeTypes );
}

public interface IMimeTypeResolver
{
    ValueTask<IReadOnlyDictionary<string, string?>> ResolveManyAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken );
}

public sealed class SharedMimeTypeResolver : IMimeTypeResolver
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds( 750 );
    private const int MaximumMimeOutputBytes = 20 * 1024;
    private const int MaximumMimeErrorBytes = 4 * 1024;
    private const long MaximumMimeProcessMemoryBytes = 64L * 1024 * 1024;
    private const int MaximumBatchSize = 64;
    private const int MaximumCacheEntries = 4096;

    // Least recently used paths leave first, so the rows a user keeps seeing stay
    // cached when a burst of new paths passes through.
    private readonly object _cacheGate = new( );
    private readonly Dictionary<string, LinkedListNode<MimeCacheEntry>> _cache = new( StringComparer.Ordinal );
    private readonly LinkedList<MimeCacheEntry> _recency = new( );
    private readonly int _capacity;

    public SharedMimeTypeResolver( )
        : this( MaximumCacheEntries )
    {
    }

    internal SharedMimeTypeResolver( int capacity )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( capacity );
        _capacity = capacity;
    }

    internal int CachedPathCount
    {
        get
        {
            lock ( _cacheGate )
            {
                return _cache.Count;
            }
        }
    }

    public async ValueTask<IReadOnlyDictionary<string, string?>> ResolveManyAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken )
    {
        Dictionary<string, string?> resolved = new( StringComparer.Ordinal );
        List<(string Path, FileStamp Stamp)> missing = [];
        foreach ( var path in paths.Distinct( StringComparer.Ordinal ) )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var stamp = ReadStamp( path );
            if ( TryGetCached( path, stamp, out var cached ) )
            {
                resolved[path] = cached;
            }
            else
            {
                missing.Add( (path, stamp) );
            }
        }

        if ( missing.Count > 0 && ProcessRunner.ExistsOnPath( "file" ) )
        {
            foreach ( var batch in missing.Chunk( MaximumBatchSize ) )
            {
                var result = await ProcessRunner.RunBoundedAsync(
                    "file",
                    ["--brief", "--mime-type", "--no-pad", "--", .. batch.Select( static item => item.Path )],
                    workingDirectory: null,
                    QueryTimeout,
                    new ProcessExecutionLimits
                    {
                        MaximumStandardOutputBytes = MaximumMimeOutputBytes,
                        MaximumStandardErrorBytes = MaximumMimeErrorBytes,
                        MaximumMemoryBytes = MaximumMimeProcessMemoryBytes,
                        ClearEnvironment = true
                    },
                    cancellationToken ).ConfigureAwait( false );
                var lines = result.StandardOutput.Replace( "\r\n", "\n", StringComparison.Ordinal )
                    .Split( '\n', StringSplitOptions.RemoveEmptyEntries );
                for ( var index = 0; index < batch.Length; index++ )
                {
                    var mimeType = result.ExitCode == 0 && !result.TimedOut &&
                                   !result.AnyOutputLimitExceeded && index < lines.Length
                        ? Normalize( lines[index] )
                        : null;
                    resolved[batch[index].Path] = mimeType;
                    Remember( batch[index].Path, batch[index].Stamp, mimeType );
                }
            }
        }

        foreach ( (var path, var stamp) in missing )
        {
            if ( !resolved.ContainsKey( path ) )
            {
                resolved[path] = null;
                Remember( path, stamp, null );
            }
        }
        return resolved;
    }

    private bool TryGetCached( string path, FileStamp stamp, out string? mimeType )
    {
        lock ( _cacheGate )
        {
            if ( _cache.TryGetValue( path, out var node ) && node.Value.Stamp == stamp )
            {
                _recency.Remove( node );
                _recency.AddLast( node );
                mimeType = node.Value.MimeType;
                return true;
            }
        }

        mimeType = null;
        return false;
    }

    private void Remember( string path, FileStamp stamp, string? mimeType )
    {
        lock ( _cacheGate )
        {
            if ( _cache.Remove( path, out var replaced ) )
            {
                _recency.Remove( replaced );
            }

            _cache[path] = _recency.AddLast( new MimeCacheEntry( path, stamp, mimeType ) );
            while ( _cache.Count > _capacity )
            {
                var oldest = _recency.First!;
                _recency.RemoveFirst( );
                _cache.Remove( oldest.Value.Path );
            }
        }
    }

    private static string? Normalize( string value )
    {
        var mimeType = value.Trim( );
        return mimeType.Length is > 0 and <= 255 &&
            mimeType.Contains( '/', StringComparison.Ordinal ) &&
            mimeType.All( static character => !char.IsControl( character ) && !char.IsWhiteSpace( character ) )
                ? mimeType
                : null;
    }

    private static FileStamp ReadStamp( string path )
    {
        try
        {
            FileInfo info = new( path );
            return new FileStamp( info.Exists, info.Length, info.LastWriteTimeUtc.Ticks );
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException )
        {
            return default;
        }
    }

    private readonly record struct FileStamp( bool Exists, long Length, long ModifiedTicks );
    private sealed record MimeCacheEntry( string Path, FileStamp Stamp, string? MimeType );
}
