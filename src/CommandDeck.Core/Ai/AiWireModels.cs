namespace CommandDeck;

public sealed class OllamaChatRequest
{
    public required string Model { get; init; }
    public bool Stream { get; init; }
    public List<AiChatMessage> Messages { get; init; } = [];
}

public sealed class OllamaChatResponse
{
    public string? Model { get; init; }
    public AiChatMessage? Message { get; init; }
}

public sealed class OllamaTagsResponse
{
    public List<OllamaModel> Models { get; init; } = [];
}

public sealed class OllamaModel
{
    public string? Name { get; init; }
    public string? Model { get; init; }
}

public sealed class OpenAiChatRequest
{
    public required string Model { get; init; }
    public List<AiChatMessage> Messages { get; init; } = [];
    public bool Stream { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName( "stream_options" )]
    public OpenAiStreamOptions? StreamOptions { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName( "tool_choice" )]
    public string ToolChoice { get; init; } = "none";
}

public sealed class OpenAiStreamOptions
{
    [System.Text.Json.Serialization.JsonPropertyName( "include_usage" )]
    public bool IncludeUsage { get; init; }
}

public sealed class OpenAiChatResponse
{
    public string? Model { get; init; }
    public List<OpenAiChoice> Choices { get; init; } = [];
}

public sealed class OpenAiModelsResponse
{
    public List<OpenAiModel> Data { get; init; } = [];
}

public sealed class OpenAiModel
{
    public string? Id { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName( "owned_by" )]
    public string? OwnedBy { get; init; }
}

public sealed class OpenAiChoice
{
    public AiChatMessage? Message { get; init; }
}

public sealed class AiChatMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}
