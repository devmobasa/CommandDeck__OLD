using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore : IIndexRootChangeStore
{
    public async ValueTask CommitRootsAsync( IndexRootPreferences preferences, IndexExclusionPolicy previousPolicy, IndexExclusionPolicy policy,
        bool reset, CancellationToken cancellationToken )
    {
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction = ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );
        if ( reset )
        {
            await ResetRowsAsync( connection, transaction, cancellationToken ).ConfigureAwait( false );
        }
        await WriteIndexRootsAsync( connection, transaction, preferences, cancellationToken ).ConfigureAwait( false );

        await PurgeUncoveredRootsAsync( connection, transaction, policy, cancellationToken ).ConfigureAwait( false );
        await PurgeRootPolicyAsync( connection, transaction, previousPolicy, policy, cancellationToken ).ConfigureAwait( false );
        await SqliteCatalog.ExecuteNonQueryAsync( connection, transaction,
            "INSERT INTO catalog_state(key, value) VALUES('reindex_required', '1') " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        if ( reset )
        {
            InvalidateHiddenItems( );
        }
    }

    private static async ValueTask PurgeUncoveredRootsAsync( SqliteConnection connection, SqliteTransaction transaction,
        IndexExclusionPolicy policy, CancellationToken cancellationToken )
    {
        await SqliteCatalog.ExecuteNonQueryAsync( connection, transaction,
            "CREATE TEMP TABLE effective_root_coverage(path TEXT, lower_path TEXT, upper_path TEXT);", cancellationToken ).ConfigureAwait( false );
        var roots = policy.ResolveRoots( );
        foreach ( var (boundary, _) in roots.Where( root => roots.Count( other =>
            other.Boundary.CanonicalRoot == root.Boundary.CanonicalRoot ) == 1 ) )
        {
            var prefix = Path.EndsInDirectorySeparator( boundary.CanonicalRoot ) ? boundary.CanonicalRoot : boundary.CanonicalRoot + Path.DirectorySeparatorChar;
            await using var insert = connection.CreateCommand( );
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO effective_root_coverage VALUES($path, $lower, $upper);";
            insert.Parameters.AddWithValue( "$path", boundary.CanonicalRoot );
            insert.Parameters.AddWithValue( "$lower", prefix );
            insert.Parameters.AddWithValue( "$upper", prefix[..^1] + ( char ) (prefix[^1] + 1) );
            await insert.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        // Canonical coverage also removes rows from a root whose symlink has
        // disappeared or changed target since the last successful scan.
        const string uncovered = """
            source_id IN ($files, $config) AND NOT EXISTS (
                SELECT 1 FROM effective_root_coverage r WHERE catalog_items.path = r.path
                    OR (catalog_items.path >= r.lower_path AND catalog_items.path < r.upper_path))
            """;
        await using var purge = connection.CreateCommand( );
        purge.Transaction = transaction;
        purge.CommandText = $"""
            DELETE FROM catalog_fts WHERE rowid IN (SELECT fts_rowid FROM catalog_items WHERE {uncovered});
            DELETE FROM catalog_items WHERE {uncovered};
            DROP TABLE effective_root_coverage;
            """;
        purge.Parameters.AddWithValue( "$files", SourceIds.Files );
        purge.Parameters.AddWithValue( "$config", SourceIds.Configuration );
        await purge.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    // No source content is read in the preference transaction. Retained file
    // rows keep their identity and Home coverage, with only allowed search
    // fields. Changed extraction limits require fresh content, so changed root
    // policies revoke old bodies and extracted configuration before that scan.
    private static async ValueTask PurgeRootPolicyAsync( SqliteConnection connection, SqliteTransaction transaction,
        IndexExclusionPolicy previousPolicy, IndexExclusionPolicy policy, CancellationToken cancellationToken )
    {
        await using var paths = connection.CreateCommand( );
        paths.Transaction = transaction;
        paths.CommandText = """
            SELECT DISTINCT path FROM catalog_items
            WHERE +source_id IN ($files, $config);
            """;
        paths.Parameters.AddWithValue( "$files", SourceIds.Files );
        paths.Parameters.AddWithValue( "$config", SourceIds.Configuration );
        List<string> affected = [];
        await using ( var reader = await paths.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                affected.Add( reader.GetString( 0 ) );
            }
        }

        Dictionary<IndexRootOptions, string> identities = [];
        string Identity( IndexRootOptions options )
        {
            if ( !identities.TryGetValue( options, out var identity ) )
            {
                identity = JsonSerializer.Serialize( options with { Path = IndexRootPaths.Normalize( options.Path ) },
                    CommandDeckJsonContext.Default.IndexRootOptions );
                identities.Add( options, identity );
            }
            return identity;
        }

        foreach ( var path in affected )
        {
            var root = policy.FindRoot( path );
            var previous = previousPolicy.FindRoot( path );
            var unchangedPolicy = root is not null && previous is not null &&
                root.Value.Boundary.CanonicalRoot == previous.Value.Boundary.CanonicalRoot &&
                Identity( root.Value.Options ) == Identity( previous.Value.Options );
            var keep = root is not null && policy.ShouldObserve( path );
            var fields = root is null ? default : FileIndexPolicyResolver.Resolve( root.Value.Options, path );
            await using var command = connection.CreateCommand( );
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM catalog_fts WHERE rowid IN (
                    SELECT fts_rowid FROM catalog_items WHERE path = $path
                    AND ((source_id = $config AND $configKeep = 0) OR (source_id = $files AND $keep = 0)));
                DELETE FROM catalog_items WHERE path = $path
                    AND ((source_id = $config AND $configKeep = 0) OR (source_id = $files AND $keep = 0));
                UPDATE catalog_fts SET body = CASE WHEN $content THEN body ELSE '' END,
                    title = CASE WHEN $title THEN title ELSE '' END,
                    subtitle = CASE WHEN $metadata THEN subtitle ELSE '' END,
                    path = CASE WHEN $metadata THEN path ELSE '' END
                WHERE rowid IN (SELECT fts_rowid FROM catalog_items WHERE path = $path AND source_id = $files);
                UPDATE catalog_items SET title_in_search = title_in_search AND $title,
                    metadata_in_search = metadata_in_search AND $metadata,
                    index_policy_json = $policy
                WHERE path = $path AND source_id = $files;
                """;
            command.Parameters.AddWithValue( "$path", path );
            command.Parameters.AddWithValue( "$files", SourceIds.Files );
            command.Parameters.AddWithValue( "$config", SourceIds.Configuration );
            command.Parameters.AddWithValue( "$keep", keep );
            command.Parameters.AddWithValue( "$configKeep", keep && fields.IncludeContent && unchangedPolicy );
            command.Parameters.AddWithValue( "$content", fields.IncludeContent && unchangedPolicy );
            command.Parameters.AddWithValue( "$title", fields.IncludeFileName );
            command.Parameters.AddWithValue( "$metadata", fields.IncludeMetadata );
            command.Parameters.AddWithValue( "$policy", fields.Provenance is null ? "[]" :
                JsonSerializer.Serialize( fields.Provenance.ToList( ), CommandDeckJsonContext.Default.ListIndexFieldPolicy ) );
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
    }

}
