using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CommandDeck;

public static class OmarchyMenuScanner
{
    public static async IAsyncEnumerable<IndexedItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken )
    {
        List<OmarchyMenuDocument> documents = [];
        foreach ( var candidate in GetCandidates( ).Distinct( StringComparer.Ordinal ) )
        {
            if ( !File.Exists( candidate ) )
            {
                continue;
            }

            try
            {
                documents.Add( new OmarchyMenuDocument(
                    candidate,
                    await File.ReadAllTextAsync( candidate, cancellationToken ).ConfigureAwait( false ) ) );
            }
            catch ( Exception exception ) when (
                exception is UnauthorizedAccessException or IOException )
            {
            }
        }

        foreach ( var item in ParseDocuments( documents ) )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            yield return item;
        }
    }

    public static IReadOnlyList<IndexedItem> ParseDocuments(
        IEnumerable<OmarchyMenuDocument> documents )
    {
        Dictionary<string, MenuEntry> merged = new( StringComparer.Ordinal );
        foreach ( var source in documents )
        {
            foreach ( (var id, var entry) in ParseDocument( source ) )
            {
                merged[id] = entry;
            }
        }

        List<IndexedItem> items = [];
        foreach ( (var id, var entry) in merged.OrderBy( static item => item.Key, StringComparer.Ordinal ) )
        {
            var menuRoot = id.Split( '.', 2 )[0];
            if ( menuRoot is "apps" or "system" || string.IsNullOrWhiteSpace( entry.Command ) )
            {
                continue;
            }

            var title = entry.Label ?? entry.Title ?? id.Split( '.' ).Last( );
            items.Add( new IndexedItem
            {
                StableId = $"omarchy:{id}",
                Kind = ResultKinds.Omarchy,
                Title = title,
                Subtitle = BuildPath( id, merged ),
                Icon = entry.Icon ?? "omarchy",
                Body = string.Join( ' ', new[]
                {
                    id,
                    title,
                    entry.Command,
                    entry.Target,
                    entry.Provider,
                    string.Join(' ', entry.Aliases)
                }.Where( static value => !string.IsNullOrWhiteSpace( value ) ) ),
                PayloadJson = PayloadWriter.Create(
                    ("command", entry.Command),
                    ("source", entry.SourcePath),
                    ("menuId", id),
                    ("menuRoot", menuRoot),
                    ("sourceGroup", SourceIds.OmarchyControl) ),
                ProviderId = ProviderIds.Omarchy,
                SourceId = SourceIds.OmarchyControl
            } );
        }

        items.Add( CreateRoute( "root", "Omarchy Control", SourceIds.OmarchyControl ) );
        items.Add( CreateRoute( "system", "System Menu", SourceIds.OmarchySystem ) );
        return items;
    }

    private static IndexedItem CreateRoute( string route, string title, string sourceId ) => new( )
    {
        StableId = $"omarchy-route:{route}",
        Kind = ResultKinds.Omarchy,
        Title = title,
        Subtitle = "Open Omarchy's native menu",
        Icon = route == "system" ? "system-shutdown" : "omarchy",
        Body = $"{title} {route}",
        PayloadJson = PayloadWriter.Create(
            ("argv", $"omarchy-menu\u001ftoggle\u001f{route}"),
            ("menuId", route),
            ("menuRoot", route),
            ("sourceGroup", sourceId),
            ("nativeRoute", "true") ),
        ProviderId = ProviderIds.Omarchy,
        SourceId = sourceId
    };

    private static Dictionary<string, MenuEntry> ParseDocument( OmarchyMenuDocument source )
    {
        JsonDocumentOptions options = new( )
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        try
        {
            using var document = JsonDocument.Parse( source.Jsonc, options );
            Dictionary<string, MenuEntry> entries = new( StringComparer.Ordinal );
            if ( document.RootElement.ValueKind == JsonValueKind.Object )
            {
                foreach ( var property in document.RootElement.EnumerateObject( ) )
                {
                    if ( property.Value.ValueKind == JsonValueKind.Object )
                    {
                        entries[property.Name] = ReadEntry( property.Name, property.Value, source.Path );
                    }
                    else if ( property.Value.ValueKind == JsonValueKind.String &&
                             property.Name.Contains( '.', StringComparison.Ordinal ) )
                    {
                        entries[property.Name] = new MenuEntry
                        {
                            Id = property.Name,
                            Label = property.Name.Split( '.' ).Last( ),
                            Command = property.Value.GetString( ),
                            SourcePath = source.Path
                        };
                    }
                }

                ReadLegacy( document.RootElement, source.Path, entries );
            }

            return entries;
        }
        catch ( JsonException )
        {
            return new Dictionary<string, MenuEntry>( StringComparer.Ordinal );
        }
    }

    private static MenuEntry ReadEntry( string id, JsonElement value, string sourcePath ) => new( )
    {
        Id = id,
        Label = GetString( value, "label", "name" ),
        Title = GetString( value, "title" ),
        Command = GetString( value, "action", "command", "exec", "run" ),
        Icon = GetString( value, "icon" ),
        Target = GetString( value, "target" ),
        Provider = GetString( value, "provider" ),
        When = GetString( value, "when" ),
        Checked = GetString( value, "checked" ),
        Aliases = GetStrings( value, "aliases" ),
        SourcePath = sourcePath
    };

    private static void ReadLegacy(
        JsonElement element,
        string sourcePath,
        Dictionary<string, MenuEntry> entries )
    {
        foreach ( var property in element.EnumerateObject( ) )
        {
            if ( property.Value.ValueKind == JsonValueKind.Array )
            {
                foreach ( var child in property.Value.EnumerateArray( ) )
                {
                    if ( child.ValueKind == JsonValueKind.Object )
                    {
                        ReadLegacyObject( child, sourcePath, entries );
                    }
                }
            }
        }
    }

    private static void ReadLegacyObject(
        JsonElement value,
        string sourcePath,
        Dictionary<string, MenuEntry> entries )
    {
        var id = GetString( value, "id" );
        var command = GetString( value, "action", "command", "exec", "run" );
        if ( !string.IsNullOrWhiteSpace( id ) && !string.IsNullOrWhiteSpace( command ) )
        {
            entries[id] = ReadEntry( id, value, sourcePath );
        }

        foreach ( var property in value.EnumerateObject( ) )
        {
            if ( property.Value.ValueKind == JsonValueKind.Object )
            {
                ReadLegacyObject( property.Value, sourcePath, entries );
            }
            else if ( property.Value.ValueKind == JsonValueKind.Array )
            {
                foreach ( var child in property.Value.EnumerateArray( ) )
                {
                    if ( child.ValueKind == JsonValueKind.Object )
                    {
                        ReadLegacyObject( child, sourcePath, entries );
                    }
                }
            }
        }
    }

    private static string? BuildPath( string id, IReadOnlyDictionary<string, MenuEntry> entries )
    {
        var segments = id.Split( '.' );
        if ( segments.Length <= 1 )
        {
            return null;
        }

        List<string> labels = [];
        for ( var index = 1; index < segments.Length; index++ )
        {
            var parent = string.Join( '.', segments.Take( index ) );
            labels.Add( entries.TryGetValue( parent, out var value )
                ? value.Label ?? value.Title ?? segments[index - 1]
                : segments[index - 1] );
        }

        return string.Join( " › ", labels );
    }

    private static string? GetString( JsonElement element, params string[] names )
    {
        foreach ( var name in names )
        {
            if ( element.TryGetProperty( name, out var value ) &&
                value.ValueKind == JsonValueKind.String )
            {
                return value.GetString( );
            }
        }

        return null;
    }

    private static List<string> GetStrings( JsonElement element, string name )
    {
        if ( !element.TryGetProperty( name, out var value ) )
        {
            return [];
        }

        if ( value.ValueKind == JsonValueKind.String )
        {
            return [value.GetString( ) ?? string.Empty];
        }

        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray( )
                .Where( static item => item.ValueKind == JsonValueKind.String )
                .Select( static item => item.GetString( ) ?? string.Empty )
                .Where( static item => item.Length > 0 )
                .ToList( )
            : [];
    }

    private static IEnumerable<string> GetCandidates( )
    {
        var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
        var configHome = Environment.GetEnvironmentVariable( "XDG_CONFIG_HOME" )
                         ?? Path.Combine( home, ".config" );
        var omarchyPath = Environment.GetEnvironmentVariable( "OMARCHY_PATH" );

        if ( !string.IsNullOrWhiteSpace( omarchyPath ) )
        {
            yield return Path.Combine( omarchyPath, "default", "omarchy", "omarchy-menu.jsonc" );
            yield return Path.Combine(
                omarchyPath,
                "config",
                "omarchy",
                "extensions",
                "omarchy-menu.jsonc" );
        }

        yield return "/usr/share/omarchy/default/omarchy/omarchy-menu.jsonc";
        yield return "/usr/share/omarchy/config/omarchy/extensions/omarchy-menu.jsonc";
        yield return Path.Combine( home, ".local", "share", "omarchy", "default", "omarchy", "omarchy-menu.jsonc" );
        yield return Path.Combine(
            home,
            ".local",
            "share",
            "omarchy",
            "config",
            "omarchy",
            "extensions",
            "omarchy-menu.jsonc" );
        yield return Path.Combine( configHome, "omarchy", "extensions", "omarchy-menu.jsonc" );
    }

    private sealed class MenuEntry
    {
        public required string Id { get; init; }
        public string? Label { get; init; }
        public string? Title { get; init; }
        public string? Command { get; init; }
        public string? Icon { get; init; }
        public string? Target { get; init; }
        public string? Provider { get; init; }
        public string? When { get; init; }
        public string? Checked { get; init; }
        public List<string> Aliases { get; init; } = [];
        public required string SourcePath { get; init; }
    }
}

public sealed record OmarchyMenuDocument( string Path, string Jsonc );
