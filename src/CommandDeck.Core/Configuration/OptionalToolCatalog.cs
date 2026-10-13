using System.Diagnostics.CodeAnalysis;

namespace CommandDeck;

public sealed record OptionalTool( string Command, string Package, string Purpose )
{
    public string InstallCommand => $"sudo pacman -S --needed {Package}";
}

// Allow-list of optional PATH tools CommandDeck can name in Doctor and
// runtime health. The catalog never installs anything; it only prints the
// exact pacman command so the user can run it in a visible terminal.
public static class OptionalToolCatalog
{
    public static IReadOnlyList<OptionalTool> All { get; } =
    [
        new("qalc", "libqalculate", "Units, currency, and scientific calculator expressions"),
        new("pdftotext", "poppler", "PDF text extraction for the file index"),
        new("pandoc", "pandoc", "Office-document text extraction for the file index"),
        new("wl-copy", "wl-clipboard", "Copy result text to the clipboard"),
        new("hyprctl", "hyprland", "Windows and workspaces"),
        new("xdg-open", "xdg-utils", "Open files and URLs"),
        new("omarchy-shell", "omarchy", "Omarchy overlay host")
    ];

    private static readonly Dictionary<string, OptionalTool> ByCommand =
        All.ToDictionary( static tool => tool.Command, StringComparer.Ordinal );

    public static bool TryGet( string command, [NotNullWhen( true )] out OptionalTool? tool ) =>
        ByCommand.TryGetValue( command, out tool );

    public static string DoctorDetail( string command, bool found )
    {
        if ( found )
        {
            return "found on PATH";
        }

        if ( TryGet( command, out var tool ) )
        {
            return $"not found. Install with: {tool.InstallCommand}";
        }

        return "not found";
    }
}
