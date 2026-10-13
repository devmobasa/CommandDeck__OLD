namespace CommandDeck.Specs;

public static partial class Program
{
    private const string PacmanSearchFixture =
        "extra/cmatrix 2.0-3\n" +
        "    A curses-based scrolling 'Matrix'-like screen\n" +
        "extra/jq 1.7.1-2 [installed]\n" +
        "    Command-line JSON processor\n" +
        "core/pacman 7.0.0-1 (base-devel) [installed: 6.9.0-2]\n" +
        "    A library-based package manager with dependency support\n";

    private static async Task PacmanCatalogParsesSearchOutputAsync( )
    {
        ScriptedPackageRunner runner = new( );
        runner.Script( "pacman", "-Ss", PackageProcess( 0, PacmanSearchFixture ) );
        runner.Script(
            "expac",
            "-S",
            PackageProcess( 0, "cmatrix\t81920\t24576\njq\t1200000\t400000\n" ) );
        PacmanCatalog catalog = new( runner.RunAsync );

        var result = await catalog.SearchAsync(
            "matrix",
            10,
            CancellationToken.None );

        True( result.Error is null );
        True( !result.Truncated );
        True( !result.HelperAvailable );
        Equal( "matrix", result.Query );
        Equal( 3, result.Packages.Count );

        var first = result.Packages[0];
        Equal( "cmatrix", first.Name );
        Equal( "extra", first.Repository );
        Equal( "2.0-3", first.Version );
        Equal( "A curses-based scrolling 'Matrix'-like screen", first.Description );
        True( !first.Installed );
        True( first.InstalledVersion is null );
        Equal( 81920L, first.InstalledBytes );
        Equal( 24576L, first.DownloadBytes );

        var second = result.Packages[1];
        Equal( "jq", second.Name );
        True( second.Installed );
        Equal( "1.7.1-2", second.InstalledVersion );
        Equal( "Command-line JSON processor", second.Description );

        var third = result.Packages[2];
        Equal( "pacman", third.Name );
        Equal( "core", third.Repository );
        Equal( "7.0.0-1", third.Version );
        True( third.Installed );
        Equal( "6.9.0-2", third.InstalledVersion );
        True( third.InstalledBytes is null );

        Equal( "pacman -Ss --color never -- matrix", runner.Invocations[0] );
        Equal( "expac -S %n\\t%m\\t%k -- cmatrix jq pacman", runner.Invocations[1] );
        Equal( 2, runner.Invocations.Count );
    }

    private static async Task PacmanCatalogBoundsSearchResultsAsync( )
    {
        ScriptedPackageRunner runner = new( );
        runner.Script( "pacman", "-Ss", PackageProcess( 0, PacmanSearchFixture ) );
        runner.Script( "expac", "-S", PackageProcess( 0, "cmatrix\t81920\t24576\n" ) );
        PacmanCatalog catalog = new( runner.RunAsync );

        var bounded = await catalog.SearchAsync( "a", 2, CancellationToken.None );
        Equal( 2, bounded.Packages.Count );
        True( bounded.Truncated );
        Equal( "expac -S %n\\t%m\\t%k -- cmatrix jq", runner.Invocations[1] );

        var clamped = await catalog.SearchAsync( "a", 0, CancellationToken.None );
        Equal( 1, clamped.Packages.Count );
        True( clamped.Truncated );
        Equal( 81920L, clamped.Packages[0].InstalledBytes );
    }

    private static async Task PacmanCatalogRejectsUnsafeQueriesAsync( )
    {
        ScriptedPackageRunner runner = new( );
        runner.Script( "pacman", "-Ss", PackageProcess( 0, PacmanSearchFixture ) );
        PacmanCatalog catalog = new( runner.RunAsync );

        string[] rejected =
        [
            "--help",
            "-Ss",
            "jq; rm -rf /",
            "jq && ls",
            "jq\nls",
            "jq\u0007ls",
            "jq*",
            "core/jq",
            "$(id)",
            "  ",
            string.Empty,
            new string('a', 129)
        ];

        foreach ( var query in rejected )
        {
            var result = await catalog.SearchAsync(
                query,
                10,
                CancellationToken.None );
            Equal( "The package query contains unsupported characters.", result.Error );
            Equal( 0, result.Packages.Count );
        }

        Equal( 0, runner.Invocations.Count );

        var accepted = await catalog.SearchAsync(
            "gnome-shell_2.0+dev@x.y",
            10,
            CancellationToken.None );
        True( accepted.Error is null );
        Equal( "pacman -Ss --color never -- gnome-shell_2.0+dev@x.y", runner.Invocations[0] );
    }

    private static async Task PacmanCatalogReportsNoSearchMatchesAsync( )
    {
        ScriptedPackageRunner runner = new( );
        runner.Script( "pacman", "-Ss", PackageProcess( 1 ) );
        PacmanCatalog catalog = new( runner.RunAsync );

        var result = await catalog.SearchAsync(
            "nosuchpackage",
            10,
            CancellationToken.None );

        True( result.Error is null );
        True( !result.Truncated );
        Equal( 0, result.Packages.Count );
        Equal( 1, runner.Invocations.Count );

        ScriptedPackageRunner broken = new( );
        broken.Script( "pacman", "-Ss", PackageProcess( 2, "garbled", "boom" ) );
        var failure = await new PacmanCatalog( broken.RunAsync )
            .SearchAsync( "jq", 10, CancellationToken.None );
        Equal( "The package search could not be completed.", failure.Error );
        Equal( 0, failure.Packages.Count );
    }

    private static async Task PacmanCatalogSurvivesMissingExpacAsync( )
    {
        ScriptedPackageRunner runner = new( );
        runner.Script( "pacman", "-Ss", PackageProcess( 0, PacmanSearchFixture ) );
        runner.Script( "expac", "-S", PackageProcess( 127, string.Empty, "expac: not found" ) );
        PacmanCatalog catalog = new( runner.RunAsync );

        var result = await catalog.SearchAsync(
            "matrix",
            10,
            CancellationToken.None );

        True( result.Error is null );
        Equal( 3, result.Packages.Count );
        True( result.Packages.All( record =>
            record.InstalledBytes is null && record.DownloadBytes is null ) );
        Equal( 2, runner.Invocations.Count );
    }

    private static void PacmanCatalogDatabaseIdentityTracksChanges( )
    {
        var root = CreatePacmanDatabaseFixture( );
        try
        {
            ScriptedPackageRunner runner = new( );
            PacmanCatalog catalog = new( runner.RunAsync, null, root );

            var first = catalog.ComputeDatabaseIdentity( );
            Equal( 64, first.Length );
            Equal( first, catalog.ComputeDatabaseIdentity( ) );
            True( !catalog.IsDatabaseLocked( ) );

            File.SetLastWriteTimeUtc(
                Path.Combine( root, "sync", "core.db" ),
                new DateTime( 2031, 4, 5, 6, 7, 8, DateTimeKind.Utc ) );
            var afterTouch = catalog.ComputeDatabaseIdentity( );
            True( !string.Equals( first, afterTouch, StringComparison.Ordinal ) );

            File.WriteAllText( Path.Combine( root, "sync", "core.db" ), "core-grown" );
            File.SetLastWriteTimeUtc(
                Path.Combine( root, "sync", "core.db" ),
                new DateTime( 2031, 4, 5, 6, 7, 8, DateTimeKind.Utc ) );
            var afterGrowth = catalog.ComputeDatabaseIdentity( );
            True( !string.Equals( afterTouch, afterGrowth, StringComparison.Ordinal ) );

            File.WriteAllText( Path.Combine( root, "local", "ALPM_DB_VERSION" ), "10\n" );
            True( !string.Equals(
                afterGrowth,
                catalog.ComputeDatabaseIdentity( ),
                StringComparison.Ordinal ) );

            PacmanCatalog missing = new( runner.RunAsync, null, Path.Combine( root, "absent" ) );
            Equal( "unavailable", missing.ComputeDatabaseIdentity( ) );
            True( !missing.IsDatabaseLocked( ) );
            Equal( 0, runner.Invocations.Count );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static string CreatePacmanDatabaseFixture( )
    {
        var root = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-pacman-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( Path.Combine( root, "sync" ) );
        Directory.CreateDirectory( Path.Combine( root, "local" ) );
        File.WriteAllText( Path.Combine( root, "sync", "core.db" ), "core" );
        File.WriteAllText( Path.Combine( root, "sync", "extra.db" ), "extra" );
        File.WriteAllText( Path.Combine( root, "local", "ALPM_DB_VERSION" ), "9\n" );
        return root;
    }

    private static ProcessResult PackageProcess(
        int exitCode,
        string standardOutput = "",
        string standardError = "" ) => new( )
        {
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError
        };

    private sealed class ScriptedPackageRunner
    {
        private readonly Dictionary<string, ProcessResult> _scripted = new( StringComparer.Ordinal );

        public List<string> Invocations { get; } = [];

        public void Script( string fileName, string mode, ProcessResult result ) =>
            _scripted[$"{fileName} {mode}"] = result;

        public ValueTask<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            Invocations.Add( $"{fileName} {string.Join( ' ', arguments )}" );
            var key = $"{fileName} {(arguments.Count > 0 ? arguments[0] : string.Empty)}";
            return ValueTask.FromResult( _scripted.TryGetValue( key, out var scripted )
                ? scripted
                : PackageProcess( 1, string.Empty, "unscripted command" ) );
        }
    }
}
