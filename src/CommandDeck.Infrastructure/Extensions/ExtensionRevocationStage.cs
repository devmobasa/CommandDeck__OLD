namespace CommandDeck;

// Registry mutation waits for each barrier in this order. A failed barrier
// prevents activation and leaves the persisted registry revision unchanged.
public enum ExtensionRevocationStage
{
    Search,
    CommandSessions
}
