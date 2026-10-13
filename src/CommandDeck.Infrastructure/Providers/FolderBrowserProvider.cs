namespace CommandDeck;

public sealed class FolderBrowserProvider : ICommandProvider
{
    public string Id => ProviderIds.Folders;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Folders ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }
        var text = query.Text;
        var path = request.BrowseDirectory ?? Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
        if ( text.StartsWith( '/' ) || text.StartsWith( "~/", StringComparison.Ordinal ) )
        {
            path = text;
            text = "";
        }
        path = Path.GetFullPath( PathExpansion.Expand( path ) );
        List<SearchResult> results = [];
        var parent = Path.GetDirectoryName( path.TrimEnd( Path.DirectorySeparatorChar ) );
        if ( parent is not null )
        {
            results.Add( new SearchResult( Build( parent, true ) ) { Id = "folder-parent:" + path, Title = "Up one folder", Score = 10 } );
        }
        try
        {
            var limit = Math.Clamp( request.Limit - 3, 1, 97 );
            if ( request.BrowseOffset > 0 )
            {
                results.Add( new SearchResult
                {
                    Id = "folder-previous:" + path,
                    Title = "Previous page",
                    Subtitle = path,
                    Kind = ResultKinds.CustomCommand,
                    ProviderId = Id,
                    SourceId = SourceIds.Folders,
                    Score = 9,
                    Actions = [new CommandAction
                    {
                        Id = "previous", Title = "Previous page", Kind = ActionKinds.BrowseFolder,
                        Arguments = new(StringComparer.Ordinal) { ["path"] = path, ["offset"] = Math.Max(0, request.BrowseOffset - limit).ToString(), ["query"] = text }
                    }]
                } );
            }
            var entries = Directory.EnumerateFileSystemEntries( path ).Take( 50000 )
                .Where( entry => Path.GetFileName( entry ).Contains( text, StringComparison.OrdinalIgnoreCase ) )
                .Select( entry => (Path: entry, Directory: Directory.Exists( entry )) )
                .OrderByDescending( static entry => entry.Directory ).ThenBy( static entry => entry.Path, StringComparer.OrdinalIgnoreCase )
                .Skip( request.BrowseOffset ).Take( limit + 1 ).ToArray( );
            foreach ( var entry in entries.Take( limit ) )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                results.Add( Build( entry.Path, entry.Directory ) );
            }
            if ( entries.Length > limit )
            {
                results.Add( new SearchResult
                {
                    Id = "folder-next:" + path,
                    Title = "Next page",
                    Subtitle = path,
                    Kind = ResultKinds.CustomCommand,
                    ProviderId = Id,
                    SourceId = SourceIds.Folders,
                    Score = 0.1,
                    Actions = [new CommandAction
                    {
                        Id = "next", Title = "Next page", Kind = ActionKinds.BrowseFolder,
                        Arguments = new(StringComparer.Ordinal) { ["path"] = path, ["offset"] = (request.BrowseOffset + limit).ToString(), ["query"] = text }
                    }]
                } );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
            results.Add( new SearchResult
            {
                Id = "folder-error:" + path,
                Title = "Folder unavailable",
                Subtitle = path,
                Kind = ResultKinds.Status,
                ProviderId = Id,
                SourceId = SourceIds.Folders,
                Score = 3,
                DetailMarkdown = exception.Message
            } );
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( results );
    }

    private static SearchResult Build( string path, bool directory ) => new( )
    {
        Id = StableId.Create( "browse", path ),
        Title = Path.GetFileName( path ),
        Subtitle = path,
        Kind = ResultKinds.File,
        ProviderId = ProviderIds.Folders,
        SourceId = SourceIds.Folders,
        Icon = directory ? "folder" : "text-x-generic",
        Score = directory ? 2.5 : 2,
        Metadata = new( StringComparer.Ordinal )
        {
            ["path"] = path,
            ["isDirectory"] = directory ? "true" : "false"
        },
        Actions =
        [
            new CommandAction { Id = "open", Title = directory ? "Browse folder" : "Open file", Kind = directory ? ActionKinds.BrowseFolder : ActionKinds.OpenPath,
                Arguments = new(StringComparer.Ordinal) { ["path"] = path } },
            new CommandAction { Id = "collect", Title = "Toggle file selection", Kind = ActionKinds.CollectFile,
                Arguments = new(StringComparer.Ordinal) { ["path"] = path } },
            new CommandAction { Id = "copy-path", Title = "Copy path", Kind = ActionKinds.CopyText,
                Arguments = new(StringComparer.Ordinal) { ["text"] = path } },
            new CommandAction { Id = "reveal", Title = "Open in file manager", Kind = ActionKinds.RevealPath,
                Arguments = new(StringComparer.Ordinal) { ["path"] = path } },
            .. directory ? new[] { HerdrProjectAction.Create( path ) } : []
        ]
    };
}
