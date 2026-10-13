namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RenamingDirectoriesWithIgnoreInputsPreservesResultsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "repository" );
        var before = Path.Combine( root, "before" );
        var after = Path.Combine( root, "after" );
        Directory.CreateDirectory( before );
        try
        {
            Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
            await File.WriteAllTextAsync( Path.Combine( before, ".gitignore" ), "ignored.txt\n" );
            await File.WriteAllTextAsync( Path.Combine( before, "source.txt" ), "movedsource marker" );
            await File.WriteAllTextAsync( Path.Combine( before, "ignored.txt" ), "movedignored marker" );
            var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.ReindexAsync( null, CancellationToken.None );
            Directory.Move( before, after );
            True( await WaitForCatalogResultAsync( catalog, "movedsource marker",
                result => result.Id == StableId.Create( ResultKinds.File, Path.Combine( after, "source.txt" ) ), TimeSpan.FromSeconds( 3 ) ) );
            var results = await SearchAsync( catalog, "movedsource marker" );
            Equal( 1, results.Count );
            Equal( 0, (await SearchAsync( catalog, "movedignored marker" )).Count );
            // The rule file's new location must remain observed after the rename.
            File.Delete( Path.Combine( after, ".gitignore" ) );
            True( await WaitForCatalogResultAsync( catalog, "movedignored marker",
                result => result.Id == StableId.Create( ResultKinds.File, Path.Combine( after, "ignored.txt" ) ), TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task PreviewsUseTheMostSpecificIndexRootAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "repository" );
        var nested = Path.Combine( root, "nested" );
        Directory.CreateDirectory( nested );
        try
        {
            Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
            await File.WriteAllTextAsync( Path.Combine( root, ".gitignore" ), "ignored.txt\n" );
            var ignored = Path.Combine( nested, "ignored.txt" );
            var allowed = Path.Combine( nested, "allowed.txt" );
            await File.WriteAllTextAsync( ignored, "excluded contents" );
            await File.WriteAllTextAsync( allowed, "allowed contents" );
            IndexRootOptions parentOptions = new( )
            {
                Path = root,
                RespectGitIgnore = false
            };
            IndexRootOptions nestedOptions = new( )
            {
                Path = nested,
                RespectGitIgnore = true
            };
            for ( var order = 0; order < 2; order++ )
            {
                IndexingOptions options = new( )
                {
                    Roots = order == 0 ? [parentOptions, nestedOptions] : [nestedOptions, parentOptions]
                };
                PrivacyOptions privacy = new( )
                {
                    NeverIndex = []
                };
                using FileIndexSource source = new( options, privacy );
                FilePreviewService previews = new( options, privacy );
                Equal( 0, (await source.IndexPathAsync( ignored, CancellationToken.None )).Count );
                True( (await previews.ReadAsync( ignored, CancellationToken.None )).Preview is null );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await previews.ReadAiAttachmentAsync( ignored, CancellationToken.None ) );
                True( (await previews.ReadAsync( allowed, CancellationToken.None )).Preview is not null );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task OrdinaryEditsDoNotRewritePolicySnapshotsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "repository" );
        Directory.CreateDirectory( root );
        var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
        var state = options.Storage.DatabasePath + ".index-policy";
        var sentinel = new DateTime( 2001, 2, 3, 4, 5, 6, DateTimeKind.Utc );
        try
        {
            Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
            for ( var index = 0; index < 100; index++ )
            {
                var directory = Path.Combine( root, $"directory-{index}" );
                Directory.CreateDirectory( directory );
                await File.WriteAllTextAsync( Path.Combine( directory, "source.txt" ), "original marker" );
            }
            var source = Path.Combine( root, "directory-0/source.txt" );
            await using SqliteCatalog catalog = new( options.Storage );
            await using ( IndexCoordinator indexer = new( catalog, catalog, options ) )
            {
                await indexer.ReindexAsync( null, CancellationToken.None );
                await WaitForPolicyBatchAsync( indexer );
                File.SetLastWriteTimeUtc( state, sentinel );
                await File.WriteAllTextAsync( source, "ordinaryedit marker" );
                True( await WaitForCatalogResultAsync( catalog, "ordinaryedit marker",
                    result => result.Id == StableId.Create( ResultKinds.File, source ), TimeSpan.FromSeconds( 3 ) ) );
                await WaitForPolicyBatchAsync( indexer );
                Equal( sentinel, File.GetLastWriteTimeUtc( state ) );

                await File.WriteAllTextAsync( Path.Combine( root, ".gitignore" ), "directory-0/\n" );
                True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "ordinaryedit marker" )).Count == 0,
                    TimeSpan.FromSeconds( 3 ) ) );
                await WaitForPolicyBatchAsync( indexer );
                True( File.GetLastWriteTimeUtc( state ) != sentinel );
            }
            // Loading a current snapshot must also avoid rewriting it on ordinary edits.
            File.SetLastWriteTimeUtc( state, sentinel );
            await using ( IndexCoordinator restarted = new( catalog, catalog, options ) )
            {
                await restarted.StartAsync( CancellationToken.None );
                var other = Path.Combine( root, "directory-1/source.txt" );
                await File.WriteAllTextAsync( other, "afterrestart marker" );
                True( await WaitForCatalogResultAsync( catalog, "afterrestart marker",
                    result => result.Id == StableId.Create( ResultKinds.File, other ), TimeSpan.FromSeconds( 3 ) ) );
                await WaitForPolicyBatchAsync( restarted );
                Equal( sentinel, File.GetLastWriteTimeUtc( state ) );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task WaitForPolicyBatchAsync( IndexCoordinator indexer )
    {
        True( await WaitUntilAsync( async ( ) =>
        {
            var status = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
            return status.PendingEventCount == 0 && !status.ReconciliationRequired && !status.Reconciling;
        }, TimeSpan.FromSeconds( 3 ) ) );
    }
}
