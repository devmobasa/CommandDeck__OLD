namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task VerifyIndexControlProtocolAsync(
        UnixProtocolClient client,
        string rootId,
        string filePath,
        CancellationToken cancellationToken )
    {
        var paused = await SendAndReadStatusAsync(
            client,
            new ProtocolRequest
            {
                RequestId = 391,
                Operation = ProtocolOperations.IndexPause,
                RootId = rootId
            },
            cancellationToken );
        True( paused.RuntimeStatus!.Indexing.Roots.Single( root => root.Id == rootId ).Paused );

        var resumed = await SendAndReadStatusAsync(
            client,
            new ProtocolRequest
            {
                RequestId = 392,
                Operation = ProtocolOperations.IndexResume,
                RootId = rootId
            },
            cancellationToken );
        True( !resumed.RuntimeStatus!.Indexing.Roots.Single( root => root.Id == rootId ).Paused );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 393,
            Operation = ProtocolOperations.Reindex,
            Scope = IndexReindexScopeKinds.File,
            Path = filePath
        }, cancellationToken );
        ReindexReport? report = null;
        var done = false;
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 5 ) );
        await foreach ( var line in client.ReadRawMessagesAsync( timeout.Token ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId != 393 )
            {
                continue;
            }

            report ??= message.ReindexReport;
            if ( message.Type == ProtocolMessageTypes.Done )
            {
                done = true;
                break;
            }
        }

        True( done );
        Equal( IndexReindexScopeKinds.File, report?.Scope );
    }

    private static async Task<ProtocolMessage> SendAndReadStatusAsync(
        UnixProtocolClient client,
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( request, cancellationToken );
        ProtocolMessage? status = null;
        var done = false;
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 5 ) );
        await foreach ( var line in client.ReadRawMessagesAsync( timeout.Token ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId != request.RequestId )
            {
                continue;
            }

            status ??= message.Type == ProtocolMessageTypes.Status ? message : null;
            if ( message.Type == ProtocolMessageTypes.Done )
            {
                done = true;
                break;
            }
        }

        True( done );
        if ( status is null )
        {
            throw new InvalidOperationException( "Expected an index status response." );
        }

        return status;
    }

    private static async Task PerRootStatusPauseAndResumeConvergeAsync( )
    {
        var temporary = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-root-operations-{Guid.NewGuid( ):N}" );
        var firstRoot = Path.Combine( temporary, "first" );
        var secondRoot = Path.Combine( temporary, "second" );
        Directory.CreateDirectory( firstRoot );
        Directory.CreateDirectory( secondRoot );
        var firstFile = Path.Combine( firstRoot, "first.txt" );
        var secondFile = Path.Combine( secondRoot, "second.txt" );
        await File.WriteAllTextAsync( firstFile, "first-old" );
        await File.WriteAllTextAsync( secondFile, "second-old" );

        var options = CreateIndexOperationsOptions(
            temporary,
            firstRoot,
            secondRoot );
        SqliteCatalog catalog = new( options.Storage );
        await catalog.InitializeAsync( CancellationToken.None );
        await using IndexCoordinator indexer = new( catalog, catalog, options );

        try
        {
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            var initial = await indexer.GetIncrementalStatusAsync(
                CancellationToken.None );
            Equal( 2, initial.Roots.Count );
            True( initial.Roots.All( static root => root.LastSuccessAt is not null ) );
            True( initial.Roots.All( static root => root.IndexedItemCount >= 1 ) );

            var first = initial.Roots.Single( root =>
                string.Equals( root.Path, Path.GetFullPath( firstRoot ), StringComparison.Ordinal ) );
            await indexer.PauseAsync( first.Id, CancellationToken.None );
            await File.WriteAllTextAsync( firstFile, "first-paused" );
            await File.WriteAllTextAsync( secondFile, "second-active" );

            await WaitUntilAsync( async ( ) => (await catalog.SearchAsync(
                QueryParser.Parse( "second-active" ),
                10,
                CancellationToken.None )).Count == 1,
                TimeSpan.FromSeconds( 3 ) );
            Equal( 0, (await catalog.SearchAsync(
                QueryParser.Parse( "first-paused" ),
                10,
                CancellationToken.None )).Count );

            var paused = await indexer.GetIncrementalStatusAsync(
                CancellationToken.None );
            var pausedRoot = paused.Roots.Single( root => root.Id == first.Id );
            True( pausedRoot.Paused );
            True( pausedRoot.PendingEventCount >= 1 );

            await indexer.ResumeAsync( first.Id, CancellationToken.None );
            await WaitUntilAsync( async ( ) => (await catalog.SearchAsync(
                QueryParser.Parse( "first-paused" ),
                10,
                CancellationToken.None )).Count == 1,
                TimeSpan.FromSeconds( 3 ) );
            var resumed = (await indexer.GetIncrementalStatusAsync(
                CancellationToken.None )).Roots.Single( root => root.Id == first.Id );
            True( !resumed.Paused );
            Equal( 0L, resumed.PendingEventCount );
        }
        finally
        {
            await indexer.DisposeAsync( );
            await catalog.DisposeAsync( );
            Directory.Delete( temporary, recursive: true );
        }
    }

    private static async Task ScopedReindexPreservesUnrelatedRowsAndPersonalizationAsync( )
    {
        var temporary = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-scoped-reindex-{Guid.NewGuid( ):N}" );
        var firstRoot = Path.Combine( temporary, "first" );
        var secondRoot = Path.Combine( temporary, "second" );
        Directory.CreateDirectory( firstRoot );
        Directory.CreateDirectory( secondRoot );
        var firstFile = Path.Combine( firstRoot, "first.txt" );
        var secondFile = Path.Combine( secondRoot, "second.txt" );
        await File.WriteAllTextAsync( firstFile, "first-before" );
        await File.WriteAllTextAsync( secondFile, "second-before" );
        var options = CreateIndexOperationsOptions(
            temporary,
            false,
            firstRoot,
            secondRoot );
        SqliteCatalog catalog = new( options.Storage );
        await catalog.InitializeAsync( CancellationToken.None );
        await using IndexCoordinator indexer = new( catalog, catalog, options );

        try
        {
            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            var favorite = (await catalog.SearchAsync(
                QueryParser.Parse( "first-before" ),
                10,
                CancellationToken.None )).Single( );
            await catalog.SetFavoriteAsync( favorite, enabled: true, CancellationToken.None );
            await File.WriteAllTextAsync( firstFile, "first-after" );
            await File.WriteAllTextAsync( secondFile, "second-after" );

            var fileReport = await indexer.ReindexAsync(
                new IndexReindexScope { Kind = IndexReindexScopeKinds.File, Path = firstFile },
                progress: null,
                CancellationToken.None );
            Equal( IndexReindexScopeKinds.File, fileReport.Scope );
            var firstAfter = (await catalog.SearchAsync(
                QueryParser.Parse( "first-after" ), 10, CancellationToken.None )).Count;
            var secondBefore = (await catalog.SearchAsync(
                QueryParser.Parse( "second-before" ), 10, CancellationToken.None )).Count;
            var secondAfter = (await catalog.SearchAsync(
                QueryParser.Parse( "second-after" ), 10, CancellationToken.None )).Count;
            var favorites = (await catalog.GetSnapshotAsync(
                includeShortcuts: false,
                CancellationToken.None )).Customizations.Count( static value => value.IsFavorite );
            if ( firstAfter != 1 || secondBefore != 1 || secondAfter != 0 || favorites != 1 )
            {
                throw new InvalidOperationException(
                    $"Unexpected scoped state: firstAfter={firstAfter}, " +
                    $"secondBefore={secondBefore}, secondAfter={secondAfter}, " +
                    $"favorites={favorites}." );
            }

            using ( FileIndexSource probe = new( options.Indexing, options.Privacy ) )
            {
                var directoryItems = await probe.IndexDirectoryAsync(
                    secondRoot,
                    CancellationToken.None );
                var fileItems = await probe.IndexPathAsync(
                    secondFile,
                    CancellationToken.None );
                if ( directoryItems.Count == 0 || fileItems.Count == 0 )
                {
                    throw new InvalidOperationException(
                        $"Direct source returned directory={directoryItems.Count}, " +
                        $"file={fileItems.Count}." );
                }
            }

            var directoryReport = await indexer.ReindexAsync(
                new IndexReindexScope
                {
                    Kind = IndexReindexScopeKinds.Directory,
                    Path = secondRoot
                },
                progress: null,
                CancellationToken.None );
            var directorySearch = (await catalog.SearchAsync(
                QueryParser.Parse( "second-after" ), 10, CancellationToken.None )).Count;
            if ( directorySearch != 1 )
            {
                throw new InvalidOperationException(
                    $"Directory scope indexed {directoryReport.Indexed} items but returned " +
                    $"{directorySearch} matching rows." );
            }

            await ThrowsAsync<InvalidOperationException>( async ( ) =>
            {
                await indexer.ReindexAsync(
                    new IndexReindexScope
                    {
                        Kind = IndexReindexScopeKinds.File,
                        Path = Path.Combine( temporary, "outside.txt" )
                    },
                    progress: null,
                    CancellationToken.None );
            } );
        }
        finally
        {
            await indexer.DisposeAsync( );
            await catalog.DisposeAsync( );
            Directory.Delete( temporary, recursive: true );
        }
    }

    private static async Task EventStormReconcilesOnlyAffectedRootWithinBatchBudgetAsync( )
    {
        var temporary = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-event-storm-{Guid.NewGuid( ):N}" );
        var stormRoot = Path.Combine( temporary, "storm" );
        var quietRoot = Path.Combine( temporary, "quiet" );
        Directory.CreateDirectory( stormRoot );
        Directory.CreateDirectory( quietRoot );
        var quietFile = Path.Combine( quietRoot, "quiet.txt" );
        await File.WriteAllTextAsync( quietFile, "quiet-original" );
        var options = CreateIndexOperationsOptions(
            temporary,
            true,
            32,
            stormRoot,
            quietRoot );
        SqliteCatalog catalog = new( options.Storage );
        await catalog.InitializeAsync( CancellationToken.None );
        await using IndexCoordinator indexer = new( catalog, catalog, options );

        try
        {
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            for ( var index = 0; index < 200; index++ )
            {
                await File.WriteAllTextAsync(
                    Path.Combine( stormRoot, $"storm-{index:D3}.txt" ),
                    index == 199 ? "storm-final-marker" : $"storm-{index}" );
            }

            await WaitUntilAsync( async ( ) =>
            {
                var status = await indexer.GetIncrementalStatusAsync(
                    CancellationToken.None );
                return status.PendingEventCount == 0 &&
                    (await catalog.SearchAsync(
                        QueryParser.Parse( "storm-final-marker" ),
                        10,
                        CancellationToken.None )).Count == 1;
            }, TimeSpan.FromSeconds( 10 ) );
            var completed = await indexer.GetIncrementalStatusAsync(
                CancellationToken.None );
            True( completed.MaximumObservedBatchCount <= 33 );
            Equal( 1, completed.LastReconciledRootCount );
            Equal( 1, (await catalog.SearchAsync(
                QueryParser.Parse( "quiet-original" ), 10, CancellationToken.None )).Count );
        }
        finally
        {
            await indexer.DisposeAsync( );
            await catalog.DisposeAsync( );
            Directory.Delete( temporary, recursive: true );
        }
    }

    private static CommandDeckOptions CreateIndexOperationsOptions(
        string temporary,
        params string[] roots ) => CreateIndexOperationsOptions(
            temporary,
            true,
            roots );

    private static CommandDeckOptions CreateIndexOperationsOptions(
        string temporary,
        bool enableIncrementalIndexing,
        params string[] roots ) => CreateIndexOperationsOptions(
            temporary,
            enableIncrementalIndexing,
            2_048,
            roots );

    private static CommandDeckOptions CreateIndexOperationsOptions(
        string temporary,
        bool enableIncrementalIndexing,
        int incrementalEventBatchLimit,
        params string[] roots ) => CreateIndexOperationsOptions(
            temporary,
            enableIncrementalIndexing,
            incrementalEventBatchLimit,
            20,
            roots );

    private static CommandDeckOptions CreateIndexOperationsOptions(
        string temporary,
        bool enableIncrementalIndexing,
        int incrementalEventBatchLimit,
        int incrementalDebounceMilliseconds,
        params string[] roots ) => CreateIndexOperationsOptions(
            temporary,
            enableIncrementalIndexing,
            incrementalEventBatchLimit,
            incrementalDebounceMilliseconds,
            true,
            roots );

    private static CommandDeckOptions CreateIndexOperationsOptions(
        string temporary,
        bool enableIncrementalIndexing,
        int incrementalEventBatchLimit,
        int incrementalDebounceMilliseconds,
        bool enableScheduledMaintenance,
        params string[] roots ) => new( )
        {
            Storage = new StorageOptions
            {
                DatabasePath = Path.Combine( temporary, "catalog.db" )
            },
            Indexing = new IndexingOptions
            {
                Roots = roots.Select( static path => new IndexRootOptions
                {
                    Path = path,
                    IncludeContentInSearch = true
                } ).ToList( ),
                IndexDesktopApplications = false,
                IndexOmarchyCommands = false,
                EnableIncrementalIndexing = enableIncrementalIndexing,
                IncrementalEventBatchLimit = incrementalEventBatchLimit,
                IncrementalDebounceMilliseconds = incrementalDebounceMilliseconds,
                IncrementalMaximumLatencyMilliseconds = Math.Max(
                    incrementalDebounceMilliseconds,
                    100 ),
                EnableScheduledMaintenance = enableScheduledMaintenance,
                MaintenanceIntervalMinutes = 1
            }
        };

}
