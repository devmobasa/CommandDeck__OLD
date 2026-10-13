using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public sealed record ExtensionRegistryDocument
{
    public int Version { get; init; } = 1;
    public long Revision { get; init; }
    public bool Enabled { get; init; }
    public ExtensionInstallation[] Installations { get; init; } = [];
}

public sealed record ExtensionInstallation
{
    public required string Key { get; init; }
    public required string Origin { get; init; }
    public required string Digest { get; init; }
    public required ExtensionManifest Manifest { get; init; }
    public bool Enabled { get; init; }
    public bool Removed { get; init; }
    public bool GlobalSearchEnabled { get; init; }
    public long GrantRevision { get; init; }
    public string[] Grants { get; init; } = [];
    public ExtensionVersion[] Versions { get; init; } = [];
    public Dictionary<string, JsonElement> Settings { get; init; } = new( StringComparer.Ordinal );
    /// <summary>Legacy registry data, read only during the one-time storage migration.</summary>
    public long StorageRevision { get; init; }
    public Dictionary<string, JsonElement> Storage { get; init; } = new( StringComparer.Ordinal );
}

public sealed record ExtensionVersion( string Version, string Digest );

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ExtensionRegistryDocument ) )]
[JsonSerializable( typeof( ExtensionInstallation ) )]
[JsonSerializable( typeof( Dictionary<string, JsonElement> ) )]
public sealed partial class ExtensionRegistryJsonContext : JsonSerializerContext;
