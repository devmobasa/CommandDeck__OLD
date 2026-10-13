namespace CommandDeck;

public static class WorkspacePreviewModes
{
    public const string Off = "off";
    public const string Layout = "layout";
    public const string Snapshots = "snapshots";

    // Preserve the saved mode even on frontends without capture support.
    public static string Normalize( string? value ) => value switch
    {
        Off => Off,
        Snapshots => Snapshots,
        _ => Layout
    };
}
