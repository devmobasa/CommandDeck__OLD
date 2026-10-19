namespace CommandDeck;

// Clipboard writes: Copy, snippets (which may read the clipboard once to fill
// {clipboard}, or retained history for {clipboard offset=N}), and clipboard
// history's own row actions.
internal sealed class ClipboardActionHandler(
    ClipboardHistoryProvider? clipboardHistory,
    BoundedProcessRunner? snippetClipboardRunner,
    TimeProvider? timeProvider = null ) : ILinuxActionHandler
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private const int MaximumSnippetClipboardBytes = 64 * 1024;
    private const int MaximumSnippetClipboardErrorBytes = 4 * 1024;
    private const long MaximumSnippetClipboardProcessBytes = 128 * 1024 * 1024;

    private static readonly string[] OwnedKinds =
    [
        ActionKinds.CopyText,
        ActionKinds.SnippetCopy,
        ActionKinds.SnippetPreview,
        ActionKinds.ClipboardHistory
    ];

    public IReadOnlyList<string> Kinds => OwnedKinds;

    public async ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken ) => action.Kind switch
        {
            ActionKinds.CopyText => await CopyTextAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.SnippetCopy or ActionKinds.SnippetPreview => await ExecuteSnippetAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.ClipboardHistory => clipboardHistory is null ? ActionExecutionResult.Failure( "Clipboard history is unavailable." ) : await clipboardHistory.ExecuteAsync( action, cancellationToken ).ConfigureAwait( false ),
            _ => LinuxActionArguments.UnknownKind( action )
        };

    private async ValueTask<ActionExecutionResult> ExecuteSnippetAsync(
        CommandAction action, CancellationToken cancellationToken )
    {
        var payload = SnippetActionPayload.Read( action.Arguments );
        var template = SnippetTemplate.Compile( payload.Template );
        if ( template.ReadsClipboardHistory && !payload.ReadsClipboardHistory )
        {
            return ActionExecutionResult.Failure( "Search for this snippet again before it reads clipboard history." );
        }

        Dictionary<int, string> clipboard = [];
        foreach ( var offset in template.ClipboardOffsets )
        {
            var (text, failure) = offset == 0
                ? await ReadCurrentClipboardAsync( cancellationToken ).ConfigureAwait( false )
                : ReadClipboardHistory( offset );
            if ( failure is not null )
            {
                return failure;
            }

            clipboard[offset] = text!;
        }

        var expanded = template.Expand( action.Arguments, _time, clipboard );
        if ( action.Kind == ActionKinds.SnippetPreview )
        {
            return ActionExecutionResult.Success( "Snippet preview", expanded );
        }

        return await CopyTextAsync( new CommandAction
        {
            Id = "copy",
            Title = "Copy",
            Kind = ActionKinds.CopyText,
            Arguments = new TextActionPayload( expanded ).ToArguments( )
        }, cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<(string? Text, ActionExecutionResult? Failure)> ReadCurrentClipboardAsync( CancellationToken cancellationToken )
    {
        ProcessExecutionLimits limits = new( )
        {
            MaximumStandardOutputBytes = MaximumSnippetClipboardBytes,
            MaximumStandardErrorBytes = MaximumSnippetClipboardErrorBytes,
            MaximumMemoryBytes = MaximumSnippetClipboardProcessBytes,
            ClearEnvironment = true,
            EnvironmentVariables = SnippetClipboardEnvironment( )
        };
        ProcessResult read;
        if ( snippetClipboardRunner is null )
        {
            read = await ProcessRunner.RunBoundedAsync( "wl-paste", ["--no-newline", "--type", "text"],
                null, TimeSpan.FromSeconds( 2 ), limits, cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            read = await snippetClipboardRunner( "wl-paste", ["--no-newline", "--type", "text"],
                TimeSpan.FromSeconds( 2 ), limits, cancellationToken ).ConfigureAwait( false );
        }

        if ( read.ExitCode != 0 || read.TimedOut || read.AnyOutputLimitExceeded || read.MemoryLimitExceeded )
        {
            return (null, ActionExecutionResult.Failure( read.AnyOutputLimitExceeded
                ? "Clipboard text exceeds 64 KiB."
                : "Could not read clipboard text." ));
        }

        return (read.StandardOutput, null);
    }

    private (string? Text, ActionExecutionResult? Failure) ReadClipboardHistory( int offset )
    {
        if ( clipboardHistory is null )
        {
            return (null, ActionExecutionResult.Failure( "Clipboard history is unavailable." ));
        }

        var text = clipboardHistory.ReadRecentText( offset );
        if ( text is null )
        {
            return (null, ActionExecutionResult.Failure( $"Clipboard history has no text entry {offset} places back." ));
        }

        return (text, null);
    }

    private static Dictionary<string, string> SnippetClipboardEnvironment( )
    {
        Dictionary<string, string> environment = new( StringComparer.Ordinal )
        {
            ["PATH"] = Environment.GetEnvironmentVariable( "PATH" ) ?? "/usr/bin:/bin",
            ["LANG"] = "C.UTF-8"
        };
        foreach ( var name in new[] { "XDG_RUNTIME_DIR", "WAYLAND_DISPLAY" } )
        {
            var value = Environment.GetEnvironmentVariable( name );
            if ( !string.IsNullOrEmpty( value ) )
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    private static async ValueTask<ActionExecutionResult> CopyTextAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var text = TextActionPayload.Read( action.Arguments ).Text;
        if ( !ProcessRunner.ExistsOnPath( "wl-copy" ) )
        {
            return ActionExecutionResult.Failure(
                "wl-copy is not installed. Install the Arch package 'wl-clipboard'." );
        }

        return await ClipboardCopyProcess.CopyAsync( text, cancellationToken ).ConfigureAwait( false );
    }
}
