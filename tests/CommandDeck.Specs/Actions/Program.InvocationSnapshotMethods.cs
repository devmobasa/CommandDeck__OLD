namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class QueryActionProvider : ICommandProvider
    {
        public string Id => "static";

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            SearchResult row = new( )
            {
                Id = "web-search:fixture",
                Kind = ResultKinds.WebSearch,
                Title = "Search fixture",
                SourceId = SourceIds.WebSearch,
                ProviderId = Id,
                Actions =
                [
                    new CommandAction
                    {
                        Id = "open",
                        Title = "Open",
                        Kind = ActionKinds.OpenUrl,
                        Hidden = true,
                        Inputs = [new ActionInput { Name = "note", DefaultValue = "literal" }],
                        Arguments = new( StringComparer.Ordinal )
                        {
                            ["urlTemplate"] = "https://example.invalid/?q=" + request.Query
                        }
                    }
                ]
            };
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [row] );
        }
    }

    private sealed class RecordingInvocationExecutor : IActionExecutor
    {
        public List<string> Targets { get; } = [];
        public List<CommandAction> Actions { get; } = [];

        public ValueTask<ActionExecutionResult> ExecuteAsync(
            SearchResult result,
            CommandAction action,
            string? query,
            CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            Targets.Add( action.Arguments["urlTemplate"] );
            Actions.Add( action );
            return ValueTask.FromResult( ActionExecutionResult.Success( ) );
        }
    }

    private static async Task InvocationSnapshotRejectsReplacedActionAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-invocation-snapshot-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            RecordingInvocationExecutor executor = new( );
            SearchCoordinator search = new( [new QueryActionProvider( )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, executor, search );

            var alpha = (await runtime.SearchAsync(
                new SearchRequest { Query = "alpha", SearchOwner = "client-a" }, default )).Single( );
            var beta = (await runtime.SearchAsync(
                new SearchRequest { Query = "beta", SearchOwner = "client-b" }, default )).Single( );
            Equal( alpha.Id, beta.Id );
            True( alpha.Actions[0].Arguments["urlTemplate"] != beta.Actions[0].Arguments["urlTemplate"] );
            True( !string.IsNullOrWhiteSpace( alpha.InvocationReference ) );
            True( !string.IsNullOrWhiteSpace( beta.InvocationReference ) );
            True( alpha.InvocationReference != beta.InvocationReference );

            var outcome = await runtime.InvokeReferenceAsync( alpha.Id, "open", alpha.InvocationReference!,
                "client-a", false, default );
            True( outcome.Succeeded, "another client cannot replace this client's action" );
            Equal( "https://example.invalid/?q=alpha", executor.Targets.Single( ) );
            True( executor.Actions.Single( ).Hidden == true, "Invocation snapshots and bound actions preserve internal action visibility." );
            Equal( "note", executor.Actions.Single( ).Inputs.Single( ).Name );
            Equal( "literal", executor.Actions.Single( ).Arguments["input:note"] );

            outcome = await runtime.InvokeReferenceAsync( beta.Id, "open", beta.InvocationReference!,
                "client-a", false, default );
            True( !outcome.Succeeded, "another client cannot reuse the reference" );
            Equal( 1, executor.Targets.Count );

            beta.Actions[0].Arguments["urlTemplate"] = "https://example.invalid/changed";
            outcome = await runtime.InvokeReferenceAsync( beta.Id, "open", beta.InvocationReference!,
                "client-b", false, default );
            True( outcome.Succeeded );
            Equal( "https://example.invalid/?q=beta", executor.Targets.Last( ) );

            var replacement = (await runtime.SearchAsync(
                new SearchRequest { Query = "gamma", SearchOwner = "client-a" }, default )).Single( );
            Equal( alpha.Id, replacement.Id );
            outcome = await runtime.InvokeReferenceAsync( alpha.Id, "open", alpha.InvocationReference!,
                "client-a", false, default );
            True( !outcome.Succeeded, "a same-client replacement revokes its earlier action" );
            Equal( 2, executor.Targets.Count );

            runtime.RevokeInvocationClient( "client-b" );
            outcome = await runtime.InvokeReferenceAsync( beta.Id, "open", beta.InvocationReference!,
                "client-b", false, default );
            True( !outcome.Succeeded, "disconnect revokes the client's action" );
            Equal( 2, executor.Targets.Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static SearchResult InvocationFileRow( string path ) => new( )
    {
        Id = "file:invocation-fixture",
        Kind = ResultKinds.File,
        Title = Path.GetFileName( path ),
        SourceId = SourceIds.Files,
        ProviderId = ProviderIds.Files,
        Metadata = new( StringComparer.Ordinal ) { ["path"] = path },
        Actions =
        [
            new CommandAction
            {
                Id = "collect",
                Title = "Toggle file selection",
                Kind = ActionKinds.CollectFile,
                Arguments = new( StringComparer.Ordinal ) { ["path"] = path }
            }
        ]
    };

    private static CommandDeckOptions InvocationFileOptions( string root ) => new( )
    {
        Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
        Indexing = new IndexingOptions
        {
            Roots = [new IndexRootOptions { Path = root, IncludeContentInSearch = true }],
            IndexDesktopApplications = false,
            IndexOmarchyCommands = false
        },
        Privacy = new PrivacyOptions { StoreQueryHistory = true, NeverIndex = [] },
        Ai = new AiOptions { Enabled = false, Providers = [] }
    };

    private static async Task PreviewMatchingLineInheritsQueryPrivacyAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-preview-privacy-" ).FullName;
        try
        {
            var path = Path.Combine( root, "notes.md" );
            await File.WriteAllTextAsync( path, "first line\nsecond needle line\n" );
            var options = InvocationFileOptions( root );
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            AcceptedUsageHistory history = new( catalog.UsageHistory );
            AcceptanceExecutor executor = new( );
            SearchCoordinator search = new( [new StaticSearchProvider( [InvocationFileRow( path )] )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, history, executor, search );

            async Task<ActionExecutionResult> OpenMatchingLineAsync( string query )
            {
                var file = (await runtime.SearchAsync(
                    new SearchRequest { Query = query, SearchOwner = "client-a" }, default )).Single( );
                var preview = await runtime.PreviewAsync( file.Id, default, "content:needle", null, 0,
                    "client-a", file.InvocationReference );
                Equal( 2, preview.Preview?.MatchLine );
                return await runtime.InvokeReferenceAsync( preview.Preview!.OpenResultId!, "edit",
                    preview.Preview.OpenInvocationReference!, "client-a", false, default );
            }

            True( (await OpenMatchingLineAsync( "needle -hey:" )).Succeeded );
            Equal( 1, executor.Calls );
            Equal( 0, history.Writes );

            True( (await OpenMatchingLineAsync( "needle" )).Succeeded );
            Equal( 2, executor.Calls );
            Equal( 1, history.Writes );
            Equal( "needle", history.LastQuery );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task FileSelectionAdmitsOnlyOwnedSnapshotsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-selection-owner-" ).FullName;
        try
        {
            var path = Path.Combine( root, "notes.md" );
            await File.WriteAllTextAsync( path, "selected" );
            var options = InvocationFileOptions( root );
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            AcceptanceExecutor executor = new( );
            SearchCoordinator search = new( [new StaticSearchProvider( [InvocationFileRow( path )] )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, executor, search );

            var file = (await runtime.SearchAsync(
                new SearchRequest { Query = "notes", SearchOwner = "client-a" }, default )).Single( );
            Throws<InvalidOperationException>( ( ) => runtime.SelectFile( "client-b", file.Id, true ) );
            True( !(await runtime.ExecuteFileSelectionAsync( "client-b", FileSelectionActionIds.CopyPaths, default )).Succeeded,
                "another client's snapshot must not enter this client's selection" );
            Equal( path, runtime.SelectFile( "client-a", file.Id, true ).Single( ).Path );

            await runtime.SearchAsync( new SearchRequest { Query = "notes", SearchOwner = "client-b" }, default );
            Equal( path, runtime.SelectFile( "client-b", file.Id, true ).Single( ).Path );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
    // A row that offers "mark" with its hidden undo until it is marked, and
    // "unmark" with its own undo after; the refresh replaces the row.
    private sealed class UndoableRowProvider : ICommandProvider
    {
        public bool Marked
        {
            get; set;
        }

        public string Id => "static";

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var change = Marked ? "unmark" : "mark";
            SearchResult row = new( )
            {
                Id = "web-search:undoable",
                Kind = ResultKinds.WebSearch,
                Title = "Undoable fixture",
                SourceId = SourceIds.WebSearch,
                ProviderId = Id,
                Actions =
                [
                    new CommandAction { Id = change, Title = change, Kind = ActionKinds.OpenUrl },
                    new CommandAction { Id = UndoActions.For( change ), Title = "Undo " + change, Kind = ActionKinds.OpenUrl, Hidden = true }
                ]
            };
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [row] );
        }
    }

    private sealed class RecordingActionIdExecutor( UndoableRowProvider provider ) : IActionExecutor
    {
        public List<string> Actions { get; } = [];

        public ValueTask<ActionExecutionResult> ExecuteAsync(
            SearchResult result,
            CommandAction action,
            string? query,
            CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            Actions.Add( action.Id );
            provider.Marked = action.Id is "mark" or "undo:unmark";
            return ValueTask.FromResult( ActionExecutionResult.Success( action.Id ) );
        }
    }

    // A refresh replaces the changed row and retires its reference, and the new
    // row no longer offers the same undo. The undo reference the change
    // returned still reaches that one undo, for that client only.
    private static async Task InvocationUndoSurvivesTheRefreshAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-invocation-undo-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            UndoableRowProvider provider = new( );
            RecordingActionIdExecutor executor = new( provider );
            SearchCoordinator search = new( [provider], options.Search );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, executor, search );
            async Task<SearchResult> SearchAsClientAsync( ) =>
                (await runtime.SearchAsync( new SearchRequest { Query = "undoable", SearchOwner = "client-a" }, default )).Single( );

            var row = await SearchAsClientAsync( );
            var changed = await runtime.InvokeReferenceAsync( row.Id, "mark", row.InvocationReference!, "client-a", false, default );
            True( changed.Succeeded );
            var undo = changed.UndoInvocationReference;
            True( !string.IsNullOrWhiteSpace( undo ) && undo != row.InvocationReference, "a change with a hidden undo returns its own undo reference" );

            var refreshed = await SearchAsClientAsync( );
            True( refreshed.Actions.All( static action => action.Id != "undo:mark" ), "the refreshed row offers other changes" );
            True( !(await runtime.InvokeReferenceAsync( row.Id, "undo:mark", row.InvocationReference!, "client-a", false, default )).Succeeded,
                "the refresh retired the changed row's own reference" );

            True( !(await runtime.InvokeReferenceAsync( row.Id, "mark", undo!, "client-a", false, default )).Succeeded,
                "the undo reference grants nothing but the undo" );
            True( !(await runtime.InvokeReferenceAsync( row.Id, "undo:mark", undo!, "client-b", false, default )).Succeeded,
                "another client cannot use the undo reference" );
            Equal( "mark", string.Join( ',', executor.Actions ) );

            var undone = await runtime.InvokeReferenceAsync( row.Id, "undo:mark", undo!, "client-a", false, default );
            True( undone.Succeeded );
            Equal( "mark,undo:mark", string.Join( ',', executor.Actions ) );
            Equal( ( string? ) null, undone.UndoInvocationReference );

            // Stable-ID lookups still see the live row, never the undo copy.
            True( !(await runtime.InvokeAsync( row.Id, "undo:mark", false, default )).Succeeded );
            True( (await runtime.InvokeAsync( row.Id, "unmark", false, default )).Succeeded );

            // Disconnect revokes the undo with the client's other actions.
            var again = await SearchAsClientAsync( );
            var second = await runtime.InvokeReferenceAsync( again.Id, again.Actions[0].Id, again.InvocationReference!, "client-a", false, default );
            runtime.RevokeInvocationClient( "client-a" );
            True( !(await runtime.InvokeReferenceAsync( again.Id, UndoActions.For( again.Actions[0].Id ), second.UndoInvocationReference!,
                "client-a", false, default )).Succeeded, "disconnect revokes the undo" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
