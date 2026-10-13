using System.Globalization;

namespace CommandDeck;

// A reversible HEY change on the box item behind a row. The message names what
// happened; the launcher shows it with Undo when the row has an undo action.
internal sealed partial class HeyActionHandler
{
    private const int MaximumTriageSubjectLength = 48;

    private async ValueTask<ActionExecutionResult> TriageHeyThreadAsync(
        SearchResult result,
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var change = action.Arguments.GetValueOrDefault( "change" );
        if ( !HeyTriageChanges.IsKnown( change ) ||
            !long.TryParse( action.Arguments.GetValueOrDefault( "postingId" ), NumberStyles.None, CultureInfo.InvariantCulture, out var postingId ) ||
            postingId <= 0 )
        {
            return ActionExecutionResult.Failure( "This HEY change is not valid." );
        }

        long? boxId = null;
        if ( action.Arguments.TryGetValue( "boxId", out var boxText ) )
        {
            if ( !long.TryParse( boxText, NumberStyles.None, CultureInfo.InvariantCulture, out var box ) || box <= 0 )
            {
                return ActionExecutionResult.Failure( "This HEY change is not valid." );
            }

            boxId = box;
        }

        if ( hey is null )
        {
            return ActionExecutionResult.Failure( "The HEY CLI is not installed." );
        }

        var failure = await hey.TriageAsync( change!, postingId, boxId, cancellationToken ).ConfigureAwait( false );
        return failure is null
            ? ActionExecutionResult.Success( TriageMessage( change!, result.Title ) )
            : ActionExecutionResult.Failure( failure.Message );
    }

    internal static string TriageMessage( string change, string subject )
    {
        var cut = char.IsHighSurrogate( subject.ElementAtOrDefault( MaximumTriageSubjectLength - 2 ) )
            ? MaximumTriageSubjectLength - 2
            : MaximumTriageSubjectLength - 1;
        var title = subject.Length > MaximumTriageSubjectLength ? subject[..cut] + "…" : subject;
        return change switch
        {
            HeyTriageChanges.Seen => $"Marked “{title}” seen",
            HeyTriageChanges.Unseen => $"Marked “{title}” unseen",
            HeyTriageChanges.ReplyLater => $"Moved “{title}” to Reply Later",
            HeyTriageChanges.SetAside => $"Set “{title}” aside",
            HeyTriageChanges.MoveBack => $"Moved “{title}” back",
            HeyTriageChanges.BubbleUp => $"“{title}” bubbles up tomorrow",
            _ => $"Cancelled the bubble-up for “{title}”"
        };
    }
}
