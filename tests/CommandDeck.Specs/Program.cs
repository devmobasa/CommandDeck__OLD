namespace CommandDeck.Specs;

public static partial class Program
{
    private static int _failed;
    private static int _executed;

    public static async Task<int> Main( string[] args )
    {
        using var temporaryFiles = SpecTemporaryFiles.Create( );
        using var termination = SpecTemporaryFiles.HandleTermination( args );
        return await RunInIsolatedStateAsync( args );
    }

    private static async Task<int> RunInIsolatedStateAsync( string[] args )
    {
        // Runtimes under test never use the developer's own hey; HEY specs inject
        // theirs, so a plain query gets the same rows on every machine.
        Environment.SetEnvironmentVariable( HeyExecutable.OverrideVariable, HeyExecutable.OverrideOff );

        if ( args is ["--native-index-aot", var nativeExecutable] )
        {
            return await RunNativeIndexAotAsync( nativeExecutable );
        }

        if ( args is ["--extension-disposal-fixture", var root, var profile] )
        {
            return await RunExtensionDisposalFixtureAsync( root, profile );
        }

        if ( args is ["--spec-lifetime-fixture"] )
        {
            await RunSpecLifetimeFixtureAsync( );
            return 0;
        }

        var clipboardFixture = TryRunClipboardFixtureAsync( args );
        if ( clipboardFixture is not null )
        {
            return await clipboardFixture;
        }

        if ( !ConfigureSpecSelection( args ) )
        {
            return 2;
        }

        if ( args.Contains( "--spec-storage-fixture", StringComparer.Ordinal ) )
        {
            await RunSpecStorageFixtureAsync( );
            return FinishSpecs( );
        }

        if ( args.Contains( "--spec-reporting-fixture", StringComparer.Ordinal ) )
        {
            await RunSpecReportingFixtureAsync( );
            return FinishSpecs( );
        }

        if ( args.Length == 1 &&
            string.Equals( args[0], "--write-protocol-contract", StringComparison.Ordinal ) )
        {
            WriteProtocolContractArtifacts( FindRepositoryRoot( ) );
            return 0;
        }

        if ( args.Length == 1 &&
            string.Equals( args[0], "--write-reference-docs", StringComparison.Ordinal ) )
        {
            WriteReferenceDocumentation( FindRepositoryRoot( ) );
            return 0;
        }

        if ( args.Contains( "--top-matches-benchmark", StringComparer.Ordinal ) )
        {
            await RunLearnedTopMatchCorpusAsync( benchmark: true );
            return 0;
        }

        if ( args.Contains( "--phase3-benchmark", StringComparer.Ordinal ) )
        {
            return await RunPhase3BenchmarkAsync( );
        }

        if ( args.Contains( "--quality-benchmark", StringComparer.Ordinal ) )
        {
            var outputPath = ReadArgumentValue( args, "--quality-output" );
            return await RunQualityBenchmarkAsync( outputPath,
                args.Contains( "--quality-observe-latency", StringComparer.Ordinal ) );
        }

        await RunExtensionTransportRegistrationsAsync( );
        if ( args.Contains( "--extensions-only", StringComparer.Ordinal ) )
        {
            Console.WriteLine( $"Executed {_executed} extension specs; {_failed} failed." );
            return FinishSpecs( );
        }

        await RunCoreRegistrationsAsync( );
        await RunCatalogRegistrationsAsync( );
        await RunIncrementalIndexRegistrationsAsync( );
        await RunIndexPolicyRegistrationsAsync( );
        await RunProtocolRegistrationsAsync( );
        await RunAiSettingsRegistrationsAsync( );
        await RunCliRegistrationsAsync( );
        await RunFeatureRegistrationsAsync( );
        await RunPackageCatalogRegistrationsAsync( );
        await RunAgentRegistrationsAsync( );
        await RunPackageHelperRegistrationsAsync( );
        await RunAgentCliRegistrationsAsync( );
        await RunCliAgentRegistrationsAsync( );
        await RunProviderRegistrationsAsync( );
        await RunHerdrRegistrationsAsync( );
        await RunHeyRegistrationsAsync( );

        if ( !_listSpecs )
        {
            Console.WriteLine( $"Executed {_executed} CommandDeck specs." );
            Console.WriteLine( _failed == 0 && _executed > 0
                ? _skipped == 0 ? "All CommandDeck specs passed." : "All executed CommandDeck specs passed."
                : $"{_failed} CommandDeck spec(s) failed." );
        }
        return FinishSpecs( );
    }

    private static string? ReadArgumentValue( string[] args, string name )
    {
        var index = Array.IndexOf( args, name );
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static ProtocolMessage? DeserializeMessage( string line ) =>
        System.Text.Json.JsonSerializer.Deserialize(
            line,
            CommandDeckJsonContext.Default.ProtocolMessage );

    private sealed class StaticSearchProvider( IReadOnlyList<SearchResult> results ) : ICommandProvider
    {
        public string Id => "static";

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken ) =>
            ValueTask.FromResult( results );
    }

    private sealed class CountingSearchProvider( string id ) : ICommandProvider
    {
        public int Calls { get; private set; }
        public string Id => id;

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            Calls++;
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }
    }

    private sealed class FixedTimeProvider( DateTimeOffset now ) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow( ) => now;
    }

    private static void Run( string name, Action test,
        [System.Runtime.CompilerServices.CallerFilePath] string registration = "" )
    {
        if ( !SelectSpec( name, registration ) )
        {
            return;
        }

        _executed++;
        var started = System.Diagnostics.Stopwatch.GetTimestamp( );
        try
        {
            test( );
            Console.WriteLine( $"PASS {name}" );
            RecordSpec( name, registration, started, null );
        }
        catch ( SpecSkippedException exception )
        {
            RecordSkippedSpec( name, registration, started, exception );
        }
        catch ( Exception exception )
        {
            _failed++;
            SpecTemporaryFiles.ReportStorageFailure( exception );
            Console.WriteLine( $"FAIL {name}: {exception.Message}" );
            RecordSpec( name, registration, started, exception );
        }
    }

    private static async Task RunAsync( string name, Func<Task> test,
        [System.Runtime.CompilerServices.CallerFilePath] string registration = "" )
    {
        if ( !SelectSpec( name, registration ) )
        {
            return;
        }

        _executed++;
        var started = System.Diagnostics.Stopwatch.GetTimestamp( );
        try
        {
            await test( );
            Console.WriteLine( $"PASS {name}" );
            RecordSpec( name, registration, started, null );
        }
        catch ( SpecSkippedException exception )
        {
            RecordSkippedSpec( name, registration, started, exception );
        }
        catch ( Exception exception )
        {
            _failed++;
            SpecTemporaryFiles.ReportStorageFailure( exception );
            Console.WriteLine( $"FAIL {name}: {exception.Message}" );
            RecordSpec( name, registration, started, exception );
        }
    }

    private static void True(
        bool condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression( nameof( condition ) )] string? expression = null )
    {
        if ( !condition )
        {
            throw new InvalidOperationException( $"Expected true: {expression}." );
        }
    }

    private static void Equal<T>( T expected, T actual )
    {
        if ( !EqualityComparer<T>.Default.Equals( expected, actual ) )
        {
            throw new InvalidOperationException( $"Expected '{expected}', got '{actual}'." );
        }
    }

    private static void Throws<TException>( Action action )
        where TException : Exception
    {
        try
        {
            action( );
        }
        catch ( TException )
        {
            return;
        }

        throw new InvalidOperationException( $"Expected {typeof( TException ).Name}." );
    }

    private static async Task ThrowsAsync<TException>( Func<Task> action )
        where TException : Exception
    {
        try
        {
            await action( );
        }
        catch ( TException )
        {
            return;
        }

        throw new InvalidOperationException( $"Expected {typeof( TException ).Name}." );
    }
}
