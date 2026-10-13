namespace CommandDeck;

public sealed partial class CommandDeckRuntime : IClientLifecycle
{
    void IClientLifecycle.OpenClient( string ownerId ) => _clipboardHistory?.RegisterOwner( ownerId );

    void IClientLifecycle.RevokeClient( string ownerId )
    {
        _clipboardHistory?.RevokeOwner( ownerId );
        RevokeInvocationClient( ownerId );
    }

    ValueTask IClientLifecycle.CloseClientSessionsAsync( string ownerId )
    {
        _extensionSearch?.CloseOwner( ownerId );
        return _extensionSessions?.CloseAsync( ownerId ) ?? ValueTask.CompletedTask;
    }

    // The transport has closed the extension sessions and joined the request
    // tasks. The assistant's conversation and its daemon-owned turn deliberately
    // survive; only the client's trust challenges go.
    void IClientLifecycle.ReleaseClient( string ownerId )
    {
        ClearDirectAiClient( ownerId );
        _fileSelection.Clear( ownerId );
        ClearAgentClient( ownerId );
        _clipboardHistory?.RemoveOwner( ownerId );
    }
}
