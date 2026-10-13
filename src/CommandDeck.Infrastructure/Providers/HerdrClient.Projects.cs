using System.Text.Json;

namespace CommandDeck;

public sealed partial class HerdrClient
{
    private const int MaximumProjectPanes = 1024;

    public async ValueTask<HerdrFocusOutcome> OpenProjectAsync(
        string path, CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask<ActionExecutionResult>>? activateWindow = null )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        var directory = NormalizeProjectDirectory( path );
        if ( directory is null || !Directory.Exists( directory ) )
        {
            return ProjectFailure( "The selected project folder is unavailable." );
        }

        if ( !_available( ) )
        {
            return ProjectFailure( "Herdr is not on the daemon PATH." );
        }

        // Reject overlapping opens so two invocations cannot both create from
        // the same empty inventory, or queue an unexpected later focus change.
        if ( !TryBeginFocus( ) )
        {
            return FocusBusy( );
        }

        try
        {
            var workspaceResult = RunAsync( ["workspace", "list"], ListTimeout,
                WorkspaceListStandardOutputBytes, WorkspaceListStandardErrorBytes, cancellationToken ).AsTask( );
            var paneResult = RunAsync( ["pane", "list"], ListTimeout,
                1024 * 1024, TabListStandardErrorBytes, cancellationToken ).AsTask( );
            await Task.WhenAll( workspaceResult, paneResult ).ConfigureAwait( false );
            cancellationToken.ThrowIfCancellationRequested( );

            var matches = FindProjectSpaces( workspaceResult.Result, paneResult.Result, directory );
            if ( matches is null )
            {
                return ProjectFailure( "Herdr's project inventory is unavailable or incomplete. No space was created." );
            }

            var targetId = matches.FirstOrDefault( );
            if ( targetId is null )
            {
                targetId = await CreateProjectSpaceAsync( directory, cancellationToken ).ConfigureAwait( false );
                if ( targetId is null )
                {
                    return ProjectFailure( "Herdr did not confirm project creation; it may already have happened. Check Herdr before trying again." );
                }
            }

            cancellationToken.ThrowIfCancellationRequested( );
            return await FocusAndActivateAsync( HerdrTargetKinds.Workspace, targetId,
                activateWindow, cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return ProjectFailure( "Herdr could not complete the project action. Check Herdr before trying again." );
        }
        finally
        {
            EndFocus( );
        }
    }

    private static List<string>? FindProjectSpaces( ProcessResult workspaceResult, ProcessResult paneResult, string directory )
    {
        var workspaces = ReadResultArray( workspaceResult, "workspace_list", "workspaces" );
        using var workspaceDocument = workspaces?.Document;
        var panes = ReadResultArray( paneResult, "pane_list", "panes" );
        using var paneDocument = panes?.Document;
        if ( workspaces is null || panes is null ||
            workspaces.Value.Items.GetArrayLength( ) > MaximumWorkspaces ||
            panes.Value.Items.GetArrayLength( ) > MaximumProjectPanes ||
            workspaceDocument!.RootElement.TryGetProperty( "error", out _ ) ||
            paneDocument!.RootElement.TryGetProperty( "error", out _ ) )
        {
            return null;
        }

        Dictionary<string, (bool Focused, long Number, bool Match, long ExpectedPanes, int SeenPanes)> spaces = new( StringComparer.Ordinal );
        foreach ( var space in workspaces.Value.Items.EnumerateArray( ) )
        {
            var id = ReadString( space, "workspace_id" );
            var paneCount = ReadInteger( space, "pane_count" );
            if ( !IsValidIdentifier( id ) || spaces.ContainsKey( id! ) || paneCount is null or < 0 or > MaximumProjectPanes )
            {
                return null;
            }

            var checkout = space.TryGetProperty( "worktree", out var worktree )
                ? ReadString( worktree, "checkout_path" ) : null;
            spaces.Add( id!, (ReadBoolean( space, "focused" ) == true, ReadCount( space, "number" ),
                NormalizeProjectDirectory( checkout ) == directory, paneCount.Value, 0) );
        }

        HashSet<string> paneIds = new( StringComparer.Ordinal );
        foreach ( var pane in panes.Value.Items.EnumerateArray( ) )
        {
            var workspaceId = ReadString( pane, "workspace_id" );
            var paneId = ReadString( pane, "pane_id" );
            if ( !IsValidIdentifier( paneId ) || !paneIds.Add( paneId! ) || workspaceId is null ||
                !spaces.TryGetValue( workspaceId, out var space ) )
            {
                return null;
            }

            // cwd is Herdr's reported shell directory; foreground_cwd can be
            // a transient child process directory and is not project identity.
            space.Match |= NormalizeProjectDirectory( ReadString( pane, "cwd" ) ) == directory;
            space.SeenPanes++;
            spaces[workspaceId] = space;
        }

        if ( spaces.Values.Any( static space => space.SeenPanes != space.ExpectedPanes ) )
        {
            return null;
        }

        return spaces.Where( static pair => pair.Value.Match )
            .OrderByDescending( static pair => pair.Value.Focused )
            .ThenBy( static pair => pair.Value.Number )
            .ThenBy( static pair => pair.Key, StringComparer.Ordinal )
            .Select( static pair => pair.Key ).ToList( );
    }

    private async ValueTask<string?> CreateProjectSpaceAsync( string directory, CancellationToken cancellationToken )
    {
        var name = Path.GetFileName( directory );
        if ( name.Length == 0 )
        {
            name = "Root";
        }

        var result = await RunAsync( ["workspace", "create", "--cwd", directory, "--label", name, "--no-focus"],
            FocusTimeout, FocusStandardOutputBytes, FocusStandardErrorBytes, cancellationToken ).ConfigureAwait( false );
        if ( result.ExitCode != 0 || result.TimedOut || result.AnyOutputLimitExceeded || result.MemoryLimitExceeded )
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse( result.StandardOutput );
            var envelope = document.RootElement;
            if ( envelope.ValueKind != JsonValueKind.Object || envelope.TryGetProperty( "error", out _ ) ||
                !envelope.TryGetProperty( "result", out var payload ) || ReadString( payload, "type" ) != "workspace_created" ||
                !payload.TryGetProperty( "workspace", out var workspace ) ||
                !payload.TryGetProperty( "root_pane", out var pane ) )
            {
                return null;
            }

            var id = ReadString( workspace, "workspace_id" );
            if ( !IsValidIdentifier( id ) || ReadString( workspace, "label" ) != name ||
                ReadString( pane, "workspace_id" ) != id || !IsValidIdentifier( ReadString( pane, "pane_id" ) ) ||
                NormalizeProjectDirectory( ReadString( pane, "cwd" ) ) != directory )
            {
                return null;
            }

            return id;
        }
        catch ( JsonException )
        {
            return null;
        }
    }

    private static string? NormalizeProjectDirectory( string? path )
    {
        if ( string.IsNullOrWhiteSpace( path ) || path.Length > 4096 || path.Any( char.IsControl ) ||
            !Path.IsPathFullyQualified( path ) )
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator( Path.GetFullPath( path ) );
    }

    private static HerdrFocusOutcome ProjectFailure( string message ) => new( ) { Succeeded = false, Message = message };
}
