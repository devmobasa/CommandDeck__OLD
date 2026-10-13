using System.Globalization;

namespace CommandDeck;

// Reversible HEY changes on a thread row, after Open, Read Thread, and Copy
// Link. A visible change has a hidden undo:<id> action on the same row that
// the launcher offers once the change succeeds, but only where the state the
// undo restores is known: a box listing knows a thread's seen state, box, and
// bubble-up; a search match knows none of them. Reply Later and Set Aside need
// the box a thread came from to undo, so only box listings offer them.
public sealed partial class HeyProvider
{
    public const string MarkSeenActionId = "mark-seen";
    public const string MarkUnseenActionId = "mark-unseen";
    public const string ReplyLaterActionId = "reply-later";
    public const string SetAsideActionId = "set-aside";
    public const string BubbleUpActionId = "bubble-up";

    private static List<CommandAction> TriageActions( HeyThread thread, HeyBox? box )
    {
        if ( thread.PostingId is not { } postingId )
        {
            return [];
        }

        // Undoing a mark on a thread whose seen state was unknown could leave
        // it in a state it never had.
        var seenKnown = thread.Seen is not null;
        List<(CommandAction Change, CommandAction? Undo)> pairs = [];
        if ( thread.Seen != true )
        {
            pairs.Add( Pair( MarkSeenActionId, "Mark Seen", HeyTriageChanges.Seen, HeyTriageChanges.Unseen, postingId, undoable: seenKnown ) );
        }

        if ( thread.Seen != false )
        {
            pairs.Add( Pair( MarkUnseenActionId, "Mark Unseen", HeyTriageChanges.Unseen, HeyTriageChanges.Seen, postingId, undoable: seenKnown ) );
        }

        if ( thread.BoxId is { } boxId && box is not null )
        {
            if ( box.CliName != "laterbox" )
            {
                pairs.Add( Pair( ReplyLaterActionId, "Reply Later", HeyTriageChanges.ReplyLater, HeyTriageChanges.MoveBack, postingId, boxId: boxId ) );
            }

            if ( box.CliName != "asidebox" )
            {
                pairs.Add( Pair( SetAsideActionId, "Set Aside", HeyTriageChanges.SetAside, HeyTriageChanges.MoveBack, postingId, boxId: boxId ) );
            }
        }

        // A bubble-up the thread already has would be replaced, and bubble pop
        // would then cancel it altogether.
        if ( thread.BubbledUp != true )
        {
            pairs.Add( Pair( BubbleUpActionId, "Bubble Up Tomorrow", HeyTriageChanges.BubbleUp, HeyTriageChanges.BubblePop, postingId,
                undoable: thread.BubbledUp is not null ) );
        }

        return [.. pairs.Select( static pair => pair.Change ), .. pairs.Select( static pair => pair.Undo ).OfType<CommandAction>( )];
    }

    private static (CommandAction Change, CommandAction? Undo) Pair(
        string id, string title, string change, string undo, long postingId, bool undoable = true, long? boxId = null ) =>
        (Triage( id, title, change, postingId, boxId, hidden: false ),
            undoable ? Triage( UndoActions.For( id ), "Undo " + title, undo, postingId, boxId, hidden: true ) : null);

    private static CommandAction Triage( string id, string title, string change, long postingId, long? boxId, bool hidden )
    {
        Dictionary<string, string> arguments = new( StringComparer.Ordinal )
        {
            ["change"] = change,
            ["postingId"] = postingId.ToString( CultureInfo.InvariantCulture )
        };
        if ( boxId is { } box )
        {
            arguments["boxId"] = box.ToString( CultureInfo.InvariantCulture );
        }

        return new CommandAction
        {
            Id = id,
            Title = title,
            Kind = ActionKinds.HeyTriage,
            Arguments = arguments,
            Hidden = hidden ? true : null
        };
    }
}
