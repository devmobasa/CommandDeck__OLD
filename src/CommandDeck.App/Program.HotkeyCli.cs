namespace CommandDeck;

public static partial class Program
{
    private const string HotkeyUsage =
        "Use 'commanddeck hotkey install [--chord CHORD]' or 'commanddeck hotkey remove'.";

    // Edits only CommandDeck's Hyprland module and its require line; the caller
    // reloads Hyprland afterwards.
    private static int RunHotkey( CliArguments arguments )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count == 1 ? positionals[0].ToLowerInvariant( ) : string.Empty;
        if ( action is not ("install" or "remove") )
        {
            Console.Error.WriteLine( HotkeyUsage );
            return 2;
        }

        HyprlandHotkeyInstaller installer = new(
            HyprlandHotkeyPaths.FromEnvironment( Environment.GetEnvironmentVariable ) );
        IReadOnlyList<string> messages;
        try
        {
            messages = action == "install"
                ? installer.Install( arguments.GetOption( "--chord" ) ?? HyprlandHotkeyConfig.DefaultChord )
                : installer.Remove( );
        }
        catch ( InvalidOperationException exception )
        {
            Console.Error.WriteLine( exception.Message );
            return 2;
        }

        foreach ( var message in messages )
        {
            Console.WriteLine( message );
        }

        return 0;
    }
}
