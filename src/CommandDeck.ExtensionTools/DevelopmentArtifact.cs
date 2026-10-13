using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandDeck.ExtensionTools;

internal static class DevelopmentArtifact
{
    public static string CreateManifest( string directory, string publish )
    {
        var stage = Path.Combine( Path.GetDirectoryName( publish )!, "artifact-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( stage );
        var manifest = JsonNode.Parse( File.ReadAllText( Path.Combine( directory, "commanddeck.extension.json" ) ) )!;
        manifest["id"] = manifest["id"]!.GetValue<string>( ) + ".development";
        manifest["name"] = manifest["name"]!.GetValue<string>( ) + " (Development)";
        manifest["version"] = manifest["version"]!.GetValue<string>( ).Split( '+' )[0] + "+dev." + Guid.NewGuid( ).ToString( "N" );
        foreach ( var command in manifest["commands"]!.AsArray( ) )
        {
            command!["title"] = "Dev: " + command["title"]!.GetValue<string>( );
        }
        var path = Path.Combine( stage, "commanddeck.extension.json" );
        File.WriteAllText( path, manifest.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
        var assets = ExtensionPackageBuilder.CollectAssets( Path.Combine( directory, "assets" ) );
        if ( assets.Length > 0 )
        {
            Directory.CreateDirectory( Path.Combine( stage, "assets" ) );
        }
        foreach ( var asset in assets )
        {
            File.Copy( asset, Path.Combine( stage, "assets", Path.GetFileName( asset ) ) );
        }
        return path;
    }
}
