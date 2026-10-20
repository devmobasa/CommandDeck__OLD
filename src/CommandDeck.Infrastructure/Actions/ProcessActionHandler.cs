using System.Globalization;

namespace CommandDeck;

// Terminate and force kill for one process the user chose from kill:. The
// action names a PID, its start time, and its owner. Before signalling, the
// handler pins the process holding that PID through a pidfd, then reads /proc
// again: the start time must match, and the process must still be the user's
// own, live, and not CommandDeck. A PID reused since the search fails that check,
// and the pidfd keeps a later reuse from receiving the signal.
internal sealed class ProcessActionHandler( LinuxProcessTable table, ProcessPinner pin ) : ILinuxActionHandler
{
    public IReadOnlyList<string> Kinds { get; } = [ActionKinds.ProcessSignal];

    public ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken ) =>
        ValueTask.FromResult( action.Kind == ActionKinds.ProcessSignal
            ? Signal( result, action )
            : LinuxActionArguments.UnknownKind( action ) );

    private ActionExecutionResult Signal( SearchResult result, CommandAction action )
    {
        var signal = action.Id switch
        {
            ProcessActionIds.Terminate => LinuxProcessSignals.Terminate,
            ProcessActionIds.ForceKill => LinuxProcessSignals.Kill,
            _ => 0
        };
        var pidText = LinuxActionArguments.Require( action, ProcessActionFields.Pid );
        var startText = LinuxActionArguments.Require( action, ProcessActionFields.StartTime );
        var uidText = LinuxActionArguments.Require( action, ProcessActionFields.Uid );
        if ( signal == 0 || result.SourceId != SourceIds.Processes ||
            !int.TryParse( pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid ) ||
            !ulong.TryParse( startText, NumberStyles.None, CultureInfo.InvariantCulture, out var startTicks ) ||
            !uint.TryParse( uidText, NumberStyles.None, CultureInfo.InvariantCulture, out var uid ) ||
            result.Id != ProcessResultIds.For( pid, startTicks ) || uid != table.UserId || pid <= 1 || pid == table.SelfPid )
        {
            return ActionExecutionResult.Failure( "The process action is invalid. Search again." );
        }

        using var pinned = pin( pid );
        if ( pinned is null )
        {
            return ActionExecutionResult.Failure( $"Process {pidText} has already exited." );
        }

        var current = table.Read( pid );
        if ( current is null || current.StartTicks != startTicks || !table.IsListable( current ) )
        {
            return ActionExecutionResult.Failure(
                $"PID {pidText} no longer belongs to the process you chose. Nothing was signalled. Search again." );
        }

        if ( !pinned.Signal( signal ) )
        {
            return ActionExecutionResult.Failure( $"Process {pidText} has already exited." );
        }

        var name = signal == LinuxProcessSignals.Terminate ? "SIGTERM" : "SIGKILL";
        return ActionExecutionResult.Success( $"Sent {name} to {current.Name} (PID {pidText})." );
    }
}
