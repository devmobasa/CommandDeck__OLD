using System.Globalization;
using System.Text;

namespace CommandDeck;

internal static class HyprlandDispatch
{
    internal const string Executable = "hyprctl";
    internal const string Command = "dispatch";
    internal const string AcceptedOutput = "ok";
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds( 3 );

    internal static bool WasAccepted( ProcessResult result ) =>
        result.ExitCode == 0 &&
        !result.TimedOut &&
        !result.AnyOutputLimitExceeded &&
        string.Equals( result.StandardOutput.Trim( ), AcceptedOutput, StringComparison.Ordinal );

    // A dispatch that was not acknowledged, including a Lua warning printed with
    // exit code zero, with Hyprland's own text as the detail.
    internal static ActionExecutionResult Failure( ProcessResult result )
    {
        var message = result.TimedOut
            ? "Hyprland dispatch timed out."
            : result.ExitCode == 0
                ? "Hyprland did not acknowledge the action."
                : $"'hyprctl' exited with code {result.ExitCode.ToString( CultureInfo.InvariantCulture )}.";
        var diagnostic = string.IsNullOrWhiteSpace( result.StandardError )
            ? result.StandardOutput.Trim( )
            : result.StandardError.Trim( );
        return ActionExecutionResult.Failure(
            message,
            string.IsNullOrWhiteSpace( diagnostic )
                ? null
                : $"```\n{diagnostic}\n```" );
    }

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
