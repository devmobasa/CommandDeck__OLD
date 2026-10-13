namespace CommandDeck;

public sealed record ClipboardHtmlProjection( bool Available, string Text, bool Truncated, string? Reason = null );
