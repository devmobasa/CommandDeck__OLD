namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SearchCliEscapesTerminalControlsAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, _) = await CreateCliCatalogAsync( root, "Notes" );
            var folder = Directory.CreateDirectory( Path.Combine( root, "hostile" ) ).FullName;
            await File.WriteAllTextAsync( Path.Combine( folder, "probe\u001b[31mRED" ), "fixture" );

            var outcome = await RunCommandDeckProcessAsync(
                "search", "--query", $"browse: {folder}", "--config", config );

            Equal( 0, outcome.ExitCode );
            True( outcome.StandardOutput.Contains( "probe\\u001B[31mRED", StringComparison.Ordinal ),
                $"search did not list the escaped file name: {outcome.StandardOutput.Replace( "\u001b", "^[", StringComparison.Ordinal )}" );
            True( !outcome.StandardOutput.Contains( '\u001b' ), "search printed a raw terminal escape" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
