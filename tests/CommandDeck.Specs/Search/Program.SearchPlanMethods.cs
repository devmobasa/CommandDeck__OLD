namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SearchPlanFreezesCatalogSourcesAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-search-plan-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( SearchPlanItems( ), null, default );
            await SetSearchPlanSourceAsync( catalog, SourceIds.Applications );

            DeferredCatalogProvider provider = new( new CatalogSearchProvider( catalog, catalog ) );
            await using SearchCoordinator search = new( [provider],
                new SearchOptions { ProviderTimeoutMilliseconds = 5000 }, catalog, catalog );
            try
            {
                var pending = search.SearchWithOutcomeAsync(
                    new SearchRequest { Query = "SnapshotProbe" }, SearchPurpose.Interactive, default ).AsTask( );
                await provider.Started.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
                await SetSearchPlanSourceAsync( catalog, SourceIds.Files );
                provider.Release.TrySetResult( );

                var outcome = await pending.WaitAsync( TimeSpan.FromSeconds( 2 ) );
                True( outcome.IsComplete );
                True( outcome.Results.Any( row => row.Id == "app:snapshot-probe" ),
                    "The application admitted by the search plan disappeared after preferences changed." );
                Equal( "app:snapshot-probe", outcome.Results.Single( ).Id );

                var next = await search.SearchWithOutcomeAsync(
                    new SearchRequest { Query = "SnapshotProbe" }, SearchPurpose.Interactive, default );
                True( next.IsComplete );
                Equal( "file:snapshot-probe", next.Results.Single( ).Id );
            }
            finally
            {
                provider.Release.TrySetResult( );
            }
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task CatalogSourceSnapshotDistinguishesOmittedAndEmptyAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-source-snapshot-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( SearchPlanItems( ), null, default );
            await SetSearchPlanSourceAsync( catalog, SourceIds.Files );
            CatalogSearchProvider provider = new( catalog, catalog );

            var omitted = await provider.SearchAsync( QueryParser.Parse( "SnapshotProbe" ),
                new SearchRequest { Query = "SnapshotProbe" }, default );
            Equal( "file:snapshot-probe", omitted.Single( ).Id );

            foreach ( var query in new[] { "SnapshotProbe", "SnaphotProbe" } )
            {
                var frozen = await provider.SearchAsync( QueryParser.Parse( query ),
                    new SearchRequest { Query = query, EnabledSearchSources = [SourceIds.Applications] }, default );
                Equal( "app:snapshot-probe", frozen.Single( ).Id );
            }

            foreach ( var query in new[] { "SnapshotProbe", "app: SnapshotProbe", "app:" } )
            {
                var empty = await provider.SearchAsync( QueryParser.Parse( query ),
                    new SearchRequest { Query = query, EnabledSearchSources = [] }, default );
                Equal( 0, empty.Count );
            }

            var unrestricted = await new CatalogSearchProvider( catalog ).SearchAsync(
                QueryParser.Parse( "SnapshotProbe" ), new SearchRequest { Query = "SnapshotProbe" }, default );
            Equal( 2, unrestricted.Count );

            ApplicationSearchProvider applications = new( catalog );
            var directApplications = await applications.SearchAsync( QueryParser.Parse( "SnapshotProbe" ),
                new SearchRequest { Query = "SnapshotProbe" }, default );
            Equal( "app:snapshot-probe", directApplications.Single( ).Id );
            var emptyApplications = await applications.SearchAsync( QueryParser.Parse( "SnapshotProbe" ),
                new SearchRequest { Query = "SnapshotProbe", EnabledSearchSources = [] }, default );
            Equal( 0, emptyApplications.Count );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task SetSearchPlanSourceAsync( SqliteCatalog catalog, string enabledSource )
    {
        var preferences = await catalog.GetPreferencesAsync( default );
        await catalog.PatchSourcePreferencesAsync( new SourcePreferencePatch
        {
            RegistryRevision = preferences.RegistryRevision,
            PreferenceRevision = preferences.PreferenceRevision,
            Changes = preferences.Sources.Select( source => new SourcePreferenceChange
            {
                SourceId = source.SourceId,
                EnabledInSearch = source.SourceId == enabledSource
            } ).ToList( )
        }, default );
    }

    private static IAsyncEnumerable<IndexedItem> SearchPlanItems( ) => AsAsync(
        [
            new( )
            {
                StableId = "app:snapshot-probe",
                Kind = ResultKinds.Application,
                Title = "SnapshotProbe",
                ProviderId = ProviderIds.Catalog,
                SourceId = SourceIds.Applications
            },
            new( )
            {
                StableId = "file:snapshot-probe",
                Kind = ResultKinds.File,
                Title = "SnapshotProbe",
                ProviderId = ProviderIds.Catalog,
                SourceId = SourceIds.Files
            }
        ] );

    private sealed class DeferredCatalogProvider( ICommandProvider inner ) : ICommandProvider
    {
        public string Id => inner.Id;
        public TaskCompletionSource Started { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            Started.TrySetResult( );
            await Release.Task.WaitAsync( cancellationToken );

            return await inner.SearchAsync( query, request, cancellationToken );
        }
    }
}
