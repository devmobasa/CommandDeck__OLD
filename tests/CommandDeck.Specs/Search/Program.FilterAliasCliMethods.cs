namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FilterAliasCliPersistsAndValidatesAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, var database) = await CreateCliCatalogAsync( root, "Notes" );
            var saved = await RunCommandDeckProcessAsync(
                "filters", "alias", "set", "docs", "files", "--config", config );
            Equal( 0, saved.ExitCode );
            True( saved.StandardOutput.Contains( "saved", StringComparison.OrdinalIgnoreCase ) );

            var listed = await RunCommandDeckProcessAsync(
                "filters", "list", "--json", "--config", config );
            Equal( 0, listed.ExitCode );
            True( listed.StandardOutput.Contains(
                "\"alias\":\"docs\"",
                StringComparison.Ordinal ) );
            await using ( SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } ) )
            {
                Equal(
                    "files",
                    (await catalog.GetFilterAliasesAsync( CancellationToken.None )).Single( ).FilterId );
            }

            var conflict = await RunCommandDeckProcessAsync(
                "filters", "alias", "set", "app", "files", "--config", config );
            Equal( 1, conflict.ExitCode );
            True( conflict.StandardError.Contains( "conflicts", StringComparison.Ordinal ) );

            var removed = await RunCommandDeckProcessAsync(
                "filters", "alias", "remove", "docs", "--config", config );
            Equal( 0, removed.ExitCode );
            await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = database } );
            Equal( 0, (await reopened.GetFilterAliasesAsync( CancellationToken.None )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
