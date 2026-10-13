using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions.Testing;

public sealed record ExtensionTestHostOptions
{
    public IReadOnlySet<string> HostFeatures { get; init; } = ExtensionFeatures.Interactive.Append( ExtensionFeatures.ErrorCodes ).ToHashSet( StringComparer.Ordinal );
    public IReadOnlySet<string> GrantedPermissions { get; init; } = new HashSet<string>( StringComparer.Ordinal )
    {
        ExtensionPermissionIds.ClipboardWrite, ExtensionPermissionIds.DesktopOpenUrl, ExtensionPermissionIds.StorageRead,
        ExtensionPermissionIds.StorageWrite, ExtensionPermissionIds.FilesRead, ExtensionPermissionIds.HttpRequest
    };
    public Dictionary<string, JsonElement> Settings { get; init; } = new( StringComparer.Ordinal );
    public Dictionary<string, JsonElement> Storage { get; init; } = new( StringComparer.Ordinal );
    public Dictionary<string, Func<BrokerHttpRequest, HttpResult>> Http { get; init; } = new( StringComparer.Ordinal );
    public Dictionary<string, Dictionary<string, byte[]>> Files { get; init; } = new( StringComparer.Ordinal );
}
