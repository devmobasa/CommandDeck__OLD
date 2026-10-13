using System.Globalization;
using System.Text;

namespace CommandDeck;

// A whole thread as Markdown, read only when the user asks for it. The text
// is shown as plain text, so no link or image in the mail is ever loaded.
public sealed partial class HeyClient
{
    // hey reads every page of a thread, so it gets longer than a search.
    public static readonly TimeSpan ThreadTimeout = TimeSpan.FromSeconds( 10 );
    public const int ThreadStandardOutputBytes = 2 * 1024 * 1024;
    public const int MaximumThreadCharacters = 64 * 1024;

    public async ValueTask<HeyThreadText> ReadThreadAsync( long topicId, CancellationToken cancellationToken )
    {
        if ( topicId <= 0 )
        {
            throw new ArgumentOutOfRangeException( nameof( topicId ), "A HEY thread id is positive." );
        }

        var executable = ResolveExecutable( );
        if ( executable is null )
        {
            return ThreadFailure( HeyFailureKind.NotInstalled, "The HEY CLI is not installed." );
        }

        var result = await RunAsync( executable, ["thread", "read", topicId.ToString( CultureInfo.InvariantCulture ), "--markdown"],
            ThreadTimeout, ThreadStandardOutputBytes, cancellationToken ).ConfigureAwait( false );
        if ( result is null )
        {
            return ThreadFailure( HeyFailureKind.Failed, "The HEY CLI could not start." );
        }

        if ( result.TimedOut )
        {
            return ThreadFailure( HeyFailureKind.TimedOut, "HEY took too long to read the thread." );
        }

        if ( result.OutputLimitExceeded || result.MemoryLimitExceeded )
        {
            return ThreadFailure( HeyFailureKind.Failed, "The thread is longer than CommandDeck reads; open it in HEY." );
        }

        if ( result.ExitCode != 0 || result.StandardErrorLimitExceeded )
        {
            return new HeyThreadText { Failure = FailureOf( result ) };
        }

        var text = CleanMarkdown( result.StandardOutput );
        return text.Length > MaximumThreadCharacters
            ? new HeyThreadText { Markdown = text[..TrimmedLength( text )], Trimmed = true }
            : new HeyThreadText { Markdown = text };
    }

    private static HeyThreadText ThreadFailure( HeyFailureKind kind, string message ) =>
        new( )
        {
            Failure = new HeyFailure( kind, message )
        };

    // Keeps line breaks and tabs; every other control or format character goes.
    internal static string CleanMarkdown( string value )
    {
        StringBuilder builder = new( value.Length );
        foreach ( var character in value.Replace( "\r\n", "\n", StringComparison.Ordinal ) )
        {
            if ( character is '\n' or '\t' ||
                (!char.IsControl( character ) && CharUnicodeInfo.GetUnicodeCategory( character ) != UnicodeCategory.Format) )
            {
                builder.Append( character );
            }
        }

        return builder.ToString( ).Trim( );
    }

    // Stops at the bound without cutting a surrogate pair.
    private static int TrimmedLength( string text ) =>
        char.IsHighSurrogate( text[MaximumThreadCharacters - 1] ) ? MaximumThreadCharacters - 1 : MaximumThreadCharacters;
}
