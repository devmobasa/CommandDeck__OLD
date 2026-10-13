namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    public ValueTask<AgentBackendInventory> GetAgentBackendsAsync( CancellationToken cancellationToken ) =>
        Assistant.GetAgentBackendsAsync( cancellationToken );

    public ValueTask<AgentStatus> GetAgentStatusAsync( CancellationToken cancellationToken ) =>
        Assistant.GetAgentStatusAsync( cancellationToken );

    public ValueTask<AgentModelInventory> DiscoverAgentModelsAsync(
        string? backendId,
        bool includeHidden,
        CancellationToken cancellationToken ) =>
        Assistant.DiscoverAgentModelsAsync( backendId, includeHidden, cancellationToken );

    public ValueTask<AgentPreferences> GetAgentPreferencesAsync( CancellationToken cancellationToken ) =>
        Assistant.GetAgentPreferencesAsync( cancellationToken );

    public ValueTask<AgentPreferences> SetAgentPreferencesAsync(
        AgentPreferences incoming,
        CancellationToken cancellationToken ) =>
        Assistant.SetAgentPreferencesAsync( incoming, cancellationToken );

    public ValueTask<AgentLoginState> StartAgentLoginAsync(
        string method,
        AiCredentialOptions? credential,
        CancellationToken cancellationToken,
        string? backendId = null ) =>
        Assistant.StartAgentLoginAsync( method, credential, cancellationToken, backendId );

    public ValueTask<AgentLoginState> GetAgentLoginStateAsync( string? backendId, CancellationToken cancellationToken ) =>
        Assistant.GetAgentLoginStateAsync( backendId, cancellationToken );

    public ValueTask<AgentLoginState> CancelAgentLoginAsync( CancellationToken cancellationToken, string? backendId = null ) =>
        Assistant.CancelAgentLoginAsync( cancellationToken, backendId );

    public ValueTask<AgentLoginState> CompleteAgentLoginAsync( string code, CancellationToken cancellationToken, string? backendId = null ) =>
        Assistant.CompleteAgentLoginAsync( code, cancellationToken, backendId );

    public ValueTask<AgentLoginState> AgentLogoutAsync( CancellationToken cancellationToken, string? backendId = null ) =>
        Assistant.AgentLogoutAsync( cancellationToken, backendId );

    public void PresentLoginResult( AgentLoginState login, string owner ) =>
        Assistant.PresentLoginResult( login, owner );

}
