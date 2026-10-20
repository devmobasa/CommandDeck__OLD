namespace CommandDeck;

public sealed partial class DesktopControlsProvider
{
    private const int MaximumWindowLabel = 80;
    private static readonly TimeSpan HyprctlTimeout = TimeSpan.FromMilliseconds( 250 );

    // The launcher is a layer-shell overlay, so opening it does not change
    // Hyprland's active window: activewindow still names the window the user was
    // in. Each row binds that window's literal address when the search runs, and
    // names it in the subtitle. No window, no hyprctl, or no reply means no rows.
    private async ValueTask AddWindowRowsAsync(
        string text,
        Dictionary<string, bool> tools,
        List<SearchResult> results,
        CancellationToken cancellationToken )
    {
        var matches = WindowCommandCatalog.All
            .Select( command => (Command: command, Score: Score( text, command.Title, command.Keywords )) )
            .Where( static match => match.Score > 0 )
            .ToArray( );
        if ( matches.Length == 0 || !IsAvailable( HyprlandDispatch.Executable, tools ) )
        {
            return;
        }

        var needsMonitors = matches.Any( static match => match.Command.NeedsSecondMonitor );
        var activeTask = ReadHyprlandAsync( "activewindow", cancellationToken );
        var monitorsTask = needsMonitors ? ReadHyprlandAsync( "monitors", cancellationToken ) : Task.FromResult<string?>( null );
        await Task.WhenAll( activeTask, monitorsTask ).ConfigureAwait( false );

        var window = activeTask.Result is { } active ? HyprlandWindowState.ParseActiveWindow( active ) : null;
        if ( window is null )
        {
            return;
        }

        var monitors = monitorsTask.Result is { } json
            ? HyprlandWindowState.ParseMonitors( json ).Count( static monitor => !monitor.Disabled )
            : 0;
        foreach ( (var command, var score) in matches )
        {
            if ( !command.NeedsSecondMonitor || monitors >= 2 )
            {
                results.Add( BuildWindowRow( command, window, score ) );
            }
        }
    }

    private async Task<string?> ReadHyprlandAsync( string request, CancellationToken cancellationToken )
    {
        ProcessResult result;
        try
        {
            result = await _hyprland( HyprlandDispatch.Executable, ["-j", request], HyprctlTimeout, cancellationToken )
                .ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return null;
        }

        return result.ExitCode == 0 && !result.TimedOut && !result.AnyOutputLimitExceeded
            ? result.StandardOutput
            : null;
    }

    private static SearchResult BuildWindowRow( WindowCommand command, HyprlandWindow window, double score )
    {
        var label = string.IsNullOrWhiteSpace( window.Title ) ? window.Class : $"{window.Class} — {window.Title}";
        if ( label.Length > MaximumWindowLabel )
        {
            label = label[..(MaximumWindowLabel - 1)] + "…";
        }

        Dictionary<string, string> arguments = new( StringComparer.Ordinal )
        {
            [HyprlandActionOperations.OperationKey] = command.Operation,
            [HyprlandActionOperations.ArgumentKey] = window.Selector
        };
        if ( command.Parameter is not null )
        {
            arguments[HyprlandActionOperations.ParameterKey] = command.Parameter;
        }

        return new SearchResult
        {
            Id = DesktopControlResultIds.For( "window-" + command.Id ),
            Kind = ResultKinds.CustomCommand,
            Title = command.Title,
            Subtitle = "Window · " + label,
            Icon = command.Icon,
            ProviderId = ProviderIds.DesktopControls,
            SourceId = SourceIds.DesktopControls,
            Score = score,
            Details =
            [
                new DetailField( "Window", string.IsNullOrWhiteSpace( window.Title ) ? window.Class : window.Title ),
                new DetailField( "Class", window.Class, Monospace: true ),
                new DetailField( "Address", window.Address, Monospace: true )
            ],
            Actions =
            [
                new CommandAction
                {
                    Id = "run",
                    Title = command.Title,
                    Kind = ActionKinds.HyprlandDispatch,
                    Arguments = arguments
                }
            ],
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                ["control"] = "window-" + command.Id,
                ["address"] = window.Address
            }
        };
    }

    private static ValueTask<ProcessResult> DefaultHyprlandRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken ) =>
        ProcessRunner.RunAsync( fileName, arguments, null, null, timeout, cancellationToken );
}
