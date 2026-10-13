using System.IO.Compression;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
namespace CommandDeck.ExtensionTools;

public static class ExtensionPackageBuilder
{
    public static void Pack( string manifestPath, string publishDirectory, string outputPath )
    {
        var manifestBytes = File.ReadAllBytes( manifestPath );
        _ = ManifestValidation.Parse( manifestBytes );
        using var manifest = JsonDocument.Parse( manifestBytes );
        var entrypoint = manifest.RootElement.GetProperty( "targets" ).GetProperty( ExtensionTargets.LinuxX64 ).GetProperty( "entrypoint" ).GetString( )!;
        if ( entrypoint.Contains( '\\' ) || Path.GetDirectoryName( entrypoint ) != "bin/linux-x64" || Path.GetFileName( entrypoint ) is ".." or "." or "" )
        {
            throw new InvalidDataException( "This preview packer requires an entrypoint directly under bin/linux-x64." );
        }
        var source = publishDirectory;
        var files = new List<string>( );
        Collect( source );
        var assetsDirectory = Path.Combine( Path.GetDirectoryName( manifestPath )!, "assets" );
        var assets = CollectAssets( assetsDirectory );
        var binary = Path.Combine( source, Path.GetFileName( entrypoint ) );
        using ( var input = File.OpenRead( binary ) )
        {
            Span<byte> magic = stackalloc byte[4];
            input.ReadExactly( magic );
            if ( !magic.SequenceEqual( "\u007fELF"u8 ) )
            {
                throw new InvalidDataException( "Publish the declared ELF executable before packing." );
            }
        }
        files.Sort( StringComparer.Ordinal );
        if ( files.Count + assets.Length > 9999 || files.Concat( assets ).Sum( file => new FileInfo( file ).Length ) + manifestBytes.Length > 500L * 1024 * 1024 )
        {
            throw new InvalidDataException( "Published output exceeds the package limits." );
        }
        Directory.CreateDirectory( Path.GetDirectoryName( outputPath )! );
        using ( var output = new FileStream( outputPath, FileMode.CreateNew ) )
        {
            using ( var archive = new ZipArchive( output, ZipArchiveMode.Create ) )
            {
                using ( var target = Entry( archive, "commanddeck.extension.json", false ).Open( ) )
                {
                    target.Write( manifestBytes );
                }
                foreach ( var file in files )
                {
                    using var input = File.OpenRead( file );
                    using var target = Entry( archive, "bin/linux-x64/" + Path.GetRelativePath( source, file ).Replace( '\\', '/' ),
                        OperatingSystem.IsLinux( ) && (File.GetUnixFileMode( file ) & UnixFileMode.UserExecute) != 0 ).Open( );
                    input.CopyTo( target );
                }
                foreach ( var file in assets.Order( StringComparer.Ordinal ) )
                {
                    using var input = File.OpenRead( file );
                    using var target = Entry( archive, "assets/" + Path.GetFileName( file ), false ).Open( );
                    input.CopyTo( target );
                }
            }
        }
        void Collect( string directory )
        {
            if ( (File.GetAttributes( directory ) & FileAttributes.ReparsePoint) != 0 )
            {
                throw new InvalidDataException( "Symlinks are not package inputs." );
            }
            foreach ( var path in Directory.EnumerateFileSystemEntries( directory ) )
            {
                var attributes = File.GetAttributes( path );
                if ( (attributes & FileAttributes.ReparsePoint) != 0 )
                {
                    throw new InvalidDataException( "Symlinks are not package inputs." );
                }
                if ( (attributes & FileAttributes.Directory) != 0 )
                {
                    Collect( path );
                }
                else if ( Path.GetExtension( path ) is not (".dbg" or ".pdb") )
                {
                    files.Add( path );
                }
            }
        }
    }
    static ZipArchiveEntry Entry( ZipArchive archive, string name, bool executable )
    {
        var entry = archive.CreateEntry( name, CompressionLevel.Optimal );
        entry.LastWriteTime = new DateTimeOffset( 2026, 1, 1, 0, 0, 0, TimeSpan.Zero );
        entry.ExternalAttributes = (0x8000 | (executable ? 0x1ed : 0x1a4)) << 16;
        return entry;
    }

    public static string[] CollectAssets( string directory )
    {
        if ( !Directory.Exists( directory ) )
        {
            return [];
        }
        if ( (File.GetAttributes( directory ) & FileAttributes.ReparsePoint) != 0 || Directory.GetDirectories( directory ).Length > 0 )
        {
            throw new InvalidDataException( "Assets must be regular files in one flat directory." );
        }
        var assets = Directory.GetFiles( directory );
        foreach ( var asset in assets )
        {
            if ( (File.GetAttributes( asset ) & FileAttributes.ReparsePoint) != 0 || Path.GetExtension( asset ) != ".png" || new FileInfo( asset ).Length > 32768 )
            {
                throw new InvalidDataException( "Assets must be regular PNG files no larger than 32 KiB." );
            }
        }
        return assets;
    }
}
