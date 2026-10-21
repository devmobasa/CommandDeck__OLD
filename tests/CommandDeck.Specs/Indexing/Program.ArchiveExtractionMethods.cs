using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ArchiveExtractionIsBoundedAsync( )
    {
        var temporary = SpecTemporaryFiles.PathFor( $"commanddeck-archive-{Guid.NewGuid( ):N}" );
        var root = Path.Combine( temporary, "root" );
        Directory.CreateDirectory( root );
        var zip = Path.Combine( root, "notes.zip" );
        var tar = Path.Combine( root, "notes.tar" );
        var tgz = Path.Combine( root, "notes.tgz" );
        WriteZip( zip,
        [
            ("safe/readme.txt", "ZipSearchMarker"),
            ("../escape.txt", "TraversalMustNotLeak"),
            ("nested.zip", "NestedArchiveMustNotLeak")
        ] );
        WriteTar( tar, "safe/readme.md", "TarSearchMarker", compressed: false );
        WriteTar( tgz, "safe/readme.txt", "TgzSearchMarker", compressed: true );
        await File.WriteAllBytesAsync( Path.Combine( root, "broken.zip" ), [1, 2, 3, 4] );

        var links = Path.Combine( root, "links.zip" );
        using ( var stream = File.Create( links ) )
        {
            using ( ZipArchive archive = new( stream, ZipArchiveMode.Create ) )
            {
                var safe = archive.CreateEntry( "safe.txt" );
                using ( StreamWriter writer = new( safe.Open( ), Encoding.UTF8, leaveOpen: false ) )
                {
                    writer.Write( "ZipLinkArchiveSafeMarker" );
                }
                var link = archive.CreateEntry( "linked.txt" );
                link.ExternalAttributes = (0xA000 | 0x1FF) << 16;
                using StreamWriter linkWriter = new( link.Open( ), Encoding.UTF8, leaveOpen: false );
                linkWriter.Write( "ZipSymlinkMustNotLeak" );
            }
        }
        var nameOverflow = Path.Combine( root, "name-overflow.zip" );
        using ( var stream = File.Create( nameOverflow ) )
        {
            using ( ZipArchive archive = new( stream, ZipArchiveMode.Create ) )
            {
                for ( var index = 0; index < 250; index++ )
                {
                    var name = $"{index:D3}-" + new string( 'n', 17_000 ) + ".txt";
                    var entry = archive.CreateEntry( name );
                    using StreamWriter writer = new( entry.Open( ), Encoding.UTF8, leaveOpen: false );
                    writer.Write( "ArchiveNameBudgetMustNotLeak" );
                }
            }
        }
        var bomb = Path.Combine( root, "bomb.zip" );
        using ( var stream = File.Create( bomb ) )
        {
            using ( ZipArchive archive = new( stream, ZipArchiveMode.Create ) )
            {
                var entry = archive.CreateEntry( "bomb.txt", CompressionLevel.SmallestSize );
                using var output = entry.Open( );
                var bytes = new byte[2 * 1024 * 1024];
                output.Write( bytes );
            }
        }
        IndexingOptions options = new( )
        {
            Roots =
            [
                new IndexRootOptions
                {
                    Path = root,
                    IncludeContentInSearch = true
                }
            ],
            MaximumDocumentFileBytes = 8 * 1024 * 1024,
            MaximumExtractedBytes = 4 * 1024 * 1024,
            IndexDesktopApplications = false,
            IndexOmarchyCommands = false
        };
        FileIndexSource source = new( options, new PrivacyOptions( ) );
        SqliteCatalog catalog = new( new StorageOptions
        {
            DatabasePath = Path.Combine( temporary, "catalog.db" )
        } );
        try
        {
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceGenerationAsync(
                source.ScanAsync( null, CancellationToken.None ),
                source.SnapshotDiagnostics,
                null,
                CancellationToken.None );

            Equal( "notes.zip", (await Search( "ZipSearchMarker" )).Single( ).Title );
            Equal( "notes.tar", (await Search( "TarSearchMarker" )).Single( ).Title );
            Equal( "notes.tgz", (await Search( "TgzSearchMarker" )).Single( ).Title );
            Equal( 0, (await Search( "TraversalMustNotLeak" )).Count );
            Equal( 0, (await Search( "NestedArchiveMustNotLeak" )).Count );
            Equal( "links.zip", (await Search( "ZipLinkArchiveSafeMarker" )).Single( ).Title );
            Equal( 0, (await Search( "ZipSymlinkMustNotLeak" )).Count );
            Equal( 0, (await Search( "ArchiveNameBudgetMustNotLeak" )).Count );
            Equal( 0, (await Search( "bomb" )).Single( ).Ranking.Matches.Count( value =>
                value.Field == RankingFieldNames.Content ) );
            Equal( "broken.zip", (await Search( "broken" )).Single( ).Title );

            async ValueTask<IReadOnlyList<SearchResult>> Search( string query ) =>
                await catalog.SearchAsync( QueryParser.Parse( query ), 10, CancellationToken.None );
        }
        finally
        {
            source.Dispose( );
            await catalog.DisposeAsync( );
            Directory.Delete( temporary, recursive: true );
        }
    }

    private static void WriteZip( string path, IReadOnlyList<(string Name, string Text)> entries )
    {
        using var stream = File.Create( path );
        using ZipArchive archive = new( stream, ZipArchiveMode.Create );
        foreach ( (var name, var text) in entries )
        {
            var entry = archive.CreateEntry( name );
            using StreamWriter writer = new( entry.Open( ), Encoding.UTF8, leaveOpen: false );
            writer.Write( text );
        }
    }

    private static void WriteTar( string path, string name, string text, bool compressed )
    {
        using var file = File.Create( path );
        using Stream target = compressed
            ? new GZipStream( file, CompressionLevel.SmallestSize, leaveOpen: false )
            : file;
        using TarWriter writer = new( target, leaveOpen: false );
        PaxTarEntry entry = new( TarEntryType.RegularFile, name )
        {
            DataStream = new MemoryStream( Encoding.UTF8.GetBytes( text ) )
        };
        writer.WriteEntry( entry );
    }
}
