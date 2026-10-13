namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    private ExtensionPackageRegistry? _extensions;
    private ExtensionConnections? _extensionConnections;
    private ExtensionDevelopment? _extensionDevelopment;
    internal ExtensionConnections ExtensionConnections
    {
        get
        {
            var connections = _extensionConnections;
            if ( connections is null )
            {
                throw new InvalidOperationException( "Extension connections are unavailable." );
            }

            return connections;
        }
    }

    private ExtensionDevelopment ExtensionDevelopment
    {
        get
        {
            var development = _extensionDevelopment;
            if ( development is null )
            {
                throw new InvalidOperationException( "Extension development is unavailable." );
            }

            return development;
        }
    }
    internal ExtensionSessionService ExtensionSessions
    {
        get
        {
            var sessions = _extensionSessions;
            if ( sessions is null )
            {
                throw new InvalidOperationException( "Extension sessions are unavailable." );
            }

            return sessions;
        }
    }
    private ExtensionSessionService? _extensionSessions;
    private ExtensionSearchService? _extensionSearch;
    internal ExtensionSearchService ExtensionSearch
    {
        get
        {
            var search = _extensionSearch;
            if ( search is null )
            {
                throw new InvalidOperationException( "Extension search is unavailable." );
            }

            return search;
        }
    }
    public ContributionRegistrySnapshot Contributions => _extensions?.Snapshot ?? ContributionRegistrySnapshot.BuiltIns;

    private ExtensionManagementService? _extensionManagement;
    public async ValueTask<ExtensionInventory> ManageExtensionsAsync(
        ExtensionManagementRequest request, string? query, int offset, int limit, CancellationToken cancellationToken )
    {
        var command = request.ToCommand( );
        var management = _extensionManagement;
        var registry = _extensions;
        if ( management is null || registry is null )
        {
            throw new InvalidOperationException( "Extension management is unavailable." );
        }

        var outcome = await management.HandleAsync( command, cancellationToken ).ConfigureAwait( false );
        return ExtensionInventoryProjector.Project( registry.Document, registry.Error, outcome, query, offset, limit );
    }

    public ValueTask<LauncherPreferences> PatchSourcePreferencesAsync( SourcePreferencePatch patch, CancellationToken cancellationToken ) =>
        _preferences.PatchSourcePreferencesAsync( patch, cancellationToken );
}
