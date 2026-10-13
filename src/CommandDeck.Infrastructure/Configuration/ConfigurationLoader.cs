using System.Text.Json;

namespace CommandDeck;

public static class ConfigurationLoader
{
    public static async ValueTask<CommandDeckOptions> LoadAsync(
        string? path,
        CancellationToken cancellationToken )
    {
        var resolved = string.IsNullOrWhiteSpace( path )
            ? PathExpansion.GetDefaultConfigPath( )
            : PathExpansion.Expand( path );

        if ( !File.Exists( resolved ) )
        {
            return CommandDeckOptions.CreateDefault( );
        }

        var json = await File.ReadAllTextAsync( resolved, cancellationToken ).ConfigureAwait( false );
        var options = JsonSerializer.Deserialize(
            json,
            CommandDeckJsonContext.Default.CommandDeckOptions );

        options ??= CommandDeckOptions.CreateDefault( );
        ConfigurationValidator.Validate( options );
        return options;
    }

    public static async ValueTask<string> WriteDefaultAsync(
        string? path,
        bool overwrite,
        CancellationToken cancellationToken )
    {
        var resolved = string.IsNullOrWhiteSpace( path )
            ? PathExpansion.GetDefaultConfigPath( )
            : PathExpansion.Expand( path );

        if ( File.Exists( resolved ) && !overwrite )
        {
            return resolved;
        }

        Directory.CreateDirectory( Path.GetDirectoryName( resolved )! );
        var json = JsonSerializer.Serialize(
            CommandDeckOptions.CreateDefault( ),
            CommandDeckJsonContext.Default.CommandDeckOptions );
        json = InjectSchemaHint( json );
        CopyShippedSchema( Path.GetDirectoryName( resolved )! );

        await File.WriteAllTextAsync( resolved, json + Environment.NewLine, cancellationToken )
            .ConfigureAwait( false );
        return resolved;
    }

    private static string InjectSchemaHint( string json )
    {
        const string hint = "\"$schema\":\"commanddeck.schema.json\",";
        return json.StartsWith( '{' ) ? "{" + hint + json[1..] : json;
    }

    private static void CopyShippedSchema( string configDirectory )
    {
        var source = Path.Combine( AppContext.BaseDirectory, "commanddeck.schema.json" );
        if ( !File.Exists( source ) )
        {
            return;
        }

        var destination = Path.Combine( configDirectory, "commanddeck.schema.json" );
        if ( PathsIdentifyTheSameFile( source, destination ) )
        {
            return;
        }

        File.Copy( source, destination, overwrite: true );
    }

    private static bool PathsIdentifyTheSameFile( string left, string right )
    {
        if ( File.Exists( left ) && File.Exists( right ) &&
            LinuxPathIdentity.TryGet( left, out var leftIdentity ) &&
            LinuxPathIdentity.TryGet( right, out var rightIdentity ) &&
            string.Equals( leftIdentity, rightIdentity, StringComparison.Ordinal ) )
        {
            return true;
        }

        return string.Equals(
            CanonicalFilePath( left ),
            CanonicalFilePath( right ),
            StringComparison.Ordinal );
    }

    private static string CanonicalFilePath( string path )
    {
        var full = Path.GetFullPath( path );
        try
        {
            FileInfo file = new( full );
            if ( file.Exists )
            {
                var target = file.ResolveLinkTarget( returnFinalTarget: true );
                return Path.GetFullPath( target?.FullName ?? file.FullName );
            }

            if ( file.Directory is not { Exists: true } directory )
            {
                return full;
            }

            var directoryTarget = directory.ResolveLinkTarget( returnFinalTarget: true );
            var canonicalDirectory = Path.GetFullPath( directoryTarget?.FullName ?? directory.FullName );
            return Path.Combine( canonicalDirectory, file.Name );
        }
        catch ( IOException )
        {
            return full;
        }
    }
}
