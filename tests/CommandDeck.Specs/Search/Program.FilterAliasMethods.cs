namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FilterAliasesPersistResolveAndResetAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-filter-alias-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using ( SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } ) )
            {
                await catalog.InitializeAsync( CancellationToken.None );
                await catalog.ReplaceAllAsync( FilterAliasItems( ), null, CancellationToken.None );
                var saved = await catalog.SetFilterAliasAsync(
                    "Docs:",
                    "files",
                    CancellationToken.None );
                Equal( "docs", saved.Single( ).Alias );
                Equal( "files", saved.Single( ).FilterId );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await catalog.SetFilterAliasAsync( "app", "files", CancellationToken.None ) );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await catalog.SetFilterAliasAsync( "home", "files", CancellationToken.None ) );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await catalog.SetFilterAliasAsync( "docs", "applications", CancellationToken.None ) );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await catalog.SetFilterAliasAsync( "other", "missing", CancellationToken.None ) );
                for ( var index = 1; index < FilterAliasValidator.MaximumAliases; index++ )
                {
                    await catalog.SetFilterAliasAsync(
                        $"alias-{index}",
                        "files",
                        CancellationToken.None );
                }
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await catalog.SetFilterAliasAsync(
                        "alias-overflow",
                        "files",
                        CancellationToken.None ) );
            }

            await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = database } );
            await reopened.InitializeAsync( CancellationToken.None );
            var reopenedAliases = await reopened.GetFilterAliasesAsync(
                CancellationToken.None );
            Equal( FilterAliasValidator.MaximumAliases, reopenedAliases.Count );
            Equal( "files", reopenedAliases.Single( value => value.Alias == "docs" ).FilterId );
            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( reopened, reopened )],
                new SearchOptions( ),
                reopened,
                reopened );
            var results = await coordinator.SearchAsync(
                new SearchRequest { Query = "docs: roadmap", Limit = 20 },
                CancellationToken.None );
            Equal( 1, results.Count );
            Equal( ResultKinds.File, results[0].Kind );
            True( SearchFilterRegistry.MergeUserAliases(
                reopenedAliases )
                .Single( value => value.Id == "files" )
                .Aliases.Contains( "docs:", StringComparer.Ordinal ) );
            Equal(
                FilterAliasValidator.MaximumAliases,
                (await reopened.ExportAsync( CancellationToken.None )).FilterAliases.Count );

            await reopened.ResetAsync( CancellationToken.None );
            Equal( 0, (await reopened.GetFilterAliasesAsync( CancellationToken.None )).Count );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> FilterAliasItems( )
    {
        yield return new IndexedItem
        {
            StableId = "file:roadmap",
            Kind = ResultKinds.File,
            Title = "Roadmap",
            Path = "/docs/roadmap.md",
            ProviderId = "files",
            SourceId = SourceIds.Files
        };
        yield return new IndexedItem
        {
            StableId = "app:roadmap",
            Kind = ResultKinds.Application,
            Title = "Roadmap",
            ProviderId = "applications",
            SourceId = SourceIds.Applications
        };
        await Task.CompletedTask;
    }

    private static async Task VerifyFilterAliasProtocolAsync(
        UnixProtocolClient client,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 190,
            Operation = ProtocolOperations.FilterAliasSet,
            Alias = "docs",
            TargetFilterId = "files"
        }, CancellationToken.None );
        var saved = await ReadMessageAsync(
            client,
            190,
            ProtocolMessageTypes.FilterAliasResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( saved?.FilterAliases );
        Equal( "docs", saved.FilterAliases.Aliases.Single( ).Alias );
        True( saved.FilterAliases.Filters.Single( value => value.Id == "files" )
            .Aliases.Contains( "docs:", StringComparer.Ordinal ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 191,
            Operation = ProtocolOperations.Bootstrap
        }, CancellationToken.None );
        var bootstrap = await ReadMessageAsync(
            client,
            191,
            ProtocolMessageTypes.BootstrapResult,
            cancellationToken );
        Equal( "docs", bootstrap?.Bootstrap?.FilterAliases.Single( ).Alias );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 192,
            Operation = ProtocolOperations.FilterAliasSet,
            Alias = "app",
            TargetFilterId = "files"
        }, CancellationToken.None );
        var conflict = await ReadMessageAsync(
            client,
            192,
            ProtocolMessageTypes.Error,
            cancellationToken );
        True( conflict?.Error?.Contains( "conflicts", StringComparison.Ordinal ) == true );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 193,
            Operation = ProtocolOperations.FilterAliasSet,
            Alias = "docs",
            TargetFilterId = null
        }, CancellationToken.None );
        var removed = await ReadMessageAsync(
            client,
            193,
            ProtocolMessageTypes.FilterAliasResult,
            cancellationToken );
        Equal( 0, removed?.FilterAliases?.Aliases.Count ?? -1 );
    }
}
