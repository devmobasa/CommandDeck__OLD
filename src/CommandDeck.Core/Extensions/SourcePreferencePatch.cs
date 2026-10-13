namespace CommandDeck;

public sealed class SourcePreferencePatch
{
    public long RegistryRevision { get; init; }
    public long PreferenceRevision { get; init; }
    public List<SourcePreferenceChange> Changes { get; init; } = [];
}

public sealed class SourcePreferenceChange
{
    public required string SourceId { get; init; }
    public bool? EnabledInSearch { get; init; }
    public bool? EnabledOnHome { get; init; }
    public int? Position { get; init; }
}
