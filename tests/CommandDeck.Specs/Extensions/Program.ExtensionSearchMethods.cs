using System.Collections.Immutable;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static ExtensionManifest SearchManifest( string version = "1.0.0" ) => PackageFixture( version ) with
    {
        SearchProviders = [new( "notes", "Notes", "uuid", "search-cache", "notes" )]
    };
    private static async Task WithSearchRegistry( Func<ExtensionPackageRegistry, SqliteCatalog, ExtensionInstallation, Task> test, ExtensionManifest? manifest = null )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            using var registry = await ExtensionPackageRegistry.OpenAsync( catalog, Path.Combine( root, "artifacts" ), CancellationToken.None );
            catalog.Contributions = registry;
            await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( root, manifest ?? SearchManifest( ) ), registry.ArtifactRoot, CancellationToken.None );
            var installed = await registry.InstallAsync( staged, "local", 0, true, [], true, CancellationToken.None );
            await registry.SetEnabledAsync( null, true, registry.Document.Revision, CancellationToken.None );
            await test( registry, catalog, installed );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
    private static SearchRequest SearchInput( ExtensionSearchService service, ExtensionInstallation package ) => new( )
    {
        Query = "meeting",
        SearchOwner = "owner",
        SearchGeneration = service.Begin( "owner" ),
        EnabledSearchSources = [ExtensionStableIds.SourceId( package.Key )]
    };
    private static async Task ExtensionSearchConsentProjectionAndLeasesAsync( ) => await WithSearchRegistry( async ( registry, catalog, package ) =>
    {
        var calls = 0;
        await registry.WithExecutionAsync( package.Key, async current =>
        {
            await registry.AccessDataAsync( current, new( "s", 1, 1, "i", "write", "private", 0, JsonElement.Parse( "\"hidden\"" ) ), CancellationToken.None );
            await registry.AccessDataAsync( current, new( "s", 1, 1, "i", "write", "search-cache", 1, JsonElement.Parse( "\"cached\"" ) ), CancellationToken.None );
            return true;
        }, CancellationToken.None );
        await using ExtensionSearchService service = new( registry, new FakeExtensionSearchWorker( ( _, _, input, _, _ ) =>
        {
            calls++;
            Equal( "cached", input.Cache!.Value.GetString( ) );
            return Task.FromResult( new ExtensionSearchResponse( [new( "meeting", "Meeting notes", Context: "chosen-item" )] ) );
        } ) );
        registry.RegisterRevocation( ExtensionRevocationStage.Search, ( _, _ ) => service.RevokeAsync( ) );
        var root = QueryParser.Parse( "meeting" );
        Equal( 0, (await service.SearchAsync( root, SearchInput( service, package ), CancellationToken.None )).Count );
        Equal( 0, calls );
        var filterId = ExtensionStableIds.SearchProviderId( package.Key, "notes" );
        var scoped = QueryParser.Parse( registry.Snapshot.FindById( filterId )!.Token + " meeting", registry: registry.Snapshot );
        var request = SearchInput( service, package );
        var rows = await service.SearchAsync( scoped, request, CancellationToken.None );
        Equal( 1, calls );
        var row = rows.Single( );
        var lease = row.Metadata["extensionSearchLease"];
        True( row.Id.StartsWith( "extension-item:" + package.Key, StringComparison.Ordinal ) );
        True( !row.Metadata.ContainsKey( "catalogResultId" ) && !row.Metadata.ContainsKey( "exactAlias" ) );
        Equal( ActionKinds.ExtensionOpen, row.Actions.Single( ).Kind );
        Throws<InvalidOperationException>( ( ) => service.Consume( "foreign", row.Id, lease ) );
        service.RetainDisplayed( request, rows );
        Equal( "chosen-item", service.Consume( "owner", row.Id, lease ).Inputs["searchContext"].GetString( ) );
        Throws<InvalidOperationException>( ( ) => service.Consume( "owner", row.Id, lease ) );
        request = SearchInput( service, package );
        row = (await service.SearchAsync( scoped, request, CancellationToken.None )).Single( );
        service.RetainDisplayed( request, [] );
        Throws<InvalidOperationException>( ( ) => service.Consume( "owner", row.Id, row.Metadata["extensionSearchLease"] ) );
        request = SearchInput( service, package );
        row = (await service.SearchAsync( scoped, request, CancellationToken.None )).Single( );
        _ = service.Begin( "owner" );
        Throws<InvalidOperationException>( ( ) => service.Consume( "owner", row.Id, row.Metadata["extensionSearchLease"] ) );
        await registry.SetGlobalSearchAsync( package.Key, true, registry.Document.Revision, CancellationToken.None );
        Equal( 1, (await service.SearchAsync( root, SearchInput( service, package ), CancellationToken.None )).Count );
        var before = calls;
        Equal( 0, (await service.SearchAsync( root, new SearchRequest { SearchOwner = "owner", SearchGeneration = service.Begin( "owner" ) }, CancellationToken.None )).Count );
        Equal( before, calls );
        await catalog.SetFilterAliasAsync( "notes", filterId, CancellationToken.None );
        await ThrowsAsync<InvalidOperationException>( async ( ) => await catalog.SetFilterAliasAsync( "notes", "applications", CancellationToken.None ) );
        await ThrowsAsync<InvalidOperationException>( async ( ) => await catalog.SetFilterAliasAsync( "app", filterId, CancellationToken.None ) );
        await registry.SetEnabledAsync( package.Key, false, registry.Document.Revision, CancellationToken.None );
        Equal( 0, (await service.SearchAsync( scoped, SearchInput( service, package ), CancellationToken.None )).Count );
        Equal( before, calls );
    } );
    private static async Task ExtensionSearchCancellationAndRevocationAsync( ) => await WithSearchRegistry( async ( registry, catalog, package ) =>
    {
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource cancelled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        await using ExtensionSearchService service = new( registry, new FakeExtensionSearchWorker( async ( _, _, _, _, token ) =>
        {
            entered.TrySetResult( );
            try
            {
                await Task.Delay( Timeout.Infinite, token );
            }
            catch ( OperationCanceledException )
            {
                cancelled.TrySetResult( );
                throw;
            }
            return new( [] );
        } ) );
        registry.RegisterRevocation( ExtensionRevocationStage.Search, ( _, _ ) => service.RevokeAsync( ) );
        await registry.SetGlobalSearchAsync( package.Key, true, registry.Document.Revision, CancellationToken.None );
        var pending = service.SearchAsync( QueryParser.Parse( "meeting" ), SearchInput( service, package ), CancellationToken.None ).AsTask( );
        await entered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
        _ = service.Begin( "owner" );
        await cancelled.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
        Equal( 0, (await pending).Count );
        entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        pending = service.SearchAsync( QueryParser.Parse( "meeting" ), SearchInput( service, package ), CancellationToken.None ).AsTask( );
        await entered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
        await registry.SetGlobalSearchAsync( package.Key, false, registry.Document.Revision, CancellationToken.None ).AsTask( ).WaitAsync( TimeSpan.FromSeconds( 2 ) );
        Equal( 0, (await pending).Count );
    } );
    private static async Task ExtensionSearchSdkPurposeAndResponseBoundsAsync( )
    {
        var manifest = SearchManifest( );
        ManifestValidation.Validate( manifest );
        Throws<InvalidDataException>( ( ) => ManifestValidation.Validate( manifest with { SearchProviders = [new( "notes", "Notes", "foreign", "cache" )] } ) );
        Throws<InvalidDataException>( ( ) => ExtensionSearchValidation.Validate( new( [new( "id", "bad\nlabel" )] ) ) );
        Throws<InvalidDataException>( ( ) => ExtensionSearchValidation.Validate( new( Enumerable.Range( 0, 32 ).Select( index => new ExtensionSearchItem( "item" + index, "Title", Context: new string( '+', 2048 ) ) ).ToArray( ) ) ) );
        Throws<JsonException>( ( ) => JsonSerializer.Deserialize( "{\"items\":[{\"id\":\"item\",\"title\":\"Title\",\"score\":1000}]}", ExtensionSearchJsonContext.Default.ExtensionSearchResponse ) );
        foreach ( var purpose in new[] { "scoped-search", "global-search" } )
        {
            foreach ( var hasCache in new[] { true, false } )
            {
                await using var pipes = await ExtensionTestPipes.CreateAsync( );
                using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 5 ) );
                var command = new SearchSdkCommand( );
                var sdk = ExtensionApp.RunAsync( pipes.Child, pipes.Child, [command], lifetime.Token );
                await using var host = NoRequestsPeer( pipes.Host );
                try
                {
                    await host.CallAsync( "initialize", JsonSerializer.SerializeToElement( new InitializeRequest( new( ), "search", 1, "notes", purpose, ["search@1"], new( ) ), ExtensionJsonContext.Default.InitializeRequest ), lifetime.Token );
                    host.Notify( "initialized" );
                    var input = JsonSerializer.SerializeToElement( new ExtensionSearchRequest( "meeting", 10, hasCache ? JsonElement.Parse( "\"cached\"" ) : null ), ExtensionSearchJsonContext.Default.ExtensionSearchRequest );
                    var response = await host.CallAsync( "search.query", input, lifetime.Token );
                    Equal( "meeting", response.Deserialize( ExtensionSearchJsonContext.Default.ExtensionSearchResponse )!.Items.Single( ).Title );
                    Equal( purpose == "global-search", command.Global );
                    Equal( 0, command.Interactive );
                    Equal( hasCache ? "cached" : null, command.Cache );
                    await ThrowsAsync<ExtensionRpcException>( async ( ) => await host.CallAsync( "search.query", input, lifetime.Token ) );
                    await ThrowsAsync<ExtensionRpcException>( async ( ) => await host.CallAsync( "command.open", JsonSerializer.SerializeToElement( new CommandOpenRequest( 1, "open", [] ), ExtensionViewJsonContext.Default.CommandOpenRequest ), lifetime.Token ) );
                }
                finally
                {
                    lifetime.Cancel( );
                    try
                    {
                        await sdk;
                    }
                    catch ( OperationCanceledException ) { }
                }
            }
        }
    }
    private static async Task ExtensionSearchAdmissionRotatesAsync( ) => await WithSearchRegistry( async ( registry, catalog, package ) =>
    {
        var manifest = SearchManifest( "1.1.0" ) with
        {
            SearchProviders = Enumerable.Range( 0, 4 ).Select( index => new ExtensionProtocol.ExtensionSearchProvider( "provider" + index, "Provider " + index, "uuid", "cache" ) ).ToImmutableArray( )
        };
        await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( Path.GetDirectoryName( registry.ArtifactRoot )!, manifest ), registry.ArtifactRoot, CancellationToken.None );
        await registry.InstallAsync( staged, "local", registry.Document.Revision, true, [], true, CancellationToken.None );
        await registry.SetGlobalSearchAsync( package.Key, true, registry.Document.Revision, CancellationToken.None );
        HashSet<string> admitted = [];
        await using ExtensionSearchService service = new( registry, new FakeExtensionSearchWorker( ( _, provider, _, _, _ ) =>
        {
            admitted.Add( provider.Id );
            return Task.FromResult( new ExtensionSearchResponse( [new( "item", "Meeting" )] ) );
        } ) );
        for ( var round = 0; round < 4; round++ )
        {
            Equal( 2, (await service.SearchAsync( QueryParser.Parse( "meeting" ), SearchInput( service, package ), CancellationToken.None )).Count );
        }
        Equal( 4, admitted.Count );
        var invalidManifest = manifest with
        {
            Version = "1.2.0",
            SearchProviders = [new( "notes", "Notes", "uuid", "cache" )]
        };
        await using var update = await ExtensionArchive.StageAsync( WritePackageFixture( Path.GetDirectoryName( registry.ArtifactRoot )!, invalidManifest ), registry.ArtifactRoot, CancellationToken.None );
        await registry.InstallAsync( update, "local", registry.Document.Revision, true, [], true, CancellationToken.None );
        True( !registry.Find( package.Key ).GlobalSearchEnabled );
    } );
    private static async Task ExtensionSearchReplacementWaitsForCleanupAsync( ) => await WithSearchRegistry( async ( registry, catalog, package ) =>
    {
        TaskCompletionSource occupied = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource cleanup = new( TaskCreationOptions.RunContinuationsAsynchronously );
        var active = 0;
        await using ExtensionSearchService service = new( registry, new FakeExtensionSearchWorker( async ( _, _, input, _, token ) =>
        {
            if ( input.Query == "latest" )
            {
                return new( [new( "latest", "Latest" )] );
            }
            if ( Interlocked.Increment( ref active ) == 2 )
            {
                occupied.SetResult( );
            }
            try
            {
                await Task.Delay( Timeout.Infinite, token );
            }
            finally
            {
                await cleanup.Task;
            }
            return new( [] );
        } ) );
        await registry.SetGlobalSearchAsync( package.Key, true, registry.Document.Revision, CancellationToken.None );
        SearchRequest Request( string owner ) => new( )
        {
            SearchOwner = owner,
            SearchGeneration = service.Begin( owner ),
            EnabledSearchSources = [ExtensionStableIds.SourceId( package.Key )]
        };
        var first = service.SearchAsync( QueryParser.Parse( "old" ), Request( "a" ), CancellationToken.None ).AsTask( );
        var second = service.SearchAsync( QueryParser.Parse( "old" ), Request( "b" ), CancellationToken.None ).AsTask( );
        await occupied.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
        var latest = service.SearchAsync( QueryParser.Parse( "latest" ), Request( "a" ), CancellationToken.None ).AsTask( );
        try
        {
            True( !latest.IsCompleted );
        }
        finally
        {
            cleanup.TrySetResult( );
            service.CloseOwner( "b" );
        }
        Equal( "Latest", (await latest).Single( ).Title );
        await Task.WhenAll( first, second );
    } );
    private sealed class SearchSdkCommand : IExtensionCommand, IExtensionSearchProvider
    {
        public string Id => "uuid";
        public string SearchId => "notes";
        public bool Global { get; private set; }
        public string? Cache { get; private set; }
        public int Interactive { get; private set; }
        public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            Interactive++;
            return ValueTask.CompletedTask;
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
        public ValueTask<ExtensionSearchResponse> SearchAsync( SearchContext context, CancellationToken cancellationToken )
        {
            Global = context.Global;
            Cache = context.Cache?.GetString( );
            return ValueTask.FromResult( new ExtensionSearchResponse( [new( "item", context.Query )] ) );
        }
    }
}
