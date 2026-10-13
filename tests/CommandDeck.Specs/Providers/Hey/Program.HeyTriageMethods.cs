using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static string ActionIds( SearchResult row ) =>
        string.Join( ',', row.Actions.Select( static action => action.Id + (action.Hidden == true ? "*" : "") ) );

    // Each visible change is followed, at the end of the row, by its hidden
    // undo; which changes appear depends on what HEY said about the thread,
    // and an undo only where the state it restores is known.
    private static async Task HeyTriageActionsFollowThreadStateAsync( )
    {
        var imbox = await HeySearchAsync( HeyProviderFor( new HeyFake( ) ), "hey: box:imbox" );
        Equal( "open,read,copy-link,mark-seen,reply-later,set-aside,bubble-up," +
            "undo:mark-seen*,undo:reply-later*,undo:set-aside*,undo:bubble-up*", ActionIds( imbox[0] ) );

        // Already scheduled to bubble up, or bubbled up: Bubble Up would
        // replace that, and its undo would cancel it.
        Equal( "open,read,copy-link,mark-unseen,reply-later,set-aside," +
            "undo:mark-unseen*,undo:reply-later*,undo:set-aside*", ActionIds( imbox[1] ) );
        HeyFake bubbled = new( )
        {
            Respond = _ => HeyResult( HeyBoxJson.Replace( "\"seen\":false,", "\"seen\":false,\"bubbled_up\":true,", StringComparison.Ordinal ) )
        };
        var bubbledRows = await HeySearchAsync( HeyProviderFor( bubbled ), "hey: box:imbox" );
        True( bubbledRows[0].Actions.All( static action => !action.Id.EndsWith( HeyProvider.BubbleUpActionId, StringComparison.Ordinal ) ) );

        var replyLater = imbox[0].Actions.Single( static action => action.Id == HeyProvider.ReplyLaterActionId );
        Equal( ActionKinds.HeyTriage, replyLater.Kind );
        Equal( "change=reply-later,postingId=601,boxId=11",
            string.Join( ',', replyLater.Arguments.Select( static pair => pair.Key + "=" + pair.Value ) ) );
        var undo = imbox[0].Actions.Single( static action => action.Id == UndoActions.For( HeyProvider.ReplyLaterActionId ) );
        Equal( HeyTriageChanges.MoveBack, undo.Arguments["change"] );
        Equal( "11", undo.Arguments["boxId"] );
        Equal( "Undo Reply Later", undo.Title );
        Equal( HeyTriageChanges.BubblePop,
            imbox[0].Actions.Single( static action => action.Id == UndoActions.For( HeyProvider.BubbleUpActionId ) ).Arguments["change"] );

        // A search match knows neither its seen state, its box, nor its
        // bubble-up: both marks and Bubble Up, none with an undo that could
        // not restore what was there, and no move. Without a box item there
        // is nothing.
        var search = await HeySearchAsync( HeyProviderFor( new HeyFake( ) ), "hey: invoice" );
        Equal( "open,read,copy-link,mark-seen,mark-unseen,bubble-up", ActionIds( search[0] ) );
        Equal( "open,read,copy-link", ActionIds( search[1] ) );

        HeyFake later = new( )
        {
            Respond = _ => HeyResult( HeyBoxJson )
        };
        var inLater = await HeySearchAsync( HeyProviderFor( later ), "hey: box:reply-later" );
        True( inLater[0].Actions.All( static action => action.Id is not HeyProvider.ReplyLaterActionId ) );
        True( inLater[0].Actions.Any( static action => action.Id == HeyProvider.SetAsideActionId ) );

        // Only an undo carries the flag on the wire.
        var json = JsonSerializer.Serialize( imbox[0], CommandDeckJsonContext.Default.SearchResult );
        Equal( 4, json.Split( "\"hidden\":true" ).Length - 1 );
        True( !json.Contains( "\"hidden\":false", StringComparison.Ordinal ) && !json.Contains( "\"hidden\":null", StringComparison.Ordinal ) );
    }

    private static async Task HeyTriageRunsOneBoundedChangeAsync( )
    {
        Equal( "seen|601|--json", string.Join( '|', HeyClient.TriageArguments( HeyTriageChanges.Seen, 601, null )! ) );
        Equal( "unseen|601|--json", string.Join( '|', HeyClient.TriageArguments( HeyTriageChanges.Unseen, 601, null )! ) );
        Equal( "move|601|--to=laterbox|--json", string.Join( '|', HeyClient.TriageArguments( HeyTriageChanges.ReplyLater, 601, 11 )! ) );
        Equal( "move|601|--to=asidebox|--json", string.Join( '|', HeyClient.TriageArguments( HeyTriageChanges.SetAside, 601, null )! ) );
        Equal( "move|601|--to=11|--json", string.Join( '|', HeyClient.TriageArguments( HeyTriageChanges.MoveBack, 601, 11 )! ) );
        Equal( "bubble|up|601|--tomorrow|--json", string.Join( '|', HeyClient.TriageArguments( HeyTriageChanges.BubbleUp, 601, null )! ) );
        Equal( "bubble|pop|601|--json", string.Join( '|', HeyClient.TriageArguments( HeyTriageChanges.BubblePop, 601, null )! ) );
        True( HeyClient.TriageArguments( HeyTriageChanges.MoveBack, 601, null ) is null );
        True( HeyClient.TriageArguments( HeyTriageChanges.Seen, 0, null ) is null );
        True( HeyClient.TriageArguments( "trash", 601, null ) is null );

        HeyFake fake = new( )
        {
            Respond = _ => HeyResult( "" )
        };
        Equal( ( HeyFailure? ) null, await fake.Client( ).TriageAsync( HeyTriageChanges.Seen, 601, null, default ) );
        Equal( HeyClient.TriageTimeout, fake.Calls.Single( ).Timeout );

        async Task<HeyFailureKind?> KindFor( ProcessResult result )
        {
            HeyFake failing = new( )
            {
                Respond = _ => result
            };
            return (await failing.Client( ).TriageAsync( HeyTriageChanges.Seen, 601, null, default ))?.Kind;
        }

        Equal( HeyFailureKind.SignedOut, await KindFor( HeyResult( HeySignedOutJson, 1 ) ) );
        Equal( HeyFailureKind.Failed, await KindFor( HeyResult( "", 2 ) ) );
        Equal( HeyFailureKind.TimedOut, await KindFor( new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true } ) );
        Equal( HeyFailureKind.Rejected, (await fake.Client( ).TriageAsync( "trash", 601, null, default ))?.Kind );
        Equal( 1, fake.Calls.Count );
    }

    // The launcher refreshes after a change; the refresh must ask HEY again
    // instead of replaying the cached listing that still shows the old state.
    private static async Task HeyTriageRefreshesTheListingItChangedAsync( )
    {
        var seen = false;
        ProcessResult Respond( IReadOnlyList<string> args )
        {
            if ( args[0] == "box" )
            {
                return HeyResult( seen ? HeyBoxJson.Replace( "\"seen\":false", "\"seen\":true", StringComparison.Ordinal ) : HeyBoxJson );
            }

            seen = args[0] == "seen" || seen;
            return args[0] == "unseen" ? HeyError( HeySignedOutJson ) : HeyResult( """{"ok":true}""" );
        }

        HeyFake fake = new( )
        {
            Respond = Respond
        };
        var client = fake.Client( );
        HeyProvider provider = new( client, new HeyClock( HeyNow ), TimeSpan.Zero );
        LinuxActionExecutor executor = new( hey: client );
        int BoxReads( ) => fake.Calls.Count( static call => call.Args[0] == "box" );

        var before = await HeySearchAsync( provider, "hey: box:imbox" );
        Equal( HeyProvider.UnreadIcon, before[0].Icon );
        var markSeen = before[0].Actions.Single( static action => action.Id == HeyProvider.MarkSeenActionId );
        True( (await executor.ExecuteAsync( before[0], markSeen, "", default )).Succeeded );

        var after = await HeySearchAsync( provider, "hey: box:imbox" );
        Equal( 2, BoxReads( ) );
        Equal( HeyProvider.ThreadIcon, after[0].Icon );
        True( after[0].Actions.Any( static action => action.Id == HeyProvider.MarkUnseenActionId ) &&
            after[0].Actions.All( static action => action.Id != HeyProvider.MarkSeenActionId ), "the refreshed row offers the other mark" );

        // A change HEY refused changed nothing, so the listing stays cached.
        var markUnseen = after[0].Actions.Single( static action => action.Id == HeyProvider.MarkUnseenActionId );
        True( !(await executor.ExecuteAsync( after[0], markUnseen, "", default )).Succeeded );
        await HeySearchAsync( provider, "hey: box:imbox" );
        Equal( 2, BoxReads( ) );

        // One that timed out may still have landed.
        HeyFake slow = new( )
        {
            Respond = args => args[0] == "box" ? HeyResult( HeyBoxJson ) : new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true }
        };
        var slowClient = slow.Client( );
        HeyProvider slowProvider = new( slowClient, new HeyClock( HeyNow ), TimeSpan.Zero );
        var rows = await HeySearchAsync( slowProvider, "hey: box:imbox" );
        True( !(await new LinuxActionExecutor( hey: slowClient ).ExecuteAsync( rows[0], markSeen, "", default )).Succeeded );
        await HeySearchAsync( slowProvider, "hey: box:imbox" );
        Equal( 2, slow.Calls.Count( static call => call.Args[0] == "box" ) );
    }

    private static async Task HeyTriageReportsWhatChangedAsync( )
    {
        HeyFake fake = new( )
        {
            Respond = _ => HeyResult( """{"ok":true}""" )
        };
        LinuxActionExecutor executor = new( hey: fake.Client( ) );
        var row = new SearchResult( HeyPrivateRow( ) ) { Title = "Lunch?" };
        CommandAction Change( string change, string posting = "601", string? box = null )
        {
            Dictionary<string, string> arguments = new( StringComparer.Ordinal )
            {
                ["change"] = change,
                ["postingId"] = posting
            };
            if ( box is not null )
            {
                arguments["boxId"] = box;
            }

            return new( )
            {
                Id = change,
                Title = change,
                Kind = ActionKinds.HeyTriage,
                Arguments = arguments
            };
        }

        foreach ( var (change, message) in new[]
        {
            (HeyTriageChanges.Seen, "Marked “Lunch?” seen"),
            (HeyTriageChanges.Unseen, "Marked “Lunch?” unseen"),
            (HeyTriageChanges.ReplyLater, "Moved “Lunch?” to Reply Later"),
            (HeyTriageChanges.SetAside, "Set “Lunch?” aside"),
            (HeyTriageChanges.BubbleUp, "“Lunch?” bubbles up tomorrow"),
            (HeyTriageChanges.BubblePop, "Cancelled the bubble-up for “Lunch?”")
        } )
        {
            var outcome = await executor.ExecuteAsync( row, Change( change ), "", default );
            True( outcome.Succeeded );
            Equal( message, outcome.Message );
        }

        Equal( "Moved “Lunch?” back", (await executor.ExecuteAsync( row, Change( HeyTriageChanges.MoveBack, box: "11" ), "", default )).Message );
        Equal( "move|601|--to=11|--json", string.Join( '|', fake.Calls[^1].Args ) );

        var calls = fake.Calls.Count;
        foreach ( var invalid in new[] { Change( "trash" ), Change( HeyTriageChanges.Seen, "-4" ), Change( HeyTriageChanges.MoveBack, box: "x" ), Change( HeyTriageChanges.MoveBack ) } )
        {
            True( !(await executor.ExecuteAsync( row, invalid, "", default )).Succeeded );
        }

        Equal( calls, fake.Calls.Count );
        var longSubject = new SearchResult( row ) { Title = new string( 'x', 80 ) };
        Equal( "Marked “" + new string( 'x', 47 ) + "…” seen", (await executor.ExecuteAsync( longSubject, Change( HeyTriageChanges.Seen ), "", default )).Message );
        True( !(await new LinuxActionExecutor( ).ExecuteAsync( row, Change( HeyTriageChanges.Seen ), "", default )).Succeeded );
    }
}
