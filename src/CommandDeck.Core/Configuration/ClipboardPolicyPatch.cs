using System.Text.Json.Serialization;

namespace CommandDeck;

[JsonUnmappedMemberHandling( JsonUnmappedMemberHandling.Disallow )]
public sealed record ClipboardPolicyPatch
{
    public ClipboardConsent? Consent { get; init; }
    public bool? Paused { get; init; }
    public int? MaximumEntries { get; init; }
    public int? RetentionHours { get; init; }
    public int? QuotaMiB { get; init; }
    public bool? ClearOnStartup { get; init; }
}

public sealed record ClipboardPolicyReview(
    string Token, long PolicyRevision, long StoreRevision, ClipboardHistoryPolicy Proposed,
    int DeleteCount, int PinnedCount, long RetainedBytes, bool RequiresConfirmation );
