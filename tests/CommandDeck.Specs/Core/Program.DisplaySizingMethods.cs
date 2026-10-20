using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task DisplaySizingCliMigratesBeforeCatalogCreationAsync( )
    {
        var root = CreateCliSpecRoot( );
        var previous = Environment.GetEnvironmentVariable( "XDG_STATE_HOME" );
        try
        {
            var database = Path.Combine( root, "catalog.db" );
            var config = Path.Combine( root, "config.json" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = database },
                Indexing = new IndexingOptions
                {
                    Roots = [],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync( config,
                JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );

            var state = Path.Combine( root, "state" );
            Environment.SetEnvironmentVariable( "XDG_STATE_HOME", state );

            var first = await RunCommandDeckProcessAsync( "catalog", "inspect", "--json", "--config", config );
            True( first.ExitCode == 0, first.StandardError );
            True( File.Exists( database ) );
            var marker = Path.Combine( state, "commanddeck", "display-sizing" );
            Equal( "logical-v1\n", await File.ReadAllTextAsync( marker ) );

            File.Delete( marker );
            var existing = await RunCommandDeckProcessAsync( "catalog", "inspect", "--json", "--config", config );
            True( existing.ExitCode == 0, existing.StandardError );
            Equal( "legacy-v1\n", await File.ReadAllTextAsync( marker ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "XDG_STATE_HOME", previous );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task DisplaySizingMigrationPreservesExistingUsersAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-display-sizing-" );
        try
        {
            var database = Path.Combine( root.FullName, "catalog.db" );
            var state = Path.Combine( root.FullName, "state" );
            var marker = Path.Combine( state, "display-sizing" );

            await DisplaySizingMigration.EnsureProfileAsync( database, state, CancellationToken.None );
            Equal( "logical-v1\n", await File.ReadAllTextAsync( marker ) );

            File.Delete( marker );
            await File.WriteAllTextAsync( database, "existing catalog" );
            await DisplaySizingMigration.EnsureProfileAsync( database, state, CancellationToken.None );
            Equal( "legacy-v1\n", await File.ReadAllTextAsync( marker ) );

            File.Delete( database );
            File.Delete( marker );
            await File.WriteAllTextAsync( Path.Combine( state, "display-size" ), "large\n" );
            await DisplaySizingMigration.EnsureProfileAsync( database, state, CancellationToken.None );
            Equal( "legacy-v1\n", await File.ReadAllTextAsync( marker ) );
            Equal( "large\n", await File.ReadAllTextAsync( Path.Combine( state, "display-size" ) ) );

            await File.WriteAllTextAsync( marker, "logical-v1\n" );
            await DisplaySizingMigration.EnsureProfileAsync( database, state, CancellationToken.None );
            Equal( "logical-v1\n", await File.ReadAllTextAsync( marker ) );

            await File.WriteAllTextAsync( marker, "future-profile\n" );
            await DisplaySizingMigration.EnsureProfileAsync( database, state, CancellationToken.None );
            Equal( "future-profile\n", await File.ReadAllTextAsync( marker ) );
        }
        finally
        {
            root.Delete( recursive: true );
        }
    }
}
