using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace CommandDeck;

internal static class ArchiveContentExtractor
{
    private const int MaximumEntries = 256;
    private const int MaximumEntryBytes = 1024 * 1024;
    private const int MaximumTotalBytes = 4 * 1024 * 1024;
    private const int MaximumExpansionRatio = 100;
    private static readonly UTF8Encoding StrictUtf8 = new( false, true );

    private static readonly HashSet<string> TextExtensions = new( StringComparer.OrdinalIgnoreCase )
    {
        ".txt", ".md", ".markdown", ".rst", ".org", ".adoc",
        ".cs", ".fs", ".vb", ".cpp", ".cc", ".c", ".h", ".hpp",
        ".rs", ".go", ".java", ".kt", ".kts", ".swift",
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
        ".py", ".rb", ".php", ".sh", ".bash", ".zsh", ".fish",
        ".lua", ".qml", ".sql", ".json", ".jsonc", ".toml",
        ".yaml", ".yml", ".xml", ".ini", ".conf", ".cfg",
        ".desktop", ".service", ".env", ".log", ".css", ".html"
    };

    public static bool IsSupported( string path ) =>
        path.EndsWith( ".zip", StringComparison.OrdinalIgnoreCase ) ||
        path.EndsWith( ".tar", StringComparison.OrdinalIgnoreCase ) ||
        path.EndsWith( ".tar.gz", StringComparison.OrdinalIgnoreCase ) ||
        path.EndsWith( ".tgz", StringComparison.OrdinalIgnoreCase );

    public static async ValueTask<string?> ExtractAsync(
        SafeRegularFile file,
        string path,
        long archiveBytes,
        int configuredOutputBytes,
        CancellationToken cancellationToken )
    {
        var totalLimit = Math.Min( configuredOutputBytes, MaximumTotalBytes );
        if ( totalLimit <= 0 || archiveBytes <= 0 )
        {
            return null;
        }

        // Reopen the verified inode rather than the path, which may since
        // have been replaced by a FIFO or device.
        var source = file.ProcessDescriptorPath;
        var compressed = path.EndsWith( ".tar.gz", StringComparison.OrdinalIgnoreCase ) ||
            path.EndsWith( ".tgz", StringComparison.OrdinalIgnoreCase );
        try
        {
            return path.EndsWith( ".zip", StringComparison.OrdinalIgnoreCase )
                ? await ExtractZipAsync( source, archiveBytes, totalLimit, cancellationToken )
                    .ConfigureAwait( false )
                : await ExtractTarAsync( source, compressed, archiveBytes, totalLimit, cancellationToken )
                    .ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is InvalidDataException or IOException or
            DecoderFallbackException )
        {
            return null;
        }
    }

    private static async ValueTask<string?> ExtractZipAsync(
        string source,
        long archiveBytes,
        int totalLimit,
        CancellationToken cancellationToken )
    {
        await using FileStream input = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan );
        using ZipArchive archive = new( input, ZipArchiveMode.Read, leaveOpen: false );
        if ( archive.Entries.Count > MaximumEntries )
        {
            return null;
        }

        BoundedArchiveText output = new( archiveBytes, totalLimit );
        foreach ( var entry in archive.Entries )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            if ( !EligibleName( entry.FullName ) || entry.FullName.EndsWith( '/' ) || IsZipLink( entry ) )
            {
                continue;
            }
            if ( entry.Length > MaximumEntryBytes ||
                entry.Length > archiveBytes * MaximumExpansionRatio )
            {
                return null;
            }

            await using var stream = entry.Open( );
            if ( !await output.AppendAsync( entry.FullName, stream, cancellationToken )
                .ConfigureAwait( false ) )
            {
                return null;
            }
        }
        return output.Build( );
    }

    private static async ValueTask<string?> ExtractTarAsync(
        string source,
        bool compressed,
        long archiveBytes,
        int totalLimit,
        CancellationToken cancellationToken )
    {
        await using FileStream file = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan );
        Stream input = compressed
            ? new GZipStream( file, CompressionMode.Decompress, leaveOpen: false )
            : file;
        await using ( input.ConfigureAwait( false ) )
        {
            TarReader archive = new( input, leaveOpen: false );
            BoundedArchiveText output = new( archiveBytes, totalLimit );
            var entries = 0;
            TarEntry? entry;
            while ( (entry = await archive.GetNextEntryAsync( copyData: false, cancellationToken )
                       .ConfigureAwait( false )) is not null )
            {
                if ( ++entries > MaximumEntries )
                {
                    return null;
                }
                if ( entry.EntryType is not TarEntryType.RegularFile ||
                    entry.DataStream is null ||
                    !EligibleName( entry.Name ) )
                {
                    continue;
                }
                if ( entry.Length > MaximumEntryBytes ||
                    entry.Length > archiveBytes * MaximumExpansionRatio )
                {
                    return null;
                }
                if ( !await output.AppendAsync( entry.Name, entry.DataStream, cancellationToken )
                    .ConfigureAwait( false ) )
                {
                    return null;
                }
            }
            return output.Build( );
        }
    }

    private static bool EligibleName( string name )
    {
        if ( string.IsNullOrWhiteSpace( name ) || Path.IsPathRooted( name ) ||
            name.Any( char.IsControl ) || IsSupported( name ) ||
            !TextExtensions.Contains( Path.GetExtension( name ) ) )
        {
            return false;
        }
        return !name.Split( ['/', '\\'], StringSplitOptions.RemoveEmptyEntries )
            .Any( segment => segment == ".." );
    }

    private static bool IsZipLink( ZipArchiveEntry entry )
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        return (unixMode & UnixFileTypeMask) == UnixSymbolicLink ||
            (entry.ExternalAttributes & ( int ) FileAttributes.ReparsePoint) != 0;
    }

    private sealed class BoundedArchiveText( long archiveBytes, int maximumBytes )
    {
        private readonly StringBuilder _text = new( );
        private long _observedBytes;

        public async ValueTask<bool> AppendAsync(
            string name,
            Stream stream,
            CancellationToken cancellationToken )
        {
            var nameBytes = StrictUtf8.GetByteCount( name );
            var framingBytes = 2;
            var overhead = ( long ) nameBytes + framingBytes;
            var remaining = maximumBytes - _observedBytes;
            if ( remaining <= overhead )
            {
                return false;
            }
            var entryLimit = Math.Min(
                MaximumEntryBytes,
                checked(( int ) (remaining - overhead)) );
            var buffer = new byte[Math.Min( 64 * 1024, entryLimit + 1 )];
            using MemoryStream bytes = new( Math.Min( entryLimit, 64 * 1024 ) );
            while ( bytes.Length <= entryLimit )
            {
                var read = await stream.ReadAsync( buffer, cancellationToken ).ConfigureAwait( false );
                if ( read == 0 )
                {
                    break;
                }
                await bytes.WriteAsync( buffer.AsMemory( 0, read ), cancellationToken )
                    .ConfigureAwait( false );
                if ( bytes.Length > entryLimit )
                {
                    return false;
                }
            }
            var emittedBytes = overhead + bytes.Length;
            _observedBytes += emittedBytes;
            if ( _observedBytes > maximumBytes ||
                _observedBytes > archiveBytes * MaximumExpansionRatio )
            {
                return false;
            }
            ReadOnlySpan<byte> content = bytes.GetBuffer( ).AsSpan( 0, checked(( int ) bytes.Length) );
            if ( content.Contains( ( byte ) 0 ) )
            {
                return true;
            }
            var decoded = StrictUtf8.GetString( content );
            _text.Append( name ).Append( '\n' ).Append( decoded ).Append( '\n' );
            return true;
        }

        public string? Build( ) => _text.Length == 0 ? null : _text.ToString( );
    }
}
