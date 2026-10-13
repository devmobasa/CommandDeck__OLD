using System.Security.Cryptography;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardProjectionUsesVerifiedHeldPayloadsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-projection-" ).FullName;
        try
        {
            var data = Path.Combine( root, "payloads" );
            using ClipboardPayloadDirectory directory = new( data );
            var bytes = Convert.FromBase64String( PreviewPngBase64 );
            var id = CreateProjectionPayload( directory, bytes );
            using var lease = directory.OpenRead( id );
            var original = Path.Combine( data, id.ToString( "N" ) + ".blob" );
            File.Move( original, Path.Combine( root, "held-original" ) );
            await File.WriteAllTextAsync( original, "replacement cannot become the image" );
            var called = 0;
            ImagePreviewRenderer renderer = new( async ( executable, args, timeout, limits, token ) =>
            {
                called++;
                Equal( "/usr/bin/bwrap", executable );
                Equal( TimeSpan.FromSeconds( 3 ), timeout );
                Equal( 384L * 1024 * 1024, limits.MaximumMemoryBytes );
                True( args.Contains( "PNG:/input[0]" ) );
                var snapshot = args[args.ToList( ).IndexOf( "/input" ) - 1];
                True( (await File.ReadAllBytesAsync( snapshot, token )).SequenceEqual( bytes ) );
                if ( OperatingSystem.IsLinux( ) )
                {
                    Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( snapshot ) );
                }

                return new ProcessResult { ExitCode = 0, StandardOutput = "32 32\ndata:image/png;base64," + PreviewPngBase64, StandardError = "" };
            }, ( ) => true );
            ClipboardPayloadProjection service = new( new( Environment.ProcessPath! ), renderer );
            var digest = Convert.ToHexStringLower( SHA256.HashData( bytes ) );
            var result = await service.ReadPngAsync( lease, bytes.Length, digest, CancellationToken.None );
            True( result.Succeeded, result.Error );
            Equal( 32, result.Preview!.ImageWidth );
            Equal( 1, called );
            await ThrowsAsync<InvalidDataException>( async ( ) => await service.ReadPngAsync( lease, bytes.Length, new string( '0', 64 ), CancellationToken.None ) );
            await ThrowsAsync<InvalidDataException>( async ( ) => await service.ReadPngAsync( lease, bytes.Length + 1, digest, CancellationToken.None ) );
            True( called == 1, "unverified bytes never reach a decoder" );

            var notPng = "GIF89a is not an accepted clipboard PNG"u8.ToArray( );
            using var invalid = directory.OpenRead( CreateProjectionPayload( directory, notPng ) );
            True( !(await service.ReadPngAsync( invalid, notPng.Length, Convert.ToHexStringLower( SHA256.HashData( notPng ) ), CancellationToken.None )).Succeeded );
            Equal( 1, called );

            await File.WriteAllBytesAsync( Path.Combine( root, "held-original" ), new byte[bytes.Length] );
            await ThrowsAsync<InvalidDataException>( async ( ) => await service.ReadPngAsync( lease, bytes.Length, digest, CancellationToken.None ) );
            Equal( 1, called );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardProjectionSharesWorkerCapacityAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-projection-" ).FullName;
        try
        {
            using ClipboardPayloadDirectory directory = new( Path.Combine( root, "payloads" ) );
            var png = Convert.FromBase64String( PreviewPngBase64 );
            using var imageLease = directory.OpenRead( CreateProjectionPayload( directory, png ) );
            var html = "<p>independent input</p>"u8.ToArray( );
            using var htmlLease = directory.OpenRead( CreateProjectionPayload( directory, html ) );
            TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource released = new( TaskCreationOptions.RunContinuationsAsynchronously );
            ImagePreviewRenderer renderer = new( async ( _, _, _, _, _ ) =>
            {
                entered.SetResult( );
                await released.Task;
                return new ProcessResult { ExitCode = 1, StandardOutput = "", StandardError = "" };
            }, ( ) => true );
            ClipboardHtmlProjectionService htmlService = new( Environment.ProcessPath! );
            ClipboardPayloadProjection service = new( htmlService, renderer );
            var work = service.ReadPngAsync( imageLease, png.Length, Convert.ToHexStringLower( SHA256.HashData( png ) ), CancellationToken.None ).AsTask( );
            try
            {
                await entered.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( "WorkerBusy", (await service.ReadHtmlAsync( htmlLease, html.Length,
                    Convert.ToHexStringLower( SHA256.HashData( html ) ), CancellationToken.None )).Reason );
                Equal( "WorkerBusy", (await htmlService.ProjectAsync( html, CancellationToken.None )).Reason );
                True( !(await service.ReadPngAsync( imageLease, png.Length, "unused", CancellationToken.None )).Succeeded );
            }
            finally
            {
                released.TrySetResult( );
                await work;
            }

            if ( ImagePreviewToolsAvailable( ) )
            {
                ClipboardPayloadProjection actual = new( htmlService );
                var decoded = await actual.ReadPngAsync( imageLease, png.Length,
                    Convert.ToHexStringLower( SHA256.HashData( png ) ), CancellationToken.None );
                True( decoded.Succeeded, decoded.Error );
                Equal( 32, decoded.Preview!.ImageHeight );
            }

            ClipboardHtmlProjectionService capturedHtml = new( Environment.ProcessPath!, async ( _, args, _, _, token ) =>
            {
                var snapshot = args[args.ToList( ).IndexOf( "/input" ) - 1];
                True( (await File.ReadAllBytesAsync( snapshot, token )).SequenceEqual( html ) );
                return new ProcessResult { ExitCode = 0, StandardOutput = "{\"available\":true,\"text\":\"independent input\",\"truncated\":false}", StandardError = "" };
            } );
            ClipboardPayloadProjection verifiedHtml = new( capturedHtml );
            var text = await verifiedHtml.ReadHtmlAsync( htmlLease, html.Length,
                Convert.ToHexStringLower( SHA256.HashData( html ) ), CancellationToken.None );
            Equal( "independent input", text.Text );
            await ThrowsAsync<InvalidDataException>( async ( ) => await verifiedHtml.ReadHtmlAsync( htmlLease, html.Length, "wrong", CancellationToken.None ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static Guid CreateProjectionPayload( ClipboardPayloadDirectory directory, byte[] bytes )
    {
        var id = Guid.NewGuid( );
        using ( var writer = directory.CreateStaging( id ) )
        {
            writer.Stream.Write( bytes );
        }

        directory.Publish( id );
        return id;
    }
}
