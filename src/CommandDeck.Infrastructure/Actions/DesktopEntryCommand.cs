using System.Buffers;
using System.Text;

namespace CommandDeck;

public sealed class DesktopEntryCommand
{
    private static readonly SearchValues<char> WhiteSpace = SearchValues.Create( " \t\r\n" );

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
