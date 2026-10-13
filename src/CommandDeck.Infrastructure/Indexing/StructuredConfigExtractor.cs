namespace CommandDeck;

public sealed class ConfigEntry
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public string? Section { get; init; }
    public required int KeyLine { get; init; }
    public required int KeyColumn { get; init; }
    public required int ValueLine { get; init; }
    public required int ValueColumn { get; init; }
    public string? Assignment { get; init; }
}

public static class StructuredConfigExtractor
{
    private static readonly HashSet<string> StructuredExtensions =
        new( StringComparer.OrdinalIgnoreCase )
        {
            ".json", ".jsonc", ".toml", ".ini", ".conf", ".cfg",
            ".yaml", ".yml", ".lua", ".desktop", ".service", ".socket",
            ".timer", ".target", ".mount", ".path", ".env"
        };

    public static bool IsStructured( string path ) =>
        StructuredExtensions.Contains( Path.GetExtension( path ) ) ||
        IsHyprlandConfiguration( path );

    public static IReadOnlyList<ConfigEntry> Extract( string path, string text ) =>
        SelectParser( path ).Parse( text );

    private static IStructuredConfigParser SelectParser( string path )
    {
        if ( IsHyprlandConfiguration( path ) )
        {
            return HyprlandConfigParser.Instance;
        }

        return Path.GetExtension( path ).ToLowerInvariant( ) switch
        {
            ".json" or ".jsonc" => JsonConfigParser.Instance,
            ".toml" => TomlConfigParser.Instance,
            ".yaml" or ".yml" => YamlConfigParser.Instance,
            ".lua" => LuaConfigParser.Instance,
            ".ini" or ".conf" or ".cfg" or ".desktop" or ".service" or
                ".socket" or ".timer" or ".target" or ".mount" or ".path" or
                ".env" => IniConfigParser.Instance,
            _ => EmptyConfigParser.Instance
        };
    }

    private static bool IsHyprlandConfiguration( string path ) =>
        string.Equals(
            Path.GetFileName( path ),
            "hyprland.conf",
            StringComparison.OrdinalIgnoreCase );
}
