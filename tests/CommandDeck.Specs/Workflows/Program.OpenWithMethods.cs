using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RuntimeSearchSurvivesMimeDetectionFailureAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-failed-mime-" ).FullName;
        try
        {
            await using var runtime = await SlowMimeRuntimeAsync( SlowMimeOptions( root ), new FailingMimeTypeResolver( ) );
            var outcome = await runtime.SearchWithOutcomeAsync( ReportSearch( ), default );
            True( outcome.Results.Any( static row => row.Kind == ResultKinds.Application ) );
            var file = outcome.Results.Single( static row => row.Id == "file:report" );
            True( file.Actions.Any( static action => action.Id == "open" ) );
            True( !file.Actions.Any( static action => action.Id.StartsWith( "open-with:", StringComparison.Ordinal ) ) );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }

    private sealed class FailingMimeTypeResolver : IMimeTypeResolver
    {
        public ValueTask<IReadOnlyDictionary<string, string?>> ResolveManyAsync(
            IReadOnlyList<string> paths, CancellationToken cancellationToken ) =>
            ValueTask.FromException<IReadOnlyDictionary<string, string?>>( new IOException( "fixture MIME failure" ) );
    }

    // "Open With" needs a MIME type per file: a stat, plus a `file` process for
    // each cache miss. Candidates that ranking discards must not pay for it.
    private static async Task RuntimeResolvesMimeTypesOnlyForShownFilesAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-open-with-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            RecordingMimeTypeResolver mime = new( "text/plain" );
            SqliteCatalog catalog = new( options.Storage, mimeTypeResolver: mime );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( OpenWithReportItems( root, 120 ), null, default );
            SearchCoordinator search = new(
                [new ApplicationSearchProvider( catalog ), new CatalogSearchProvider( catalog, catalog )],
                options.Search,
                catalog,
                catalog,
                catalog.UsageHistory );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, new AcceptanceExecutor( ), search );

            var outcome = await runtime.SearchWithOutcomeAsync( new SearchRequest { Query = "report", Limit = 10 }, default );

            var shown = outcome.Results.Where( static result => result.Kind == ResultKinds.File ).ToArray( );
            True( shown.Length is > 0 and <= 10, $"expected shown file rows, got {shown.Length}" );
            var shownPaths = shown.Select( static result => result.Metadata["path"] ).ToHashSet( StringComparer.Ordinal );
            True( mime.RequestedPaths.SetEquals( shownPaths ),
                $"MIME detection ran for {mime.RequestedPaths.Count} paths; the launcher shows {shownPaths.Count} files" );
            foreach ( var file in shown )
            {
                var openWith = file.Actions.Single( static action => action.Id == "open-with:viewer.desktop" );
                Equal( ActionKinds.LaunchDesktop, openWith.Kind );
                Equal( file.Metadata["path"],
                    LaunchArgumentCodec.Decode( openWith.Arguments["launchArguments"] ).Single( ) );
                Equal( "open", file.Actions[0].Id );
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }

    // Open With is optional. The runtime used to finish MIME detection before any
    // row reached the caller, so a stalled `file` held up even an application
    // match. A search now waits a bounded time, then cancels detection and returns
    // the rows with their own actions.
    private static async Task RuntimeSearchDoesNotWaitForSlowMimeDetectionAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-slow-mime-" ).FullName;
        try
        {
            var options = SlowMimeOptions( root );
            SlowMimeTypeResolver mime = new( honoursCancellation: true );
            await using var runtime = await SlowMimeRuntimeAsync( options, mime );

            // A prompt answer gives the complete rows and reads the handler list once.
            var answered = await runtime.SearchWithOutcomeAsync( ReportSearch( ), default );

            // The declared bound: the providers' deadline, then the Open With wait.
            mime.Stall( );
            var bound = TimeSpan.FromMilliseconds( options.Search.ProviderTimeoutMilliseconds ) +
                OpenWithDetection.DefaultWait;
            using CancellationTokenSource abandon = new( );
            var searching = runtime.SearchWithOutcomeAsync( ReportSearch( ), abandon.Token ).AsTask( );
            await (( Task ) searching).WaitAsync( bound ).ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );
            var returned = searching.IsCompleted;
            if ( !returned )
            {
                abandon.Cancel( );
                await (( Task ) searching).ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );
            }

            True( returned, $"no row reached the caller within {bound.TotalMilliseconds:F0} ms while MIME detection stalled" );
            var stalled = await searching;
            True( stalled.IsComplete );
            Equal( DescribeRows( answered.Results, withOpenWith: false ), DescribeRows( stalled.Results, withOpenWith: true ) );
            True( answered.Results.Single( static row => row.Id == "file:report" ).Actions
                .Any( static action => action.Id == "open-with:viewer.desktop" ) );
            await mime.Cancelled.WaitAsync( TimeSpan.FromSeconds( 5 ) ).ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );
            True( mime.Cancelled.IsCompleted, "MIME detection kept running after the search returned" );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }

    // Cancelling a search while it waits for Open With stops detection as well. A
    // long wait leaves the request's cancellation as the only way out.
    private static async Task RuntimeSearchCancellationEndsMimeDetectionAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-cancelled-mime-" ).FullName;
        try
        {
            SlowMimeTypeResolver mime = new( honoursCancellation: true );
            mime.Stall( );
            await using var runtime = await SlowMimeRuntimeAsync(
                SlowMimeOptions( root ),
                mime,
                new RuntimeConstructionOptions { OpenWithWait = TimeSpan.FromMinutes( 5 ) } );

            using CancellationTokenSource request = new( );
            var searching = runtime.SearchWithOutcomeAsync( ReportSearch( ), request.Token ).AsTask( );
            await mime.Started.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            request.Cancel( );

            await ThrowsAsync<OperationCanceledException>( ( ) => searching.WaitAsync( TimeSpan.FromSeconds( 5 ) ) );
            await mime.Cancelled.WaitAsync( TimeSpan.FromSeconds( 5 ) ).ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );
            True( mime.Cancelled.IsCompleted, "MIME detection kept running after its search was cancelled" );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }

    // A stat on a mount that stopped answering ignores cancellation. Detection
    // that outlives its search keeps one of two slots, a later search skips Open
    // With instead of starting more, and shutdown waits for that work to end.
    private static async Task RuntimeBoundsAndJoinsAbandonedMimeDetectionAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-abandoned-mime-" ).FullName;
        SlowMimeTypeResolver mime = new( honoursCancellation: false );
        CommandDeckRuntime? runtime = null;
        try
        {
            mime.Stall( );
            runtime = await SlowMimeRuntimeAsync( SlowMimeOptions( root ), mime );

            for ( var search = 0; search < 3; search++ )
            {
                var outcome = await runtime.SearchWithOutcomeAsync( ReportSearch( ), default ).AsTask( )
                    .WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( "app:report-viewer: launch | file:report: open,collect,browse-parent,reveal,copy-path,copy-file-url",
                    DescribeRows( outcome.Results, withOpenWith: true ) );
            }

            Equal( 2, mime.Calls );
            Equal( 2, mime.Running );

            var disposing = runtime.DisposeAsync( ).AsTask( );
            True( !disposing.IsCompleted, "runtime shutdown did not wait for MIME detection that was still running" );
            mime.Release( );
            await disposing.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 0, mime.Running );
        }
        finally
        {
            mime.Release( );
            if ( runtime is not null )
            {
                await runtime.DisposeAsync( );
            }
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static CommandDeckOptions SlowMimeOptions( string root ) => new( )
    {
        Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
        Ai = new AiOptions { Enabled = false, Providers = [] }
    };

    private static SearchRequest ReportSearch( ) => new( ) { Query = "report", Limit = 10 };

    // Row ids and action ids in order. Open With actions are left out of the
    // complete rows so they compare with rows that never received them.
    private static string DescribeRows( IReadOnlyList<SearchResult> rows, bool withOpenWith ) => string.Join(
        " | ",
        rows.Select( row => $"{row.Id}: " + string.Join( ",", row.Actions
            .Select( static action => action.Id )
            .Where( id => withOpenWith || !id.StartsWith( "open-with:", StringComparison.Ordinal ) ) ) ) );

    private static async Task<CommandDeckRuntime> SlowMimeRuntimeAsync(
        CommandDeckOptions options,
        IMimeTypeResolver mime,
        RuntimeConstructionOptions? constructionOptions = null )
    {
        SqliteCatalog catalog = new( options.Storage, mimeTypeResolver: mime );
        await catalog.InitializeAsync( default );
        await catalog.ReplaceAllAsync( ReportViewerItems( options.Storage.DatabasePath ), null, default );
        SearchCoordinator search = new(
            [new ApplicationSearchProvider( catalog ), new CatalogSearchProvider( catalog, catalog )],
            options.Search,
            catalog,
            catalog.LauncherPreferences,
            catalog.UsageHistory );
        return InvocationFixture( options, catalog, catalog.UsageHistory, new AcceptanceExecutor( ), search, constructionOptions );
    }

    private static async IAsyncEnumerable<IndexedItem> ReportViewerItems( string databasePath )
    {
        yield return new IndexedItem
        {
            StableId = "app:report-viewer",
            Kind = ResultKinds.Application,
            Title = "Report Viewer",
            PayloadJson = PayloadWriter.Create(
                ("desktopFile", "/tmp/viewer.desktop"),
                ("desktopId", "viewer.desktop"),
                ("exec", "viewer %U"),
                ("mimeTypes", "text/plain"),
                ("terminal", "false") ),
            ProviderId = ProviderIds.Applications,
            SourceId = SourceIds.Applications
        };
        yield return new IndexedItem
        {
            StableId = "file:report",
            Kind = ResultKinds.File,
            Title = "report.txt",
            Path = Path.Combine( Path.GetDirectoryName( databasePath )!, "report.txt" ),
            ProviderId = ProviderIds.Files,
            SourceId = SourceIds.Files
        };
        await Task.CompletedTask;
    }

    // The MIME cache used to drop every entry once it overflowed, so the rows a
    // user keeps seeing paid for `file` again. It now evicts the least recent path.
    private static async Task MimeTypeCacheEvictsLeastRecentlyUsedPathAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-mime-cache-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var log = Path.Combine( root, "file-arguments.txt" );
            var mimeExecutable = Path.Combine( root, "file" );
            await File.WriteAllTextAsync( mimeExecutable, $"""
                #!/bin/sh
                while [ "$#" -gt 0 ]; do
                  [ "$1" = "--" ] && shift && break
                  shift
                done
                for path in "$@"; do
                  printf '%s\n' "$path" >> '{log}'
                  printf '%s\n' 'text/plain'
                done
                """.ReplaceLineEndings( "\n" ) );
            File.SetUnixFileMode(
                mimeExecutable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", root + Path.PathSeparator + originalPath );
            var first = Path.Combine( root, "first.txt" );
            var second = Path.Combine( root, "second.txt" );
            var third = Path.Combine( root, "third.txt" );
            foreach ( var path in new[] { first, second, third } )
            {
                await File.WriteAllTextAsync( path, "text" );
            }

            SharedMimeTypeResolver resolver = new( capacity: 2 );
            foreach ( var path in new[] { first, second, first, third, first, second } )
            {
                var resolved = await resolver.ResolveManyAsync( [path], default );
                Equal<string?>( "text/plain", resolved[path] );
            }

            // The repeated first path stays cached; the second is the least recent
            // entry when the third arrives, so only it is detected again.
            string[] detected = await File.ReadAllLinesAsync( log );
            string[] expected = [first, second, third, second];
            True( expected.SequenceEqual( detected ),
                $"file ran for: {string.Join( ", ", detected.Select( Path.GetFileName ) )}" );
            Equal( 2, resolver.CachedPathCount );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> OpenWithReportItems( string root, int count )
    {
        yield return new IndexedItem
        {
            StableId = "app:viewer.desktop",
            Kind = ResultKinds.Application,
            Title = "Text Viewer",
            PayloadJson = PayloadWriter.Create(
                ("desktopFile", "/tmp/viewer.desktop"),
                ("desktopId", "viewer.desktop"),
                ("exec", "viewer %U"),
                ("mimeTypes", "text/plain"),
                ("terminal", "false") ),
            ProviderId = ProviderIds.Applications,
            SourceId = SourceIds.Applications
        };
        for ( var index = 0; index < count; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"file:report-{index}",
                Kind = ResultKinds.File,
                Title = $"report {index}.txt",
                Path = Path.Combine( root, "reports", $"report {index}.txt" ),
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files
            };
        }
        await Task.CompletedTask;
    }

    private sealed class RecordingMimeTypeResolver( string mimeType ) : IMimeTypeResolver
    {
        private readonly object _gate = new( );
        private readonly HashSet<string> _requested = new( StringComparer.Ordinal );

        public HashSet<string> RequestedPaths
        {
            get
            {
                lock ( _gate )
                {
                    return [.. _requested];
                }
            }
        }

        public ValueTask<IReadOnlyDictionary<string, string?>> ResolveManyAsync(
            IReadOnlyList<string> paths,
            CancellationToken cancellationToken )
        {
            lock ( _gate )
            {
                _requested.UnionWith( paths );
            }

            return ValueTask.FromResult<IReadOnlyDictionary<string, string?>>(
                paths.Distinct( StringComparer.Ordinal ).ToDictionary(
                    static path => path,
                    _ => ( string? ) mimeType,
                    StringComparer.Ordinal ) );
        }
    }

    // Answers at once until stalled. A stalled call waits for cancellation, like a
    // `file` process that is killed, or, when it ignores cancellation, for Release,
    // like a stat on a mount that stopped answering.
    private sealed class SlowMimeTypeResolver( bool honoursCancellation ) : IMimeTypeResolver
    {
        private readonly TaskCompletionSource _started = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private readonly TaskCompletionSource _cancelled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private readonly TaskCompletionSource _released = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private volatile bool _stalled;
        private int _calls;
        private int _running;

        public Task Started => _started.Task;

        public Task Cancelled => _cancelled.Task;

        public int Calls => Volatile.Read( ref _calls );

        public int Running => Volatile.Read( ref _running );

        public void Stall( ) => _stalled = true;

        public void Release( ) => _released.TrySetResult( );

        public async ValueTask<IReadOnlyDictionary<string, string?>> ResolveManyAsync(
            IReadOnlyList<string> paths,
            CancellationToken cancellationToken )
        {
            Interlocked.Increment( ref _calls );
            Interlocked.Increment( ref _running );
            try
            {
                if ( _stalled )
                {
                    _started.TrySetResult( );
                }

                if ( _stalled && honoursCancellation )
                {
                    await Task.Delay( Timeout.InfiniteTimeSpan, cancellationToken ).ConfigureAwait( false );
                }
                else if ( _stalled )
                {
                    await _released.Task.ConfigureAwait( false );
                }
            }
            catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
            {
                _cancelled.TrySetResult( );
                throw;
            }
            finally
            {
                Interlocked.Decrement( ref _running );
            }

            return paths.Distinct( StringComparer.Ordinal ).ToDictionary(
                static path => path,
                static _ => ( string? ) "text/plain",
                StringComparer.Ordinal );
        }
    }
}
