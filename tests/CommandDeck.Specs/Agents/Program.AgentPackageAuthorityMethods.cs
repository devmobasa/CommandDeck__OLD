namespace CommandDeck.Specs;

public static partial class Program
{
    private const string PackageAuthorityOwner = "socket-spec-package-owner";

    private static async Task PackageCommitRechecksAssistantAuthorityAsync( )
    {
        foreach ( var revocation in new[] { "lower trust", "disable", "lower trust elsewhere", "lower and raise" } )
        {
            await PackageCommitIsRefusedAfterRevocationAsync( revocation );
        }
    }

    private static async Task PackageCommitIsRefusedAfterRevocationAsync( string revocation )
    {
        var databaseRoot = CreatePacmanDatabaseFixture( );
        ScriptedPackageRunner runner = new( );
        runner.Script( "pacman", "-S", PackageProcess( 0, PacmanInstallPlanFixture ) );
        runner.Script( "pacman", "-Q", PackageProcess( 0, "jq 1.7.1-2\n" ) );
        runner.Script( "expac", "-S", PackageProcess( 0, "cmatrix\t81920\nlibncurses\t1622016\n" ) );
        FakePackageHelper helper = new( );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) = await StartAgentRuntimeAsync(
            new ScriptedAgentBackend( ),
            serverCancellation.Token,
            new RuntimeConstructionOptions
            {
                PackageCatalogFactory = ( ) => new PacmanCatalog( runner.RunAsync, null, databaseRoot ),
                PackageHelperFactory = ( ) => helper
            } );
        try
        {
            await using ( runtime )
            {
                var database = Path.Combine( root, "catalog.db" );
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, CancellationToken.None );
                await ChangeTrustAsync( runtime, AgentTrustLevels.Packages );
                var committed = await PreviewCommitableInstallAsync( runtime );
                var revoked = await PreviewCommitableInstallAsync( runtime );
                var stale = await PreviewCommitableInstallAsync( runtime );

                await CommitPackagePreviewAsync( runtime, committed );
                Equal( 1, helper.Commits );

                await SetPackageAuthorityAsync( runtime, revocation, database, granted: false );
                if ( revocation == "lower and raise" )
                {
                    await ChangeTrustAsync( runtime, AgentTrustLevels.Packages );
                }
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await runtime.CommitPackagesAsync( revoked.PreviewId, CancellationToken.None ) );
                True( helper.Commits == 1, $"a package change committed after '{revocation}' revoked the assistant's authority" );

                // Restored authority does not revive a preview granted before the revocation.
                await SetPackageAuthorityAsync( runtime, revocation, database, granted: true );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await runtime.CommitPackagesAsync( stale.PreviewId, CancellationToken.None ) );
                True( helper.Commits == 1, $"a preview from before '{revocation}' committed once authority returned" );

                await CommitPackagePreviewAsync( runtime, await PreviewCommitableInstallAsync( runtime ) );
                Equal( 2, helper.Commits );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
            Directory.Delete( databaseRoot, recursive: true );
        }
    }

    private static async ValueTask<PackageTransactionPreview> PreviewCommitableInstallAsync( CommandDeckRuntime runtime )
    {
        var preview = await runtime.PreviewPackagesAsync( "install", ["cmatrix"], CancellationToken.None );
        True( preview.Commitable, "the package preview is not commitable" );
        return preview;
    }

    private static async ValueTask CommitPackagePreviewAsync(
        CommandDeckRuntime runtime,
        PackageTransactionPreview preview )
    {
        var result = await runtime.CommitPackagesAsync( preview.PreviewId, CancellationToken.None );
        Equal( PackageOperationStates.Completed, result.State );
    }

    private static async ValueTask SetPackageAuthorityAsync(
        CommandDeckRuntime runtime,
        string revocation,
        string database,
        bool granted )
    {
        var level = granted ? AgentTrustLevels.Packages : AgentTrustLevels.ApprovedContext;
        switch ( revocation )
        {
            case "lower trust":
            case "lower and raise":
                await ChangeTrustAsync( runtime, level );
                break;

            case "disable" when granted:
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, CancellationToken.None );
                await ChangeTrustAsync( runtime, level );
                break;

            case "disable":
                await runtime.SetAgentPreferencesAsync(
                    new AgentPreferences { Enabled = false, TrustLevel = AgentTrustLevels.Packages },
                    CancellationToken.None );
                break;

            default:
                // The CLI's own runtime writes the shared store without telling the daemon.
                await using ( SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } ) )
                {
                    await catalog.InitializeAsync( CancellationToken.None );
                    using AgentAuthority authority = new( catalog.AgentPreferences );
                    await authority.SetTrustAsync( level, null, null, CancellationToken.None );
                }
                break;
        }
    }

    private static async ValueTask ChangeTrustAsync( CommandDeckRuntime runtime, int level )
    {
        var challenge = await runtime.CreateAgentTrustChallengeAsync(
            PackageAuthorityOwner, level, CancellationToken.None );
        var applied = await runtime.ApplyAgentTrustAsync(
            PackageAuthorityOwner, challenge.Id, level, null, null, CancellationToken.None );
        Equal( level, applied.Level );
    }

    private sealed class FakePackageHelper : IPackageHelper
    {
        private int _commits;

        public int Commits => Volatile.Read( ref _commits );

        public ValueTask<AgentHelperStatus> GetStatusAsync( CancellationToken cancellationToken ) =>
            ValueTask.FromResult( new AgentHelperStatus
            {
                Installed = true,
                Version = PackageHelperProtocol.ProtocolVersion,
                VersionMatches = true
            } );

        public ValueTask<string> CommitAsync(
            string operation,
            IReadOnlyList<string> names,
            string databaseIdentity,
            string transactionDigest,
            CancellationToken cancellationToken )
        {
            var commit = Interlocked.Increment( ref _commits );
            return ValueTask.FromResult( $"spec-operation-{commit}" );
        }

        public ValueTask<PackageCommitResult> GetOperationAsync( string operationId, CancellationToken cancellationToken ) =>
            ValueTask.FromResult( new PackageCommitResult
            {
                OperationId = operationId,
                State = PackageOperationStates.Completed,
                Message = "Installed."
            } );

        public ValueTask<bool> CancelAsync( string operationId, CancellationToken cancellationToken ) =>
            ValueTask.FromResult( false );

        public ValueTask DisposeAsync( ) => ValueTask.CompletedTask;
    }
}
