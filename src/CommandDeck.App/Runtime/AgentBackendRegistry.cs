namespace CommandDeck;

public interface IAgentBackendRegistry
{
    /// <summary>The backend for an ID, the default for null, or null for an unknown or answer-only ID.</summary>
    IAgentBackend? Resolve( string? backendId );

    IReadOnlyCollection<IAgentBackend> All { get; }
}

/// <summary>
/// The assistant backends the daemon owns, keyed by their reserved IDs. Every
/// backend implements the same contract and inherits no trust, grants, or
/// credentials from another (ADR 0014).
/// </summary>
public sealed class AgentBackendRegistry : IAgentBackendRegistry
{
    private readonly Dictionary<string, IAgentBackend> _backends = new( StringComparer.OrdinalIgnoreCase );
    private readonly string _defaultId;

    public AgentBackendRegistry( IEnumerable<IAgentBackend> backends, string defaultId )
    {
        ArgumentNullException.ThrowIfNull( backends );
        ArgumentException.ThrowIfNullOrWhiteSpace( defaultId );
        foreach ( var backend in backends )
        {
            _backends[backend.Id] = backend;
        }
        if ( !_backends.ContainsKey( defaultId ) )
        {
            throw new ArgumentException( "The default backend is not registered.", nameof( defaultId ) );
        }
        _defaultId = defaultId;
    }

    public IReadOnlyCollection<IAgentBackend> All => _backends.Values;

    public IAgentBackend? Resolve( string? backendId ) =>
        _backends.TryGetValue( backendId ?? _defaultId, out var backend ) ? backend : null;

    public bool Contains( string? backendId ) => backendId is not null && _backends.ContainsKey( backendId );
}
