namespace CommandDeck;

/// <summary>The in-launcher clipboard chord; empty disables it without changing desktop bindings.</summary>
public static class ClipboardShortcut
{
    public const string Default = "CTRL+SHIFT+V";

    public static string Parse( string? value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return string.Empty;
        }

        if ( value.Length > 64 || !value.Any( char.IsAsciiLetter ) )
        {
            throw new InvalidOperationException( "Clipboard shortcut must be Ctrl+Shift+letter or Ctrl+Alt+letter." );
        }

        var chord = SavedSearchValidator.NormalizeShortcut( value )!;
        var parts = chord.Split( '+' );
        if ( parts.Length != 3 || parts[0] != "CTRL" || parts[1] is not ("SHIFT" or "ALT") ||
            parts[2].Length != 1 || !char.IsAsciiLetterUpper( parts[2][0] ) || chord == "CTRL+SHIFT+C" )
        {
            throw new InvalidOperationException( "Use Ctrl+Shift+letter or Ctrl+Alt+letter; Ctrl+Shift+C is reserved for copying file paths." );
        }

        return chord;
    }
}
