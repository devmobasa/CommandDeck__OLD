namespace CommandDeck;

public enum ClipboardContentKind
{
    Text, Images, Links, Files
}

/// <summary>Inert semantic result. Retained indexes identify original, unmodified restore offers.</summary>
public sealed record ClipboardSelectionContent(
    ClipboardContentKind Kind,
    string Text,
    bool ExtractedText,
    bool Truncated,
    bool ReducedFidelity,
    IReadOnlyList<int> RetainedIndexes,
    IReadOnlyList<ClipboardUriDestination> Destinations,
    ClipboardUriDestination? OpenDestination,
    ClipboardHttpUrl? ReportedSource );
