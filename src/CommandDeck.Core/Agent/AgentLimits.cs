namespace CommandDeck;

public static class AgentLimits
{
    public const string UnrestrictedPhrase = "ALLOW UNRESTRICTED AGENT";
    public const int MaximumPromptBytes = 64 * 1024;
    public const int MaximumAnswerBytes = 1024 * 1024;
    public const int MaximumReasoningBytes = 256 * 1024;
    public const int MaximumEventTextBytes = 16 * 1024;
    public const int MaximumTranscriptEntries = 64;
    public const int MaximumToolCallsPerTurn = 64;
    public const int MaximumContextBytesPerTurn = 512 * 1024;
    public const int MaximumFileBytes = 64 * 1024;
    public const int MaximumDirectoryEntries = 256;
    public const int MaximumSearchMatches = 64;
    public const int MaximumSearchResults = 16;
    public const int MaximumCommandArguments = 64;
    public const int MaximumCommandArgumentBytes = 4 * 1024;
    public const int MaximumCommandOutputBytes = 64 * 1024;
    public const int MaximumCommandSeconds = 120;
    public const int MaximumPackageNames = 16;
    public const int MaximumModels = 256;
    public const int MaximumGrants = 256;
    public const int MaximumPendingRequestSeconds = 120;
    public const int ChallengeLifetimeSeconds = 120;
    public const int PreviewLifetimeSeconds = 120;
    public const int DefaultConversationTtlMinutes = 30;
    public const int MaximumConversationTtlMinutes = 24 * 60;
    // The sandboxed Codex process outlives the conversation transcript: dropping
    // the transcript at its TTL keeps the warm process so the next prompt skips
    // the cold-start spawn, and only this longer idle window reclaims it.
    public const int DefaultBackendKeepAliveMinutes = 120;
    public const int DefaultTurnTimeoutMinutes = 30;
    public const int MinimumTurnTimeoutMinutes = 5;
    public const int MaximumTurnTimeoutMinutes = 120;
    public const int StartupDeadlineSeconds = 10;
    public const int StreamIdleSeconds = 30;
    public const int MaximumFrameBytes = 4 * 1024 * 1024;
    public const int MaximumQuestionOptions = 16;
    public const int MaximumAnswerCharacters = 4 * 1024;
    public const int TrustLeaseHourMinutes = 60;
}
