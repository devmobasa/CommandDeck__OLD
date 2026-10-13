using System.Text;

namespace CommandDeck;

public enum ClipboardLaunchTarget
{
    HttpUrl, LocalPath
}

public enum ClipboardLaunchRejection
{
    None, InvalidDesktopExec, NoTargetPlaceholder, TargetKindUnsupported, DescriptorLimit
}

/// <summary>Strictly parsed, target-specific launch argv. Never shell text, never a retokenized target.</summary>
public sealed record ClipboardDesktopLaunch( string Executable, IReadOnlyList<string> Arguments, string TargetMode );

public static class ClipboardDesktopExec
{
    public const int GrammarVersion = 1;
    private const int MaximumArguments = 256;
    private const int MaximumArgumentBytes = 16 * 1024;
    private const int MaximumDescriptorBytes = 32 * 1024;
    private const string Reserved = " \t\n\"'\\><~|&;$*?#()`";

    public static ClipboardLaunchRejection TryExpand( string exec, string localizedName, string? icon, string desktopFilePath,
        ClipboardLaunchTarget targetKind, string target, out ClipboardDesktopLaunch? launch )
    {
        ArgumentNullException.ThrowIfNull( exec );
        ArgumentNullException.ThrowIfNull( target );
        launch = null;
        if ( !TryDecodeStringEscapes( exec, out var decoded ) || !TryTokenize( decoded, out var tokens ) || tokens.Count == 0 )
        {
            return ClipboardLaunchRejection.InvalidDesktopExec;
        }

        List<string> argv = [];
        string? targetMode = null;
        for ( var index = 0; index < tokens.Count; index++ )
        {
            var token = tokens[index];
            if ( token.Quoted )
            {
                if ( token.Text.Contains( '%' ) )
                {
                    return ClipboardLaunchRejection.InvalidDesktopExec;
                }

                if ( index == 0 && !ValidExecutable( token.Text ) )
                {
                    return ClipboardLaunchRejection.InvalidDesktopExec;
                }

                argv.Add( token.Text );
                continue;
            }

            var rejection = ExpandToken( token.Text, index == 0, localizedName, icon, desktopFilePath, targetKind, target, argv, ref targetMode );
            if ( rejection != ClipboardLaunchRejection.None )
            {
                return rejection;
            }
        }

        if ( argv.Count == 0 || !ValidExecutable( argv[0] ) )
        {
            return ClipboardLaunchRejection.InvalidDesktopExec;
        }

        if ( targetMode is null )
        {
            return ClipboardLaunchRejection.NoTargetPlaceholder;
        }

        var total = argv.Sum( static argument => Encoding.UTF8.GetByteCount( argument ) + 1 ) + Encoding.UTF8.GetByteCount( desktopFilePath );
        if ( argv.Count > MaximumArguments || total > MaximumDescriptorBytes ||
            argv.Any( static argument => Encoding.UTF8.GetByteCount( argument ) > MaximumArgumentBytes ) )
        {
            return ClipboardLaunchRejection.DescriptorLimit;
        }

        launch = new( argv[0], argv.Skip( 1 ).ToArray( ), targetMode );
        return ClipboardLaunchRejection.None;
    }

    private static ClipboardLaunchRejection ExpandToken( string token, bool executable, string name, string? icon, string desktopFile,
        ClipboardLaunchTarget targetKind, string target, List<string> argv, ref string? targetMode )
    {
        if ( executable )
        {
            if ( token.Contains( '%' ) || !ValidExecutable( token ) )
            {
                return ClipboardLaunchRejection.InvalidDesktopExec;
            }

            argv.Add( token );
            return ClipboardLaunchRejection.None;
        }

        switch ( token )
        {
            case "%F" or "%U" or "%f" or "%u":
                if ( targetMode is not null )
                {
                    return ClipboardLaunchRejection.InvalidDesktopExec;
                }

                if ( !TrySubstituteTarget( token[1], targetKind, target, out var substituted, out targetMode ) )
                {
                    return ClipboardLaunchRejection.TargetKindUnsupported;
                }

                argv.Add( substituted );
                return ClipboardLaunchRejection.None;
            case "%i":
                if ( !string.IsNullOrWhiteSpace( icon ) )
                {
                    argv.Add( "--icon" );
                    argv.Add( icon );
                }

                return ClipboardLaunchRejection.None;
        }

        return ExpandEmbedded( token, name, desktopFile, targetKind, target, argv, ref targetMode );
    }

    private static ClipboardLaunchRejection ExpandEmbedded( string token, string name, string desktopFile,
        ClipboardLaunchTarget targetKind, string target, List<string> argv, ref string? targetMode )
    {
        StringBuilder expanded = new( );
        var removedOnly = true;
        for ( var index = 0; index < token.Length; index++ )
        {
            var character = token[index];
            if ( character != '%' )
            {
                expanded.Append( character );
                removedOnly = false;
                continue;
            }

            if ( ++index == token.Length )
            {
                return ClipboardLaunchRejection.InvalidDesktopExec;
            }

            var code = token[index];
            switch ( code )
            {
                case '%':
                    expanded.Append( '%' );
                    removedOnly = false;
                    break;
                case 'c':
                    expanded.Append( name );
                    removedOnly = false;
                    break;
                case 'k':
                    expanded.Append( desktopFile );
                    removedOnly = false;
                    break;
                case 'f' or 'u':
                    if ( targetMode is not null || !TrySubstituteTarget( code, targetKind, target, out var substituted, out targetMode ) )
                    {
                        return targetMode is not null ? ClipboardLaunchRejection.InvalidDesktopExec : ClipboardLaunchRejection.TargetKindUnsupported;
                    }

                    expanded.Append( substituted );
                    removedOnly = false;
                    break;
                case 'd' or 'D' or 'n' or 'N' or 'v' or 'm':
                    break;
                default:
                    // %F, %U and %i must be standalone; unknown codes are rejected.
                    return ClipboardLaunchRejection.InvalidDesktopExec;
            }
        }

        if ( expanded.Length > 0 || !removedOnly )
        {
            argv.Add( expanded.ToString( ) );
        }

        return ClipboardLaunchRejection.None;
    }

    private static bool TrySubstituteTarget( char code, ClipboardLaunchTarget kind, string target, out string value, out string? mode )
    {
        value = "";
        mode = null;
        var wantsUrl = code is 'u' or 'U';
        if ( kind == ClipboardLaunchTarget.HttpUrl )
        {
            if ( !wantsUrl )
            {
                return false;
            }

            value = target;
            mode = "url";
            return true;
        }

        value = wantsUrl ? ClipboardFileUri.FromPath( target ) : target;
        mode = wantsUrl ? "file-uri" : "path";
        return true;
    }

    private static bool ValidExecutable( string token ) =>
        token.Length > 0 && !token.Contains( '=' ) && !token.Contains( '%' ) &&
        (!token.Contains( '/' ) || token.StartsWith( '/' )) && !token.Contains( '\0' );

    private static bool TryDecodeStringEscapes( string value, out string decoded )
    {
        StringBuilder builder = new( value.Length );
        for ( var index = 0; index < value.Length; index++ )
        {
            var character = value[index];
            if ( character != '\\' )
            {
                builder.Append( character );
                continue;
            }

            if ( ++index == value.Length )
            {
                decoded = "";
                return false;
            }

            builder.Append( value[index] switch
            {
                's' => ' ',
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                _ => '\0'
            } );
            if ( builder[^1] == '\0' )
            {
                // Keep unknown escapes for Exec-level quoting, which requires backslash pairs inside quotes.
                builder.Length--;
                builder.Append( '\\' ).Append( value[index] );
            }
        }

        decoded = builder.ToString( );
        return !decoded.Contains( '\0' );
    }

    private static bool TryTokenize( string exec, out List<Token> tokens )
    {
        tokens = [];
        var index = 0;
        while ( index < exec.Length )
        {
            if ( exec[index] is ' ' or '\t' or '\n' )
            {
                index++;
                continue;
            }

            if ( exec[index] == '"' )
            {
                if ( !TryReadQuoted( exec, ref index, out var quoted ) )
                {
                    return false;
                }

                tokens.Add( new( quoted, true ) );
                continue;
            }

            var start = index;
            while ( index < exec.Length && exec[index] is not (' ' or '\t' or '\n') )
            {
                if ( Reserved.Contains( exec[index] ) )
                {
                    return false;
                }

                index++;
            }

            tokens.Add( new( exec[start..index], false ) );
        }

        return true;
    }

    private static bool TryReadQuoted( string exec, ref int index, out string value )
    {
        StringBuilder builder = new( );
        index++;
        while ( index < exec.Length )
        {
            var character = exec[index++];
            if ( character == '"' )
            {
                if ( index < exec.Length && exec[index] is not (' ' or '\t' or '\n') )
                {
                    value = "";
                    return false;
                }

                value = builder.ToString( );
                return true;
            }

            if ( character == '\\' )
            {
                if ( index == exec.Length || exec[index] is not ('"' or '`' or '$' or '\\') )
                {
                    value = "";
                    return false;
                }

                builder.Append( exec[index++] );
                continue;
            }

            if ( character is '`' or '$' )
            {
                value = "";
                return false;
            }

            builder.Append( character );
        }

        value = "";
        return false;
    }

    private readonly record struct Token( string Text, bool Quoted );
}
