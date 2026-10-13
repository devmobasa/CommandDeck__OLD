using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandDeck.ExtensionProtocol;

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.ExtensionManifestConverter ) )]
public sealed record ExtensionManifest
{
    public required int ManifestVersion { get; init; }
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string License { get; init; }
    public required ManifestProtocol Protocol { get; init; }
    public required ManifestExecution Execution { get; init; }
    public required Dictionary<string, ExtensionTarget> Targets { get; init; }
    public required ImmutableArray<ExtensionCommand> Commands { get; init; }
    public ImmutableArray<string> RequiredFeatures { get; init; } = [];
    public Dictionary<string, CapabilityRequest> Capabilities { get; init; } = new( StringComparer.Ordinal );
    public ImmutableArray<ExtensionPreference> Preferences { get; init; } = [];
    public ImmutableArray<ExtensionSearchProvider> SearchProviders { get; init; } = [];
}

public sealed record ManifestProtocol( int Major, int MinimumMinor );
public sealed record ManifestExecution( string Profile );
public sealed record ExtensionTarget( string Entrypoint, string Deployment, string RuntimeProfile );
public sealed record CapabilityRequest( string Reason );

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.ExtensionCommandConverter ) )]
public sealed record ExtensionCommand
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string Mode { get; init; } = ExtensionCommandModes.View;
    public string? Icon { get; init; }
    public ImmutableArray<string> Keywords { get; init; } = [];
    public ImmutableArray<string> Capabilities { get; init; } = [];
    public ImmutableArray<string> RequiredFeatures { get; init; } = [];
}

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.ExtensionPreferenceConverter ) )]
public sealed record ExtensionPreference
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Type { get; init; }
    public string Description { get; init; } = "";
    public bool Required { get; init; }
    public JsonElement? Default { get; init; }
    public int MaximumLength { get; init; } = 8192;
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public ImmutableArray<CommandDeck.Extensions.Views.Choice> Choices { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ExtensionManifest ) )]
public sealed partial class ExtensionManifestJsonContext : JsonSerializerContext;
