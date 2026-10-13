using System.IO.Compression;
using System.Text.Json;
using CommandDeck.ExtensionTools;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionAuthorTemplateAndPackage( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var project = Path.Combine( root, "author project" );
            AuthorProject.Create( project, "org.example.draft", "An author's draft", root );
            var manifest = AuthorProject.Check( project );
            Equal( "org.example.draft", manifest.Id );
            Equal( "An author's draft", manifest.Name );
            True( File.ReadAllText( Path.Combine( project, "Program.cs" ) ).Contains( "ExtensionApp.RunAsync", StringComparison.Ordinal ) );
            Throws<IOException>( ( ) => AuthorProject.Create( project, "org.example.other", "Other", root ) );
            Equal( "org.example.draft", AuthorProject.Check( project ).Id );
            var invalid = Path.Combine( root, "invalid" );
            Throws<InvalidDataException>( ( ) => AuthorProject.Create( invalid, "invalid", "Draft", root ) );
            True( !Directory.Exists( invalid ) );
            var publish = Path.Combine( root, "publish" );
            Directory.CreateDirectory( publish );
            var binary = Path.Combine( publish, "LocalDraft" );
            File.WriteAllBytes( binary, "\u007fELFtest"u8.ToArray( ) );
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }
            File.WriteAllText( Path.Combine( publish, "LocalDraft.dbg" ), "debug symbols" );
            var archive = Path.Combine( root, "draft.cdext" );
            var manifestPath = Path.Combine( project, "commanddeck.extension.json" );
            ExtensionPackageBuilder.Pack( manifestPath, publish, archive );
            using ( var zip = ZipFile.OpenRead( archive ) )
            {
                Equal( 2, zip.Entries.Count );
                True( zip.GetEntry( "bin/linux-x64/LocalDraft" ) is not null );
            }
            Throws<IOException>( ( ) => ExtensionPackageBuilder.Pack( manifestPath, publish, archive ) );
            File.CreateSymbolicLink( Path.Combine( publish, "link" ), binary );
            Throws<InvalidDataException>( ( ) => ExtensionPackageBuilder.Pack( manifestPath, publish, Path.Combine( root, "bad.cdext" ) ) );
            True( !File.Exists( Path.Combine( root, "bad.cdext" ) ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task ExtensionCliSettingsPreserveRevisionAsync( )
    {
        var root = CreateCliSpecRoot( );
        using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 60 ) );
        Task? server = null;
        try
        {
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions { Enabled = false } );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, lifetime.Token );
            var socket = Path.Combine( root, "settings.sock" );
            server = new UnixProtocolServer( runtime, socket ).RunAsync( lifetime.Token );
            var manifest = PackageFixture( ) with
            {
                Preferences = [new( ) { Id = "title", Title = "Title", Type = "text", Default = JsonElement.Parse( "\"Default\"" ) }]
            };
            var archive = WritePackageFixture( root, manifest );
            async Task<ExtensionInventory> Inventory( params string[] args )
            {
                var result = await RunCommandDeckProcessAsync( ["extensions", .. args, "--json", "--socket", socket] );
                Equal( 0, result.ExitCode );
                return JsonSerializer.Deserialize( result.StandardOutput, CommandDeckJsonContext.Default.ExtensionInventory )!;
            }
            var inspected = await Inventory( "inspect", "--archive", archive );
            var installed = await Inventory( "install", "--archive", archive, "--digest", inspected.ArchiveDigest!, "--yes" );
            var settings = Path.Combine( root, "settings.json" );
            await File.WriteAllTextAsync( settings, "{\"title\":\"First\"}" );
            var originalRevision = installed.Revision.ToString( System.Globalization.CultureInfo.InvariantCulture );
            var saved = await Inventory( "configure", manifest.Id, "--settings-file", settings, "--expected-revision", originalRevision );
            True( saved.Revision > installed.Revision );
            await File.WriteAllTextAsync( settings, "{\"title\":\"Stale editor\"}" );
            var stale = await RunCommandDeckProcessAsync( "extensions", "configure", manifest.Id, "--settings-file", settings, "--expected-revision", originalRevision, "--socket", socket );
            True( stale.ExitCode != 0 );
            var current = await Inventory( "inspect", manifest.Id );
            Equal( "First", current.Settings!["title"].GetString( ) );
            var missing = await RunCommandDeckProcessAsync( "extensions", "configure", manifest.Id, "--settings-file", settings, "--socket", socket );
            True( missing.ExitCode != 0 );
            await AssertInvalidSettingsFilesAsync( settings, socket, manifest.Id, current.Revision );
            await File.WriteAllTextAsync( settings, "{}" );
            await Inventory( "configure", manifest.Id, "--settings-file", settings, "--expected-revision", current.Revision.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
            Equal( 0, (await Inventory( "inspect", manifest.Id )).Settings!.Count );
        }
        finally
        {
            await lifetime.CancelAsync( );
            if ( server is not null )
            {
                await server;
            }
            Directory.Delete( root, true );
        }
    }

    private static async Task AssertInvalidSettingsFilesAsync( string file, string socket, string id, long revision )
    {
        foreach ( var input in new[] { "[]", "{", "{\"title\":1}", "{\"title\":\"A\",\"title\":\"B\"}", new string( ' ', 65537 ) } )
        {
            await File.WriteAllTextAsync( file, input );
            var result = await RunCommandDeckProcessAsync( "extensions", "configure", id, "--settings-file", file, "--expected-revision", revision.ToString( System.Globalization.CultureInfo.InvariantCulture ), "--socket", socket );
            True( result.ExitCode != 0 );
        }
    }
}
