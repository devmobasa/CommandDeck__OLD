using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FailedGenerationPreservesKnownGoodCatalogAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-generation-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        StorageOptions storage = new( )
        {
            DatabasePath = Path.Combine( root, "catalog.db" )
        };
        SqliteCatalog catalog = new( storage );
        TaskCompletionSource staged = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );

        static async IAsyncEnumerable<IndexedItem> KnownGood( )
        {
            yield return Item( "file:generation-a", "KnownGoodGeneration" );
            await Task.CompletedTask;
        }

        async IAsyncEnumerable<IndexedItem> Failing(
            [EnumeratorCancellation] CancellationToken cancellationToken = default )
        {
            yield return Item( "file:generation-b", "BrokenGeneration" );
            staged.TrySetResult( );
            await release.Task.WaitAsync( cancellationToken );
            throw new InvalidOperationException( "synthetic enumeration failure" );
        }

        try
        {
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceGenerationAsync(
                KnownGood( ),
                static ( ) =>
                [
                    new IndexExclusionDiagnostic
                    {
                        Root = "/known-good",
                        RelativePath = "excluded.txt",
                        Reason = IndexExclusionReasons.RootGlob,
                        Rule = "*.txt"
                    }
                ],
                progress: null,
                CancellationToken.None );

            var replacement = catalog.ReplaceGenerationAsync(
                Failing( ),
                static ( ) => [],
                progress: null,
                CancellationToken.None ).AsTask( );
            await staged.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );

            var concurrent = await catalog.SearchAsync(
                QueryParser.Parse( "KnownGoodGeneration" ),
                10,
                CancellationToken.None );
            Equal( "file:generation-a", concurrent.Single( ).Id );

            release.TrySetResult( );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await replacement );
            await AssertKnownGoodGenerationAsync( catalog );
            await catalog.DisposeAsync( );

            catalog = new SqliteCatalog( storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await AssertKnownGoodGenerationAsync( catalog );
        }
        finally
        {
            release.TrySetResult( );
            await catalog.DisposeAsync( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task GenerationStagingKeepsReadersAndWritersAvailableAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-generation-writers-" ).FullName;
        StorageOptions storage = new( )
        {
            DatabasePath = Path.Combine( root, "catalog.db" )
        };
        PrivacyOptions privacy = new( )
        {
            StoreQueryHistory = true
        };
        SqliteCatalog catalog = new( storage, privacy: privacy );
        TaskCompletionSource staged = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );

        async IAsyncEnumerable<IndexedItem> SlowGeneration(
            [EnumeratorCancellation] CancellationToken cancellationToken = default )
        {
            yield return Item( "file:replacement", "ReplacementGeneration" );
            staged.TrySetResult( );
            await release.Task.WaitAsync( cancellationToken );
        }

        Task? replacement = null;
        Task? preferenceWrite = null;
        Task? historyWrite = null;
        try
        {
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( OneKnownGoodItem( ), null, default );
            var preferences = await catalog.GetPreferencesAsync( default );
            replacement = catalog.ReplaceAllAsync( SlowGeneration( ), null, default ).AsTask( );
            await staged.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );

            var readStart = Stopwatch.GetTimestamp( );
            var visible = await catalog.SearchAsync(
                QueryParser.Parse( "KnownGoodGeneration" ), 10, default );
            var readWait = Stopwatch.GetElapsedTime( readStart );
            Equal( "file:known-good", visible.Single( ).Id );

            TaskCompletionSource preferenceStarted = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource historyStarted = new( TaskCreationOptions.RunContinuationsAsynchronously );
            var preferenceStart = Stopwatch.GetTimestamp( );
            preferenceWrite = Task.Factory.StartNew( ( ) =>
            {
                preferenceStarted.TrySetResult( );
                catalog.PatchSourcePreferencesAsync( new SourcePreferencePatch
                {
                    RegistryRevision = preferences.RegistryRevision,
                    PreferenceRevision = preferences.PreferenceRevision,
                    Changes = [new SourcePreferenceChange
                    {
                        SourceId = SourceIds.Applications,
                        EnabledOnHome = false
                    }]
                }, default ).AsTask( ).GetAwaiter( ).GetResult( );
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default );
            var historyStart = Stopwatch.GetTimestamp( );
            historyWrite = Task.Factory.StartNew( ( ) =>
            {
                historyStarted.TrySetResult( );
                catalog.UsageHistory.RecordInvocationAsync(
                    "app:accepted", "launch", "ordinary", default ).AsTask( ).GetAwaiter( ).GetResult( );
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default );
            await Task.WhenAll( preferenceStarted.Task, historyStarted.Task )
                .WaitAsync( TimeSpan.FromSeconds( 5 ) );
            await Task.WhenAll( preferenceWrite, historyWrite )
                .WaitAsync( TimeSpan.FromSeconds( 2 ) );
            var preferenceWait = Stopwatch.GetElapsedTime( preferenceStart );
            var historyWait = Stopwatch.GetElapsedTime( historyStart );

            var stagedRead = await catalog.SearchAsync(
                QueryParser.Parse( "KnownGoodGeneration" ), 10, default );
            Equal( "file:known-good", stagedRead.Single( ).Id );
            True( !replacement.IsCompleted,
                "generation activation still waits for extraction to finish" );

            release.TrySetResult( );
            await replacement.WaitAsync( TimeSpan.FromSeconds( 10 ) );

            True( preferenceWrite.IsCompletedSuccessfully && historyWrite.IsCompletedSuccessfully,
                "independent writes finish while extraction is paused before activation" );
            Equal( 1L, (await catalog.UsageHistory.GetQueryHistoryAsync( 10, default )).TotalEntries );
            Console.WriteLine( $"MEASURE generation staged reader={readWait.TotalMilliseconds:F0}ms " +
                $"preference-writer={preferenceWait.TotalMilliseconds:F0}ms history-writer={historyWait.TotalMilliseconds:F0}ms" );
        }
        finally
        {
            release.TrySetResult( );
            foreach ( var operation in new[] { replacement, preferenceWrite, historyWrite } )
            {
                if ( operation is null )
                {
                    continue;
                }

                try
                {
                    await operation.ConfigureAwait( false );
                }
                catch ( Exception )
                {
                    // Preserve the measurement assertion or operation failure.
                }
            }

            await catalog.DisposeAsync( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> OneKnownGoodItem( )
    {
        yield return Item( "file:known-good", "KnownGoodGeneration" );
        await Task.CompletedTask;
    }

    private static async Task AssertKnownGoodGenerationAsync( SqliteCatalog catalog )
    {
        var knownGood = await catalog.SearchAsync(
            QueryParser.Parse( "KnownGoodGeneration" ),
            10,
            CancellationToken.None );
        var failed = await catalog.SearchAsync(
            QueryParser.Parse( "BrokenGeneration" ),
            10,
            CancellationToken.None );
        var inspection = await catalog.InspectAsync( CancellationToken.None );

        Equal( "file:generation-a", knownGood.Single( ).Id );
        Equal( 0, failed.Count );
        True( !inspection.RebuildRequired );
        Equal( 1L, inspection.TotalExclusionDiagnosticCount );
        Equal( "*.txt", inspection.ExclusionDiagnostics.Single( ).Rule );
    }

    private static IndexedItem Item( string id, string title ) => new( )
    {
        StableId = id,
        Kind = ResultKinds.File,
        Title = title,
        ProviderId = "files",
        SourceId = SourceIds.Files
    };
}
