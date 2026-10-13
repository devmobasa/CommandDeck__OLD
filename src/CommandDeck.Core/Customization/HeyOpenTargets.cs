namespace CommandDeck;

/// <summary>
/// Where a HEY thread opens: HEY's web app (the default, as Omarchy's own HEY
/// launcher does) or the HEY terminal app, <c>hey tui</c>.
/// </summary>
public static class HeyOpenTargets
{
    public const string Web = "web";
    public const string Terminal = "terminal";

    public static bool IsKnown( string? value ) => value is Web or Terminal;

    public static string Normalize( string? value ) => value == Terminal ? Terminal : Web;
}
