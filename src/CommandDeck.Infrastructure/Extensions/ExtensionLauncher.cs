namespace CommandDeck;

public enum ExtensionLaunchProfile
{
    Interactive,
    Search
}

public sealed record ExtensionLaunchRequest(
    string ArtifactDirectory, string Entrypoint, string InstallationKey,
    ExtensionLaunchProfile Profile, string? DebugSocket = null );

public interface IExtensionProcess : IAsyncDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    string ReadDiagnostics( );
}

public interface IExtensionLauncher
{
    ValueTask<IExtensionProcess> StartAsync( ExtensionLaunchRequest request, CancellationToken cancellationToken );
}
