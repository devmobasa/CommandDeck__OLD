namespace CommandDeck;

// Clipboard writes: Copy, snippets (which may read the clipboard once to fill
// {clipboard}), and clipboard history's own row actions.
internal sealed class ClipboardActionHandler(
    ClipboardHistoryProvider? clipboardHistory,
    BoundedProcessRunner? snippetClipboardRunner ) : ILinuxActionHandler
{
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
        var template = SnippetActionPayload.Read( action.Arguments ).Template;
        var clipboard = string.Empty;
        if ( template.Contains( "{clipboard}", StringComparison.Ordinal ) )
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
                return ActionExecutionResult.Failure( read.AnyOutputLimitExceeded
                    ? "Clipboard text exceeds 64 KiB."
                    : "Could not read clipboard text." );
            }
            clipboard = read.StandardOutput;
        }
        var expanded = SnippetTemplate.Expand( template, action.Arguments, DateTimeOffset.Now, clipboard );
        if ( expanded.Length > 128 * 1024 )
        {
            return ActionExecutionResult.Failure( "Expanded snippet exceeds 128 KiB." );
        }
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
