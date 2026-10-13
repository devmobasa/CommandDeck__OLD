namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    private AssistantRuntime? _assistant;

    private AssistantRuntime Assistant
    {
        get
        {
            var assistant = _assistant;
            if ( assistant is null )
            {
                throw new InvalidOperationException( "The assistant runtime is not attached." );
            }

            return assistant;
        }
    }

    private void AttachAgent( SqliteCatalog catalog, SearchCoordinator search )
    {
        var assistant = new AssistantRuntime( _options.Agent, _constructionOptions,
            _aiSettings, _aiOutputs, this, CacheSyntheticResult );
        _assistant = assistant;
        assistant.Initialize( new AgentAuthority( catalog.AgentPreferences, _constructionOptions.AgentClock ) );

        search.HomeSupplement = _ => ValueTask.FromResult<IReadOnlyList<SearchResult>>( assistant.BuildAgentHomeRows( ) );
    }

    private AgentRowState? AgentRowState( ) => _assistant?.AgentRowState( );
    private string? SelectedAnswerBackendId( ) => _assistant?.SelectedAnswerBackendId( );
    private string? TryGetAgentOutput( string owner, long generation ) => _assistant?.TryGetAgentOutput( owner, generation );
    private void ClearAgentClient( string clientOwnerId ) => _assistant?.ClearAgentClient( clientOwnerId );
    private ValueTask DisposeAgentAsync( ) => _assistant?.DisposeAsync( ) ?? ValueTask.CompletedTask;
}
