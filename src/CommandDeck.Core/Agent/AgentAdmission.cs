namespace CommandDeck;

/// <summary>Answers whether an authority epoch is still the current one.</summary>
public interface IAgentAuthorityEpoch
{
    bool IsCurrent( long epoch );
}

/// <summary>
/// The authority epoch a session was admitted under. Each place that commits
/// work on that admission (a process launch, a conversation install, a turn)
/// checks it again under the lock its revocation also takes.
/// </summary>
public sealed record AgentAdmission( long Epoch, IAgentAuthorityEpoch Authority )
{
    public bool IsCurrent => Authority.IsCurrent( Epoch );
}

/// <summary>An authority change overtook a turn or prewarm that was still starting.</summary>
public sealed class AgentAuthorityChangedException( )
    : AiOperationException( AiErrorCategories.Unavailable, safeSummary: Summary )
{
    public const string Summary = "Assistant permissions changed while starting; send again.";
}
