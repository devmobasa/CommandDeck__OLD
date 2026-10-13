using System.Buffers.Binary;
using System.Globalization;
using System.Net.Mime;

namespace CommandDeck;

/// <summary>Converts authorized raster snapshots to bounded PNG thumbnails.</summary>
public sealed class ImagePreviewRenderer( BoundedProcessRunner? runner = null, Func<bool>? available = null )
{
    public const int MaximumInputBytes = 16 * 1024 * 1024;
    public const int MaximumOutputBytes = 1536 * 1024;
    public const int ThumbnailSize = 512;
    private const string PngFormat = "PNG";
    private const string JpegFormat = "JPEG";
    private const string GifFormat = "GIF";
    private const string WebpFormat = "WEBP";
    private const string BmpFormat = "BMP";
    private const string PngDataUrlPrefix = "data:" + MediaTypeNames.Image.Png + ";base64,";
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Gif87Signature => "GIF87a"u8;
    private static ReadOnlySpan<byte> Gif89Signature => "GIF89a"u8;
    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;
    private static ReadOnlySpan<byte> WebpSignature => "WEBP"u8;
    private static ReadOnlySpan<byte> BmpSignature => "BM"u8;
    private static ReadOnlySpan<byte> PngHeaderChunk => "IHDR"u8;

    private readonly BoundedProcessRunner _run = runner ?? RunAsync;
    private readonly Func<bool> _available = available ?? ImagePreviewSandbox.IsAvailable;
    internal bool Available => _available( );

    internal static string? DetectFormat( ReadOnlySpan<byte> bytes )
    {
        if ( bytes.StartsWith( PngSignature ) )
        {
            return PngFormat;
        }

        if ( bytes.StartsWith( JpegSignature ) )
        {
            return JpegFormat;
        }

        if ( bytes.StartsWith( Gif87Signature ) || bytes.StartsWith( Gif89Signature ) )
        {
            return GifFormat;
        }

        if ( bytes.Length >= 12 && bytes.StartsWith( RiffSignature ) && bytes[8..].StartsWith( WebpSignature ) )
        {
            return WebpFormat;
        }

        if ( bytes.Length >= 14 && bytes.StartsWith( BmpSignature ) &&
            BinaryPrimitives.ReadUInt32LittleEndian( bytes[6..] ) == 0 )
        {
            return BmpFormat;
        }

        return null;
    }

    internal async ValueTask<FilePreviewOutcome> ReadAsync( SafeRegularFile file, string format, CancellationToken cancellationToken )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return FilePreviewOutcome.Failure( "Image previews require Linux." );
        }

        if ( file.Length > MaximumInputBytes )
        {
            return FilePreviewOutcome.Failure( "Image preview is limited to files up to 16 MiB." );
        }

        if ( !_available( ) )
        {
            return FilePreviewOutcome.Failure( "Image previews require ImageMagick and Bubblewrap in /usr/bin." );
        }

        var bytes = new byte[( int ) file.Length + 1];
        var count = await file.ReadPrefixAsync( bytes, cancellationToken ).ConfigureAwait( false );
        if ( count != file.Length || DetectFormat( bytes.AsSpan( 0, count ) ) != format ||
            !LinuxMountIdentity.TryGet( file.Handle, out var after ) || after != file.Identity )
        {
            return FilePreviewOutcome.Failure( "The image changed while it was being read. Select it again." );
        }

        return await ReadSnapshotAsync( bytes.AsMemory( 0, count ), format, cancellationToken ).ConfigureAwait( false );
    }

    internal ValueTask<FilePreviewOutcome> ReadClipboardPngAsync( ReadOnlyMemory<byte> bytes, CancellationToken token )
    {
        token.ThrowIfCancellationRequested( );
        if ( !OperatingSystem.IsLinux( ) || bytes.Length is 0 or > MaximumInputBytes || DetectFormat( bytes.Span ) != PngFormat || !_available( ) )
        {
            return ValueTask.FromResult( FilePreviewOutcome.Failure( "Clipboard PNG preview is unavailable." ) );
        }

        return ReadSnapshotAsync( bytes, PngFormat, token );
    }

    private async ValueTask<FilePreviewOutcome> ReadSnapshotAsync( ReadOnlyMemory<byte> bytes, string format, CancellationToken cancellationToken )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return FilePreviewOutcome.Failure( "Image previews require Linux." );
        }

        // Copy only the already-authorized bytes. The decoder never reopens a
        // user path or sees an extension-selected decoder, URL, or SVG document.
        var directory = Path.Combine( Path.GetTempPath( ), "commanddeck-image-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        try
        {
            var snapshot = Path.Combine( directory, "source" );
            using ( var output = SafeRegularFile.CreatePrivateWriteStream( snapshot ) )
            {
                await output.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
            }

            string[] arguments =
            [
                "-limit", "thread", "1", "-limit", "memory", "128MiB", "-limit", "map", "0",
                "-limit", "disk", "0", "-limit", "width", "32768", "-limit", "height", "32768",
                format + ":" + ImagePreviewSandbox.Input + "[0]", "-auto-orient",
                "-format", "%w %h\n", "-write", "info:-",
                "-thumbnail", $"{ThumbnailSize}x{ThumbnailSize}>", "-strip", "-depth", "8", "-interlace", "none",
                "-define", PngFormat.ToLowerInvariant( ) + ":exclude-chunks=all", "inline:" + PngFormat.ToLowerInvariant( ) + ":-"
            ];
            var result = await _run( ImagePreviewSandbox.Executable, ImagePreviewSandbox.BuildArguments( snapshot, arguments ), TimeSpan.FromSeconds( 3 ), new ProcessExecutionLimits
            {
                MaximumStandardOutputBytes = MaximumOutputBytes,
                MaximumStandardErrorBytes = 4096,
                MaximumMemoryBytes = 384L * 1024 * 1024,
                ClearEnvironment = true
            }, cancellationToken ).ConfigureAwait( false );
            cancellationToken.ThrowIfCancellationRequested( );
            if ( result.ExitCode != 0 || result.TimedOut || result.MemoryLimitExceeded || result.AnyOutputLimitExceeded )
            {
                return FilePreviewOutcome.Failure( "The image could not be previewed. It may be damaged, exceed preview limits, or the Bubblewrap sandbox could not start." );
            }

            return ParseThumbnail( result.StandardOutput, format );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }

    private static FilePreviewOutcome ParseThumbnail( string output, string format )
    {
        var newline = output.IndexOf( '\n' );
        var dimensions = newline < 0 ? [] : output[..newline].Split( ' ' );
        var source = newline < 0 ? "" : output[(newline + 1)..].Trim( );
        if ( dimensions.Length != 2 || !int.TryParse( dimensions[0], CultureInfo.InvariantCulture, out var width ) ||
            !int.TryParse( dimensions[1], CultureInfo.InvariantCulture, out var height ) ||
            width is < 1 or > 32768 || height is < 1 or > 32768 ||
            source.Length > MaximumOutputBytes || !source.StartsWith( PngDataUrlPrefix, StringComparison.Ordinal ) )
        {
            return FilePreviewOutcome.Failure( "The image decoder returned an invalid preview." );
        }

        var png = new byte[source.Length];
        if ( !Convert.TryFromBase64String( source[PngDataUrlPrefix.Length..], png, out var length ) || length < 33 ||
            DetectFormat( png.AsSpan( 0, length ) ) != PngFormat || !png.AsSpan( 12, 4 ).SequenceEqual( PngHeaderChunk ) ||
            BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 16 ) ) is < 1 or > ThumbnailSize ||
            BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 20 ) ) is < 1 or > ThumbnailSize )
        {
            return FilePreviewOutcome.Failure( "The image decoder returned an invalid preview." );
        }

        return FilePreviewOutcome.Success( new FilePreview
        {
            Text = "",
            Language = format.ToLowerInvariant( ),
            ImageSource = source,
            ImageWidth = width,
            ImageHeight = height
        } );
    }

    private static ValueTask<ProcessResult> RunAsync( string file, IReadOnlyList<string> arguments, TimeSpan timeout,
        ProcessExecutionLimits limits, CancellationToken cancellationToken ) =>
        ProcessRunner.RunBoundedAsync( file, arguments, null, timeout, limits, cancellationToken );
}
