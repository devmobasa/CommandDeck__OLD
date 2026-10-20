using System.Text.Json;

namespace CommandDeck;

// Reads the entries of one Omarchy menu document in document order: the
// current schema (IDs as keys, optionally wrapped in "items"), the plain
// `"id": "command"` form, and the legacy arrays of objects with an "id".
public sealed partial class OmarchyMenu
{
    private static List<MenuEntry> ParseDocument( OmarchyMenuDocument source )
    {
        JsonDocumentOptions options = new( )
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        try
        {
            using var document = JsonDocument.Parse( source.Jsonc, options );
            List<MenuEntry> entries = [];
            if ( document.RootElement.ValueKind != JsonValueKind.Object )
            {
                return entries;
            }

            var items = document.RootElement.TryGetProperty( "items", out var wrapped ) &&
                wrapped.ValueKind == JsonValueKind.Object
                ? wrapped
                : document.RootElement;
            foreach ( var property in items.EnumerateObject( ) )
            {
                if ( property.Value.ValueKind == JsonValueKind.Object )
                {
                    entries.Add( ReadEntry( property.Name, property.Value, source.Path ) );
                }
                else if ( property.Value.ValueKind == JsonValueKind.String &&
                         property.Name.Contains( '.', StringComparison.Ordinal ) )
                {
                    entries.Add( new MenuEntry
                    {
                        Id = property.Name,
                        Label = property.Name.Split( '.' ).Last( ),
                        Command = property.Value.GetString( ),
                        SourcePath = source.Path
                    } );
                }
            }

            ReadLegacy( document.RootElement, source.Path, entries );
            return entries;
        }
        catch ( JsonException )
        {
            return [];
        }
    }

    private static MenuEntry ReadEntry( string id, JsonElement value, string sourcePath ) => new( )
    {
        Id = id,
        Parent = GetString( value, "parent" ),
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
        List<MenuEntry> entries )
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
        List<MenuEntry> entries )
    {
        var id = GetString( value, "id" );
        var command = GetString( value, "action", "command", "exec", "run" );
        if ( !string.IsNullOrWhiteSpace( id ) && !string.IsNullOrWhiteSpace( command ) )
        {
            entries.Add( ReadEntry( id, value, sourcePath ) );
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

    private sealed class MenuEntry
    {
        public required string Id { get; init; }
        public string? Parent { get; init; }
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

        public bool IsAction => !string.IsNullOrWhiteSpace( Command );

        public bool IsLink => !IsAction && !string.IsNullOrWhiteSpace( Target );
    }
}
