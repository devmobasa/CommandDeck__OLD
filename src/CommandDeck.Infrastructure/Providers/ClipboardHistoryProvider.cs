namespace CommandDeck;

public sealed class ClipboardHistoryProvider(
    ClipboardHistoryStore store,
    Func<string>? captureStatus = null,
    Func<IReadOnlyList<ClipboardServeOffer>, CancellationToken, ValueTask<ActionExecutionResult>>? serve = null ) : ICommandProvider
{
    public string Id => ProviderIds.Clipboard;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Clipboard ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }
        var enabled = store.Enabled;
        var status = captureStatus?.Invoke( );
        List<SearchResult> results = [new()
        {
            Id = "clipboard:controls", Title = enabled ? "Clipboard capture enabled" : "Clipboard capture is off",
            Subtitle = status ?? "Text only · retention and pin controls", Kind = ResultKinds.CustomCommand,
            SourceId = SourceIds.Clipboard, ProviderId = Id, Icon = "edit-paste", Score = 10,
            Actions = [Action(enabled ? "disable" : "enable", enabled ? "Disable capture" : "Enable clipboard capture", confirm: !enabled),
                Action("clear", "Delete all clipboard history", confirm: true)]
        }];
        foreach ( var entry in store.Search( query.Text, Math.Clamp( request.Limit - 1, 1, 99 ) ) )
        {
            results.Add( new SearchResult
            {
                Id = "clipboard:" + entry.Id,
                Title = entry.Text.Replace( '\n', ' ' ).Replace( '\r', ' ' )[..Math.Min( entry.Text.Length, 100 )],
                Subtitle = entry.Pinned ? "Pinned clipboard text" : "Clipboard text",
                DetailMarkdown = entry.Text,
                Kind = ResultKinds.CustomCommand,
                SourceId = SourceIds.Clipboard,
                ProviderId = Id,
                Icon = "edit-paste",
                Score = entry.Pinned ? 3 : 2,
                Section = new ResultSection { Id = "clipboard", Title = "Clipboard history", Order = 6 },
                Actions = [Action("copy", "Copy again", entry.Id),
                    Action(entry.Pinned ? "unpin" : "pin", entry.Pinned ? "Unpin" : "Pin", entry.Id),
                    Action(ClipboardActionIds.SequenceStart, "Start clipboard sequence here", entry.Id),
                    Action("delete", "Delete entry", entry.Id)]
            } );
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( results.Take( Math.Clamp( request.Limit, 1, 100 ) ).ToArray( ) );
    }

    public string? ReadRecentText( int offset ) => store.ReadRecentText( offset );

    public async ValueTask<ActionExecutionResult> ExecuteAsync( CommandAction action, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( action );
        var id = action.Arguments.GetValueOrDefault( "entry", "" );
        switch ( action.Id )
        {
            case "enable":
                store.Enabled = true;
                break;
            case "disable":
                store.Enabled = false;
                break;
            case "clear":
                store.Clear( );
                break;
            case "delete":
                store.Delete( id );
                break;
            case "pin":
                if ( !store.Pin( id, true ) )
                {
                    return ActionExecutionResult.Failure( "The pin limit is reached or the entry no longer exists. Unpin another entry first." );
                }
                break;
            case "unpin":
                store.Pin( id, false );
                break;
            case "copy":
                var text = store.Read( id );
                if ( text is null )
                {
                    return ActionExecutionResult.Failure( "This clipboard entry expired or was deleted. Search again." );
                }
                return await ClipboardCopyProcess.CopyAsync( text, cancellationToken ).ConfigureAwait( false );
            case ClipboardActionIds.SequenceStart:
                return ActionExecutionResult.Success( ClipboardSequenceCommands.Started( store.StartSequenceAt( id ) ) );
            case ClipboardSequenceCommands.RestartAction:
                store.RestartSequence( );
                return ActionExecutionResult.Success( "Clipboard sequence restarted. Copy next clipboard entry starts with the next entry you copy." );
            case ClipboardSequenceCommands.NextAction:
                return await CopyNextInSequenceAsync( cancellationToken ).ConfigureAwait( false );
            default:
                return ActionExecutionResult.Failure( "Unknown clipboard action." );
        }
        return ActionExecutionResult.Success( "Clipboard history updated" );
    }

    // Copies like the browser does: every retained offer through native serving, or
    // plain text when serving is unavailable. The position moves only after a copy succeeds.
    private async ValueTask<ActionExecutionResult> CopyNextInSequenceAsync( CancellationToken cancellationToken )
    {
        var next = store.PeekSequence( );
        if ( next is null )
        {
            return ActionExecutionResult.Failure( "The clipboard sequence has no newer entry. Copy something, or start the sequence again." );
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        lifetime.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        ActionExecutionResult copied;
        if ( serve is null )
        {
            var text = store.ReadCopyText( next.Id, next.Incarnation, next.Revision );
            copied = await ClipboardCopyProcess.CopyAsync( text, lifetime.Token ).ConfigureAwait( false );
        }
        else
        {
            using var selection = store.ReadCopySelection( next.Id, next.Incarnation, next.Revision );
            copied = await serve( selection.Offers, lifetime.Token ).ConfigureAwait( false );
            if ( copied.Succeeded )
            {
                store.TouchCopied( next.Id, next.Incarnation );
            }
        }

        if ( !copied.Succeeded )
        {
            return ActionExecutionResult.Failure( "Clipboard copy failed; the sequence did not advance." );
        }

        var progress = store.AdvanceSequence( next );
        return ActionExecutionResult.Success( $"Copied clipboard sequence entry {progress.Copied}; {progress.Queued} more queued." );
    }

    private static CommandAction Action( string id, string title, string entry = "", bool confirm = false ) => new( )
    {
        Id = id,
        Title = title,
        Kind = ActionKinds.ClipboardHistory,
        RequiresConfirmation = confirm,
        Arguments = new( StringComparer.Ordinal )
        {
            ["entry"] = entry
        }
    };
}
