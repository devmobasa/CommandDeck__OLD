namespace CommandDeck.ExtensionProtocol;

public static class ExtensionSessionProtocol
{
    /// <summary>Reserved for future resumption. Current sessions always use a fresh session ID and epoch one.</summary>
    public const long ExecutionEpoch = 1;
}
