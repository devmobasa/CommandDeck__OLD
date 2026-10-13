using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    // Every search checks candidates against the hidden set, so it is cached in
    // memory and rebuilt lazily after a hide, restore, or reset. The set is
    // bounded by HiddenItemLimits.MaximumHiddenItems.
    private volatile HashSet<string>? _hiddenStableIds;

    private void InvalidateHiddenItems( ) => _hiddenStableIds = null;

    public async ValueTask<IReadOnlySet<string>> GetHiddenStableIdsAsync( CancellationToken cancellationToken )
    {
        var cached = _hiddenStableIds;
        if ( cached is not null )
        {
            return cached;
        }

        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT stable_id FROM hidden_items;";
        HashSet<string> ids = new( StringComparer.Ordinal );
        await using var reader =
            await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            ids.Add( reader.GetString( 0 ) );
        }

        _hiddenStableIds = ids;
        return ids;
    }

    public async ValueTask<CustomizationResult> SetHiddenAsync(
        SearchResult result,
        bool hidden,
        CancellationToken cancellationToken )
    {
        EnsureCustomizable( result );
        var stableId = StableCustomizationId( result );
        var now = _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( );

        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );
        if ( hidden )
        {
            await EnsureHiddenCapacityAsync( connection, transaction, stableId, cancellationToken )
                .ConfigureAwait( false );
            await using var insert = connection.CreateCommand( );
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO hidden_items(
                    stable_id, title_snapshot, subtitle_snapshot, source_id, icon_snapshot, hidden_unix_ms)
                VALUES($id, $title, $subtitle, $source, $icon, $now)
                ON CONFLICT(stable_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue( "$id", stableId );
            insert.Parameters.AddWithValue( "$title", result.Title );
            insert.Parameters.AddWithValue( "$subtitle", ( object? ) result.Subtitle ?? DBNull.Value );
            insert.Parameters.AddWithValue( "$source", result.SourceId );
            insert.Parameters.AddWithValue( "$icon", ( object? ) result.Icon ?? DBNull.Value );
            insert.Parameters.AddWithValue( "$now", now );
            await insert.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            await DeleteHiddenAsync( connection, transaction, stableId, cancellationToken ).ConfigureAwait( false );
        }

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        InvalidateHiddenItems( );
        return await ReadCustomizationResultAsync(
            stableId,
            hidden ? $"Hidden {result.Title}" : $"Restored {result.Title}",
            cancellationToken ).ConfigureAwait( false );
    }

    // Restore works by id so an entry whose source item no longer exists can
    // still be removed; it never recreates the item.
    public async ValueTask<CustomizationResult> RestoreHiddenAsync(
        string customizationId,
        CancellationToken cancellationToken )
    {
        var stableId = ParseHiddenId( customizationId );

        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );
        var title = await ReadHiddenTitleAsync( connection, transaction, stableId, cancellationToken )
            .ConfigureAwait( false );
        await DeleteHiddenAsync( connection, transaction, stableId, cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        InvalidateHiddenItems( );

        return await ReadCustomizationResultAsync(
            stableId,
            title is null ? "Item restored" : $"Restored {title}",
            cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<HiddenItemsResult> ListHiddenAsync(
        string? query,
        int limit,
        int offset,
        CancellationToken cancellationToken )
    {
        var pageSize = Math.Clamp( limit, 1, HiddenItemLimits.MaximumHiddenItems );
        var start = Math.Max( 0, offset );
        var pattern = "%" + EscapeLikePattern( (query ?? string.Empty).Trim( ) ) + "%";

        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT h.stable_id, h.title_snapshot, h.subtitle_snapshot, h.source_id, h.icon_snapshot,
                   h.hidden_unix_ms,
                   EXISTS(SELECT 1 FROM catalog_items i WHERE i.stable_id = h.stable_id)
                       OR EXISTS(SELECT 1 FROM extension_commands e WHERE e.stable_id = h.stable_id),
                   COUNT(*) OVER ()
            FROM hidden_items h
            WHERE $pattern = '%%'
               OR h.title_snapshot LIKE $pattern ESCAPE '\'
               OR COALESCE(h.subtitle_snapshot, '') LIKE $pattern ESCAPE '\'
            ORDER BY h.hidden_unix_ms DESC, h.title_snapshot COLLATE NOCASE
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue( "$pattern", pattern );
        command.Parameters.AddWithValue( "$limit", pageSize );
        command.Parameters.AddWithValue( "$offset", start );

        List<HiddenItemSummary> items = [];
        var total = 0;
        await using var reader =
            await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var stableId = reader.GetString( 0 );
            total = reader.GetInt32( 7 );
            items.Add( new HiddenItemSummary
            {
                Id = HiddenItemLimits.IdPrefix + stableId,
                StableId = stableId,
                Title = reader.GetString( 1 ),
                Subtitle = reader.IsDBNull( 2 ) ? null : reader.GetString( 2 ),
                SourceId = reader.GetString( 3 ),
                Icon = reader.IsDBNull( 4 ) ? null : reader.GetString( 4 ),
                HiddenUnixMs = reader.GetInt64( 5 ),
                IsAvailable = reader.GetInt64( 6 ) != 0
            } );
        }

        return new HiddenItemsResult
        {
            Items = items,
            Total = total,
            Truncated = start + items.Count < total
        };
    }

    private static string ParseHiddenId( string customizationId )
    {
        var prefix = HiddenItemLimits.IdPrefix;
        if ( !customizationId.StartsWith( prefix, StringComparison.Ordinal ) ||
            customizationId.Length <= prefix.Length )
        {
            throw new InvalidOperationException( "Unknown hidden item ID." );
        }

        return customizationId[prefix.Length..];
    }

    private static string EscapeLikePattern( string value ) =>
        value.Replace( "\\", "\\\\", StringComparison.Ordinal )
            .Replace( "%", "\\%", StringComparison.Ordinal )
            .Replace( "_", "\\_", StringComparison.Ordinal );

    private static async ValueTask EnsureHiddenCapacityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string stableId,
        CancellationToken cancellationToken )
    {
        await using var capacity = connection.CreateCommand( );
        capacity.Transaction = transaction;
        capacity.CommandText = """
            SELECT EXISTS(SELECT 1 FROM hidden_items WHERE stable_id = $id), COUNT(*)
            FROM hidden_items;
            """;
        capacity.Parameters.AddWithValue( "$id", stableId );
        await using var reader =
            await capacity.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );

        var alreadyHidden = reader.GetInt64( 0 ) != 0;
        var count = reader.GetInt64( 1 );
        if ( !alreadyHidden && count >= HiddenItemLimits.MaximumHiddenItems )
        {
            throw new InvalidOperationException(
                $"At most {HiddenItemLimits.MaximumHiddenItems} items can be hidden. Restore some in Settings first." );
        }
    }

    private static async ValueTask<string?> ReadHiddenTitleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string stableId,
        CancellationToken cancellationToken )
    {
        await using var read = connection.CreateCommand( );
        read.Transaction = transaction;
        read.CommandText = "SELECT title_snapshot FROM hidden_items WHERE stable_id = $id;";
        read.Parameters.AddWithValue( "$id", stableId );
        var value = await read.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );

        return value as string;
    }

    private static async ValueTask DeleteHiddenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string stableId,
        CancellationToken cancellationToken )
    {
        await using var delete = connection.CreateCommand( );
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM hidden_items WHERE stable_id = $id;";
        delete.Parameters.AddWithValue( "$id", stableId );
        await delete.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }
}
