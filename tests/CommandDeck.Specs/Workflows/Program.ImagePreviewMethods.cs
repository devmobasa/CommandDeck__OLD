using System.Buffers.Binary;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static bool ImagePreviewToolsAvailable( ) => File.Exists( "/usr/bin/magick" ) && File.Exists( "/usr/bin/bwrap" );
    private const string PreviewPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAASElEQVR4nGNwi7r0fyAxw6gDRkNgNARGQ4BQCFAKKC6IiDGEEr2jDhgNgdEQGA2B0RAYGiEwoJXRaLN8NARGQ2A0BNxo3D0HAMBAGv92W3CjAAAAAElFTkSuQmCC";

    private static async Task VerifyImagePreviewProtocolAsync( UnixProtocolClient client, CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest { RequestId = 520, Operation = ProtocolOperations.Search, Query = "preview-image" }, cancellationToken );
        SearchResult? image = null;
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId != 520 )
            {
                continue;
            }

            if ( message.Type == ProtocolMessageTypes.Result && message.Result?.Kind == ResultKinds.File )
            {
                image = message.Result;
            }

            if ( message.Type == ProtocolMessageTypes.Done )
            {
                break;
            }
        }

        True( image is not null );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 521,
            Operation = ProtocolOperations.Preview,
            ResultId = image!.Id,
            InvocationReference = image.InvocationReference
        }, cancellationToken );
        var response = await ReadMessageAsync( client, 521,
            ImagePreviewToolsAvailable( ) ? ProtocolMessageTypes.PreviewResult : ProtocolMessageTypes.Error, cancellationToken );
        if ( ImagePreviewToolsAvailable( ) )
        {
            True( response?.Preview?.ImageSource?.StartsWith( "data:image/png;base64,", StringComparison.Ordinal ) == true );
            Equal( 32, response?.Preview?.ImageWidth );
            Equal( 32, response?.Preview?.ImageHeight );
        }
        else
        {
            True( response?.Error?.Contains( "ImageMagick", StringComparison.Ordinal ) == true );
        }
    }

    private static async Task ImagePreviewsRenderRasterFormatsAsync( )
    {
        if ( !ImagePreviewToolsAvailable( ) )
        {
            SkipSpec( "ImageMagick or Bubblewrap is not installed; image boundary fixtures still run separately." );
        }

        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-raster-spec-" ).FullName;
        try
        {
            var source = Path.Combine( directory, "source.png" );
            await File.WriteAllBytesAsync( source, Convert.FromBase64String( PreviewPngBase64 ) );
            var service = PreviewService( directory, false, [] );
            foreach ( var format in new[] { "png", "jpeg", "webp", "gif", "bmp" } )
            {
                var path = Path.Combine( directory, "--literal [1] ; preview." + format );
                var convert = await ProcessRunner.RunAsync( "magick", [source, "-resize", "1024x512!", format + ":" + path],
                    null, null, TimeSpan.FromSeconds( 5 ), default );
                Equal( 0, convert.ExitCode );
                var outcome = await service.ReadAsync( path, default, QueryParser.Parse( "content:needle" ).Expression );
                True( outcome.Succeeded, outcome.Error );
                var preview = outcome.Preview!;
                Equal( format, preview.Language );
                Equal( 1024, preview.ImageWidth );
                Equal( 512, preview.ImageHeight );
                Equal( "", preview.Text );
                Equal( 0, preview.MatchCount );
                var png = Convert.FromBase64String( preview.ImageSource!["data:image/png;base64,".Length..] );
                Equal( 512u, BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 16 ) ) );
                Equal( 256u, BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 20 ) ) );
                var json = System.Text.Json.JsonSerializer.Serialize( outcome, CommandDeckJsonContext.Default.FilePreviewOutcome );
                True( System.Text.Encoding.UTF8.GetByteCount( json ) < ProtocolLimits.MaximumResponseBytes );
                True( !json.Contains( directory, StringComparison.Ordinal ) );
            }

            var animated = Path.Combine( directory, "animation.gif" );
            Equal( 0, (await ProcessRunner.RunAsync( "magick", [source, source, "-loop", "0", animated],
                null, null, TimeSpan.FromSeconds( 5 ), default )).ExitCode );
            True( (await service.ReadAsync( animated, default )).Succeeded );
            var corrupt = Path.Combine( directory, "corrupt.png" );
            await File.WriteAllBytesAsync( corrupt, Convert.FromBase64String( PreviewPngBase64 )[..24] );
            True( !(await service.ReadAsync( corrupt, default )).Succeeded );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static async Task ImagePreviewsEnforceBoundariesAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-image-boundary-" ).FullName;
        try
        {
            var path = Path.Combine( directory, "picture.png" );
            var png = Convert.FromBase64String( PreviewPngBase64 );
            await File.WriteAllBytesAsync( path, png );
            var calls = 0;
            string? temporary = null;
            ProcessResult response = HerdrResult( "32 32\ndata:image/png;base64," + PreviewPngBase64 );
            BoundedProcessRunner runner = async ( executable, args, timeout, limits, token ) =>
            {
                calls++;
                Equal( "/usr/bin/bwrap", executable );
                True( timeout <= TimeSpan.FromSeconds( 3 ) );
                True( limits.ClearEnvironment );
                True( limits.MaximumStandardOutputBytes < ProtocolLimits.MaximumResponseBytes );
                True( limits.MaximumMemoryBytes <= 384L * 1024 * 1024 );
                var input = args.Single( static value => value.StartsWith( "PNG:", StringComparison.Ordinal ) );
                True( input.EndsWith( "[0]", StringComparison.Ordinal ) );
                Equal( "PNG:/input[0]", input );
                var snapshot = args[Array.IndexOf( args.ToArray( ), "/input" ) - 1];
                temporary = Path.GetDirectoryName( snapshot );
                if ( OperatingSystem.IsLinux( ) )
                {
                    Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode( temporary! ) );
                }

                True( (await File.ReadAllBytesAsync( snapshot, token )).SequenceEqual( png ) );
                token.ThrowIfCancellationRequested( );
                return response;
            };
            var renderer = new ImagePreviewRenderer( runner, static ( ) => true );
            var service = PreviewService( directory, false, [], renderer );
            True( (await service.ReadAsync( path, default )).Succeeded );
            True( !Directory.Exists( temporary ) );
            foreach ( var failed in new[]
            {
                HerdrResult( "not a thumbnail" ), HerdrResult( "32 32\nfile:///private.png" ),
                HerdrResult( "32 32\ndata:image/png;base64,bad" ),
                new ProcessResult { ExitCode = 0, StandardOutput = "", StandardError = "", TimedOut = true },
                new ProcessResult { ExitCode = 0, StandardOutput = "", StandardError = "", OutputLimitExceeded = true },
                HerdrResult( "", 1 )
            } )
            {
                response = failed;
                var callsBeforeFailure = calls;
                True( !(await service.ReadAsync( path, default )).Succeeded );
                Equal( callsBeforeFailure + 1, calls );
                True( !Directory.Exists( temporary ) );
            }

            var before = calls;
            True( !(await PreviewService( directory, false, [path], renderer ).ReadAsync( path, default )).Succeeded );
            var link = Path.Combine( directory, "link.png" );
            File.CreateSymbolicLink( link, path );
            True( !(await service.ReadAsync( link, default )).Succeeded );
            using CancellationTokenSource canceled = new( );
            canceled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await service.ReadAsync( path, canceled.Token ) );
            True( !(await PreviewService( directory, false, [], new ImagePreviewRenderer( runner, static ( ) => false ) )
                .ReadAsync( path, default )).Succeeded );
            using ( var file = File.OpenWrite( path ) )
            {
                file.SetLength( ImagePreviewRenderer.MaximumInputBytes + 1 );
            }

            True( !(await service.ReadAsync( path, default )).Succeeded );
            Equal( before, calls );

            await File.WriteAllBytesAsync( path, png );
            using CancellationTokenSource interrupted = new( );
            string? canceledSnapshot = null;
            var interrupting = new ImagePreviewRenderer( ( _, args, _, _, token ) =>
            {
                canceledSnapshot = args[Array.IndexOf( args.ToArray( ), "/input" ) - 1];
                interrupted.Cancel( );
                token.ThrowIfCancellationRequested( );
                return ValueTask.FromResult( HerdrResult( "", 1 ) );
            }, static ( ) => true );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await PreviewService( directory, false, [], interrupting )
                .ReadAsync( path, interrupted.Token ) );
            True( canceledSnapshot is not null && !Directory.Exists( Path.GetDirectoryName( canceledSnapshot ) ) );

            await File.WriteAllTextAsync( path, "BM is also a text prefix." );
            var textPreview = await service.ReadAsync( path, default );
            Equal( "BM is also a text prefix.", textPreview.Preview?.Text );
            True( textPreview.Preview?.ImageSource is null );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }
}
