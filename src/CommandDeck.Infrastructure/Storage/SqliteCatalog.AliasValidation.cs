using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    internal static async ValueTask EnsureAliasAvailableAsync( SqliteConnection connection, SqliteTransaction transaction,
        string alias, string aliasNamespace, string? ownerId, IReadOnlyList<QuicklinkOptions> configured,
        CancellationToken cancellationToken )
    {
        AliasValidator.NormalizeAndValidate( alias );
        var configuredConflict = configured.Any( link =>
            string.Equals( link.Alias, alias, StringComparison.OrdinalIgnoreCase ) &&
            (aliasNamespace != "quicklink" || !string.Equals( link.Id, ownerId, StringComparison.OrdinalIgnoreCase )) );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM aliases WHERE alias=$alias COLLATE NOCASE
                          AND ($namespace!='bookmark' OR stable_id!=$owner))
                OR EXISTS(SELECT 1 FROM filter_aliases WHERE alias=$alias COLLATE NOCASE AND $namespace!='filter')
                OR EXISTS(SELECT 1 FROM user_quicklinks WHERE alias=$alias COLLATE NOCASE
                          AND ($namespace!='quicklink' OR id!=$owner COLLATE NOCASE));
            """;
        command.Parameters.AddWithValue( "$alias", alias );
        command.Parameters.AddWithValue( "$namespace", aliasNamespace );
        command.Parameters.AddWithValue( "$owner", ownerId ?? string.Empty );
        var conflict = ( long ) (await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ))!;
        if ( configuredConflict || conflict != 0 )
        {
            throw new InvalidOperationException( $"Alias '{alias}' is already assigned." );
        }
    }
}
