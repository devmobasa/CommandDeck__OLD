namespace CommandDeck;

// Switching workspaces, focusing or closing windows, and the window commands,
// each through Hyprland dispatches that must be acknowledged.
internal sealed class HyprlandActionHandler : ILinuxActionHandler
{
    private readonly ExternalProcessRunner _run;
    private readonly HyprlandWindowCommands _windowCommands;

    public HyprlandActionHandler( ExternalProcessRunner? hyprlandRunner )
    {
        _run = hyprlandRunner ?? DefaultRunAsync;
        _windowCommands = new HyprlandWindowCommands( _run );
    }

    public IReadOnlyList<string> Kinds { get; } = [ActionKinds.HyprlandDispatch];

    public ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken ) =>
        action.Kind == ActionKinds.HyprlandDispatch
            ? DispatchHyprlandAsync( action, cancellationToken )
            : ValueTask.FromResult( LinuxActionArguments.UnknownKind( action ) );

    private async ValueTask<ActionExecutionResult> DispatchHyprlandAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var operation = LinuxActionArguments.Require( action, HyprlandActionOperations.OperationKey );
        var argument = LinuxActionArguments.Require( action, HyprlandActionOperations.ArgumentKey );
        if ( HyprlandActionOperations.IsWindowCommand( operation ) )
        {
            return await _windowCommands.ExecuteAsync(
                operation,
                argument,
                action.Arguments.GetValueOrDefault( HyprlandActionOperations.ParameterKey ),
                cancellationToken ).ConfigureAwait( false );
        }

        var target = HyprlandDispatch.QuoteLuaString( argument );
        var lua = operation switch
        {
            HyprlandActionOperations.SwitchWorkspace => $"hl.dsp.focus({{ workspace = {target} }})",
            HyprlandActionOperations.FocusWindow => $"hl.dsp.focus({{ window = {target} }})",
            HyprlandActionOperations.CloseWindow => $"hl.dsp.window.close({{ window = {target} }})",
            _ => throw new InvalidOperationException( $"Unsupported Hyprland operation '{operation}'." )
        };

        var result = await _run( HyprlandDispatch.Executable, [HyprlandDispatch.Command, lua],
            HyprlandDispatch.Timeout, cancellationToken ).ConfigureAwait( false );
        return HyprlandDispatch.WasAccepted( result )
            ? ActionExecutionResult.Success( )
            : HyprlandDispatch.Failure( result );
    }

    private static ValueTask<ProcessResult> DefaultRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken ) =>
        ProcessRunner.RunAsync( fileName, arguments, null, null, timeout, cancellationToken );
}
