using System.Text;

namespace CommandDeck;

// Run before catalog initialization: after it, a new installation and an old
// default user with no display-size file are indistinguishable to the UI.
internal static class DisplaySizingMigration
{
    internal static async Task InitializeAsync( string? configPath, CancellationToken cancellationToken )
    {
        var stateDirectory = PathExpansion.Expand( "$XDG_STATE_HOME/commanddeck" );
        if ( File.Exists( Path.Combine( stateDirectory, "display-sizing" ) ) )
        {
            return;
        }

        var options = await ConfigurationLoader.LoadAsync( configPath, cancellationToken ).ConfigureAwait( false );
        await EnsureProfileAsync( PathExpansion.Expand( options.Storage.DatabasePath ),
            stateDirectory, cancellationToken ).ConfigureAwait( false );
    }

    internal static async Task EnsureProfileAsync( string databasePath, string stateDirectory,
        CancellationToken cancellationToken )
    {
        var path = Path.Combine( stateDirectory, "display-sizing" );
        try
        {
            if ( File.Exists( path ) )
            {
                return;
            }

            var existing = File.Exists( databasePath ) ||
                (Directory.Exists( stateDirectory ) && Directory.EnumerateFileSystemEntries( stateDirectory ).Any( ));
            Directory.CreateDirectory( stateDirectory );

            // CreateNew preserves an explicit choice or a concurrent initializer.
            await using var stream = new FileStream( path, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, bufferSize: 4096, useAsync: true );
            await stream.WriteAsync( Encoding.UTF8.GetBytes( existing ? "legacy-v1\n" : "logical-v1\n" ),
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException )
        {
            if ( !File.Exists( path ) )
            {
                AppLog.Warning( "Display sizing migration could not be saved; keeping previous sizing: " + error.Message );
            }
        }
    }
}
