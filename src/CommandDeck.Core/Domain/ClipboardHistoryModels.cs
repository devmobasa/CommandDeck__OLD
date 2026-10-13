namespace CommandDeck;

public sealed record ClipboardEntryIdentity( string Id, string Incarnation );

public static class ClipboardPresentationKinds
{
    public const string Text = "text";
    public const string Links = "links";
    public const string Images = "images";
    public const string Files = "files";
}

public static class ClipboardHandlerStatuses
{
    public const string Resolved = "Resolved";
    public const string NoAssociations = "NoAssociations";
    public const string InvalidAssociations = "InvalidAssociations";
    public const string ActionsBusy = "ActionsBusy";
    public const string CheckTimedOut = "CheckTimedOut";
    public const string HandlerDiscoveryUnavailable = "HandlerDiscoveryUnavailable";
}

public static class ClipboardHandlerReasons
{
    public const string UnsupportedTerminalLaunch = "UnsupportedTerminalLaunch";
    public const string UnsupportedActivation = "UnsupportedActivation";
    public const string ExecutableUnavailable = "ExecutableUnavailable";
}

public static class ClipboardHandlerRanks
{
    public const string Default = "default";
    public const string Preferred = "preferred";
    public const string Declared = "declared";
}

/// <summary>Inert, bounded classification facts for one entry; never file contents or raw markup.</summary>
public sealed record ClipboardEntryPresentation(
    string Kind, int? ImageWidth, int? ImageHeight, int DestinationCount, bool HasOpenDestination, bool ReducedFidelity )
{
    public static readonly ClipboardEntryPresentation Text = new( ClipboardPresentationKinds.Text, null, null, 0, false, false );
}

public sealed record ClipboardEntrySummary(
    string Id, string Incarnation, long Revision, string Summary, bool Pinned,
    long CopiedUnixSeconds, IReadOnlyList<string> Keywords, ClipboardEntryPresentation Presentation );
public sealed record ClipboardHistoryPage(
    string SessionId, IReadOnlyList<ClipboardEntrySummary> Entries, string? Cursor,
    long StoreRevision, bool NewerAvailable );
public sealed record ClipboardEntryPreview(
    string Id, string Incarnation, long Revision, long QueryGeneration, ClipboardPreviewSlice Slice,
    ClipboardEntryPresentation Presentation, string? OpenDestination = null, bool ExtractedText = false,
    bool Truncated = false, string? ImageSource = null );

public sealed record ClipboardHandlerChoice( string DesktopId, string Label, string Rank, bool Available, string? Reason );

/// <summary>Bounded public view of one resolution. Paths, Exec text and argv never leave the resolver.</summary>
public sealed record ClipboardHandlerResolution( string Status, IReadOnlyList<ClipboardHandlerChoice> Handlers, int Total,
    bool MoreAvailable, string Revision, string? DefaultDesktopId );

/// <summary>A prepared Open capability. It never launches; effects stay unavailable until the release gates pass.</summary>
public sealed record ClipboardActionPreparation( string PreparationId, string ReleaseId, string Status,
    IReadOnlyList<ClipboardHandlerChoice> Handlers, int Total, bool MoreAvailable, int ExpiresInSeconds,
    bool EffectsAvailable, string? UnavailableReason );

public sealed record ClipboardReleaseAbortResult( string FrontendInstance, long OpenGeneration, string? ReleaseId, string Outcome );

/// <summary>Operations the current clipboard browser can offer to this frontend.</summary>
public sealed record ClipboardOperationCapabilities(
    bool Browse, bool Copy, bool InspectOpenHandlers, bool Open, bool Paste, string? EffectsUnavailableReason )
{
    public const string ReleaseBarrierUnavailable = "ReleaseBarrierUnavailable";

    public static readonly ClipboardOperationCapabilities Current =
        new( true, true, true, false, false, ReleaseBarrierUnavailable );
}
