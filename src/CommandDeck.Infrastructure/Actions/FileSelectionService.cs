namespace CommandDeck;

// Selection admits only results from the selecting client's own search
// snapshots; resolve receives that client and the requested result ID.
public sealed class FileSelectionService( IActionExecutor actions, Func<string, string, SearchResult?> resolve )
{
    private readonly object _gate = new( );
    private readonly Dictionary<string, List<FileSelectionItem>> _clients = new( StringComparer.Ordinal );

    public List<FileSelectionItem> Select( string client, string resultId, bool enabled )
    {
        lock ( _gate )
        {
            if ( !_clients.TryGetValue( client, out var selection ) )
            {
                _clients[client] = selection = [];
            }
            var result = resolve( client, resultId );
            if ( !enabled )
            {
                selection.RemoveAll( item => item.Id == resultId || item.Path == result?.Metadata.GetValueOrDefault( "path" ) );
            }
            else
            {
                var path = result?.Metadata.GetValueOrDefault( "path" );
                if ( result is null || !result.Actions.Any( static action => action.Kind == ActionKinds.CollectFile ) ||
                    string.IsNullOrEmpty( path ) || !Path.IsPathFullyQualified( path ) )
                {
                    throw new InvalidOperationException( "Select a current file result first." );
                }
                if ( !selection.Any( item => item.Path == path ) )
                {
                    if ( selection.Count >= 32 )
                    {
                        throw new InvalidOperationException( "Select at most 32 files at once." );
                    }
                    selection.Add( new( result.Id, path, result.Title ) );
                }
            }
            return [.. selection];
        }
    }

    public void Clear( string client )
    {
        lock ( _gate )
        {
            _clients.Remove( client );
        }
    }

    public async ValueTask<ActionExecutionResult> ExecuteAsync( string client, string actionId, CancellationToken cancellationToken )
    {
        List<FileSelectionItem> selection;
        lock ( _gate )
        {
            selection = _clients.TryGetValue( client, out var current ) ? [.. current] : [];
        }
        if ( selection.Count == 0 )
        {
            return ActionExecutionResult.Failure( "Select some files first." );
        }
        if ( selection.Any( static item => !File.Exists( item.Path ) && !Directory.Exists( item.Path ) ) )
        {
            return ActionExecutionResult.Failure( "A selected path no longer exists. Remove it from the selection and try again." );
        }
        SearchResult target = new( )
        {
            Id = "file-selection",
            Title = "Selected files",
            Kind = ResultKinds.File,
            ProviderId = ProviderIds.Folders,
            SourceId = SourceIds.Folders
        };
        if ( actionId == FileSelectionActionIds.CopyPaths )
        {
            return await actions.ExecuteAsync( target, new CommandAction
            {
                Id = "copy",
                Title = "Copy paths",
                Kind = ActionKinds.CopyText,
                Arguments = new( StringComparer.Ordinal ) { ["text"] = string.Join( '\n', selection.Select( static item => item.Path ) ) }
            }, "", cancellationToken ).ConfigureAwait( false );
        }
        if ( actionId != FileSelectionActionIds.Open )
        {
            return ActionExecutionResult.Failure( "Unknown file selection action." );
        }
        var opened = 0;
        foreach ( var item in selection )
        {
            var outcome = await actions.ExecuteAsync( target, new CommandAction
            {
                Id = "open",
                Title = "Open file",
                Kind = ActionKinds.OpenPath,
                Arguments = new( StringComparer.Ordinal ) { ["path"] = item.Path }
            }, "", cancellationToken ).ConfigureAwait( false );
            if ( !outcome.Succeeded )
            {
                return ActionExecutionResult.Failure( $"Opened {opened} of {selection.Count} paths. {outcome.Message}" );
            }
            opened++;
        }
        return ActionExecutionResult.Success( $"Opened {opened} paths." );
    }
}
