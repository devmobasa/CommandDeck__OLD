namespace CommandDeck;

public enum ClipboardConsent
{
    None, Text, Rich
}

public sealed record ClipboardHistoryPolicy(
    long Revision,
    ClipboardConsent Consent,
    bool Paused,
    int MaximumEntries,
    int RetentionHours,
    int QuotaMiB,
    bool ClearOnStartup );

public sealed record ClipboardMigrationReview(
    bool Required,
    long StoreRevision,
    int EntryCount,
    int PinnedCount,
    int ExpiredCount,
    int OriginalMaximumEntries,
    int EffectiveMaximumEntries,
    bool PendingStartupClear = false );
