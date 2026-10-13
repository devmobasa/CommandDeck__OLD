using CommandDeck.PackageHelper;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed record ProgressEntry( string OperationId, string State, string Message );

    private sealed class FakePolkit : IPolkitAuthority
    {
        private IReadOnlyList<KeyValuePair<string, string>> _details = [];

        public int Calls { get; private set; }
        public bool Authorized { get; set; } = true;
        public bool Challenge { get; set; }
        public string? LastSender { get; private set; }
        public string? LastAction { get; private set; }
        public Func<Task>? BeforeReturnAsync { get; set; }

        public string? Detail( string key ) => _details
            .FirstOrDefault( entry => string.Equals( entry.Key, key, StringComparison.Ordinal ) )
            .Value;

        public async ValueTask<PolkitAuthorizationResult> CheckAuthorizationAsync(
            string senderBusName,
            string actionId,
            IReadOnlyList<KeyValuePair<string, string>> details,
            CancellationToken cancellationToken )
        {
            Calls++;
            LastSender = senderBusName;
            LastAction = actionId;
            _details = details;
            if ( BeforeReturnAsync is not null )
            {
                await BeforeReturnAsync( ).ConfigureAwait( false );
            }
            return new PolkitAuthorizationResult( Authorized, Challenge );
        }
    }

    private sealed class FakePacman
    {
        public List<string> Invocations { get; } = [];
        public string PrintOutput { get; set; } = string.Empty;
        public string QueryOutput { get; set; } = string.Empty;
        public PackageProcessResult? QueryResult { get; set; }
        public string VersionOutput { get; set; } = "Pacman v7.1.0 - libalpm v15.0.0\n";
        public int CommitExitCode { get; set; }
        public bool CommitTimedOut { get; set; }
        public bool RanCommit => Commits > 0;
        public int Commits { get; private set; }

        public Task<PackageProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken )
        {
            Invocations.Add( $"{fileName} {string.Join( ' ', arguments )}" );
            if ( arguments.Contains( "--version", StringComparer.Ordinal ) )
            {
                return Completed( 0, VersionOutput );
            }

            if ( arguments.Contains( "--print", StringComparer.Ordinal ) )
            {
                return Completed( 0, PrintOutput );
            }

            if ( arguments.Contains( "-Q", StringComparer.Ordinal ) )
            {
                return QueryResult is null ? Query( arguments ) : Task.FromResult( QueryResult );
            }

            Commits++;
            return Completed( CommitExitCode, string.Empty, CommitTimedOut );
        }

        // Like pacman -Q: installed names on stdout, one "was not found" error
        // per missing name on stderr, and exit status 1 when any is missing.
        private Task<PackageProcessResult> Query( IReadOnlyList<string> arguments )
        {
            var installed = QueryOutput
                .Split( '\n', StringSplitOptions.RemoveEmptyEntries )
                .Select( static line => line.Split( ' ' )[0] )
                .ToHashSet( StringComparer.Ordinal );
            var missing = arguments
                .SkipWhile( static argument => !string.Equals( argument, "--", StringComparison.Ordinal ) )
                .Skip( 1 )
                .Where( name => !installed.Contains( name ) )
                .Select( static name => $"error: package '{name}' was not found\n" )
                .ToArray( );

            return Task.FromResult( new PackageProcessResult
            {
                ExitCode = missing.Length == 0 ? 0 : 1,
                StandardOutput = QueryOutput,
                StandardError = string.Concat( missing )
            } );
        }

        private static Task<PackageProcessResult> Completed(
            int exitCode,
            string standardOutput,
            bool timedOut = false ) =>
            Task.FromResult( new PackageProcessResult
            {
                ExitCode = exitCode,
                StandardOutput = standardOutput,
                StandardError = string.Empty,
                TimedOut = timedOut
            } );
    }

    private sealed class PackageHelperFixture : IDisposable
    {
        private PackageHelperFixture( string databaseRoot, FakePacman pacman, FakePolkit polkit )
        {
            DatabaseRoot = databaseRoot;
            Pacman = pacman;
            Polkit = polkit;
            Core = new PackageHelperCore( new PackageHelperOptions
            {
                Authority = polkit,
                Runner = pacman.RunAsync,
                DatabaseRoot = databaseRoot,
                Progress = ( operationId, state, message ) =>
                {
                    lock ( Progress )
                    {
                        Progress.Add( new ProgressEntry( operationId, state, message ) );
                    }
                }
            } );
        }

        public string DatabaseRoot { get; }
        public FakePacman Pacman { get; }
        public FakePolkit Polkit { get; }
        public PackageHelperCore Core { get; }
        public List<ProgressEntry> Progress { get; } = [];

        public static PackageHelperFixture Create( )
        {
            var root = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-package-helper-{Guid.NewGuid( ):N}" );
            Directory.CreateDirectory( Path.Combine( root, "sync" ) );
            Directory.CreateDirectory( Path.Combine( root, "local" ) );
            File.WriteAllText( Path.Combine( root, "sync", "core.db" ), "core" );
            File.WriteAllText( Path.Combine( root, "sync", "extra.db" ), "extra" );
            File.WriteAllText( Path.Combine( root, "local", "ALPM_DB_VERSION" ), "9\n" );
            return new PackageHelperFixture( root, new FakePacman( ), new FakePolkit( ) );
        }

        // A commit carries the identity the daemon's preview computed, so every
        // helper spec also proves the helper accepts the daemon's identity.
        public Task<string> CommitAsync(
            string operation,
            IReadOnlyList<string> names,
            string? databaseIdentity = null,
            string? transactionDigest = null ) =>
            Core.CommitAsync(
                "commanddeck-daemon",
                operation,
                names,
                databaseIdentity ?? new PacmanCatalog( pacmanDatabaseDirectory: DatabaseRoot ).ComputeDatabaseIdentity( ),
                transactionDigest ?? PacmanTransactionDigest.Compute( operation, Pacman.PrintOutput ),
                CancellationToken.None ).AsTask( );

        public void Dispose( )
        {
            try
            {
                Directory.Delete( DatabaseRoot, recursive: true );
            }
            catch ( Exception exception ) when (
                exception is IOException or UnauthorizedAccessException )
            {
            }
        }
    }
}
