using System.Globalization;
using System.Text;

namespace CommandDeck;

internal static class HyprlandDispatch
{
    internal const string Executable = "hyprctl";
    internal const string Command = "dispatch";
    internal const string AcceptedOutput = "ok";

    internal static bool WasAccepted( ProcessResult result ) =>
        result.ExitCode == 0 &&
        !result.TimedOut &&
        !result.AnyOutputLimitExceeded &&
        string.Equals( result.StandardOutput.Trim( ), AcceptedOutput, StringComparison.Ordinal );

    internal static string QuoteLuaString( string value )
    {
        StringBuilder quoted = new( "\"" );
        foreach ( var character in value )
        {
            if ( character is '"' or '\\' )
            {
                quoted.Append( '\\' ).Append( character );
            }
            else if ( character < ' ' || character == '\u007f' )
            {
                quoted.Append( '\\' ).Append( (( int ) character).ToString( "D3", CultureInfo.InvariantCulture ) );
            }
            else
            {
                quoted.Append( character );
            }
        }

        return quoted.Append( '"' ).ToString( );
    }
}
