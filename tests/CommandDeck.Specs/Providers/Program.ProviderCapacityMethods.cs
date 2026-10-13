namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ProviderCapacityRetainsLateWorkAsync( )
    {
        CapacityProvider blocked = new( );
        CountingSearchProvider fast = new( "fast" );
        SearchCoordinator search = new( [blocked, fast], new SearchOptions { ProviderTimeoutMilliseconds = 30 } );
        try
        {
            for ( var index = 0; index < 20; index++ )
            {
                var outcome = await search.SearchWithOutcomeAsync(
                    new SearchRequest { Query = "capacity" }, SearchPurpose.Interactive, default );
                True( outcome.Results.All( result => result.Id != "late" ) );
                Equal( 1, outcome.IncompleteProviderCount );
            }

            Equal( 2, blocked.Calls );
            Equal( 20, fast.Calls );
            var drain = search.DisposeAsync( ).AsTask( );
            True( !drain.IsCompleted, "Shutdown must retain dependencies while a late provider is still running." );
            blocked.Release.TrySetResult( );
            await drain.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        }
        finally
        {
            blocked.Release.TrySetResult( );
        }

        await blocked.Finished.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
    }

    private static async Task SearchOutcomeDistinguishesFailedProviderAsync( )
    {
        SearchCoordinator search = new( [new FailingSearchProvider( )], new SearchOptions( ) );
        try
        {
            var outcome = await search.SearchWithOutcomeAsync(
                new SearchRequest { Query = "ordinary" }, SearchPurpose.Interactive, default );

            Equal( 0, outcome.Results.Count );
            True( !outcome.IsComplete && outcome.IncompleteProviderCount == 1,
                "an empty failed search must report incomplete execution" );
        }
        finally
        {
            await search.DisposeAsync( );
        }
    }

    private static async Task SearchPlanFreezesFolderRequestAsync( )
    {
        DeferredFolderRequestProvider provider = new( );
        SearchCoordinator search = new( [provider],
            new SearchOptions { ProviderTimeoutMilliseconds = 5000 } );
        try
        {
            SearchRequest request = new( )
            {
                Query = "browse:note",
                SearchGeneration = 1
            };
            var pending = search.SearchWithOutcomeAsync( request, SearchPurpose.Interactive, default ).AsTask( );
            await provider.Started.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
            request.SearchGeneration = 2;
            provider.Release.TrySetResult( );
            await pending.WaitAsync( TimeSpan.FromSeconds( 2 ) );

            Equal( 1L, provider.ObservedGeneration );
        }
        finally
        {
            provider.Release.TrySetResult( );
            await search.DisposeAsync( );
        }
    }

    private sealed class DeferredFolderRequestProvider : ICommandProvider
    {
        public string Id => "folders";
        public long ObservedGeneration { get; private set; }
        public TaskCompletionSource Started { get; } =
            new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Release { get; } =
            new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            Started.TrySetResult( );
            await Release.Task.WaitAsync( cancellationToken );
            ObservedGeneration = request.SearchGeneration;
            return [];
        }
    }

    private sealed class FailingSearchProvider : ICommandProvider
    {
        public string Id => "failure-fixture";

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken ) => throw new IOException( "private synthetic error" );
    }

    private static async Task HomeProviderDeadlineRetainsCapacityAsync( )
    {
        await AssertHomeProviderDeadlineAsync( "hyprland", SourceIds.WindowsWorkspaces );
        await AssertHomeProviderDeadlineAsync( "herdr", SourceIds.Herdr );
    }

    private static async Task AssertHomeProviderDeadlineAsync( string providerId, string sourceId )
    {
        var directory = Directory.CreateTempSubdirectory( "commanddeck-home-deadline-" ).FullName;
        CapacityProvider blocked = new( providerId );
        SearchCoordinator? search = null;
        SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( directory, "catalog.db" ) } );
        Task<IReadOnlyList<SearchResult>>? pending = null;
        try
        {
            await catalog.InitializeAsync( default );
            var preferences = await catalog.GetPreferencesAsync( default );
            await catalog.PatchSourcePreferencesAsync( new SourcePreferencePatch
            {
                RegistryRevision = preferences.RegistryRevision,
                PreferenceRevision = preferences.PreferenceRevision,
                Changes = preferences.Sources.Select( source => new SourcePreferenceChange
                {
                    SourceId = source.SourceId,
                    EnabledOnHome = source.SourceId == sourceId
                } ).ToList( )
            }, default );
            List<SearchProviderDiagnostic> diagnostics = [];
            search = new SearchCoordinator( [blocked], new SearchOptions { ProviderTimeoutMilliseconds = 100 }, catalog, catalog,
                providerDiagnostic: diagnostic => { lock ( diagnostics ) { diagnostics.Add( diagnostic ); } } );
            search.HomeSupplement = _ => ValueTask.FromResult<IReadOnlyList<SearchResult>>( [CacheResult( "home-survivor" )] );

            using CancellationTokenSource cancellation = new( );
            pending = search.SearchAsync( new SearchRequest( ), cancellation.Token ).AsTask( );
            await blocked.Started.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
            await cancellation.CancelAsync( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await pending.WaitAsync( TimeSpan.FromSeconds( 2 ) ) );
            lock ( diagnostics )
            {
                Equal( SearchProviderOutcomes.Cancelled, diagnostics.Single( ).Outcome );
            }
            pending = null;

            for ( var index = 0; index < 2; index++ )
            {
                var outcome = await search.SearchWithOutcomeAsync(
                    new SearchRequest( ), SearchPurpose.Interactive, default )
                    .AsTask( ).WaitAsync( TimeSpan.FromSeconds( 2 ) );
                var rows = outcome.Results;
                True( rows.Any( row => row.Id == "home-survivor" ) );
                True( rows.All( row => row.Id != "late" ) );
                Equal( 1, outcome.IncompleteProviderCount );
            }
            Equal( 2, blocked.Calls );
            lock ( diagnostics )
            {
                Equal( 3, diagnostics.Count );
                Equal( SearchProviderOutcomes.TimedOut, diagnostics[1].Outcome );
                Equal( SearchProviderOutcomes.Saturated, diagnostics[2].Outcome );
            }

            var drain = search.DisposeAsync( ).AsTask( );
            True( !drain.IsCompleted );
            blocked.Release.TrySetResult( );
            await drain.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        }
        finally
        {
            blocked.Release.TrySetResult( );
            if ( search is not null )
            {
                await search.DisposeAsync( );
            }
            if ( pending is not null )
            {
                try
                {
                    await pending;
                }
                catch ( OperationCanceledException ) { }
            }
            await catalog.DisposeAsync( );
            Directory.Delete( directory, recursive: true );
        }
    }

    private sealed class CapacityProvider( string id = "capacity" ) : ICommandProvider
    {
        private int _calls;
        private int _finished;
        public int Calls => Volatile.Read( ref _calls );
        public string Id => id;
        public TaskCompletionSource Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Started { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Finished { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
        {
            Interlocked.Increment( ref _calls );
            Started.TrySetResult( );
            await Release.Task;
            if ( Interlocked.Increment( ref _finished ) == 2 )
            {
                Finished.TrySetResult( );
            }

            return [CacheResult( "late" )];
        }
    }
}
