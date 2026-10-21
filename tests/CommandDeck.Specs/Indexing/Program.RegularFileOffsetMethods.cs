namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RegularFileHandlesPreserveOffsetsAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-regular-offsets-" ).FullName;
        try
        {
            var bytes = "first half - second half"u8.ToArray( );
            var path = Path.Combine( root, "file" );
            await File.WriteAllBytesAsync( path, bytes );
            using ( var file = SafeRegularFile.Open( path ) )
            {
                var first = new byte[8];
                var second = new byte[8];
                Equal( 8, await file.ReadPrefixAsync( first, default ) );
                Equal( 8, await file.ReadPrefixAsync( second, default ) );
                True( first.SequenceEqual( second ), "prefix reads must start at offset zero every time" );
                using var duplicate = file.Duplicate( );
                Equal( 8, await duplicate.ReadPrefixAsync( second, default ) );
                True( first.SequenceEqual( second ), "a duplicated held descriptor preserves positional I/O" );
            }

            using ClipboardPayloadDirectory directory = new( Path.Combine( root, "payloads" ) );
            var id = Guid.NewGuid( );
            using ( var writer = directory.CreateStaging( id ) )
            {
                await writer.Stream.WriteAsync( bytes );
            }

            directory.Publish( id );
            using var payload = directory.OpenRead( id );
            var buffer = new byte[bytes.Length];
            await payload.Stream.ReadExactlyAsync( buffer );
            Equal( -1, payload.Stream.ReadByte( ) );
            payload.Stream.Position = 0;
            await payload.Stream.ReadExactlyAsync( buffer );
            True( bytes.SequenceEqual( buffer ), "payload streams can rewind after digest verification" );
            var handle = payload.Stream.SafeFileHandle;
            Equal( bytes.Length, await RandomAccess.ReadAsync( handle, buffer, 0 ) );
            Equal( bytes.Length, await RandomAccess.ReadAsync( handle, buffer, 0 ) );
            True( bytes.SequenceEqual( buffer ), "repeated raw-handle reads honor the supplied offset" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
