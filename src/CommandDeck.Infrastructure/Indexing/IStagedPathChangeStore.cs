namespace CommandDeck;

// A catalog that takes path changes as streams. It stages the items away from
// the database writer lock and publishes them in one short transaction, as a
// full generation is published, so a large subtree is never held in memory.
internal interface IStagedPathChangeStore
{
    // Applies the changes in order in one transaction, as ApplyPathChangesAsync
    // does: each replaces the file and configuration rows at its path, and below
    // it when RemoveDescendants is set, with its streamed items, of which the
    // first with a stable ID wins. Readers see all of the changes or none.
    // Returns the number of items published.
    ValueTask<long> ReplacePathsAsync(
        IReadOnlyList<StagedPathChange> changes,
        CancellationToken cancellationToken );
}

internal sealed record StagedPathChange(
    string Path,
    bool RemoveDescendants,
    IAsyncEnumerable<IndexedItem> Items );
