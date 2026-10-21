using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionRejectedAssetsPreserveNavigationAndAllowRecovery( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-asset-recovery-" ).FullName;
        try
        {
            Directory.CreateDirectory( Path.Combine( root, "assets" ) );
            var image = Path.Combine( root, "assets/icon.png" );
            File.Copy( Path.Combine( FindRepositoryRoot( ), "samples/extensions/QuickTools/assets/uuid.png" ), image );
            ExtensionAssets broker = new( root );
            ExtensionViewAuthority authority = new( "session", 1, 1, broker.Resolve );
            var invocation = authority.BeginInvocation( InvocationOrigin.Open );
            var parent = new ListView
            {
                Id = "parent",
                Title = "Parent",
                Items = [new ListItem {
                Id = "row", Title = "Row", Asset = "assets/icon.png", Actions = [ViewAction.CopyText( "copy", "Copy", "parent payload" )] }]
            };
            var first = authority.Replace( new ViewUpdate( "session", 1, "parent", 0, 1, parent ) );
            authority.Present( 1, first.Revision );
            var handle = first.Content.GetProperty( "items" )[0].GetProperty( "actions" )[0].GetProperty( "handle" ).GetString( )!;
            var admitted = authority.AdmitAction( 2, first.Revision, handle, "row" );
            var child = new DetailView { Id = "child", Title = "Child", Asset = "assets/missing.png" };
            Throws<InvalidOperationException>( ( ) => authority.Push( new ViewUpdate( "session", 1, "child", 0, 1, child ) ) );
            var badParent = parent with
            {
                Items = [parent.Items[0] with { Asset = "assets/missing.png" }]
            };
            Throws<InvalidOperationException>( ( ) => authority.Replace( new ViewUpdate( "session", 1, "parent", 1, 1, badParent ) ) );
            HashSet<string> grants = ["clipboard.write"];
            Equal( "parent payload", (( EffectBinding ) authority.BeginAction( admitted, 1, grants )).Action.Text );
            var stillVisible = authority.AdmitAction( 3, first.Revision, handle, "row" );
            Equal( "parent payload", (( EffectBinding ) authority.BeginAction( stillVisible, 1, grants )).Action.Text );
            var fallback = authority.Push( new ViewUpdate( "session", 1, "child", 0, 1, child with
            {
                Asset = null
            } ) );
            Equal( 2L, fallback.Revision );
            Equal( 2, fallback.Content.GetProperty( "depth" ).GetInt32( ) );
            authority.Present( 4, fallback.Revision );
            File.Delete( image );
            var restored = authority.Pop( );
            Equal( 3L, restored.Revision );
            Equal( 1, restored.Content.GetProperty( "depth" ).GetInt32( ) );
            True( !restored.Content.TryGetProperty( "backHandle", out _ ) );
            Equal( first.Content.GetProperty( "items" )[0].GetProperty( "imageSource" ).GetString( ),
                restored.Content.GetProperty( "items" )[0].GetProperty( "imageSource" ).GetString( ) );
            True( handle != restored.Content.GetProperty( "items" )[0].GetProperty( "actions" )[0].GetProperty( "handle" ).GetString( ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ExtensionAssetsRejectPathsAndBoundDecodedImages( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-assets-" ).FullName;
        try
        {
            Directory.CreateDirectory( Path.Combine( root, "assets" ) );
            var bytes = File.ReadAllBytes( Path.Combine( FindRepositoryRoot( ), "samples/extensions/QuickTools/assets/uuid.png" ) );
            File.WriteAllBytes( Path.Combine( root, "assets/icon.png" ), bytes );
            ExtensionAssets broker = new( root );
            ExtensionView View( string asset ) => new DetailView { Id = "details", Title = "Details", Asset = asset };
            JsonElement Render( ExtensionView view )
            {
                ExtensionViewAuthority authority = new( "assets", 1, 1, broker.Resolve );
                authority.BeginInvocation( InvocationOrigin.Open );
                return authority.Replace( new( "assets", 1, view.Id, 0, 1, view ) ).Content;
            }
            var rendered = Render( View( "assets/icon.png" ) );
            True( !rendered.TryGetProperty( "asset", out _ ) );
            Equal( "data:image/png;base64," + Convert.ToBase64String( bytes ), rendered.GetProperty( "imageSource" ).GetString( ) );
            foreach ( var path in new[] { "file:///etc/passwd", "https://example.com/a.png", "data:image/png;base64,x", "../icon.png", "assets/../icon.png", "assets/nested/icon.png", "assets/a.svg", "assets/a\\b.png" } )
            {
                Throws<InvalidDataException>( ( ) => Render( View( path ) ) );
            }
            File.CreateSymbolicLink( Path.Combine( root, "assets/link.png" ), Path.Combine( root, "assets/icon.png" ) );
            Throws<InvalidOperationException>( ( ) => Render( View( "assets/link.png" ) ) );
            File.WriteAllBytes( Path.Combine( root, "assets/huge.png" ), new byte[32769] );
            Throws<InvalidDataException>( ( ) => Render( View( "assets/huge.png" ) ) );
            var corrupt = bytes.ToArray( );
            corrupt[20] = 0xff;
            Throws<InvalidDataException>( ( ) => ExtensionAssets.ValidatePng( corrupt ) );
            Throws<InvalidDataException>( ( ) => ExtensionAssets.ValidatePng( bytes.AsSpan( 0, bytes.Length - 1 ) ) );
            Throws<InvalidDataException>( ( ) => ExtensionAssets.ValidatePng( [.. bytes, 0] ) );
            Throws<InvalidDataException>( ( ) => ExtensionAssets.ValidatePng( AssetPng( 513, 1 ) ) );
            Throws<InvalidDataException>( ( ) => ExtensionAssets.ValidatePng( AssetPng( 1, 513 ) ) );
            Throws<InvalidDataException>( ( ) => ExtensionAssets.ValidatePng( AssetPng( 32, 32, extraDecodedByte: true ) ) );
            Throws<InvalidDataException>( ( ) => ExtensionAssets.ValidatePng( AssetPng( 32, 32, ancillary: true ) ) );
            var noise = AssetPng( 64, 64 );
            File.WriteAllBytes( Path.Combine( root, "assets/noise.png" ), noise );
            ExtensionAssets.ValidatePng( noise );
            var crowded = new ListView
            {
                Id = "list",
                Title = "Images",
                Items = Enumerable.Range( 0, 7 )
                .Select( index => new ListItem { Id = index.ToString( ), Title = "Image", Asset = "assets/noise.png" } ).ToImmutableArray( )
            };
            Throws<InvalidDataException>( ( ) => Render( crowded ) );
            var assets = Path.Combine( root, "assets" );
            Directory.Move( assets, Path.Combine( root, "moved" ) );
            Directory.CreateSymbolicLink( assets, Path.Combine( root, "moved" ) );
            Throws<InvalidOperationException>( ( ) => Render( View( "assets/icon.png" ) ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static byte[] AssetPng( int width, int height, bool extraDecodedByte = false, bool ancillary = false )
    {
        // Build structurally valid fixtures so CRC rejection cannot mask limit regressions.
        using var output = new MemoryStream( );
        output.Write( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } );
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian( header, width );
        BinaryPrimitives.WriteInt32BigEndian( header.AsSpan( 4 ), height );
        header[8] = 8;
        header[9] = 6;
        Chunk( "IHDR"u8.ToArray( ), header );
        if ( ancillary )
        {
            Chunk( "acTL"u8.ToArray( ), new byte[8] );
        }
        var pixels = new byte[(width * 4 + 1) * height + (extraDecodedByte ? 1 : 0)];
        new Random( 42 ).NextBytes( pixels );
        for ( var y = 0; y < height; y++ )
        {
            pixels[y * (width * 4 + 1)] = 0;
        }
        using var compressed = new MemoryStream( );
        using ( var stream = new ZLibStream( compressed, CompressionLevel.Optimal, true ) )
        {
            stream.Write( pixels );
        }
        Chunk( "IDAT"u8.ToArray( ), compressed.ToArray( ) );
        Chunk( "IEND"u8.ToArray( ), [] );
        return output.ToArray( );

        void Chunk( byte[] kind, byte[] data )
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian( number, data.Length );
            output.Write( number );
            output.Write( kind );
            output.Write( data );
            var crc = uint.MaxValue;
            foreach ( var value in kind.Concat( data ) )
            {
                crc ^= value;
                for ( var bit = 0; bit < 8; bit++ )
                {
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
                }
            }
            BinaryPrimitives.WriteUInt32BigEndian( number, ~crc );
            output.Write( number );
        }
    }
}
