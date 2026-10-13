namespace CommandDeck.Specs;

public static partial class Program
{
    private static void OptionalToolCatalogPrintsExactPacmanCommand( )
    {
        Equal(
            "not found. Install with: sudo pacman -S --needed libqalculate",
            OptionalToolCatalog.DoctorDetail( "qalc", found: false ) );
        Equal( "found on PATH", OptionalToolCatalog.DoctorDetail( "qalc", found: true ) );
        if ( !OptionalToolCatalog.TryGet( "pdftotext", out var poppler ) || poppler is null )
        {
            throw new InvalidOperationException( "pdftotext must be in the optional tool catalog." );
        }

        Equal( "poppler", poppler.Package );
    }

    private static async Task ConfigInitWritesSchemaHintAsync( )
    {
        var directory = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-schema-init-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( directory );
        var path = Path.Combine( directory, "commanddeck.jsonc" );
        try
        {
            await ConfigurationLoader.WriteDefaultAsync( path, overwrite: true, CancellationToken.None );
            var json = await File.ReadAllTextAsync( path );
            True( json.Contains( "\"$schema\":\"commanddeck.schema.json\"", StringComparison.Ordinal ) );
            var loaded = await ConfigurationLoader.LoadAsync( path, CancellationToken.None );
            Equal( CalculatorOptions.UseQalcAuto, loaded.Search.Calculator.UseQalc );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }

        var besideBinary = Path.Combine(
            AppContext.BaseDirectory,
            $"commanddeck-init-{Guid.NewGuid( ):N}.jsonc" );
        var schemaBesideBinary = Path.Combine( AppContext.BaseDirectory, "commanddeck.schema.json" );
        var plantedSchema = false;
        try
        {
            if ( !File.Exists( schemaBesideBinary ) )
            {
                File.WriteAllText( schemaBesideBinary, "{}\n" );
                plantedSchema = true;
            }

            await ConfigurationLoader.WriteDefaultAsync( besideBinary, overwrite: true, CancellationToken.None );
            True(
                File.Exists( besideBinary ),
                "config init must succeed when the config directory is the binary directory" );
        }
        finally
        {
            if ( File.Exists( besideBinary ) )
            {
                File.Delete( besideBinary );
            }

            if ( plantedSchema && File.Exists( schemaBesideBinary ) )
            {
                File.Delete( schemaBesideBinary );
            }
        }

        var aliasRoot = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-schema-alias-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( aliasRoot );
        var aliasDirectory = Path.Combine( aliasRoot, "alias" );
        var aliasConfig = Path.Combine( aliasDirectory, $"commanddeck-init-{Guid.NewGuid( ):N}.jsonc" );
        var plantedAliasSchema = false;
        try
        {
            if ( !File.Exists( schemaBesideBinary ) )
            {
                File.WriteAllText( schemaBesideBinary, "{}\n" );
                plantedAliasSchema = true;
            }

            Directory.CreateSymbolicLink( aliasDirectory, AppContext.BaseDirectory );
            await ConfigurationLoader.WriteDefaultAsync( aliasConfig, overwrite: true, CancellationToken.None );
            True(
                File.Exists( aliasConfig ),
                "config init must succeed when the config directory is a symlink to the binary directory" );
        }
        finally
        {
            if ( File.Exists( aliasConfig ) )
            {
                File.Delete( aliasConfig );
            }

            if ( Directory.Exists( aliasDirectory ) || File.Exists( aliasDirectory ) )
            {
                Directory.Delete( aliasDirectory );
            }

            Directory.Delete( aliasRoot, recursive: true );
            if ( plantedAliasSchema && File.Exists( schemaBesideBinary ) )
            {
                File.Delete( schemaBesideBinary );
            }
        }
    }
}
