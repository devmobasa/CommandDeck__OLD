using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.ExtensionTools;

public static class AuthorProject
{
    public static void Create( string directory, string id, string name, string feed )
    {
        directory = Path.GetFullPath( directory );
        feed = Path.GetFullPath( feed );
        if ( Directory.Exists( directory ) || File.Exists( directory ) )
        {
            throw new IOException( "The template requires a new directory; existing files are never overwritten." );
        }
        if ( !Directory.Exists( feed ) )
        {
            throw new DirectoryNotFoundException( "The local preview SDK feed does not exist." );
        }
        var manifest = JsonNode.Parse( Resource( "commanddeck.extension.json" ) )!;
        manifest["id"] = id;
        manifest["name"] = name;
        var json = manifest.ToJsonString( new JsonSerializerOptions { WriteIndented = true } );
        _ = ManifestValidation.Parse( System.Text.Encoding.UTF8.GetBytes( json ) );
        Directory.CreateDirectory( directory );
        File.WriteAllText( Path.Combine( directory, "commanddeck.extension.json" ), json + "\n" );
        foreach ( var file in new[] { "Program.cs", "LocalDraft.csproj" } )
        {
            File.WriteAllText( Path.Combine( directory, file ), Resource( file ) );
        }
        File.WriteAllText( Path.Combine( directory, "global.json" ), "{\"sdk\":{\"version\":\"10.0.400\",\"rollForward\":\"disable\"}}\n" );
        File.WriteAllText( Path.Combine( directory, ".gitignore" ), "bin/\nobj/\n.commanddeck/\n*.cdext\n" );
        new XDocument( new XElement( "configuration",
            new XElement( "packageSources", new XElement( "clear" ), Source( "preview", feed ), Source( "nuget.org", "https://api.nuget.org/v3/index.json" ) ),
            new XElement( "packageSourceMapping", Mapping( "preview", "CommandDeck.*" ), Mapping( "nuget.org", "*" ) ) ) )
            .Save( Path.Combine( directory, "NuGet.Config" ) );
    }

    public static ExtensionManifest Check( string directory )
    {
        var path = Path.Combine( directory, "commanddeck.extension.json" );
        if ( new FileInfo( path ).Length > ManifestValidation.MaximumManifestBytes )
        {
            throw new InvalidDataException( "The manifest exceeds 64 KiB." );
        }
        var manifest = ManifestValidation.Parse( File.ReadAllBytes( path ) );
        _ = ProjectPath( directory );
        if ( !manifest.Targets.TryGetValue( ExtensionTargets.LinuxX64, out var target ) || target.Deployment != ExtensionTargets.NativeAot ||
            Path.GetDirectoryName( target.Entrypoint ) != "bin/linux-x64" )
        {
            throw new InvalidDataException( "Author tooling currently builds native-aot linux-x64 packages with a flat entrypoint." );
        }
        return manifest;
    }

    public static string ProjectPath( string directory )
    {
        var projects = Directory.GetFiles( directory, "*.csproj" );
        return projects.Length == 1 ? projects[0] : throw new InvalidDataException( "The author directory must contain exactly one .csproj." );
    }

    private static string Resource( string name )
    {
        using var input = typeof( AuthorProject ).Assembly.GetManifestResourceStream( "Template." + name )!;
        using var reader = new StreamReader( input );
        return reader.ReadToEnd( );
    }
    private static XElement Source( string key, string value ) => new( "add", new XAttribute( "key", key ), new XAttribute( "value", value ) );
    private static XElement Mapping( string key, string pattern ) => new( "packageSource", new XAttribute( "key", key ), new XElement( "package", new XAttribute( "pattern", pattern ) ) );
}
