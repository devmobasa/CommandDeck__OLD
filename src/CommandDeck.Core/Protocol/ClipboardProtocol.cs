namespace CommandDeck;

public static class ClipboardActionIds
{
    public const string Copy = "copy";
    public const string Pin = "pin";
    public const string Unpin = "unpin";
    public const string Delete = "delete";
    public const string Keywords = "keywords";
    public const string Clear = "clear";
    public const string Open = "open";
    public const string SequenceStart = "sequence-start";
}

public static partial class ProtocolOperations
{
    public const string ClipboardOpen = "clipboard.open";
    public const string ClipboardClose = "clipboard.close";
    public const string ClipboardQuery = "clipboard.query";
    public const string ClipboardPreview = "clipboard.preview";
    public const string ClipboardAction = "clipboard.action";
    public const string ClipboardSettingsGet = "clipboard.settings.get";
    public const string ClipboardSettingsSet = "clipboard.settings.set";
    public const string ClipboardMigrationAcknowledge = "clipboard.migration.acknowledge";
    public const string ClipboardActionsPrepare = "clipboard.actions.prepare";
    public const string ClipboardReleaseAbort = "clipboard.release.abort";
    public const string LauncherOpenCancel = "launcher.open.cancel";
}

public sealed class ClipboardRequest
{
    public ClipboardPolicyPatch? Patch { get; init; }
    public string? FrontendInstance { get; init; }
    public long OpenGeneration { get; init; }
    public string? SessionId { get; init; }
    public string? EntryId { get; init; }
    public string? Incarnation { get; init; }
    public long EntryRevision { get; init; }
    public long QueryGeneration { get; init; }
    public string? Cursor { get; init; }
    public bool PinnedOnly { get; init; }
    public string? KindFilter { get; init; }
    public string? PreparationId { get; init; }
    public string? ReleaseId { get; init; }
    public int MatchIndex { get; init; }
    public string? Action { get; init; }
    public List<string>? Keywords { get; init; }
    public long PolicyRevision { get; init; }
    public long StoreRevision { get; init; }
    public ClipboardConsent Consent { get; init; }
    public bool Paused { get; init; }
}

public sealed class ClipboardResponse
{
    public ClipboardOperationCapabilities? Capabilities { get; init; }

    public ActionExecutionResult? Action { get; init; }

    public ClipboardPolicyReview? PolicyReview { get; init; }
    public string? SessionId { get; init; }
    public ClipboardHistoryPage? Page { get; init; }
    public ClipboardEntryPreview? Preview { get; init; }
    public ClipboardHistoryPolicy? Policy { get; init; }
    public ClipboardMigrationReview? Migration { get; init; }
    public string? Status { get; init; }
    public ClipboardActionPreparation? Actions { get; init; }
    public ClipboardReleaseAbortResult? Abort { get; init; }
}
