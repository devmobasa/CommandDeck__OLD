using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

/// <summary>
/// Identity of the local Pacman databases. The daemon binds a package preview
/// to it, and the privileged helper recomputes it in its own process and
/// refuses to commit when the two values differ. Both compile this one
/// definition, so they cannot drift apart.
/// </summary>
/// <remarks>
/// The identity is the lowercase SHA-256 hex digest of the UTF-8 text built
/// from, in order: one <c>absolutePath|mtimeUnixNanoseconds|sizeBytes\n</c>
/// line per <c>sync/*.db</c> file sorted ordinal by absolute path, one
/// <c>local|mtimeUnixNanoseconds\n</c> line for the local database directory
/// (<c>0</c> when it is absent), and one <c>version|text\n</c> line holding
/// the sanitized contents of <c>local/ALPM_DB_VERSION</c>.
/// </remarks>
public static class PacmanDatabaseIdentity
{
    public const string DefaultRoot = "/var/lib/pacman";
    public const string UnavailableIdentity = "unavailable";
    public const string LockFileName = "db.lck";

    private const string DatabaseVersionFileName = "ALPM_DB_VERSION";
    private const int MaximumVersionCharacters = 512;

    public static string LockFilePath( string root ) => Path.Combine( root, LockFileName );

    public static string Compute( string root )
    {
        ArgumentException.ThrowIfNullOrEmpty( root );
        try
        {
            if ( !Directory.Exists( root ) )
            {
                return UnavailableIdentity;
            }

            StringBuilder builder = new( );
            AppendSyncDatabases( builder, root );
            AppendLocalDatabase( builder, root );
            return Convert.ToHexStringLower(
                SHA256.HashData( Encoding.UTF8.GetBytes( builder.ToString( ) ) ) );
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or System.Security.SecurityException )
        {
            return UnavailableIdentity;
        }
    }

    private static void AppendSyncDatabases( StringBuilder builder, string root )
    {
        var syncDirectory = Path.Combine( root, "sync" );
        if ( !Directory.Exists( syncDirectory ) )
        {
            return;
        }

        var databases = Directory.GetFiles( syncDirectory, "*.db" );
        Array.Sort( databases, StringComparer.Ordinal );
        foreach ( var database in databases )
        {
            FileInfo info = new( database );
            builder.Append( database ).Append( '|' )
                .Append( FormatUnixNanoseconds( File.GetLastWriteTimeUtc( database ) ) ).Append( '|' )
                .Append( info.Length.ToString( CultureInfo.InvariantCulture ) ).Append( '\n' );
        }
    }

    private static void AppendLocalDatabase( StringBuilder builder, string root )
    {
        var localDirectory = Path.Combine( root, "local" );
        builder.Append( "local|" )
            .Append( Directory.Exists( localDirectory )
                ? FormatUnixNanoseconds( Directory.GetLastWriteTimeUtc( localDirectory ) )
                : "0" )
            .Append( '\n' );

        var versionFile = Path.Combine( localDirectory, DatabaseVersionFileName );
        builder.Append( "version|" )
            .Append( File.Exists( versionFile )
                ? Sanitize( File.ReadAllText( versionFile ) )
                : string.Empty )
            .Append( '\n' );
    }

    private static string FormatUnixNanoseconds( DateTime value ) =>
        ((value.Ticks - DateTime.UnixEpoch.Ticks) * 100L).ToString( CultureInfo.InvariantCulture );

    private static string Sanitize( string? value )
    {
        if ( string.IsNullOrEmpty( value ) )
        {
            return string.Empty;
        }

        StringBuilder builder = new( Math.Min( value.Length, MaximumVersionCharacters ) );
        foreach ( var character in value )
        {
            if ( builder.Length >= MaximumVersionCharacters )
            {
                break;
            }

            if ( !char.IsControl( character ) )
            {
                builder.Append( character );
            }
        }

        return builder.ToString( ).Trim( );
    }
}
