using System.Security.Cryptography;
using System.Text;
using CommandDeck.PackageHelper;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void PackageDatabaseIdentityMatchesTheSharedDefinition( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-package-identity-{Guid.NewGuid( ):N}" );
        try
        {
            Directory.CreateDirectory( Path.Combine( root, "sync" ) );
            Directory.CreateDirectory( Path.Combine( root, "local" ) );
            File.WriteAllText( Path.Combine( root, "sync", "core.db" ), "core" );
            File.WriteAllText( Path.Combine( root, "sync", "extra.db" ), "extra" );
            File.WriteAllText( Path.Combine( root, "sync", "ignored.txt" ), "not a database" );
            File.WriteAllText( Path.Combine( root, "local", "ALPM_DB_VERSION" ), "9\n" );

            // The daemon's preview and the helper's commit check both call this
            // one definition; the expected value is written out independently.
            var identity = PacmanDatabaseIdentity.Compute( root );
            Equal( 64, identity.Length );
            Equal( identity, PacmanDatabaseIdentity.Compute( root ) );
            Equal( ExpectedDatabaseIdentity( root ), identity );
            Equal(
                new PacmanCatalog( pacmanDatabaseDirectory: root ).ComputeDatabaseIdentity( ),
                identity );

            File.WriteAllText( Path.Combine( root, "sync", "core.db" ), "core updated" );
            var afterDatabaseChange = PacmanDatabaseIdentity.Compute( root );
            True( !string.Equals( identity, afterDatabaseChange, StringComparison.Ordinal ) );

            File.WriteAllText( Path.Combine( root, "local", "ALPM_DB_VERSION" ), "10\n" );
            var afterVersionChange = PacmanDatabaseIdentity.Compute( root );
            True( !string.Equals(
                afterDatabaseChange,
                afterVersionChange,
                StringComparison.Ordinal ) );
            Equal( ExpectedDatabaseIdentity( root ), afterVersionChange );
            Equal(
                new PacmanCatalog( pacmanDatabaseDirectory: root ).ComputeDatabaseIdentity( ),
                afterVersionChange );

            Equal( Path.Combine( root, "db.lck" ), PacmanDatabaseIdentity.LockFilePath( root ) );
            Equal(
                PacmanCatalog.UnavailableIdentity,
                PacmanDatabaseIdentity.Compute( Path.Combine( root, "absent" ) ) );

            // Neither side may grow a private copy of the definition again.
            var repository = FindRepositoryRoot( );
            foreach ( var project in new[] { "src/CommandDeck.PackageHelper", "src/CommandDeck.Infrastructure" } )
            {
                AssertSourcesExclude( Path.Combine( repository, project ), "ALPM_DB_VERSION" );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void PacmanTransactionDigestIsStable( )
    {
        var install = PacmanTransactionDigest.Compute( "install", "b|2.0|extra\na|1.0|core\n" );
        Equal( 64, install.Length );
        Equal( install, PacmanTransactionDigest.Compute( "install", "a|1.0|core\r\nb|2.0|extra" ) );
        True( !string.Equals(
            install,
            PacmanTransactionDigest.Compute( "remove", "a|1.0|core\nb|2.0|extra\n" ),
            StringComparison.Ordinal ) );
        Equal(
            PacmanTransactionDigest.Compute( "remove", "alpha|1.0\n" ),
            PacmanTransactionDigest.Compute( "remove", "alpha|1.0|\n" ) );
        Equal(
            "-S --print --print-format %n|%v|%r --needed -- alpha beta",
            string.Join( ' ', PacmanTransactionDigest.BuildPrintArguments(
                PackageOperations.Install,
                ["alpha", "beta"] ) ) );
        Equal(
            "-Rs --print --print-format %n|%v -- alpha",
            string.Join( ' ', PacmanTransactionDigest.BuildPrintArguments(
                PackageOperations.Remove,
                ["alpha"] ) ) );
        Throws<ArgumentOutOfRangeException>( ( ) =>
            PacmanTransactionDigest.BuildPrintArguments( "upgrade", ["alpha"] ) );
        Throws<ArgumentOutOfRangeException>( ( ) =>
            PackageHelperCore.BuildCommitArguments( "upgrade", ["alpha"] ) );

        // The daemon prints five install fields and the helper prints three;
        // both must digest to the same value.
        Equal(
            PacmanTransactionDigest.Compute( PackageOperations.Install, "alpha|1.0|core\n" ),
            PacmanTransactionDigest.Compute(
                PackageOperations.Install,
                "alpha|1.0|core|100|200\n" ) );
        Equal(
            PacmanTransactionDigest.Compute( PackageOperations.Install, "alpha|1.0|core\n" ),
            PacmanTransactionDigest.Compute(
                PackageOperations.Install,
                "--root|9|core\nalpha|1.0|core\nnot a plan line\n" ) );

        List<PackageTransactionEntry> installEntries =
        [
            new() { Name = "alpha", TargetVersion = "1.0", Repository = "core" },
            new() { Name = "beta", TargetVersion = "2.0", Repository = "extra" }
        ];
        Equal(
            PacmanCatalog.ComputeTransactionDigest( PackageOperations.Install, installEntries ),
            PacmanTransactionDigest.Compute(
                PackageOperations.Install,
                "beta|2.0|extra\nalpha|1.0|core\n" ) );

        List<PackageTransactionEntry> removeEntries =
        [
            new() { Name = "alpha", CurrentVersion = "1.0" }
        ];
        Equal(
            PacmanCatalog.ComputeTransactionDigest( PackageOperations.Remove, removeEntries ),
            PacmanTransactionDigest.Compute( PackageOperations.Remove, "alpha|1.0\n" ) );
    }

    private static void PackageHelperClientMapsMissingHelperFailures( )
    {
        var unknownService = PackageHelperClient.MapStatusFailure(
            "org.freedesktop.DBus.Error.ServiceUnknown",
            "The name org.commanddeck.Package1 was not provided by /usr/lib/commanddeck." );
        True( !unknownService.Installed );
        Equal( "The package helper is not installed.", unknownService.Message );

        var noOwner = PackageHelperClient.MapStatusFailure(
            "org.freedesktop.DBus.Error.NameHasNoOwner",
            null );
        True( !noOwner.Installed );
        Equal( "The package helper is not installed.", noOwner.Message );

        var other = PackageHelperClient.MapStatusFailure(
            "org.freedesktop.DBus.Error.NoReply",
            "the reply timed out at /var/lib/pacman" );
        True( !other.Installed );
        Equal( "The package helper reported a failure.", other.Message );

        True( PackageHelperClient.VersionMatches( PackageHelperProtocol.ProtocolVersion ) );
        True( !PackageHelperClient.VersionMatches( "2" ) );
        True( !PackageHelperClient.VersionMatches( null ) );

        string[] errorNames =
        [
            PackageHelperErrors.DatabaseChanged,
            PackageHelperErrors.TransactionChanged,
            PackageHelperErrors.DatabaseLocked,
            PackageHelperErrors.Busy,
            PackageHelperErrors.NotAuthorized,
            PackageHelperErrors.NoAuthenticationAgent,
            PackageHelperErrors.InvalidOperation,
            PackageHelperErrors.InvalidNames,
            PackageHelperErrors.InvalidRequest,
            PackageHelperErrors.UnknownOperation,
            PackageHelperErrors.Failed
        ];
        foreach ( var errorName in errorNames )
        {
            var message = PackageHelperClient.DescribeError( errorName, "/var/lib/pacman/db.lck" );
            True( message.Length > 0 );
            True( !message.Contains( '/', StringComparison.Ordinal ) );
        }

        True( errorNames.All( static name =>
            name.StartsWith( PackageHelperErrors.Prefix, StringComparison.Ordinal ) ) );
        Equal( "org.commanddeck.Package1.Error.", PackageHelperErrors.Prefix );
    }

    private static string ExpectedDatabaseIdentity( string root )
    {
        StringBuilder builder = new( );
        var databases = Directory.GetFiles( Path.Combine( root, "sync" ), "*.db" );
        Array.Sort( databases, StringComparer.Ordinal );
        foreach ( var database in databases )
        {
            FileInfo info = new( database );
            builder.Append( database ).Append( '|' )
                .Append( UnixNanoseconds( File.GetLastWriteTimeUtc( database ) ) ).Append( '|' )
                .Append( info.Length ).Append( '\n' );
        }

        var local = Path.Combine( root, "local" );
        builder.Append( "local|" )
            .Append( UnixNanoseconds( Directory.GetLastWriteTimeUtc( local ) ) ).Append( '\n' );
        builder.Append( "version|" )
            .Append( File.ReadAllText( Path.Combine( local, "ALPM_DB_VERSION" ) ).Trim( ) )
            .Append( '\n' );
        return Convert.ToHexStringLower(
            SHA256.HashData( Encoding.UTF8.GetBytes( builder.ToString( ) ) ) );
    }

    private static long UnixNanoseconds( DateTime utc ) =>
        (utc.Ticks - DateTime.UnixEpoch.Ticks) * 100L;
}
