using System.Security.Cryptography;
using System.Text.Json;

namespace CommandDeck;

/// <summary>
/// Identity of the managed document as last observed: the Linux file identity
/// token (mount, device, inode, size, timestamps) and a SHA-256 of its bytes.
/// Both are null when no document exists. A write compares the identity it was
/// based on with the identity on disk and refuses to replace a file that changed
/// underneath it. This is conflict detection, not exclusion of every same-user race.
/// </summary>
public sealed record AiProviderSettingsIdentity( string? FileIdentity, string? ContentDigest )
{
    public static AiProviderSettingsIdentity Absent { get; } = new( null, null );

    public bool Exists => FileIdentity is not null;
}

public sealed class AiProviderSettingsLoadResult
{
    public AiProviderSettingsDocument? Document { get; init; }
    public required AiProviderSettingsIdentity Identity { get; init; }
    public bool Exists { get; init; }
    public AiError? Error { get; init; }
}

/// <summary>
/// Daemon-owned persistence for <see cref="AiProviderSettingsDocument"/>. The
/// document sits next to the main configuration, is created with mode 0600
/// through a same-directory staged write, and is never merged with the JSONC
/// file: once it exists it owns the enabled flag and the provider set.
/// </summary>
public sealed class AiProviderSettingsStore
{
    public const string FileName = "ai-providers.json";
    // A second source-generated context so the on-disk document stays readable
    // without falling back to reflection-based serialization (Native AOT).
    private static readonly CommandDeckJsonContext PrettyContext = new( new JsonSerializerOptions( CommandDeckJsonContext.Default.Options )
    {
        WriteIndented = true
    } );
    private readonly string _directory;

    public AiProviderSettingsStore( string path )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( path );
        Path = System.IO.Path.GetFullPath( path );
        var value = System.IO.Path.GetDirectoryName( Path );
        if ( value is null )
        {
            throw new InvalidOperationException( "The provider settings path has no directory." );
        }

        _directory = value;
    }

    public string Path { get; }

    /// <summary>
    /// Resolves the managed document beside the effective configuration file so a
    /// custom <c>--config</c> location keeps its own provider document.
    /// </summary>
    public static string ResolvePath( string? configPath )
    {
        var resolved = string.IsNullOrWhiteSpace( configPath )
            ? PathExpansion.GetDefaultConfigPath( )
            : PathExpansion.Expand( configPath );
        var directory = System.IO.Path.GetDirectoryName( resolved );
        if ( directory is null )
        {
            throw new InvalidOperationException( "The configuration path has no directory." );
        }

        return System.IO.Path.Combine( directory, FileName );
    }

    public AiProviderSettingsLoadResult Load( )
    {
        byte[]? bytes;
        string identity;
        try
        {
            if ( !File.Exists( Path ) && !Directory.Exists( Path ) )
            {
                return new AiProviderSettingsLoadResult { Identity = AiProviderSettingsIdentity.Absent };
            }
            (bytes, identity) = ReadBounded( );
        }
        catch ( InvalidOperationException )
        {
            // A symlink, a directory, or an unreadable node where the document should
            // be. Fail closed rather than silently returning to legacy endpoints.
            return Invalid( "The AI provider settings document is not a readable regular file." );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
            return Invalid( "The AI provider settings document could not be read." );
        }
        if ( bytes is null )
        {
            return Invalid( "The AI provider settings document exceeds its size bound." );
        }

        AiProviderSettingsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize( bytes, CommandDeckJsonContext.Default.AiProviderSettingsDocument );
        }
        catch ( JsonException )
        {
            return Invalid( "The AI provider settings document is not valid JSON." );
        }
        if ( document is null || document.SchemaVersion != AiProviderSettingsLimits.SchemaVersion ||
            document.Revision < 0 || document.Providers is null )
        {
            return Invalid( "The AI provider settings document uses an unsupported schema." );
        }
        try
        {
            ConfigurationValidator.ValidateAi( new AiOptions
            {
                Enabled = document.Enabled,
                Providers = document.Providers
            } );
        }
        catch ( InvalidOperationException )
        {
            return Invalid( "The AI provider settings document contains an invalid provider definition." );
        }
        return new AiProviderSettingsLoadResult
        {
            Document = document,
            Exists = true,
            Identity = new AiProviderSettingsIdentity( identity, Digest( bytes ) )
        };
    }

    /// <summary>
    /// Replaces the document atomically. <paramref name="expected"/> is the identity
    /// the caller loaded; a different identity on disk means an external edit and
    /// raises a <c>conflict</c> so the newer file is not overwritten.
    /// </summary>
    public AiProviderSettingsIdentity Write(
        AiProviderSettingsDocument document,
        AiProviderSettingsIdentity expected )
    {
        ArgumentNullException.ThrowIfNull( document );
        ArgumentNullException.ThrowIfNull( expected );
        ConfigurationValidator.ValidateAi( new AiOptions { Enabled = document.Enabled, Providers = document.Providers } );
        EnsureUnchanged( expected );
        var json = JsonSerializer.Serialize( document, PrettyContext.AiProviderSettingsDocument ) + "\n";
        var payload = System.Text.Encoding.UTF8.GetBytes( json );
        if ( payload.Length > AiProviderSettingsLimits.MaximumDocumentBytes )
        {
            throw new AiOperationException(
                AiErrorCategories.InvalidConfiguration,
                safeSummary: "The AI provider settings document would exceed its size bound." );
        }
        var temporary = System.IO.Path.Combine( _directory, $".ai-providers-{Guid.NewGuid( ):N}.tmp" );
        try
        {
            EnsurePrivateDirectory( );
            using ( var stream = SafeRegularFile.CreatePrivateWriteStream( temporary ) )
            {
                stream.Write( payload );
                stream.Flush( flushToDisk: true );
            }
            // The replacement is the commit point: a crash before it leaves the old
            // document (or none) active; a crash after it leaves the new one.
            File.Move( temporary, Path, overwrite: true );
            LinuxDirectoryDurability.FlushParent( Path );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
            throw new AiOperationException(
                AiErrorCategories.PersistenceFailed,
                safeSummary: "The AI provider settings could not be written." );
        }
        finally
        {
            TryDelete( temporary );
        }
        var written = Load( );
        if ( written.Document is null )
        {
            throw new AiOperationException(
                AiErrorCategories.PersistenceFailed,
                safeSummary: "The AI provider settings were written but could not be read back." );
        }
        return written.Identity;
    }

    private void EnsureUnchanged( AiProviderSettingsIdentity expected )
    {
        AiProviderSettingsIdentity current;
        try
        {
            if ( !File.Exists( Path ) && !Directory.Exists( Path ) )
            {
                current = AiProviderSettingsIdentity.Absent;
            }
            else
            {
                (var bytes, var identity) = ReadBounded( );
                current = new AiProviderSettingsIdentity( identity, bytes is null ? null : Digest( bytes ) );
            }
        }
        catch ( Exception exception ) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException )
        {
            throw Conflict( );
        }
        if ( current != expected )
        {
            throw Conflict( );
        }
    }

    private static AiOperationException Conflict( ) => new(
        AiErrorCategories.Conflict,
        safeSummary: "The AI provider settings changed on disk since they were loaded. Reload before saving again." );

    private (byte[]? Bytes, string Identity) ReadBounded( )
    {
        using var file = SafeRegularFile.Open( Path, followLeafSymbolicLink: false );
        if ( file.Length > AiProviderSettingsLimits.MaximumDocumentBytes )
        {
            return (null, file.IdentityToken);
        }
        var buffer = new byte[file.Length];
        var total = 0;
        while ( total < buffer.Length )
        {
            var read = RandomAccess.Read( file.Handle, buffer.AsSpan( total ), total );
            if ( read <= 0 )
            {
                break;
            }
            total += read;
        }
        return (buffer[..total], file.IdentityToken);
    }

    private void EnsurePrivateDirectory( )
    {
        if ( Directory.Exists( _directory ) )
        {
            return;
        }
        Directory.CreateDirectory( _directory );
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( _directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }
    }

    private static void TryDelete( string path )
    {
        try
        {
            if ( File.Exists( path ) )
            {
                File.Delete( path );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
        }
    }

    private static string Digest( byte[] bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant( );

    private static AiProviderSettingsLoadResult Invalid( string summary ) => new( )
    {
        Exists = true,
        Identity = AiProviderSettingsIdentity.Absent,
        Error = new AiError { Category = AiErrorCategories.InvalidConfiguration, Summary = summary }
    };
}
