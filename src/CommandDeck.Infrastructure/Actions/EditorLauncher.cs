namespace CommandDeck;

// Opens a file at a line in the editor Settings selects, or in $VISUAL/$EDITOR
// when no preference store is available. Edit at Line and a systemd unit's
// Edit both open files this way.
internal sealed class EditorLauncher( ILauncherSearchPreferences? preferences )
{
    public async ValueTask<ActionExecutionResult> EditAtLineAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var location = EditorActionPayload.Read( action.Arguments );
        var path = location.Path;
        var line = location.Line.ToString( System.Globalization.CultureInfo.InvariantCulture );
        var column = location.Column.ToString( System.Globalization.CultureInfo.InvariantCulture );
        var key = location.Key;
        if ( preferences is not null )
        {
            var launcher = await preferences.GetPreferencesAsync( cancellationToken )
                .ConfigureAwait( false );
            var selected = launcher.PreferredEditor == EditorKinds.Terminal
                ? launcher.TerminalEditor
                : launcher.GraphicalEditor;
            selected = EditorAdapterValidator.Validate( selected, selected.Terminal );
            var expanded = EditorAdapterValidator.Expand(
                selected, path, line, column, key );
            if ( selected.Terminal )
            {
                if ( !ProcessRunner.ExistsOnPath( "xdg-terminal-exec" ) )
                {
                    return ActionExecutionResult.Failure(
                        "The terminal editor requires xdg-terminal-exec, which is unavailable." );
                }
                return await ManagedLaunch.RunAndReportAsync(
                    "xdg-terminal-exec",
                    ["--", selected.Executable, .. expanded],
                    null,
                    cancellationToken ).ConfigureAwait( false );
            }
            return await ManagedLaunch.RunAndReportAsync(
                selected.Executable,
                expanded,
                null,
                cancellationToken ).ConfigureAwait( false );
        }
        var editorValue = Environment.GetEnvironmentVariable( "VISUAL" )
                          ?? Environment.GetEnvironmentVariable( "EDITOR" )
                          ?? "nvim";

        var editor = editorValue.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
        if ( editor.Length == 0 )
        {
            return ActionExecutionResult.Failure( "No editor is configured." );
        }

        var arguments = editor.Skip( 1 ).ToList( );
        var executableName = Path.GetFileName( editor[0] );
        if ( executableName is "code" or "codium" )
        {
            arguments.Add( "--goto" );
            arguments.Add( $"{path}:{line}:{column}" );
        }
        else if ( executableName is "nvim" or "vim" or "vi" )
        {
            arguments.Add( $"+call cursor({line},{column})" );
            arguments.Add( path );
        }
        else
        {
            arguments.Add( $"+{line}" );
            arguments.Add( path );
        }

        return await ManagedLaunch.RunAndReportAsync(
            editor[0],
            arguments,
            null,
            cancellationToken ).ConfigureAwait( false );
    }
}
