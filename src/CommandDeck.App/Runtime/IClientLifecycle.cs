namespace CommandDeck;

/// <summary>
/// The runtime state one protocol connection owns, in the order the connection
/// needs it: opened when the connection starts; revoked, closed, and released
/// when it ends. The assistant's conversation and running turn belong to the
/// daemon and survive an ordinary disconnect.
/// </summary>
internal interface IClientLifecycle
{
    /// <summary>Admits the connection's owner.</summary>
    void OpenClient( string ownerId );

    /// <summary>
    /// Withdraws the owner's clipboard and invocation authority at once, before
    /// the connection cancels its requests.
    /// </summary>
    void RevokeClient( string ownerId );

    /// <summary>Closes the owner's extension searches and sessions.</summary>
    ValueTask CloseClientSessionsAsync( string ownerId );

    /// <summary>
    /// After the connection's requests have drained, releases the rest: direct AI
    /// state, file selections, trust challenges, and the clipboard owner.
    /// </summary>
    void ReleaseClient( string ownerId );
}
