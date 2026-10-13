using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public static class ExtensionPermissionPolicy
{
    public static string? Data( string operation ) => operation switch
    {
        ExtensionDataOperations.Settings => null,
        ExtensionDataOperations.Read => ExtensionPermissionIds.StorageRead,
        ExtensionDataOperations.Write or ExtensionDataOperations.Delete => ExtensionPermissionIds.StorageWrite,
        _ => throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidRequest )
    };

    public static string Broker( string operation ) => operation switch
    {
        ExtensionBrokerOperations.ListFiles or ExtensionBrokerOperations.ReadFile => ExtensionPermissionIds.FilesRead,
        ExtensionBrokerOperations.SendHttp => ExtensionPermissionIds.HttpRequest,
        _ => throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidRequest )
    };

    public static string? Effect( string kind ) => kind switch
    {
        ExtensionActionKinds.CopyText => ExtensionPermissionIds.ClipboardWrite,
        ExtensionActionKinds.OpenUrl => ExtensionPermissionIds.DesktopOpenUrl,
        _ => null
    };

    public static IReadOnlySet<string> Effective( ExtensionInstallation installation, ExtensionCommand command ) =>
        installation.Grants.Intersect( command.Capabilities, StringComparer.Ordinal ).ToHashSet( StringComparer.Ordinal );
}
