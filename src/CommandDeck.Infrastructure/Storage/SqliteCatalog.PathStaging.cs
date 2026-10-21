using Microsoft.Data.Sqlite;

namespace CommandDeck;

// Scoped, resumed, and overflow rescans, and incremental batches that include a
// directory, stream here. Rows are staged as a full rebuild stages them, off the
// database writer lock; each change's items take consecutive stage numbers. One
// transaction then applies the changes in order: it removes a change's path
// rows as ApplyPathChangesAsync does, through the rowid link, and publishes the
// change's staged rows with search rowids above the highest one in use before
// the batch. Readers see either the old rows or the whole batch.
public sealed partial class SqliteCatalog : IStagedPathChangeStore
{
    private const string StagedPathItems = "staged_path_items";
    private const string StagedPathSearchRows = "staged_path_fts";

    async ValueTask<long> IStagedPathChangeStore.ReplacePathsAsync(
        IReadOnlyList<StagedPathChange> changes,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( changes );
        if ( changes.Count == 0 )
        {
            return 0;
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var stage = new GenerationStage( connection, StagedPathItems, StagedPathSearchRows );
        await using ( var createStage = connection.CreateCommand( ) )
        {
            createStage.CommandText = "PRAGMA temp_store = FILE;\n" +
                CreateRowStageSql( StagedPathItems, StagedPathSearchRows, byChange: true );
            await createStage.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        var lastNumbers = await StagePathChangesAsync( connection, changes, cancellationToken ).ConfigureAwait( false );

        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );
        long searchBase;
        await using ( var highest = connection.CreateCommand( ) )
        {
            highest.Transaction = transaction;
            highest.CommandText = "SELECT rowid FROM catalog_fts ORDER BY rowid DESC LIMIT 1;";
            searchBase = await highest.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ) is long value
                ? value
                : 0;
        }

        await using var removeSearchRows = CreateRemovePathSearchRowsCommand( connection );
        removeSearchRows.Transaction = transaction;
        await removeSearchRows.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        await using var removeItems = CreateRemovePathItemsCommand( connection );
        removeItems.Transaction = transaction;
        await removeItems.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        await using var publish = connection.CreateCommand( );
        publish.Transaction = transaction;
        publish.CommandText = CopyStagedRowsSql(
            StagedPathItems,
            StagedPathSearchRows,
            searchBase: "$search_base",
            itemFilter: "change_index = $change_index",
            searchFilter: "rowid > $after_number AND rowid <= $last_number" );
        publish.Parameters.AddWithValue( "$search_base", searchBase );
        AddParameters( publish, "$change_index", "$after_number", "$last_number" );
        await publish.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        long afterNumber = 0;
        for ( var index = 0; index < changes.Count; index++ )
        {
            IndexedPathChange removal = new( )
            {
                Path = changes[index].Path,
                RemoveDescendants = changes[index].RemoveDescendants
            };
            var pathPrefix = PathPrefix( removal.Path );
            BindPathRemoval( removeSearchRows, removal, pathPrefix );
            await removeSearchRows.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            BindPathRemoval( removeItems, removal, pathPrefix );
            await removeItems.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );

            if ( lastNumbers[index] > afterNumber )
            {
                publish.Parameters["$change_index"].Value = index;
                publish.Parameters["$after_number"].Value = afterNumber;
                publish.Parameters["$last_number"].Value = lastNumbers[index];
                await publish.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            }

            afterNumber = lastNumbers[index];
        }

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return afterNumber;
    }

    // Stages each change's items in order and returns, per change, the last
    // stage number its items took.
    private static async ValueTask<long[]> StagePathChangesAsync(
        SqliteConnection connection,
        IReadOnlyList<StagedPathChange> changes,
        CancellationToken cancellationToken )
    {
        await using var stager = await RowStager.CreateAsync(
            connection,
            StagedPathItems,
            StagedPathSearchRows,
            byChange: true,
            cancellationToken ).ConfigureAwait( false );

        var lastNumbers = new long[changes.Count];
        for ( var index = 0; index < changes.Count; index++ )
        {
            var change = changes[index];
            ArgumentException.ThrowIfNullOrEmpty( change.Path );
            var pathPrefix = PathPrefix( change.Path );
            stager.ChangeIndex = index;
            await foreach ( var item in change.Items.WithCancellation( cancellationToken ).ConfigureAwait( false ) )
            {
                if ( !string.Equals( item.Path, change.Path, StringComparison.Ordinal ) &&
                    (!change.RemoveDescendants ||
                     item.Path is null ||
                     !item.Path.StartsWith( pathPrefix, StringComparison.Ordinal )) )
                {
                    throw new InvalidOperationException(
                        "An incremental catalog item must belong to its path change." );
                }

                await stager.StageAsync( item, cancellationToken ).ConfigureAwait( false );
            }

            lastNumbers[index] = stager.Count;
        }

        return lastNumbers;
    }

    private static string PathPrefix( string path ) =>
        Path.EndsInDirectorySeparator( path ) ? path : path + Path.DirectorySeparatorChar;
}
