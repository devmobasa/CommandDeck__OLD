using System.Runtime.CompilerServices;

namespace CommandDeck;

public sealed partial class IndexCoordinator
{
    // Replaces the rows below a directory with a streamed rescan in one atomic
    // step, so no rescan holds its subtree in memory. A catalog that cannot
    // stage a stream receives the distinct items at once.
    private async ValueTask<long> ReplaceDirectoryAsync(
        string directory,
        string changePath,
        CancellationToken cancellationToken )
    {
        if ( _catalog is IStagedPathChangeStore staged )
        {
            return await staged.ReplacePathsAsync(
                [new StagedPathChange( changePath, RemoveDescendants: true, _files.ScanDirectoryAsync( directory, cancellationToken ) )],
                cancellationToken ).ConfigureAwait( false );
        }

        var items = await _files.IndexDirectoryAsync( directory, cancellationToken ).ConfigureAwait( false );
        await _catalog.ApplyPathChangesAsync(
        [
            new IndexedPathChange
            {
                Path = changePath,
                RemoveDescendants = true,
                Items = items
            }
        ], cancellationToken ).ConfigureAwait( false );
        return items.Count;
    }

    // Applies an incremental batch as one catalog transaction. A batch with a
    // directory streams it, and the batch's other changes join that stage, so
    // readers never see a moved directory at both paths or at neither.
    private async ValueTask ApplyPathBatchAsync(
        List<(IncrementalPathRequest Request, string ChangePath, bool IsDirectory)> batch,
        CancellationToken cancellationToken )
    {
        if ( _catalog is IStagedPathChangeStore staged && batch.Exists( change => change.IsDirectory ) )
        {
            List<StagedPathChange> changes = new( batch.Count );
            foreach ( (var request, var changePath, var isDirectory) in batch )
            {
                changes.Add( new StagedPathChange(
                    changePath,
                    request.RemoveDescendants || isDirectory,
                    isDirectory
                        ? _files.ScanDirectoryAsync( request.Path, cancellationToken )
                        : IndexPathItemsAsync( request.Path, cancellationToken ) ) );
            }

            await staged.ReplacePathsAsync( changes, cancellationToken ).ConfigureAwait( false );
            return;
        }

        List<IndexedPathChange> pathChanges = new( batch.Count );
        foreach ( (var request, var changePath, var isDirectory) in batch )
        {
            var items = isDirectory
                ? await _files.IndexDirectoryAsync( request.Path, cancellationToken ).ConfigureAwait( false )
                : await _files.IndexPathAsync( request.Path, cancellationToken ).ConfigureAwait( false );
            pathChanges.Add( new IndexedPathChange
            {
                Path = changePath,
                RemoveDescendants = request.RemoveDescendants || isDirectory,
                Items = items
            } );
        }

        await _catalog.ApplyPathChangesAsync( pathChanges, cancellationToken ).ConfigureAwait( false );
    }

    // Reads a path when its change is staged, in batch order.
    private async IAsyncEnumerable<IndexedItem> IndexPathItemsAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken )
    {
        foreach ( var item in await _files.IndexPathAsync( path, cancellationToken ).ConfigureAwait( false ) )
        {
            yield return item;
        }
    }
}
