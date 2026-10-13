using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

/// <summary>
/// Runs one literal package command argv. No shell is ever involved.
/// </summary>
public delegate ValueTask<ProcessResult> PackageCommandRunner(
    string fileName,
    IReadOnlyList<string> arguments,
    CancellationToken cancellationToken );

/// <summary>
/// Unprivileged, read-only view of the local Arch Linux package databases.
/// Every command is a literal argv; nothing here mutates the system.
/// </summary>
public sealed partial class PacmanCatalog
{
    public const string DefaultDatabaseDirectory = PacmanDatabaseIdentity.DefaultRoot;
    public const string UnavailableIdentity = PacmanDatabaseIdentity.UnavailableIdentity;

    internal const string PacmanExecutable = "pacman";
    internal const string ExpacExecutable = "expac";
    internal const int MaximumMessageCharacters = 512;

    private const int PreviewIdentifierBytes = 32;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds( 20 );

    private static readonly ProcessExecutionLimits CommandLimits = new( )
    {
        MaximumStandardOutputBytes = 1024 * 1024,
        MaximumStandardErrorBytes = 64 * 1024,
        MaximumMemoryBytes = 512L * 1024 * 1024,
        ClearEnvironment = true
    };

    private static readonly string[] CommitFlags =
        ["--noconfirm", "--noprogressbar", "--color", "never"];

    private readonly PackageCommandRunner _runner;
    private readonly TimeProvider _clock;
    private readonly string _databaseDirectory;

    public PacmanCatalog(
        PackageCommandRunner? runner = null,
        TimeProvider? clock = null,
        string pacmanDatabaseDirectory = DefaultDatabaseDirectory )
    {
        _runner = runner ?? RunDefaultAsync;
        _clock = clock ?? TimeProvider.System;
        _databaseDirectory = string.IsNullOrWhiteSpace( pacmanDatabaseDirectory )
            ? DefaultDatabaseDirectory
            : pacmanDatabaseDirectory;
    }

    /// <summary>
    /// Hashes the identity of the local package databases so a preview can be
    /// bound to the exact database state it was computed from. The privileged
    /// helper recomputes the same <see cref="PacmanDatabaseIdentity"/> before
    /// it commits.
    /// </summary>
    public string ComputeDatabaseIdentity( ) => PacmanDatabaseIdentity.Compute( _databaseDirectory );

    public bool IsDatabaseLocked( )
    {
        try
        {
            return File.Exists( PacmanDatabaseIdentity.LockFilePath( _databaseDirectory ) );
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException )
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the literal argv the privileged commit would run. Display only:
    /// this type never executes it.
    /// </summary>
    public static IReadOnlyList<string> BuildCommitArgv(
        string operation,
        IReadOnlyList<string> validatedNames )
    {
        ArgumentNullException.ThrowIfNull( validatedNames );
        if ( validatedNames.Count == 0 )
        {
            throw new InvalidOperationException( "At least one package name is required." );
        }

        var mode = operation switch
        {
            PackageOperations.Install => "-S",
            PackageOperations.Remove => "-Rns",
            _ => throw new InvalidOperationException(
                "The package operation must be install or remove." )
        };

        List<string> argv = [PacmanExecutable, mode];
        if ( string.Equals( operation, PackageOperations.Install, StringComparison.Ordinal ) )
        {
            argv.Add( "--needed" );
        }

        argv.AddRange( CommitFlags );
        foreach ( var name in validatedNames )
        {
            if ( !AgentContractValidator.IsValidPackageName( name ) )
            {
                throw new InvalidOperationException( "A package name is invalid." );
            }

            argv.Add( name );
        }

        return argv;
    }

    internal static string ComputeTransactionDigest(
        string operation,
        IReadOnlyList<PackageTransactionEntry> entries )
    {
        List<string> lines = [];
        foreach ( var entry in entries )
        {
            var version = entry.TargetVersion ?? entry.CurrentVersion ?? string.Empty;
            lines.Add( $"{entry.Name}|{version}|{entry.Repository ?? string.Empty}" );
        }

        lines.Sort( StringComparer.Ordinal );
        StringBuilder builder = new( );
        builder.Append( operation ).Append( '\n' );
        foreach ( var line in lines )
        {
            builder.Append( line ).Append( '\n' );
        }

        return Convert.ToHexStringLower(
            SHA256.HashData( Encoding.UTF8.GetBytes( builder.ToString( ) ) ) );
    }

    /// <summary>
    /// Removes control characters and caps length so process text can never
    /// smuggle escape sequences or unbounded payloads into a response.
    /// </summary>
    internal static string Sanitize( string? value )
    {
        if ( string.IsNullOrEmpty( value ) )
        {
            return string.Empty;
        }

        StringBuilder builder = new( Math.Min( value.Length, MaximumMessageCharacters ) );
        foreach ( var character in value )
        {
            if ( builder.Length >= MaximumMessageCharacters )
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

    private static string NewPreviewIdentifier( ) =>
        Convert.ToHexStringLower( RandomNumberGenerator.GetBytes( PreviewIdentifierBytes ) );

    private static async ValueTask<ProcessResult> RunDefaultAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken ) =>
        await ProcessRunner.RunBoundedAsync(
            fileName,
            arguments,
            workingDirectory: null,
            CommandTimeout,
            CommandLimits,
            cancellationToken ).ConfigureAwait( false );
}
