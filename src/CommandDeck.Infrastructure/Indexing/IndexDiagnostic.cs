namespace CommandDeck;

public enum IndexDiagnosticSeverity
{
    Info,
    Warning
}

public sealed class IndexDiagnostic
{
    public required string Message { get; init; }
    public IndexDiagnosticSeverity Severity { get; init; } = IndexDiagnosticSeverity.Info;
}
