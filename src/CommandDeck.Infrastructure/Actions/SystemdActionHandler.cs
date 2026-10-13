using System.Text;

namespace CommandDeck;

// A user unit's status, logs, editing, and control. Every operation checks the
// unit against systemd again before it runs.
internal sealed class SystemdActionHandler(
    EditorLauncher editor,
    BoundedProcessRunner? systemdRunner ) : ILinuxActionHandler
{
    public IReadOnlyList<string> Kinds { get; } = [ActionKinds.SystemdUnit];

    public ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken ) =>
        action.Kind == ActionKinds.SystemdUnit
            ? ExecuteSystemdUnitAsync( result, action, cancellationToken )
            : ValueTask.FromResult( LinuxActionArguments.UnknownKind( action ) );

    private async ValueTask<ActionExecutionResult> ExecuteSystemdUnitAsync(
        SearchResult result,
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var unit = LinuxActionArguments.Require( action, SystemdUnitActionFields.Unit );
        var operation = LinuxActionArguments.Require( action, SystemdUnitActionFields.Operation );
        if ( result.SourceId != SourceIds.SystemdUnits || result.Id != SystemdUnitResultIds.ForUnit( unit ) ||
            !SystemdUserServiceProvider.IsValidUnitName( unit ) ||
            !SystemdUnitActionIds.IsSupported( operation ) )
        {
            return ActionExecutionResult.Failure( "The user-unit action is invalid. Search again." );
        }

        var properties = await ReadSystemdUnitAsync( unit, cancellationToken ).ConfigureAwait( false );
        if ( properties.GetValueOrDefault( "Id" ) != unit || properties.GetValueOrDefault( "LoadState" ) != "loaded" )
        {
            return ActionExecutionResult.Failure( "The user unit is no longer loaded or its identity changed. Search again." );
        }

        if ( operation == SystemdUnitActionIds.Status )
        {
            var status = string.Join( '\n', properties.Select( property => $"{property.Key}: {property.Value}" ) );
            return ActionExecutionResult.Success( $"Current status: {unit}", SafeSystemdOutput( status ) );
        }

        if ( operation == SystemdUnitActionIds.Logs )
        {
            return await ReadSystemdLogsAsync( unit, cancellationToken ).ConfigureAwait( false );
        }

        if ( operation == SystemdUnitActionIds.Edit )
        {
            var path = properties.GetValueOrDefault( "FragmentPath" );
            if ( string.IsNullOrWhiteSpace( path ) || !Path.IsPathFullyQualified( path ) || !File.Exists( path ) )
            {
                return ActionExecutionResult.Failure( "This unit has no editable unit file." );
            }

            var edited = await editor.EditAtLineAsync( new CommandAction
            {
                Id = SystemdUnitActionIds.Edit,
                Title = "Edit user unit",
                Kind = ActionKinds.EditAtLine,
                Arguments = new EditorActionPayload( path ).ToArguments( )
            }, cancellationToken ).ConfigureAwait( false );
            return edited.Succeeded
                ? ActionExecutionResult.Success( "Editor opened. After saving, run systemctl --user daemon-reload to apply unit-file changes." )
                : edited;
        }

        var control = await RunSystemdAsync( "systemctl",
            ["--user", "--no-ask-password", "--no-block", operation, "--", unit],
            cancellationToken ).ConfigureAwait( false );
        if ( control.ExitCode != 0 || control.TimedOut || control.AnyOutputLimitExceeded || control.MemoryLimitExceeded )
        {
            return ActionExecutionResult.Failure( $"Could not request {operation} for {unit}.", SafeSystemdOutput( control.StandardError ) );
        }

        return ActionExecutionResult.Success( $"{operation} requested for {unit}. Check current status for completion." );
    }

    private async ValueTask<ActionExecutionResult> ReadSystemdLogsAsync( string unit, CancellationToken cancellationToken )
    {
        var logs = await RunSystemdAsync( "journalctl",
            ["--user", "--unit=" + unit, "--lines=100", "--no-pager", "--output=short-iso"],
            cancellationToken ).ConfigureAwait( false );
        if ( logs.TimedOut || logs.MemoryLimitExceeded || (logs.ExitCode != 0 && !logs.AnyOutputLimitExceeded) )
        {
            return ActionExecutionResult.Failure( "Could not read recent user-unit logs." );
        }

        var text = SafeSystemdOutput( logs.StandardOutput );
        if ( logs.AnyOutputLimitExceeded )
        {
            text += "\n[Log output truncated]";
        }

        return ActionExecutionResult.Success( $"Recent logs: {unit}",
            string.IsNullOrWhiteSpace( text ) ? "No recent journal entries." : text );
    }

    private async ValueTask<Dictionary<string, string>> ReadSystemdUnitAsync(
        string unit,
        CancellationToken cancellationToken )
    {
        var response = await RunSystemdAsync( "systemctl",
            ["--user", "show", "--no-pager",
                "--property=Id,LoadState,ActiveState,SubState,FragmentPath,Unit,NextElapseUSecRealtime,NextElapseUSecMonotonic,LastTriggerUSec",
                "--", unit], cancellationToken ).ConfigureAwait( false );
        if ( response.ExitCode != 0 || response.TimedOut || response.AnyOutputLimitExceeded || response.MemoryLimitExceeded )
        {
            throw new InvalidOperationException( "Could not refresh the selected user unit." );
        }

        Dictionary<string, string> properties = new( StringComparer.Ordinal );
        using var reader = new StringReader( response.StandardOutput );
        while ( reader.ReadLine( ) is { } line )
        {
            var separator = line.IndexOf( '=' );
            if ( separator > 0 && !properties.TryAdd( line[..separator], line[(separator + 1)..] ) )
            {
                throw new InvalidOperationException( "The selected user unit returned ambiguous properties." );
            }
        }

        return properties;
    }

    private ValueTask<ProcessResult> RunSystemdAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        ProcessExecutionLimits limits = new( )
        {
            MaximumStandardOutputBytes = 16 * 1024,
            MaximumStandardErrorBytes = 4096,
            MaximumMemoryBytes = 128 * 1024 * 1024,
            ClearEnvironment = false
        };
        var timeout = TimeSpan.FromSeconds( 3 );
        return systemdRunner is not null
            ? systemdRunner( executable, arguments, timeout, limits, cancellationToken )
            : ProcessRunner.RunBoundedAsync( executable, arguments, null, timeout, limits, cancellationToken );
    }

    private static string SafeSystemdOutput( string text )
    {
        StringBuilder output = new( );
        var bytes = 0;
        foreach ( var rune in text.EnumerateRunes( ) )
        {
            var safe = Rune.IsControl( rune ) && rune.Value is not ('\n' or '\t') ? Rune.ReplacementChar : rune;
            bytes += safe.Utf8SequenceLength;
            if ( bytes > 16 * 1024 )
            {
                output.Append( "\n[Output truncated]" );
                break;
            }

            output.Append( safe );
        }

        return output.ToString( );
    }
}
