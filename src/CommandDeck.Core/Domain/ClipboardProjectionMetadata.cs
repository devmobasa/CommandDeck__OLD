namespace CommandDeck;

/// <summary>Stored inert metadata. Destinations are lexical URI literals, never file contents.</summary>
public sealed record ClipboardProjectionMetadata(
    bool ExtractedText, bool Truncated, bool ReducedFidelity, string[] Destinations,
    string? ReportedSource = null, int? ImageWidth = null, int? ImageHeight = null,
    string? OpenDestination = null );
