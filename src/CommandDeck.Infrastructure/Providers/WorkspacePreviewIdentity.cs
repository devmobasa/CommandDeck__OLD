using System.Globalization;
using System.Text.Json;

namespace CommandDeck;

internal static class WorkspacePreviewIdentity
{
    public static Dictionary<string, string> CreateMetadata( JsonElement workspace )
    {
        Dictionary<string, string> metadata = new( StringComparer.Ordinal );
        if ( !workspace.TryGetProperty( "id", out var rawId ) ||
            !int.TryParse( rawId.ToString( ), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id ) ||
            !workspace.TryGetProperty( "name", out var rawName ) || rawName.ValueKind != JsonValueKind.String ||
            !workspace.TryGetProperty( "monitor", out var rawMonitor ) || rawMonitor.ValueKind != JsonValueKind.String )
        {
            return metadata;
        }

        var name = rawName.GetString( );
        var monitor = rawMonitor.GetString( );
        if ( string.IsNullOrWhiteSpace( name ) || string.IsNullOrWhiteSpace( monitor ) ||
            name.Length > 256 || monitor.Length > 256 || name.Any( char.IsControl ) || monitor.Any( char.IsControl ) )
        {
            return metadata;
        }

        // Hyprland reserves -99..-2 for special workspaces; named ordinary
        // workspaces live below that range. Name and range must agree.
        var specialName = name == "special" || name.StartsWith( "special:", StringComparison.Ordinal );
        var specialId = id is >= -99 and <= -2;
        if ( id is 0 or -1 || specialName != specialId )
        {
            return metadata;
        }

        metadata["workspaceId"] = id.ToString( CultureInfo.InvariantCulture );
        metadata["workspaceName"] = name;
        metadata["monitorName"] = monitor;
        metadata["workspaceKind"] = specialId ? "special" : "ordinary";
        return metadata;
    }
}
