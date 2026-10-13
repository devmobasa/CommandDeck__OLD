using System.Text;

namespace CommandDeck;

public static class PreferencesTextFormatter
{
    public static IReadOnlyList<string> Format( PreferencesSnapshot snapshot )
    {
        ArgumentNullException.ThrowIfNull( snapshot );
        List<string> lines = [];
        foreach ( var source in snapshot.Preferences.Sources )
        {
            lines.Add(
                $"{source.Position}. {source.SourceId}: " +
                $"search={(source.EnabledInSearch ? "on" : "off")}, " +
                $"home={(source.EnabledOnHome ? "on" : "off")}" );
        }

        lines.Add( $"Home folder only: {(snapshot.Preferences.SearchHomeOnly ? "on" : "off")}" );
        lines.Add( $"Reduce motion: {(snapshot.Preferences.ReduceMotion ? "on" : "off")}" );
        lines.Add(
            $"Web fallback: {EscapeTerminalText( snapshot.Preferences.WebFallbackQuicklinkId ?? "none" )}" );
        lines.Add( $"Preferred editor: {snapshot.Preferences.PreferredEditor}" );
        lines.Add( $"HEY threads open in: {snapshot.Preferences.HeyOpenTarget}" );
        lines.Add(
            $"Graphical editor: {EscapeTerminalText( snapshot.Preferences.GraphicalEditor.Executable )}" );
        lines.Add(
            $"Terminal editor: {EscapeTerminalText( snapshot.Preferences.TerminalEditor.Executable )}" );
        lines.Add( $"Bookmarks: {snapshot.Customizations.Count}" );
        foreach ( var bookmark in snapshot.Customizations )
        {
            var aliases = bookmark.Aliases.Count == 0
                ? "none"
                : string.Join( ", ", bookmark.Aliases );
            var availability = bookmark.IsAvailable ? "available" : "missing";
            lines.Add(
                $"  {bookmark.Position}. {EscapeTerminalText( bookmark.Title )} " +
                $"[{availability}; aliases: {aliases}]" );
        }

        return lines;
    }

    public static string EscapeTerminalText( string value )
    {
        StringBuilder escaped = new( value.Length );
        foreach ( var character in value )
        {
            switch ( character )
            {
                case '\n':
                    escaped.Append( "\\n" );
                    break;
                case '\r':
                    escaped.Append( "\\r" );
                    break;
                case '\t':
                    escaped.Append( "\\t" );
                    break;
                default:
                    if ( char.IsControl( character ) )
                    {
                        escaped.Append( $"\\u{( int ) character:X4}" );
                    }
                    else
                    {
                        escaped.Append( character );
                    }
                    break;
            }
        }

        return escaped.ToString( );
    }
}
