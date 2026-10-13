namespace CommandDeck.Specs;

public static partial class Program
{
    private static void DeveloperInstructionsSteerFileWorkToCommandDeckTools( )
    {
        var context =
            AgentToolCatalog.ForTrustLevel( AgentTrustLevels.ApprovedContext, packagesAvailable: false );
        var withContext = AgentToolCatalog.DeveloperInstructions( AgentTrustLevels.ApprovedContext, context );
        True( withContext.Contains( "commanddeck_read_file", StringComparison.Ordinal ),
            "the file-read tool is named for the model" );
        True( withContext.Contains( "no direct or native access", StringComparison.Ordinal ),
            "the model is told it has no native filesystem access" );
        True( withContext.Contains( "Never report a failure of your own built-in access", StringComparison.Ordinal ),
            "the model is told not to report its own built-in access failing" );

        // Below approved context there are no file tools; the steering must not
        // promise them and must say file work is unavailable.
        var launcher =
            AgentToolCatalog.ForTrustLevel( AgentTrustLevels.Launcher, packagesAvailable: false );
        var atLauncher = AgentToolCatalog.DeveloperInstructions( AgentTrustLevels.Launcher, launcher );
        True( !atLauncher.Contains( "commanddeck_read_file", StringComparison.Ordinal ),
            "no file-read tool is promised below approved context" );
        True( atLauncher.Contains( "cannot read files", StringComparison.Ordinal ),
            "the model is told file reading is unavailable at this level" );

        // With no tools at all, the model is still told it has no direct access.
        var none = AgentToolCatalog.DeveloperInstructions( AgentTrustLevels.Answers, [] );
        True( none.Contains( "no direct access to the filesystem", StringComparison.Ordinal ),
            "the toolless prompt still states there is no direct filesystem access" );
    }
}
