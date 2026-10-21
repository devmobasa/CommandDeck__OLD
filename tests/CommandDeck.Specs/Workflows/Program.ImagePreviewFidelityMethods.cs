using System.Buffers.Binary;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ImagePreviewsPreserveOrientationAndAlphaAsync( )
    {
        if ( !ImagePreviewToolsAvailable( ) )
        {
            SkipSpec( "ImageMagick or Bubblewrap is not installed." );
        }

        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-image-fidelity-" ).FullName;
        try
        {
            var service = PreviewService( directory, false, [] );
            var jpegPath = Path.Combine( directory, "rotated.jpg" );
            await ImageFixtureCommandAsync( ["-size", "80x40", "xc:red", "-fill", "blue", "-draw", "rectangle 40,0 79,39", jpegPath] );
            var jpeg = await File.ReadAllBytesAsync( jpegPath );
            // EXIF/TIFF orientation 6 (RightTop): stored 80x40 pixels display as 40x80.
            var exif = Convert.FromHexString( "FFE1002245786966000049492A0008000000010012010300010000000600000000000000" );
            await File.WriteAllBytesAsync( jpegPath, [.. jpeg.AsSpan( 0, 2 ), .. exif, .. jpeg.AsSpan( 2 )] );
            Equal( "RightTop", await ImageFixtureCommandAsync( [jpegPath, "-format", "%[orientation]", "info:"] ) );

            var oriented = await service.ReadAsync( jpegPath, default );
            True( oriented.Succeeded, oriented.Error );
            Equal( 40, oriented.Preview!.ImageWidth );
            Equal( 80, oriented.Preview.ImageHeight );
            var thumbnail = Path.Combine( directory, "thumbnail.png" );
            var orientedPng = Convert.FromBase64String( oriented.Preview.ImageSource!["data:image/png;base64,".Length..] );
            Equal( 40u, BinaryPrimitives.ReadUInt32BigEndian( orientedPng.AsSpan( 16 ) ) );
            Equal( 80u, BinaryPrimitives.ReadUInt32BigEndian( orientedPng.AsSpan( 20 ) ) );
            await File.WriteAllBytesAsync( thumbnail, orientedPng );
            Equal( "1 1", await ImageFixtureCommandAsync( [thumbnail, "-format", "%[fx:p{20,10}.r>0.9] %[fx:p{20,70}.b>0.9]", "info:"] ) );

            foreach ( var format in new[] { "png", "webp" } )
            {
                var path = Path.Combine( directory, "transparent." + format );
                await ImageFixtureCommandAsync( ["-size", "32x16", "xc:none", "-fill", "red", "-draw", "rectangle 16,0 31,15",
                    "-define", "webp:lossless=true", path] );
                var outcome = await service.ReadAsync( path, default );
                True( outcome.Succeeded, outcome.Error );
                await File.WriteAllBytesAsync( thumbnail, Convert.FromBase64String( outcome.Preview!.ImageSource!["data:image/png;base64,".Length..] ) );
                Equal( "0 1", await ImageFixtureCommandAsync( [thumbnail, "-format", "%[fx:p{0,0}.a] %[fx:p{31,0}.a]", "info:"] ) );
            }
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static async Task<string> ImageFixtureCommandAsync( IReadOnlyList<string> arguments )
    {
        var result = await ProcessRunner.RunAsync( "/usr/bin/magick", arguments, null, null, TimeSpan.FromSeconds( 5 ), default );
        Equal( 0, result.ExitCode );
        return result.StandardOutput.Trim( );
    }

    private static async Task ImagePreviewsConfineDecoderAsync( )
    {
        if ( !ImagePreviewToolsAvailable( ) )
        {
            SkipSpec( "ImageMagick or Bubblewrap is not installed." );
        }

        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-image-confinement-" ).FullName;
        try
        {
            var path = Path.Combine( directory, "image.png" );
            var secret = Path.Combine( directory, "private.txt" );
            await File.WriteAllBytesAsync( path, Convert.FromBase64String( PreviewPngBase64 ) );
            await File.WriteAllTextAsync( secret, "must remain outside decoder" );
            var calls = 0;
            BoundedProcessRunner runner = async ( executable, args, timeout, limits, token ) =>
            {
                calls++;
                Equal( "/usr/bin/bwrap", executable );
                var prefix = args.TakeWhile( static argument => argument != "--" ).ToArray( );
                async Task<ProcessResult> ProbeAsync( params string[] command ) =>
                    await ProcessRunner.RunBoundedAsync( executable, [.. prefix, "--", .. command], null, timeout, limits, token );

                Equal( 0, (await ProbeAsync( "/usr/bin/test", "!", "-e", secret )).ExitCode );
                Equal( 0, (await ProbeAsync( "/usr/bin/test", "!", "-e", "/home" )).ExitCode );
                Equal( 0, (await ProbeAsync( "/usr/bin/test", "-r", "/input" )).ExitCode );
                Equal( 1, (await ProbeAsync( "/usr/bin/touch", "/input" )).ExitCode );
                Equal( 1, (await ProbeAsync( "/usr/bin/touch", "/usr/commanddeck-write-probe" )).ExitCode );
                var network = await ProbeAsync( "/usr/bin/readlink", "/proc/self/ns/net" );
                Equal( 0, network.ExitCode );
                var hostNetwork = new FileInfo( "/proc/self/ns/net" ).LinkTarget;
                True( hostNetwork is not null );
                True( network.StandardOutput.Trim( ).StartsWith( "net:[", StringComparison.Ordinal ) );
                True( network.StandardOutput.Trim( ) != hostNetwork );
                return await ProcessRunner.RunBoundedAsync( executable, args, null, timeout, limits, token );
            };
            var result = await PreviewService( directory, false, [], new ImagePreviewRenderer( runner ) ).ReadAsync( path, default );
            True( result.Succeeded, result.Error );
            Equal( 1, calls );
            Equal( "must remain outside decoder", await File.ReadAllTextAsync( secret ) );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }
}
