using System.IO.Compression;
using System.Security.Cryptography;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public sealed class StagedExtension : IAsyncDisposable
{
    internal StagedExtension( string stagingDirectory, string digest, ExtensionManifest manifest )
    {
        StagingDirectory = stagingDirectory;
        Digest = digest;
        Manifest = manifest;
    }

    public string StagingDirectory { get; }
    public string Digest { get; }
    public ExtensionManifest Manifest { get; }

    public string Materialize( string artifactRoot, string installationKey )
    {
        if ( installationKey.Length != 64 || installationKey.Any( c => !char.IsAsciiHexDigit( c ) ) )
        {
            throw new ArgumentException( "The installation key is invalid.", nameof( installationKey ) );
        }
        var versions = Path.Combine( artifactRoot, installationKey, "versions" );
        ExtensionArchive.CreatePrivateDirectory( versions );
        var destination = Path.Combine( versions, Digest );
        if ( !Directory.Exists( destination ) )
        {
            Directory.Move( Path.Combine( StagingDirectory, "content" ), destination );
        }
        // Persist the rename and each newly created ancestor before the database
        // can refer to this artifact. A surviving activation must have its files.
        LinuxDirectoryDurability.FlushParent( destination );
        LinuxDirectoryDurability.FlushParent( versions );
        LinuxDirectoryDurability.FlushParent( Path.GetDirectoryName( versions )! );
        LinuxDirectoryDurability.FlushParent( Path.GetFullPath( artifactRoot ) );
        return destination;
    }

    public ValueTask DisposeAsync( )
    {
        if ( Directory.Exists( StagingDirectory ) )
        {
            Directory.Delete( StagingDirectory, recursive: true );
        }
        return ValueTask.CompletedTask;
    }
}

public static class ExtensionArchive
{
    public const long MaximumCompressedBytes = 200L * 1024 * 1024;
    public const long MaximumExpandedBytes = 500L * 1024 * 1024;
    private const int MaximumFiles = 10000;
    private const string ManifestFile = "commanddeck.extension.json";

    public static async ValueTask<StagedExtension> StageAsync(
        string archivePath, string artifactRoot, CancellationToken cancellationToken, string? expectedDigest = null )
    {
        CreatePrivateDirectory( artifactRoot );
        var staging = Path.Combine( artifactRoot, ".staging-" + Guid.NewGuid( ).ToString( "N" ) );
        CreatePrivateDirectory( staging );
        try
        {
            var copy = Path.Combine( staging, "package.cdext" );
            var digest = await CopyArchiveAsync( archivePath, copy, cancellationToken ).ConfigureAwait( false );
            if ( expectedDigest is not null && !string.Equals( digest, expectedDigest, StringComparison.OrdinalIgnoreCase ) )
            {
                throw new InvalidDataException( "The extension archive does not match its expected digest." );
            }
            using var archive = ZipFile.OpenRead( copy );
            ValidateEntries( archive );
            var entry = archive.GetEntry( ManifestFile );
            if ( entry is null )
            {
                throw new InvalidDataException( "The archive has no root manifest." );
            }

            if ( entry.Length > ManifestValidation.MaximumManifestBytes )
            {
                throw new InvalidDataException( "The manifest is too large." );
            }
            using MemoryStream manifestBytes = new( );
            await using ( var input = entry.Open( ) )
            {
                await CopyBoundedAsync( input, manifestBytes, ManifestValidation.MaximumManifestBytes, cancellationToken ).ConfigureAwait( false );
            }
            var manifest = ManifestValidation.Parse( manifestBytes.ToArray( ) );
            var content = Path.Combine( staging, "content" );
            CreatePrivateDirectory( content );
            await ExtractAsync( archive, content, manifest, cancellationToken ).ConfigureAwait( false );
            return new StagedExtension( staging, digest, manifest );
        }
        catch
        {
            Directory.Delete( staging, recursive: true );
            throw;
        }
    }

    private static async Task<string> CopyArchiveAsync( string source, string destination, CancellationToken cancellationToken )
    {
        await using var input = new FileStream( source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan );
        if ( input.Length > MaximumCompressedBytes )
        {
            throw new InvalidDataException( "The extension archive exceeds 200 MiB." );
        }
        await using var output = new FileStream( destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous );
        await CopyBoundedAsync( input, output, MaximumCompressedBytes, cancellationToken ).ConfigureAwait( false );
        await output.FlushAsync( cancellationToken ).ConfigureAwait( false );
        output.Flush( flushToDisk: true );
        output.Position = 0;
        return Convert.ToHexString( await SHA256.HashDataAsync( output, cancellationToken ).ConfigureAwait( false ) ).ToLowerInvariant( );
    }

    private static void ValidateEntries( ZipArchive archive )
    {
        if ( archive.Entries.Count is < 1 or > MaximumFiles )
        {
            throw new InvalidDataException( "The archive file count is invalid." );
        }
        HashSet<string> paths = new( StringComparer.OrdinalIgnoreCase );
        long expanded = 0;
        foreach ( var entry in archive.Entries )
        {
            var directory = entry.FullName.EndsWith( '/' );
            var name = directory ? entry.FullName[..^1] : entry.FullName;
            ManifestValidation.ValidatePackagePath( name );
            if ( !paths.Add( name ) )
            {
                throw new InvalidDataException( "The archive contains duplicate or colliding paths." );
            }
            var type = (entry.ExternalAttributes >> 16) & 0xF000;
            if ( (type != 0 && type != (directory ? 0x4000 : 0x8000)) || ((entry.ExternalAttributes >> 16) & 0xE00) != 0 )
            {
                throw new InvalidDataException( "The archive contains a link, special file, or unsupported permissions." );
            }
            expanded = checked(expanded + entry.Length);
            if ( expanded > MaximumExpandedBytes || (directory && entry.Length != 0) ||
                 entry.Length > Math.Max( 1024 * 1024, entry.CompressedLength * 200 ) )
            {
                throw new InvalidDataException( "The archive exceeds its expansion limits." );
            }
        }
        var files = archive.Entries.Where( entry => !entry.FullName.EndsWith( '/' ) ).Select( entry => entry.FullName ).ToHashSet( StringComparer.OrdinalIgnoreCase );
        foreach ( var name in paths )
        {
            var parent = name;
            while ( parent.LastIndexOf( '/' ) is var slash && slash > 0 )
            {
                parent = parent[..slash];
                if ( files.Contains( parent ) )
                {
                    throw new InvalidDataException( "An archive file is also used as a directory." );
                }
            }
        }
    }

    private static async Task ExtractAsync( ZipArchive archive, string destination, ExtensionManifest manifest, CancellationToken cancellationToken )
    {
        var entrypoints = manifest.Targets.Values.Select( target => target.Entrypoint ).ToHashSet( StringComparer.Ordinal );
        long remaining = MaximumExpandedBytes;
        foreach ( var entry in archive.Entries )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            if ( entry.FullName.EndsWith( '/' ) )
            {
                continue;
            }
            var path = Path.Combine( destination, entry.FullName );
            CreatePrivateDirectory( Path.GetDirectoryName( path )! );
            var executable = ((entry.ExternalAttributes >> 16) & 0x49) != 0;
            if ( executable && !manifest.Targets.Keys.Any( rid => entry.FullName.StartsWith( "bin/" + rid + "/", StringComparison.Ordinal ) ) )
            {
                throw new InvalidDataException( "The archive has an executable outside its target directories." );
            }
            await using ( var input = entry.Open( ) )
            {
                await using ( var output = new FileStream( path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous ) )
                {
                    var written = await CopyBoundedAsync( input, output, Math.Min( entry.Length, remaining ), cancellationToken ).ConfigureAwait( false );
                    if ( written != entry.Length )
                    {
                        throw new InvalidDataException( "The archive entry size is inconsistent." );
                    }
                    remaining -= written;
                    output.Flush( flushToDisk: true );
                }
            }
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( path, UnixFileMode.UserRead | (entrypoints.Contains( entry.FullName ) ? UnixFileMode.UserExecute : 0) );
            }
        }
        foreach ( var entrypoint in entrypoints )
        {
            if ( !File.Exists( Path.Combine( destination, entrypoint ) ) )
            {
                throw new InvalidDataException( "A declared executable is missing." );
            }
        }
        foreach ( var (rid, target) in manifest.Targets )
        {
            await ValidateElfAsync( Path.Combine( destination, target.Entrypoint ), rid, cancellationToken ).ConfigureAwait( false );
        }
        foreach ( var command in manifest.Commands )
        {
            if ( command.Icon is not null && !File.Exists( Path.Combine( destination, command.Icon ) ) )
            {
                throw new InvalidDataException( "A declared icon is missing." );
            }
        }
        foreach ( var directory in Directory.EnumerateDirectories( destination, "*", SearchOption.AllDirectories ).OrderByDescending( path => path.Length ) )
        {
            LinuxDirectoryDurability.FlushParent( Path.Combine( directory, "." ) );
        }
        LinuxDirectoryDurability.FlushParent( Path.Combine( destination, "." ) );
    }

    private static async Task ValidateElfAsync( string path, string rid, CancellationToken cancellationToken )
    {
        await using var input = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous );
        var header = new byte[64];
        await input.ReadExactlyAsync( header, cancellationToken ).ConfigureAwait( false );
        var machine = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( header.AsSpan( 18, 2 ) );
        var kind = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( header.AsSpan( 16, 2 ) );
        if ( !header.AsSpan( 0, 4 ).SequenceEqual( "\u007fELF"u8 ) || header[4] != 2 || header[5] != 1 || header[6] != 1 ||
             kind is not (2 or 3) || machine != (rid == ExtensionTargets.LinuxX64 ? ExtensionTargets.ElfX64 : ExtensionTargets.ElfArm64) )
        {
            throw new InvalidDataException( "The executable is not a 64-bit ELF target for its declared architecture." );
        }
    }

    private static async Task<long> CopyBoundedAsync( Stream input, Stream output, long maximum, CancellationToken cancellationToken )
    {
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ( (count = await input.ReadAsync( buffer, cancellationToken ).ConfigureAwait( false )) > 0 )
        {
            total += count;
            if ( total > maximum )
            {
                throw new InvalidDataException( "The extension content exceeds its byte limit." );
            }
            await output.WriteAsync( buffer.AsMemory( 0, count ), cancellationToken ).ConfigureAwait( false );
        }
        return total;
    }

    internal static void CreatePrivateDirectory( string path )
    {
        if ( OperatingSystem.IsLinux( ) )
        {
            Directory.CreateDirectory( path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }
        else
        {
            Directory.CreateDirectory( path );
        }
    }
}
