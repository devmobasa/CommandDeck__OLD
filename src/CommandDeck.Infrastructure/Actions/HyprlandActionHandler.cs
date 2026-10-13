using System.Globalization;

namespace CommandDeck;

// Switching workspaces and focusing or closing windows through one Hyprland
// dispatch that must be acknowledged.
internal sealed class HyprlandActionHandler( ExternalProcessRunner? hyprlandRunner ) : ILinuxActionHandler
{
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
        var target = HyprlandDispatch.QuoteLuaString( argument );
        var lua = operation switch
        {
            HyprlandActionOperations.SwitchWorkspace => $"hl.dsp.focus({{ workspace = {target} }})",
            HyprlandActionOperations.FocusWindow => $"hl.dsp.focus({{ window = {target} }})",
            HyprlandActionOperations.CloseWindow => $"hl.dsp.window.close({{ window = {target} }})",
            _ => throw new InvalidOperationException( $"Unsupported Hyprland operation '{operation}'." )
        };

        var result = await RunHyprlandAsync( [HyprlandDispatch.Command, lua], cancellationToken )
            .ConfigureAwait( false );
        if ( HyprlandDispatch.WasAccepted( result ) )
        {
            return ActionExecutionResult.Success( );
        }

        var message = result.TimedOut
            ? "Hyprland dispatch timed out."
            : result.ExitCode == 0
                ? "Hyprland did not acknowledge the action."
                : $"'hyprctl' exited with code {result.ExitCode.ToString( CultureInfo.InvariantCulture )}.";
        var diagnostic = string.IsNullOrWhiteSpace( result.StandardError )
            ? result.StandardOutput.Trim( )
            : result.StandardError.Trim( );
        return ActionExecutionResult.Failure(
            message,
            string.IsNullOrWhiteSpace( diagnostic )
                ? null
                : $"```\n{diagnostic}\n```" );
    }

    private ValueTask<ProcessResult> RunHyprlandAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken ) =>
        hyprlandRunner is not null
            ? hyprlandRunner( HyprlandDispatch.Executable, arguments, TimeSpan.FromSeconds( 3 ), cancellationToken )
            : ProcessRunner.RunAsync( HyprlandDispatch.Executable, arguments, null, null,
                TimeSpan.FromSeconds( 3 ), cancellationToken );
}
