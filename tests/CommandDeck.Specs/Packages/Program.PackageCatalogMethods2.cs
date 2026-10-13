namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly DateTimeOffset PackagePreviewOrigin =
        new( 2026, 3, 4, 5, 6, 7, TimeSpan.Zero );

    private const string PacmanInstallPlanFixture =
        "cmatrix|2.0-3|extra|24576|https://example.invalid/extra/cmatrix-2.0-3-x86_64.pkg.tar.zst\n" +
        "libncurses|6.5-3|core|180224|https://example.invalid/core/libncurses-6.5-3-x86_64.pkg.tar.zst\n";

    private static async Task PacmanCatalogPreviewsAnInstallAsync( )
    {
        var root = CreatePacmanDatabaseFixture( );
        try
        {
            ScriptedPackageRunner runner = new( );
            runner.Script( "pacman", "-S", PackageProcess( 0, PacmanInstallPlanFixture ) );
            runner.Script( "pacman", "-Q", PackageProcess( 0, "jq 1.7.1-2\n" ) );
            runner.Script(
                "expac",
                "-S",
                PackageProcess( 0, "cmatrix\t81920\nlibncurses\t1622016\n" ) );
            PacmanCatalog catalog = new(
                runner.RunAsync,
                new FixedTimeProvider( PackagePreviewOrigin ),
                root );

            var preview = await catalog.PreviewAsync(
                "install",
                ["jq", "cmatrix"],
                "The user asked for a terminal toy.",
                CancellationToken.None );

            Equal( "install", preview.Operation );
            Equal( "cmatrix jq", string.Join( ' ', preview.RequestedNames ) );
            Equal( 2, preview.Entries.Count );
            Equal( 204800L, preview.DownloadBytes );
            Equal( 1703936L, preview.InstalledDeltaBytes );
            True( preview.Commitable );
            True( !preview.Destructive );
            True( preview.Reason is null );
            Equal( 64, preview.PreviewId.Length );
            Equal( 64, preview.DatabaseIdentity.Length );
            Equal( 64, preview.TransactionDigest.Length );
            Equal( "The user asked for a terminal toy.", preview.Rationale );
            Equal(
                PackagePreviewOrigin.ToUnixTimeMilliseconds( ) + 120_000,
                preview.ExpiresUnixMilliseconds );

            var requested =
                preview.Entries.Single( entry => entry.Name == "cmatrix" );
            True( requested.Requested );
            Equal( "extra", requested.Repository );
            Equal( "2.0-3", requested.TargetVersion );
            True( requested.CurrentVersion is null );
            Equal( 24576L, requested.DownloadBytes );
            Equal( 81920L, requested.InstalledBytes );
            True( !preview.Entries.Single( entry => entry.Name == "libncurses" ).Requested );

            Equal( "jq is already installed at version 1.7.1-2.", string.Join( '|', preview.Warnings ) );
            Equal(
                "pacman -S --needed --noconfirm --noprogressbar --color never cmatrix jq",
                string.Join( ' ', preview.Route ) );
            Equal(
                "pacman -S --print --print-format %n|%v|%r|%s|%l --needed -- cmatrix jq",
                runner.Invocations[0] );
            Equal( "pacman -Q -- jq", runner.Invocations[1] );
            Equal( "expac -S %n\\t%m -- cmatrix libncurses", runner.Invocations[2] );

            var reordered = await catalog.PreviewAsync(
                "install",
                ["cmatrix", "jq"],
                null,
                CancellationToken.None );
            Equal( preview.TransactionDigest, reordered.TransactionDigest );
            Equal( preview.DatabaseIdentity, reordered.DatabaseIdentity );
            True( !string.Equals( preview.PreviewId, reordered.PreviewId, StringComparison.Ordinal ) );
            True( reordered.Rationale is null );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task PacmanCatalogRefusesUnknownInstallTargetsAsync( )
    {
        var root = CreatePacmanDatabaseFixture( );
        try
        {
            ScriptedPackageRunner runner = new( );
            runner.Script(
                "pacman",
                "-S",
                PackageProcess(
                    1,
                    string.Empty,
                    "error: target not found: aether\nerror: target not found: nosuchpkg\n" ) );
            PacmanCatalog catalog = new( runner.RunAsync, null, root );

            var preview = await catalog.PreviewAsync(
                "install",
                ["nosuchpkg", "aether"],
                null,
                CancellationToken.None );

            True( !preview.Commitable );
            Equal( 0, preview.Entries.Count );
            Equal( 0L, preview.DownloadBytes );
            Equal( 0L, preview.InstalledDeltaBytes );
            Equal( 0, preview.Warnings.Count );
            Equal(
                "Package aether was not found in the official repositories. " +
                "Package nosuchpkg was not found in the official repositories.",
                preview.Reason );
            Equal(
                "pacman -S --needed --noconfirm --noprogressbar --color never aether nosuchpkg",
                string.Join( ' ', preview.Route ) );

            ScriptedPackageRunner installed = new( );
            installed.Script( "pacman", "-S", PackageProcess( 0 ) );
            installed.Script( "pacman", "-Q", PackageProcess( 0, "jq 1.7.1-2\n" ) );
            var settled = await new PacmanCatalog(
                installed.RunAsync,
                null,
                root ).PreviewAsync( "install", ["jq"], null, CancellationToken.None );
            True( !settled.Commitable );
            Equal( "All requested packages are already installed.", settled.Reason );
            Equal( "jq is already installed at version 1.7.1-2.", string.Join( '|', settled.Warnings ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task PacmanCatalogPreviewsARemoveCascadeAsync( )
    {
        var root = CreatePacmanDatabaseFixture( );
        try
        {
            ScriptedPackageRunner runner = new( );
            runner.Script( "pacman", "-Rs", PackageProcess( 0, "aether|1.2.3-1\nlibaether|1.0-2\n" ) );
            runner.Script( "expac", "-Q", PackageProcess( 0, "aether\t409600\nlibaether\t102400\n" ) );
            PacmanCatalog catalog = new( runner.RunAsync, null, root );

            var preview = await catalog.PreviewAsync(
                "remove",
                ["aether"],
                null,
                CancellationToken.None );

            Equal( "remove", preview.Operation );
            Equal( 2, preview.Entries.Count );
            Equal( 0L, preview.DownloadBytes );
            Equal( -512000L, preview.InstalledDeltaBytes );
            True( preview.Destructive );
            True( preview.Commitable );
            True( preview.Reason is null );
            Equal(
                "Commit uses pacman -Rns, so backup configuration files are not saved as .pacsave.",
                string.Join( '|', preview.Warnings ) );

            var requested =
                preview.Entries.Single( entry => entry.Name == "aether" );
            True( requested.Requested );
            Equal( "1.2.3-1", requested.CurrentVersion );
            True( requested.TargetVersion is null );
            Equal( 409600L, requested.InstalledBytes );
            True( !preview.Entries.Single( entry => entry.Name == "libaether" ).Requested );

            Equal(
                "pacman -Rns --noconfirm --noprogressbar --color never aether",
                string.Join( ' ', preview.Route ) );
            Equal( "pacman -Rs --print --print-format %n|%v -- aether", runner.Invocations[0] );
            Equal( "expac -Q %n\\t%m -- aether libaether", runner.Invocations[1] );
            Equal( 2, runner.Invocations.Count );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task PacmanCatalogReportsBlockedRemovalsAsync( )
    {
        var root = CreatePacmanDatabaseFixture( );
        try
        {
            ScriptedPackageRunner runner = new( );
            runner.Script(
                "pacman",
                "-Rs",
                PackageProcess(
                    1,
                    string.Empty,
                    "error: failed to prepare transaction (could not satisfy dependencies)\n" +
                    ":: removing jq breaks dependency 'jq' required by aether\n" ) );
            PacmanCatalog catalog = new( runner.RunAsync, null, root );

            var preview = await catalog.PreviewAsync(
                "remove",
                ["jq"],
                null,
                CancellationToken.None );

            True( !preview.Commitable );
            True( preview.Destructive );
            Equal( 0, preview.Entries.Count );
            Equal( ":: removing jq breaks dependency 'jq' required by aether", preview.Reason );
            Equal(
                "Commit uses pacman -Rns, so backup configuration files are not saved as .pacsave.",
                string.Join( '|', preview.Warnings ) );
            Equal( 1, runner.Invocations.Count );

            ScriptedPackageRunner absent = new( );
            absent.Script(
                "pacman",
                "-Rs",
                PackageProcess( 1, string.Empty, "error: target not found: aether\n" ) );
            var unknown = await new PacmanCatalog(
                absent.RunAsync,
                null,
                root ).PreviewAsync( "remove", ["aether"], null, CancellationToken.None );
            True( !unknown.Commitable );
            Equal( "Package aether is not installed.", unknown.Reason );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task PacmanCatalogRejectsUnsafePreviewRequestsAsync( )
    {
        var root = CreatePacmanDatabaseFixture( );
        try
        {
            ScriptedPackageRunner runner = new( );
            PacmanCatalog catalog = new( runner.RunAsync, null, root );
            string[] tooMany = [.. Enumerable.Range( 0, 17 ).Select( index => $"pkg{index}" )];

            await ThrowsAsync<InvalidOperationException>( ( ) =>
                catalog.PreviewAsync( "install", tooMany, null, CancellationToken.None ).AsTask( ) );
            await ThrowsAsync<InvalidOperationException>( ( ) =>
                catalog.PreviewAsync( "install", ["--force"], null, CancellationToken.None )
                    .AsTask( ) );
            await ThrowsAsync<InvalidOperationException>( ( ) =>
                catalog.PreviewAsync( "remove", ["core/jq"], null, CancellationToken.None )
                    .AsTask( ) );
            await ThrowsAsync<InvalidOperationException>( ( ) =>
                catalog.PreviewAsync( "install", ["base=1"], null, CancellationToken.None )
                    .AsTask( ) );
            await ThrowsAsync<InvalidOperationException>( ( ) =>
                catalog.PreviewAsync( "install", [], null, CancellationToken.None ).AsTask( ) );
            await ThrowsAsync<InvalidOperationException>( ( ) =>
                catalog.PreviewAsync( "upgrade", ["jq"], null, CancellationToken.None ).AsTask( ) );

            Equal( 0, runner.Invocations.Count );

            Throws<InvalidOperationException>( ( ) =>
                PacmanCatalog.BuildCommitArgv( "upgrade", ["jq"] ) );
            Throws<InvalidOperationException>( ( ) =>
                PacmanCatalog.BuildCommitArgv( "install", ["-rf"] ) );
            Throws<InvalidOperationException>( ( ) =>
                PacmanCatalog.BuildCommitArgv( "install", [] ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task PacmanCatalogRefusesCommitWhileLockedAsync( )
    {
        var root = CreatePacmanDatabaseFixture( );
        try
        {
            File.WriteAllText( Path.Combine( root, "db.lck" ), string.Empty );
            ScriptedPackageRunner runner = new( );
            runner.Script(
                "pacman",
                "-S",
                PackageProcess( 0, "cmatrix|2.0-3|extra|24576|https://example.invalid/cmatrix\n" ) );
            runner.Script( "expac", "-S", PackageProcess( 0, "cmatrix\t81920\n" ) );
            PacmanCatalog catalog = new( runner.RunAsync, null, root );

            True( catalog.IsDatabaseLocked( ) );
            var preview = await catalog.PreviewAsync(
                "install",
                ["cmatrix"],
                null,
                CancellationToken.None );

            Equal( 1, preview.Entries.Count );
            True( !preview.Commitable );
            True( preview.Reason is null );
            Equal(
                "Another package operation holds the pacman database lock.",
                string.Join( '|', preview.Warnings ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
