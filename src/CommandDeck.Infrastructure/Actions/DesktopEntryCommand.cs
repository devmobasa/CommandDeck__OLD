using System.Buffers;
using System.Text;

namespace CommandDeck;

public sealed class DesktopEntryCommand
{
    private static readonly SearchValues<char> WhiteSpace = SearchValues.Create( " \t\r\n" );

    // Launchers whose own name says nothing about the application: the program
    // is their first word after NAME=value settings, --name=value options and the
    // options below. The first set takes the next word as its value, the second
    // none; any other option is an unsupported form, which gives no program.
    private static readonly string[] PassThroughLaunchers = ["env", "exec", "xdg-terminal-exec"];
    private static readonly string[] Shells = ["sh", "bash", "dash", "zsh"];
    private static readonly string[] OperandOptions = ["-u", "--unset", "-C", "--chdir"];
    private static readonly string[] FlagOptions =
        ["-i", "--ignore-environment", "-0", "--null", "-e", "-l", "--login", "--norc", "--noprofile", "--"];

    // A `-c` script with any of these is more than one plain command.
    private static readonly SearchValues<char> ShellSyntax = SearchValues.Create( "\"'`$;&|<>(){}[]*?~\\" );

    public required string Executable { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public static DesktopEntryCommand Create(
        string exec,
        string applicationName,
        string? icon,
        string desktopFile,
        IReadOnlyList<string> userArguments )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( exec );
        var suppliedArguments = userArguments.Select( ExpandUserArgument ).ToArray( );
        var tokens = Tokenize( exec );
        if ( tokens.Count == 0 )
        {
            throw new InvalidOperationException( "The desktop entry has an empty Exec command." );
        }

        List<string> argv = [];
        var consumedUserArguments = false;
        foreach ( var token in tokens )
        {
            switch ( token )
            {
                case "%F":
                case "%U":
                    argv.AddRange( suppliedArguments );
                    consumedUserArguments = true;
                    continue;
                case "%f":
                case "%u":
                    if ( suppliedArguments.Length > 0 )
                    {
                        argv.Add( suppliedArguments[0] );
                    }

                    consumedUserArguments = true;
                    continue;
                case "%i":
                    if ( !string.IsNullOrWhiteSpace( icon ) )
                    {
                        argv.Add( "--icon" );
                        argv.Add( icon );
                    }

                    continue;
            }

            var expanded = ExpandFieldCodes(
                token,
                applicationName,
                desktopFile,
                suppliedArguments,
                ref consumedUserArguments );
            if ( expanded.Length > 0 )
            {
                argv.Add( expanded );
            }
        }

        if ( !consumedUserArguments )
        {
            argv.AddRange( suppliedArguments );
        }

        if ( argv.Count == 0 || string.IsNullOrWhiteSpace( argv[0] ) )
        {
            throw new InvalidOperationException( "The desktop entry has no executable." );
        }

        return new DesktopEntryCommand
        {
            Executable = argv[0],
            Arguments = argv.Skip( 1 ).ToArray( )
        };
    }

    // The program an Exec line starts, as a search keyword: the basename of its
    // first word, seen through env-style launchers, `flatpak run --command=` and a
    // plain `sh -c` script. Null when that is not cheap to tell, such as a
    // flatpak without --command, whose application ID is the desktop ID.
    public static string? ProgramName( string? exec )
    {
        List<string> words;
        try
        {
            words = Tokenize( exec ?? string.Empty );
        }
        catch ( InvalidOperationException )
        {
            return null;
        }

        for ( var index = 0; index < words.Count; index++ )
        {
            var name = Path.GetFileName( words[index] );
            var shellScript = Shells.Contains( name ) && index + 1 < words.Count && IsScriptOption( words[index + 1] );
            if ( PassThroughLaunchers.Contains( name ) || (Shells.Contains( name ) && !shellScript) )
            {
                while ( index + 1 < words.Count && IsLauncherSetting( words[index + 1] ) )
                {
                    index += OperandOptions.Contains( words[index + 1] ) ? 2 : 1;
                }

                if ( index + 1 < words.Count && words[index + 1].StartsWith( '-' ) )
                {
                    return null;
                }

                continue;
            }

            if ( shellScript )
            {
                var script = index + 2 < words.Count ? words[index + 2] : string.Empty;
                return script.AsSpan( ).IndexOfAny( ShellSyntax ) < 0 ? ProgramName( script ) : null;
            }

            if ( name == "flatpak" )
            {
                var command = words.Skip( index + 1 )
                    .FirstOrDefault( static word => word.StartsWith( "--command=", StringComparison.Ordinal ) );
                name = command is null ? string.Empty : Path.GetFileName( command["--command=".Length..] );
            }

            return name.Length > 0 && name.AsSpan( ).IndexOfAny( WhiteSpace ) < 0 ? name : null;
        }

        return null;
    }

    private static bool IsLauncherSetting( string word ) =>
        OperandOptions.Contains( word ) || FlagOptions.Contains( word ) ||
        (word.Contains( '=' ) && (!word.StartsWith( '-' ) || word.StartsWith( "--", StringComparison.Ordinal )));

    // -c, or flags that include it such as -lc.
    private static bool IsScriptOption( string word ) =>
        word.Length > 1 && word[0] == '-' && word[1] != '-' && word.Contains( 'c' );

    private static string ExpandFieldCodes(
        string token,
        string applicationName,
        string desktopFile,
        IReadOnlyList<string> userArguments,
        ref bool consumedUserArguments )
    {
        StringBuilder expanded = new( token.Length );
        for ( var index = 0; index < token.Length; index++ )
        {
            var current = token[index];
            if ( current != '%' )
            {
                expanded.Append( current );
                continue;
            }

            if ( ++index >= token.Length )
            {
                throw new InvalidOperationException( "The desktop entry Exec command ends with an incomplete field code." );
            }

            AppendFieldCode(
                expanded,
                token[index],
                applicationName,
                desktopFile,
                userArguments,
                ref consumedUserArguments );
        }

        return expanded.ToString( );
    }

    private static void AppendFieldCode(
        StringBuilder expanded,
        char code,
        string applicationName,
        string desktopFile,
        IReadOnlyList<string> userArguments,
        ref bool consumedUserArguments )
    {
        switch ( code )
        {
            case '%':
                expanded.Append( '%' );
                break;
            case 'c':
                expanded.Append( applicationName );
                break;
            case 'k':
                expanded.Append( desktopFile );
                break;
            case 'f':
            case 'u':
                if ( userArguments.Count > 0 )
                {
                    expanded.Append( userArguments[0] );
                }

                consumedUserArguments = true;
                break;
            case 'F':
            case 'U':
                if ( userArguments.Count > 1 )
                {
                    throw new InvalidOperationException(
                        $"Desktop entry field code '%{code}' must be a separate argument when opening multiple items." );
                }

                if ( userArguments.Count == 1 )
                {
                    expanded.Append( userArguments[0] );
                }

                consumedUserArguments = true;
                break;
            case 'd':
            case 'D':
            case 'n':
            case 'N':
            case 'v':
            case 'm':
                break;
            case 'i':
                throw new InvalidOperationException(
                    "Desktop entry field code '%i' must be a separate argument." );
            default:
                throw new InvalidOperationException(
                    $"The desktop entry Exec command contains unsupported field code '%{code}'." );
        }
    }

    private static string ExpandUserArgument( string argument )
    {
        if ( argument.Any( char.IsControl ) )
        {
            throw new InvalidOperationException( "Application arguments cannot contain control characters." );
        }

        if ( argument == "~" ||
            argument.StartsWith( "~/", StringComparison.Ordinal ) ||
            argument.StartsWith( "$HOME", StringComparison.Ordinal ) ||
            argument.StartsWith( "${HOME}", StringComparison.Ordinal ) ||
            argument.StartsWith( "$XDG_", StringComparison.Ordinal ) ||
            argument.StartsWith( "${XDG_", StringComparison.Ordinal ) )
        {
            return PathExpansion.Expand( argument );
        }

        return argument;
    }

    private static List<string> Tokenize( string exec )
    {
        List<string> tokens = [];
        StringBuilder current = new( );
        var quoted = false;
        var escaped = false;

        foreach ( var character in exec )
        {
            if ( escaped )
            {
                current.Append( character );
                escaped = false;
                continue;
            }

            if ( character == '\\' )
            {
                escaped = true;
                continue;
            }

            if ( character == '"' )
            {
                quoted = !quoted;
                continue;
            }

            if ( !quoted && WhiteSpace.Contains( character ) )
            {
                AddToken( tokens, current );
                continue;
            }

            current.Append( character );
        }

        if ( escaped || quoted )
        {
            throw new InvalidOperationException( "The desktop entry Exec command has invalid quoting." );
        }

        AddToken( tokens, current );
        return tokens;
    }

    private static void AddToken( List<string> tokens, StringBuilder current )
    {
        if ( current.Length == 0 )
        {
            return;
        }

        tokens.Add( current.ToString( ) );
        current.Clear( );
    }
}
