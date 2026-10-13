using System.Text.Json;

namespace CommandDeck;

public sealed class HyprlandProvider : ICommandProvider
{
    // Hyprland reports an empty title for windows that never set one.
    private const string UntitledWindowTitle = "(untitled window)";

    private readonly IApplicationIconResolver? _iconResolver;

    public HyprlandProvider( IApplicationIconResolver? iconResolver = null )
    {
        _iconResolver = iconResolver;
    }

    public string Id => ProviderIds.Hyprland;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        if ( !ProcessRunner.ExistsOnPath( "hyprctl" ) )
        {
            return [];
        }

        var clientsTask = ProcessRunner.RunAsync(
                "hyprctl",
                ["-j", "clients"],
                standardInput: null,
                workingDirectory: null,
                TimeSpan.FromMilliseconds( 250 ),
                cancellationToken )
            .AsTask( );

        var workspacesTask = ProcessRunner.RunAsync(
                "hyprctl",
                ["-j", "workspaces"],
                standardInput: null,
                workingDirectory: null,
                TimeSpan.FromMilliseconds( 250 ),
                cancellationToken )
            .AsTask( );

        await Task.WhenAll( clientsTask, workspacesTask ).ConfigureAwait( false );

        List<SearchResult> results = [];
        await ParseClientsAsync(
            clientsTask.Result.StandardOutput,
            query.Text,
            request.ActiveWorkspace,
            results,
            cancellationToken ).ConfigureAwait( false );
        ParseWorkspaces(
            workspacesTask.Result.StandardOutput,
            query.Text,
            request.ActiveWorkspace,
            results );

        return results
            .OrderByDescending( static result => result.Score )
            .ThenBy( static result => WorkspaceNumber( result ) )
            .ThenBy( static result => result.Title, StringComparer.OrdinalIgnoreCase )
            .Take( Math.Clamp( request.Limit, 1, 100 ) )
            .ToArray( );
    }

    private async ValueTask ParseClientsAsync(
        string json,
        string query,
        string? activeWorkspace,
        List<SearchResult> results,
        CancellationToken cancellationToken )
    {
        if ( string.IsNullOrWhiteSpace( json ) )
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse( json );
            var windowClasses = document.RootElement
                .EnumerateArray( )
                .Select( GetApplicationClass )
                .Distinct( StringComparer.OrdinalIgnoreCase )
                .ToArray( );
            var icons = await ResolveWindowIconsAsync(
                windowClasses,
                cancellationToken ).ConfigureAwait( false );
            foreach ( var client in document.RootElement.EnumerateArray( ) )
            {
                var address = GetString( client, "address" ) ?? string.Empty;
                var title = GetString( client, "title" );
                if ( string.IsNullOrWhiteSpace( title ) )
                {
                    title = UntitledWindowTitle;
                }

                var appClass = GetApplicationClass( client );
                var workspace = GetNestedStringOrNumber( client, "workspace", "name" )
                                ?? GetNestedStringOrNumber( client, "workspace", "id" )
                                ?? "?";
                var haystack = $"{title} {appClass} {workspace}";
                var isActiveWorkspace = IsActiveWorkspace( workspace, activeWorkspace );
                var score = Math.Max(
                    FuzzyMatcher.Score( query, title ),
                    FuzzyMatcher.Score( query, appClass ) );

                if ( !string.IsNullOrWhiteSpace( query ) && score <= 0 )
                {
                    continue;
                }

                results.Add( new SearchResult
                {
                    Id = $"window:{address}",
                    Kind = ResultKinds.Window,
                    Title = title,
                    Subtitle = $"{appClass} · workspace {workspace}",
                    Icon = icons.GetValueOrDefault( appClass, appClass.ToLowerInvariant( ) ),
                    ProviderId = ProviderIds.Hyprland,
                    SourceId = SourceIds.WindowsWorkspaces,
                    Score = string.IsNullOrWhiteSpace( query )
                        ? isActiveWorkspace ? 0.35 : 0.2
                        : score + (isActiveWorkspace ? 0.03 : 0),
                    Details =
                    [
                        new DetailField("Class", appClass, Monospace: true),
                        new DetailField("Workspace", workspace),
                        new DetailField("Address", address, Monospace: true)
                    ],
                    Actions =
                    [
                        new CommandAction
                        {
                            Id = "focus",
                            Title = "Focus Window",
                            Kind = ActionKinds.HyprlandDispatch,
                            Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [HyprlandActionOperations.OperationKey] = HyprlandActionOperations.FocusWindow,
                                [HyprlandActionOperations.ArgumentKey] = $"address:{address}"
                            }
                        },
                        new CommandAction
                        {
                            Id = "close",
                            Title = "Close Window",
                            Kind = ActionKinds.HyprlandDispatch,
                            IsDestructive = true,
                            RequiresConfirmation = true,
                            Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [HyprlandActionOperations.OperationKey] = HyprlandActionOperations.CloseWindow,
                                [HyprlandActionOperations.ArgumentKey] = $"address:{address}"
                            }
                        }
                    ],
                    Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
                    {
                        ["address"] = address,
                        ["class"] = appClass,
                        ["workspace"] = workspace,
                        ["haystack"] = haystack
                    }
                } );
            }
        }
        catch ( JsonException )
        {
        }
    }

    private async ValueTask<IReadOnlyDictionary<string, string>> ResolveWindowIconsAsync(
        IReadOnlyList<string> windowClasses,
        CancellationToken cancellationToken )
    {
        if ( _iconResolver is null )
        {
            return windowClasses.ToDictionary(
                static windowClass => windowClass,
                static windowClass => windowClass.ToLowerInvariant( ),
                StringComparer.OrdinalIgnoreCase );
        }

        return await _iconResolver.ResolveApplicationIconsAsync(
            windowClasses,
            cancellationToken ).ConfigureAwait( false );
    }

    private static string GetApplicationClass( JsonElement client ) =>
        GetString( client, "class" ) ?? GetString( client, "initialClass" ) ?? "window";

    private static void ParseWorkspaces(
        string json,
        string query,
        string? activeWorkspace,
        List<SearchResult> results )
    {
        if ( string.IsNullOrWhiteSpace( json ) )
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse( json );
            foreach ( var workspace in document.RootElement.EnumerateArray( ) )
            {
                var id = GetNumberOrString( workspace, "id" ) ?? "?";
                var name = GetString( workspace, "name" ) ?? id;
                var windows = GetNumberOrString( workspace, "windows" ) ?? "0";
                var monitor = GetString( workspace, "monitor" ) ?? "unknown";
                var title = $"Workspace {name}";
                var occupied = int.TryParse( windows, out var windowCount ) && windowCount > 0;
                var isActiveWorkspace =
                    IsActiveWorkspace( id, activeWorkspace ) ||
                    IsActiveWorkspace( name, activeWorkspace );
                var score = Math.Max(
                    FuzzyMatcher.Score( query, title ),
                    FuzzyMatcher.Score( query, monitor ) );

                if ( !string.IsNullOrWhiteSpace( query ) && score <= 0 )
                {
                    continue;
                }

                results.Add( new SearchResult
                {
                    Id = $"workspace:{id}",
                    Kind = ResultKinds.Workspace,
                    Title = title,
                    Subtitle = $"{windows} windows · {monitor}",
                    Icon = "view-grid",
                    ProviderId = ProviderIds.Hyprland,
                    SourceId = SourceIds.WindowsWorkspaces,
                    Score = string.IsNullOrWhiteSpace( query )
                        ? isActiveWorkspace ? 0.36 : occupied ? 0.19 : 0.18
                        : score + (isActiveWorkspace ? 0.03 : 0),
                    Details =
                    [
                        new DetailField("Monitor", monitor, Monospace: true),
                        new DetailField("Windows", windows)
                    ],
                    Actions =
                    [
                        new CommandAction
                        {
                            Id = "switch",
                            Title = "Switch Workspace",
                            Kind = ActionKinds.HyprlandDispatch,
                            Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [HyprlandActionOperations.OperationKey] = HyprlandActionOperations.SwitchWorkspace,
                                [HyprlandActionOperations.ArgumentKey] = name
                            }
                        }
                    ],
                    Metadata = WorkspacePreviewIdentity.CreateMetadata( workspace )
                } );
            }
        }
        catch ( JsonException )
        {
        }
    }

    private static string? GetString( JsonElement element, string property ) =>
        element.TryGetProperty( property, out var value ) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString( )
            : null;

    private static string? GetNumberOrString( JsonElement element, string property )
    {
        if ( !element.TryGetProperty( property, out var value ) )
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString( ),
            JsonValueKind.Number => value.GetRawText( ),
            _ => null
        };
    }

    private static string? GetNestedStringOrNumber(
        JsonElement element,
        string parent,
        string property ) =>
        element.TryGetProperty( parent, out var nested )
            ? GetNumberOrString( nested, property )
            : null;

    private static bool IsActiveWorkspace( string candidate, string? activeWorkspace ) =>
        !string.IsNullOrWhiteSpace( activeWorkspace ) &&
        string.Equals( candidate, activeWorkspace, StringComparison.OrdinalIgnoreCase );

    private static int WorkspaceNumber( SearchResult result ) =>
        result.Kind == ResultKinds.Workspace &&
        result.Metadata.TryGetValue( "workspaceName", out var name ) &&
        int.TryParse( name, out var number ) && number > 0
            ? number
            : int.MaxValue;

}
