namespace CommandDeck;

// Window commands for the window that was focused when the launcher opened. The
// search row carries that window's literal address; each command first checks
// that the address is still a client, then dispatches only to that address, so
// it never acts on whatever happens to be focused at dispatch time. Every
// dispatch must be acknowledged with "ok"; a Lua warning stops the sequence.
internal sealed class HyprlandWindowCommands( ExternalProcessRunner run )
{
    private const int MaximumWorkspace = 10;

    public async ValueTask<ActionExecutionResult> ExecuteAsync(
        string operation,
        string selector,
        string? parameter,
        CancellationToken cancellationToken )
    {
        if ( !HyprlandWindowState.IsWindowSelector( selector ) )
        {
            return ActionExecutionResult.Failure( "The window command has no valid window address. Search again." );
        }

        var clients = await ReadAsync( "clients", cancellationToken ).ConfigureAwait( false );
        if ( clients is null )
        {
            return ActionExecutionResult.Failure( "Hyprland window inspection failed." );
        }

        var window = HyprlandWindowState.ParseClients( clients )
            .FirstOrDefault( client => string.Equals( client.Selector, selector, StringComparison.Ordinal ) );
        if ( window is null )
        {
            return ActionExecutionResult.Failure( "The window that was focused when the launcher opened has closed. Search again." );
        }

        var target = HyprlandDispatch.QuoteLuaString( selector );
        return operation switch
        {
            HyprlandActionOperations.ToggleFloating =>
                await DispatchAsync( [Float( "toggle", target )], cancellationToken ).ConfigureAwait( false ),
            HyprlandActionOperations.TileWindow =>
                await DispatchAsync( [Float( "disable", target )], cancellationToken ).ConfigureAwait( false ),
            HyprlandActionOperations.ToggleFullscreen =>
                await DispatchAsync( [$"hl.dsp.window.fullscreen({{ mode = \"fullscreen\", window = {target} }})"],
                    cancellationToken ).ConfigureAwait( false ),
            HyprlandActionOperations.MoveWindowToWorkspace =>
                await MoveToWorkspaceAsync( target, parameter, cancellationToken ).ConfigureAwait( false ),
            HyprlandActionOperations.MoveWindowToMonitor =>
                await MoveToMonitorAsync( window, target, parameter, cancellationToken ).ConfigureAwait( false ),
            HyprlandActionOperations.PlaceWindow =>
                await PlaceAsync( window, target, parameter, cancellationToken ).ConfigureAwait( false ),
            _ => throw new InvalidOperationException( $"Unsupported Hyprland operation '{operation}'." )
        };
    }

    // Hyprland tiles windows, so a placement floats the window first. Leaving
    // fullscreen comes before both, because Hyprland refuses to resize it.
    private async ValueTask<ActionExecutionResult> PlaceAsync(
        HyprlandWindow window,
        string target,
        string? placement,
        CancellationToken cancellationToken )
    {
        if ( placement is null || !HyprlandWindowGeometry.IsPlacement( placement ) )
        {
            return ActionExecutionResult.Failure( "The window placement is invalid. Search again." );
        }

        var monitor = (await ReadMonitorsAsync( cancellationToken ).ConfigureAwait( false ))
            .FirstOrDefault( value => value.Id == window.MonitorId );
        if ( monitor is null )
        {
            return ActionExecutionResult.Failure( "Hyprland did not report the window's monitor." );
        }

        var area = HyprlandWindowGeometry.WorkArea( monitor.Layout );
        var rect = HyprlandWindowGeometry.Place( placement, area, window.Width, window.Height );
        List<string> steps = [];
        if ( window.Fullscreen != 0 )
        {
            var mode = (window.Fullscreen & 2) != 0 ? "fullscreen" : "maximized";
            steps.Add( $"hl.dsp.window.fullscreen({{ mode = \"{mode}\", action = \"unset\", window = {target} }})" );
        }

        if ( !window.Floating )
        {
            steps.Add( Float( "enable", target ) );
        }

        steps.Add( $"hl.dsp.window.resize({{ x = {HyprlandWindowState.Number( rect.Width )}, " +
            $"y = {HyprlandWindowState.Number( rect.Height )}, window = {target} }})" );
        steps.Add( $"hl.dsp.window.move({{ x = {HyprlandWindowState.Number( rect.X )}, " +
            $"y = {HyprlandWindowState.Number( rect.Y )}, window = {target} }})" );
        return await DispatchAsync( steps, cancellationToken ).ConfigureAwait( false );
    }

    // Monitors are ordered by ID, as Hyprland's own relative monitor selectors
    // are, and the target is named so the move does not depend on focus.
    private async ValueTask<ActionExecutionResult> MoveToMonitorAsync(
        HyprlandWindow window,
        string target,
        string? direction,
        CancellationToken cancellationToken )
    {
        if ( direction is not (HyprlandActionOperations.NextMonitor or HyprlandActionOperations.PreviousMonitor) )
        {
            return ActionExecutionResult.Failure( "The monitor direction is invalid. Search again." );
        }

        var monitors = (await ReadMonitorsAsync( cancellationToken ).ConfigureAwait( false ))
            .Where( static monitor => !monitor.Disabled )
            .OrderBy( static monitor => monitor.Id )
            .ToArray( );
        var index = Array.FindIndex( monitors, monitor => monitor.Id == window.MonitorId );
        if ( index < 0 || monitors.Length < 2 )
        {
            return ActionExecutionResult.Failure( "There is no other monitor to move the window to." );
        }

        var step = direction == HyprlandActionOperations.NextMonitor ? 1 : monitors.Length - 1;
        var name = HyprlandDispatch.QuoteLuaString( monitors[(index + step) % monitors.Length].Name );
        return await DispatchAsync( [$"hl.dsp.window.move({{ monitor = {name}, window = {target} }})"],
            cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<ActionExecutionResult> MoveToWorkspaceAsync(
        string target,
        string? workspace,
        CancellationToken cancellationToken )
    {
        if ( !int.TryParse( workspace, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number ) ||
            number is < 1 or > MaximumWorkspace )
        {
            return ActionExecutionResult.Failure( "The workspace is invalid. Search again." );
        }

        var name = HyprlandDispatch.QuoteLuaString( HyprlandWindowState.Number( number ) );
        return await DispatchAsync( [$"hl.dsp.window.move({{ workspace = {name}, window = {target} }})"],
            cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<ActionExecutionResult> DispatchAsync(
        IReadOnlyList<string> steps,
        CancellationToken cancellationToken )
    {
        foreach ( var lua in steps )
        {
            var result = await run( HyprlandDispatch.Executable, [HyprlandDispatch.Command, lua],
                HyprlandDispatch.Timeout, cancellationToken ).ConfigureAwait( false );
            if ( !HyprlandDispatch.WasAccepted( result ) )
            {
                return HyprlandDispatch.Failure( result );
            }
        }

        return ActionExecutionResult.Success( );
    }

    private async ValueTask<IReadOnlyList<HyprlandMonitor>> ReadMonitorsAsync( CancellationToken cancellationToken )
    {
        var json = await ReadAsync( "monitors", cancellationToken ).ConfigureAwait( false );
        return json is null ? [] : HyprlandWindowState.ParseMonitors( json );
    }

    private async ValueTask<string?> ReadAsync( string request, CancellationToken cancellationToken )
    {
        var result = await run( HyprlandDispatch.Executable, ["-j", request], HyprlandDispatch.Timeout,
            cancellationToken ).ConfigureAwait( false );
        return result.ExitCode == 0 && !result.TimedOut && !result.AnyOutputLimitExceeded
            ? result.StandardOutput
            : null;
    }

    private static string Float( string action, string target ) =>
        $"hl.dsp.window.float({{ action = \"{action}\", window = {target} }})";
}
