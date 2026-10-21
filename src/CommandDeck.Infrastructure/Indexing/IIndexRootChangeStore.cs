namespace CommandDeck;

// Preferences, revoked catalog coverage and the full reindex request share one commit.
internal interface IIndexRootChangeStore
{
    ValueTask CommitRootsAsync( IndexRootPreferences preferences, IndexExclusionPolicy previousPolicy, IndexExclusionPolicy policy,
        bool reset, CancellationToken cancellationToken );
}
