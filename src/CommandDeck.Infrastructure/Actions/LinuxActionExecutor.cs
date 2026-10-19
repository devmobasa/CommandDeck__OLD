using System.Collections.Frozen;

namespace CommandDeck;

// Runs a result's action on Linux. Each action kind belongs to exactly one
// feature handler; the table below is the whole routing, and a kind no handler
// declares fails closed.
public sealed class LinuxActionExecutor : IActionExecutor
{
    private readonly FrozenDictionary<string, ILinuxActionHandler> _routes;

    public LinuxActionExecutor(
        ILauncherSearchPreferences? preferences = null,
        ClipboardHistoryProvider? clipboardHistory = null,
        HerdrClient? herdr = null,
        HerdrWindowActivator? herdrWindowActivator = null,
        BoundedProcessRunner? systemdRunner = null,
        ExternalProcessRunner? hyprlandRunner = null,
        BoundedProcessRunner? snippetClipboardRunner = null,
        HeyClient? hey = null,
        LinuxProcessTable? processTable = null,
        ProcessPinner? processPinner = null,
        TimeProvider? timeProvider = null )
    {
        EditorLauncher editor = new( preferences );
        ILinuxActionHandler[] handlers =
        [
            new DesktopActionHandler( editor ),
            new ClipboardActionHandler( clipboardHistory, snippetClipboardRunner, timeProvider ),
            new SystemdActionHandler( editor, systemdRunner ),
            new HyprlandActionHandler( hyprlandRunner ),
            new HerdrActionHandler( herdr, herdrWindowActivator ),
            new HeyActionHandler( preferences, hey ),
            new ProcessActionHandler( processTable ?? new LinuxProcessTable( ), processPinner ?? LinuxProcessSignals.Pin ),
            new LauncherOwnedActionHandler( )
        ];

        Dictionary<string, ILinuxActionHandler> routes = new( StringComparer.Ordinal );
        foreach ( var handler in handlers )
        {
            foreach ( var kind in handler.Kinds )
            {
                if ( !routes.TryAdd( kind, handler ) )
                {
                    throw new InvalidOperationException( $"Action kind '{kind}' has more than one handler." );
                }
            }
        }

        _routes = routes.ToFrozenDictionary( StringComparer.Ordinal );
    }

    public async ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken )
    {
        if ( action.Kind is null || !_routes.TryGetValue( action.Kind, out var handler ) )
        {
            return LinuxActionArguments.UnknownKind( action );
        }

        try
        {
            return await handler.ExecuteAsync( result, action, query, cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException )
        {
            return ActionExecutionResult.Failure( exception.Message );
        }
    }
}
