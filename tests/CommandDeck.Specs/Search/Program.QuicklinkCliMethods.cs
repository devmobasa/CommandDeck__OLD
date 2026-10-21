using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task QuicklinkCliManagesDurableStateAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, _) = await CreateCliCatalogAsync( root, "Notes" );
            var original = await ConfigurationLoader.LoadAsync( config, default );
            var configured = new CommandDeckOptions
            {
                Storage = original.Storage,
                Indexing = original.Indexing,
                Privacy = original.Privacy,
                Ai = original.Ai,
                Quicklinks = [TestQuicklink( "configured", alias: "configdocs" )]
            };
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( configured, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            var add = await RunCommandDeckProcessAsync( "quicklinks", "add", "--title", "CLI docs",
                "--url", "https://example.test/?q={query}", "--alias", "cldocs", "--icon", "help-browser", "--json", "--config", config );
            Equal( 0, add.ExitCode );
            var snapshot = JsonSerializer.Deserialize( add.StandardOutput, CommandDeckJsonContext.Default.QuicklinksResult );
            var link = snapshot!.Quicklinks.Single( entry => entry.Source == "user" ).Quicklink;
            Equal( "CLI docs", link.Title );
            Equal( "help-browser", link.Icon );
            var edit = await RunCommandDeckProcessAsync( "quicklinks", "edit", link.Id, "--title", "CLI renamed",
                "--url", "https://example.test/static", "--config", config );
            Equal( 0, edit.ExitCode );
            var list = await RunCommandDeckProcessAsync( "quicklinks", "--json", "--config", config );
            Equal( 0, list.ExitCode );
            True( list.StandardOutput.Contains( "CLI renamed", StringComparison.Ordinal ) );
            var invalid = await RunCommandDeckProcessAsync( "quicklinks", "add", "--title", "Unsafe",
                "--url", "javascript:alert(1)", "--config", config );
            Equal( 1, invalid.ExitCode );
            True( invalid.StandardError.Contains( "HTTP", StringComparison.Ordinal ) );
            var disable = await RunCommandDeckProcessAsync( "quicklinks", "disable", link.Id, "--config", config );
            Equal( 1, disable.ExitCode );
            True( disable.StandardError.Contains( "Only configured", StringComparison.Ordinal ) );
            var disabled = await RunCommandDeckProcessAsync( "quicklinks", "disable", "configured", "--json", "--config", config );
            Equal( 0, disabled.ExitCode );
            True( !JsonSerializer.Deserialize( disabled.StandardOutput, CommandDeckJsonContext.Default.QuicklinksResult )!
                .Quicklinks.Single( entry => entry.Source == "configured" ).Enabled );
            var exported = await RunCommandDeckProcessAsync( "preferences", "export", "--config", config );
            Equal( 0, exported.ExitCode );
            var backupPath = Path.Combine( root, "backup.json" );
            await File.WriteAllTextAsync( backupPath, exported.StandardOutput );
            var enabled = await RunCommandDeckProcessAsync( "quicklinks", "enable", "configured", "--json", "--config", config );
            Equal( 0, enabled.ExitCode );
            True( JsonSerializer.Deserialize( enabled.StandardOutput, CommandDeckJsonContext.Default.QuicklinksResult )!
                .Quicklinks.Single( entry => entry.Source == "configured" ).Enabled );
            var removed = await RunCommandDeckProcessAsync( "quicklinks", "remove", link.Id, "--config", config );
            Equal( 0, removed.ExitCode );
            var empty = await RunCommandDeckProcessAsync( "quicklinks", "--json", "--config", config );
            Equal( 1, JsonSerializer.Deserialize( empty.StandardOutput, CommandDeckJsonContext.Default.QuicklinksResult )!.Quicklinks.Count );
            var restore = await RunCommandDeckProcessAsync( "quicklinks", "restore", backupPath, "--json", "--config", config );
            Equal( 0, restore.ExitCode );
            var restored = JsonSerializer.Deserialize( restore.StandardOutput, CommandDeckJsonContext.Default.QuicklinksResult );
            Equal( "CLI renamed", restored!.Quicklinks.Single( entry => entry.Source == "user" ).Quicklink.Title );
            True( !restored.Quicklinks.Single( entry => entry.Source == "configured" ).Enabled );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
