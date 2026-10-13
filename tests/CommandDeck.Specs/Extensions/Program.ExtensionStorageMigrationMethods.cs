using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionStorageMigrationAndEraseAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            var artifacts = Path.Combine( root, "artifacts" );
            var archive = WritePackageFixture( root, PackageFixture( ) );
            string key;
            long revision;
            using ( var registry = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None ) )
            {
                catalog.Contributions = registry;
                await using var staged = await ExtensionArchive.StageAsync( archive, artifacts, CancellationToken.None );
                var installed = await registry.InstallAsync( staged, "local", 0, true, [], true, CancellationToken.None );
                key = installed.Key;
                var legacy = registry.Document with
                {
                    Revision = registry.Document.Revision + 1,
                    Installations = [installed with { StorageRevision = 7, Storage = new( ) { ["draft"] = JsonElement.Parse( "\"Legacy\"" ) } }]
                };
                await catalog.CommitExtensionRegistryAsync( legacy, registry.Snapshot, registry.Document.Revision, CancellationToken.None );
                revision = legacy.Revision;
            }

            using ( var migrated = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None ) )
            {

                catalog.Contributions = migrated;
                Equal( revision + 1, migrated.Document.Revision );
                Equal( 0L, migrated.Find( key ).StorageRevision );
                Equal( 0, migrated.Find( key ).Storage.Count );
                Equal( 7L, migrated.DataStore.Read( key ).Revision );
                Equal( "Legacy", migrated.DataStore.Read( key ).Values["draft"].GetString( ) );
                revision = migrated.Document.Revision;
            }
            using ( var reopened = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None ) )
            {
                catalog.Contributions = reopened;
                Equal( revision, reopened.Document.Revision );
                Equal( 7L, reopened.DataStore.Read( key ).Revision );
                await reopened.RemoveAsync( key, true, revision, CancellationToken.None );
                Equal( 8L, reopened.DataStore.Read( key ).Revision );
                await ThrowsAsync<ExtensionApplicationException>( async ( ) => await reopened.DataStore.AccessAsync( key,
                    new( "s", 1, 1, "i", "write", "draft", 7, JsonElement.Parse( "\"Stale\"" ) ), CancellationToken.None ) );
            }
            using var erased = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None );
            catalog.Contributions = erased;
            Equal( 0, erased.DataStore.Read( key ).Values.Count );
            Equal( 8L, erased.DataStore.Read( key ).Revision );
            Equal( 0, erased.Find( key ).Settings.Count );
            True( erased.Find( key ).Removed );

            await using var reinstall = await ExtensionArchive.StageAsync( archive, artifacts, CancellationToken.None );
            await erased.InstallAsync( reinstall, "local", erased.Document.Revision, true, [], true, CancellationToken.None );
            await erased.SetEnabledAsync( null, true, erased.Document.Revision, CancellationToken.None );
            Equal( 8L, erased.DataStore.Read( key ).Revision );
            var written = await erased.WithExecutionAsync( key, current => erased.AccessDataAsync( current,
                new( "s", 1, 1, "i", "write", "draft", 8, JsonElement.Parse( "\"Fresh\"" ) ), CancellationToken.None ), CancellationToken.None );
            Equal( 9L, written.Revision );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task ExtensionSearchRejectsChangedDataAsync( ) => await WithSearchRegistry( async ( registry, _, package ) =>
    {
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        await using ExtensionSearchService search = new( registry, new FakeExtensionSearchWorker( async ( _, _, _, _, token ) =>
        {
            entered.TrySetResult( );
            await release.Task.WaitAsync( token );
            return new( [new( "old-cache", "Stale result" )] );
        } ) );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        var filter = registry.Snapshot.FindById( ExtensionStableIds.SearchProviderId( package.Key, "notes" ) )!;
        var query = QueryParser.Parse( filter.Token + " meeting", registry: registry.Snapshot );
        var pending = search.SearchAsync( query, SearchInput( search, package ), deadline.Token ).AsTask( );
        await entered.Task.WaitAsync( deadline.Token );

        await registry.WithExecutionAsync( package.Key, current => registry.AccessDataAsync( current,
            new( "s", 1, 1, "i", "write", "search-cache", 0, JsonElement.Parse( "\"new-cache\"" ) ), deadline.Token ), deadline.Token );
        release.TrySetResult( );

        Equal( 0, (await pending).Count );
    } );
}
