namespace CommandDeck.Specs;

public static partial class Program
{
    // Home used to wait for its catalog rows, then the Hyprland section, then
    // Herdr, so the live sections' latencies added up. Each fake live provider
    // here answers only once the other one has started: run one after the other,
    // the first waits until its deadline and Home reports it incomplete.
    private static async Task HomeRunsLiveSectionsConcurrentlyAsync( )
    {
        var directory = Directory.CreateTempSubdirectory( "commanddeck-home-concurrency-" ).FullName;
        SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( directory, "catalog.db" ) } );
        SearchCoordinator? search = null;
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
                    EnabledOnHome = source.SourceId is SourceIds.WindowsWorkspaces or SourceIds.Herdr
                } ).ToList( )
            }, default );
            RendezvousProvider windows = new( ProviderIds.Hyprland, "window:rendezvous", SourceIds.WindowsWorkspaces );
            RendezvousProvider herdr = new( ProviderIds.Herdr, "herdr:rendezvous", SourceIds.Herdr );
            windows.Partner = herdr;
            herdr.Partner = windows;
            search = new SearchCoordinator(
                [windows, herdr],
                new SearchOptions { ProviderTimeoutMilliseconds = 1_000 },
                catalog,
                catalog );

            var outcome = await search.SearchWithOutcomeAsync( new SearchRequest( ), SearchPurpose.Interactive, default );

            Equal( 0, outcome.IncompleteProviderCount );
            Equal( "window:rendezvous@windows-workspaces | herdr:rendezvous@herdr", DescribeOrder( outcome.Results ) );
        }
        finally
        {
            if ( search is not null )
            {
                await search.DisposeAsync( );
            }
            await catalog.DisposeAsync( );
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools( );
            Directory.Delete( directory, recursive: true );
        }
    }

    private sealed class RendezvousProvider( string id, string resultId, string sourceId ) : ICommandProvider
    {
        public TaskCompletionSource Started { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public RendezvousProvider? Partner
        {
            get; set;
        }

        public string Id => id;

        public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            var partner = Partner;
            if ( partner is null )
            {
                throw new InvalidOperationException( "The rendezvous provider has no partner." );
            }

            Started.TrySetResult( );
            await partner.Started.Task.WaitAsync( cancellationToken );
            return
            [
                new SearchResult
                {
                    Id = resultId,
                    Title = resultId,
                    Kind = ResultKinds.Window,
                    ProviderId = id,
                    SourceId = sourceId,
                    Actions = [new CommandAction { Id = "focus", Title = "Focus", Kind = ActionKinds.HyprlandDispatch }]
                }
            ];
        }
    }
}
