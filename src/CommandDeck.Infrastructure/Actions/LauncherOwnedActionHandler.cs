namespace CommandDeck;

// Kinds the launcher or another protocol operation carries out. The daemon
// never runs them as an invocation, and says where they belong.
internal sealed class LauncherOwnedActionHandler : ILinuxActionHandler
{
    private static readonly Dictionary<string, string> Refusals = new( StringComparer.Ordinal )
    {
        [ActionKinds.BrowseFolder] = "This action is handled by the launcher file browser.",
        [ActionKinds.CollectFile] = "This action is handled by the launcher file browser.",
        [ActionKinds.BrowseHerdrTabs] = "This action is handled by the launcher Herdr browser.",
        [ActionKinds.BrowseOmarchyMenu] = "This action is handled by the launcher Omarchy menu browser.",
        [ActionKinds.ApplyQuery] = "This action is handled by the launcher search field.",
        [ActionKinds.ApplySavedSearch] = "This action is handled by the launcher search field.",
        [ActionKinds.OpenClipboardHistory] = "Open Clipboard History from the launcher.",
        [ActionKinds.ExtensionOpen] = "Extension commands must use the protocol 'extension.open' operation so their view can be rendered.",
        [ActionKinds.AiPrompt] = "AI actions must use the protocol 'ai' operation so the response can be rendered.",
        [ActionKinds.AgentPrompt] = "Assistant actions must use the protocol 'agent' operations so the conversation can be rendered.",
        [ActionKinds.AgentOpen] = "Assistant actions must use the protocol 'agent' operations so the conversation can be rendered.",
        [ActionKinds.ResetRanking] = "Ranking resets run in the daemon's invocation workflow."
    };

    public IReadOnlyList<string> Kinds { get; } = [.. Refusals.Keys];

    public ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken ) =>
        ValueTask.FromResult( Refusals.TryGetValue( action.Kind, out var refusal )
            ? ActionExecutionResult.Failure( refusal )
            : LinuxActionArguments.UnknownKind( action ) );
}
