namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardPayloadDirectoryPublishesAndHonorsLeases( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-payload-" ).FullName;
        try
        {
            var path = Path.Combine( root, "history.data" );
            using ClipboardPayloadDirectory directory = new( path );
            True( directory.AvailableFreeSpace > 0, "migration space lookup uses the opened filesystem" );
            Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode( path ) );
            var id = Guid.NewGuid( );
            using ( var writer = directory.CreateStaging( id ) )
            {
                writer.Stream.Write( "synthetic raw bytes\0\xff"u8 );
                Throws<IOException>( ( ) => directory.Publish( id ) );
                Throws<IOException>( ( ) => directory.Delete( id, true ) );
                Throws<IOException>( ( ) => { using var duplicate = directory.CreateStaging( id ); } );
            }

            directory.Publish( id );
            Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( Path.Combine( path, id.ToString( "N" ) + ".blob" ) ) );
            var inventory = directory.Inventory( ).Single( );
            Equal( id, inventory.Id );
            True( !inventory.Staging );
            using ( var reader = directory.OpenRead( id ) )
            {
                using MemoryStream copy = new( );
                reader.Stream.CopyTo( copy );
                True( copy.ToArray( ).AsSpan( ).SequenceEqual( "synthetic raw bytes\0\xff"u8 ) );
                Throws<IOException>( ( ) => directory.Delete( id, false ) );
                reader.Dispose( );
                reader.Dispose( );
            }

            using ( var second = directory.CreateStaging( id ) )
            {
                second.Stream.Write( "new bytes must not replace old bytes"u8 );
            }

            Throws<IOException>( ( ) => directory.Publish( id ) );
            Equal( 2, directory.Inventory( ).Count );
            directory.Delete( id, true );
            directory.Delete( id, false );
            directory.Delete( id, false );
            Equal( 0, directory.Inventory( ).Count );
            Throws<ArgumentException>( ( ) => directory.Delete( Guid.Empty, false ) );

            var replacedId = Guid.NewGuid( );
            using ( var writer = directory.CreateStaging( replacedId ) )
            {
                writer.Stream.Write( "original identity"u8 );
            }

            directory.Publish( replacedId );
            var replacedPath = Path.Combine( path, replacedId.ToString( "N" ) + ".blob" );
            File.Move( replacedPath, Path.Combine( root, "original-object" ) );
            File.WriteAllText( replacedPath, "substituted object must survive" );
            File.SetUnixFileMode( replacedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite );
            Throws<IOException>( ( ) => { using var reader = directory.OpenRead( replacedId ); } );
            Throws<IOException>( ( ) => directory.Delete( replacedId, false ) );
            Equal( "substituted object must survive", File.ReadAllText( replacedPath ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardPayloadDirectoryRetainsAuthorityAcrossPathReplacement( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-directory-swap-" ).FullName;
        try
        {
            var original = Path.Combine( root, "history.data" );
            var moved = Path.Combine( root, "moved.data" );
            using ClipboardPayloadDirectory directory = new( original );
            var id = Guid.NewGuid( );
            using ( var writer = directory.CreateStaging( id ) )
            {
                writer.Stream.Write( "owned bytes"u8 );
            }

            Directory.Move( original, moved );
            Directory.CreateDirectory( original );
            var replacement = Path.Combine( original, id.ToString( "N" ) + ".blob" );
            File.WriteAllText( replacement, "replacement must remain untouched" );
            directory.Publish( id );
            using ( var reader = directory.OpenRead( id ) )
            {
                using StreamReader text = new( reader.Stream );
                Equal( "owned bytes", text.ReadToEnd( ) );
            }

            directory.Delete( id, false );
            Equal( "replacement must remain untouched", File.ReadAllText( replacement ) );
            Equal( 0, Directory.GetFileSystemEntries( moved ).Length );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardPayloadDirectoryRejectsSuspiciousNodesAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-payload-nodes-" ).FullName;
        try
        {
            var path = Path.Combine( root, "history.data" );
            using ClipboardPayloadDirectory directory = new( path );
            var source = Path.Combine( root, "source" );
            File.WriteAllText( source, "preserve external fixture" );
            File.SetUnixFileMode( source, UnixFileMode.UserRead | UnixFileMode.UserWrite );
            var leafLink = Path.Combine( root, "leaf-link" );
            Directory.CreateSymbolicLink( leafLink, path );
            Throws<IOException>( ( ) => { using var rejected = new ClipboardPayloadDirectory( leafLink ); } );
            Throws<IOException>( ( ) => { using var rejected = new ClipboardPayloadDirectory( Path.Combine( leafLink, "child" ) ); } );
            True( !Directory.Exists( Path.Combine( path, "child" ) ) );

            foreach ( var kind in new[] { "symlink", "hardlink", "fifo", "directory", "permissions" } )
            {
                var id = Guid.NewGuid( );
                var target = Path.Combine( path, id.ToString( "N" ) + ".blob" );
                if ( kind == "symlink" )
                {
                    File.CreateSymbolicLink( target, source );
                }
                else if ( kind is "hardlink" or "fifo" )
                {
                    var result = await ProcessRunner.RunAsync( kind == "hardlink" ? "ln" : "mkfifo",
                        kind == "hardlink" ? [source, target] : [target], null, null, TimeSpan.FromSeconds( 2 ), default );
                    Equal( 0, result.ExitCode );
                }
                else if ( kind == "directory" )
                {
                    Directory.CreateDirectory( target );
                }
                else
                {
                    File.WriteAllText( target, "insecure file" );
                    File.SetUnixFileMode( target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead );
                }

                Throws<IOException>( ( ) => { using var rejected = directory.OpenRead( id ); } );
                Throws<IOException>( ( ) => directory.Delete( id, false ) );
                Throws<IOException>( ( ) => directory.Inventory( ) );
                Equal( "preserve external fixture", File.ReadAllText( source ) );
                if ( kind == "directory" )
                {
                    Directory.Delete( target );
                }
                else
                {
                    File.Delete( target );
                }
            }

            File.WriteAllText( Path.Combine( path, "unexpected-user-file" ), "do not clean" );
            Throws<IOException>( ( ) => directory.Inventory( ) );
            Equal( "do not clean", File.ReadAllText( Path.Combine( path, "unexpected-user-file" ) ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
