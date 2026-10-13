namespace CommandDeck;

public static class AgentBackendIds
{
    public const string Codex = "codex.local";
    public const string ClaudeCode = "claude.local";
    public const string Cursor = "cursor.local";
    public const string Grok = "grok.local";

    /// <summary>
    /// Every reserved backend identifier. Direct AI provider IDs may not collide
    /// with these, compared case-insensitively.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Codex, ClaudeCode, Cursor, Grok];

    public static bool IsReserved( string? id ) =>
        id is not null && All.Any( value => string.Equals( value, id, StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// Maps an explicit launcher prefix (<c>codex:</c>, <c>claude:</c>, <c>cursor:</c>,
    /// <c>grok:</c>, or the legacy <c>codex </c> form) to its backend ID.
    /// </summary>
    public static string? FromQueryPrefix( string? query )
    {
        var text = query?.TrimStart( ) ?? string.Empty;
        if ( text.StartsWith( "codex:", StringComparison.OrdinalIgnoreCase ) ||
            text.StartsWith( "codex ", StringComparison.OrdinalIgnoreCase ) )
        {
            return Codex;
        }
        if ( text.StartsWith( "claude:", StringComparison.OrdinalIgnoreCase ) )
        {
            return ClaudeCode;
        }
        if ( text.StartsWith( "cursor:", StringComparison.OrdinalIgnoreCase ) )
        {
            return Cursor;
        }
        if ( text.StartsWith( "grok:", StringComparison.OrdinalIgnoreCase ) )
        {
            return Grok;
        }
        return null;
    }

    public static string DisplayName( string? id ) => id switch
    {
        Codex => "Codex",
        ClaudeCode => "Claude Code",
        Cursor => "Cursor",
        Grok => "Grok",
        _ => id ?? "assistant"
    };
}

public static class AgentBackendKinds
{
    public const string CodexAppServer = "codex-app-server";
    public const string ClaudeCodeCli = "claude-code-cli";
    public const string CursorCli = "cursor-cli";
    public const string GrokCli = "grok-cli";
    public const string AnswerOnly = "answer-only";
}

public static class AgentTrustLevels
{
    public const int Answers = 0;
    public const int Launcher = 1;
    public const int ApprovedContext = 2;
    public const int ReadEntireHome = 3;
    public const int Packages = 4;
    public const int ConfirmedCommands = 5;
    public const int Unrestricted = 6;
    public const int Default = ApprovedContext;
    public const int Maximum = Unrestricted;

    public static bool IsValid( int level ) => level is >= Answers and <= Unrestricted;

    public static string Name( int level ) => level switch
    {
        Answers => "Answers",
        Launcher => "Launcher",
        ApprovedContext => "Approved context",
        ReadEntireHome => "Read entire home",
        Packages => "Packages",
        ConfirmedCommands => "Confirmed commands",
        Unrestricted => "Unrestricted",
        _ => "Unknown"
    };
}

public static class AgentTrustDurations
{
    public const string OneHour = "one-hour";
    public const string UntilDowngrade = "until-downgrade";
}

public static class AgentContextPolicies
{
    public const string AskEveryAccess = "ask-every-access";
    public const string ApprovedFolders = "approved-folders";
}

public static class AgentAuthenticationStates
{
    public const string NotApplicable = "not-applicable";
    public const string Unauthenticated = "unauthenticated";
    public const string Pending = "pending";
    public const string Authenticated = "authenticated";
    public const string Failed = "failed";
}

public static class AgentHealthStates
{
    public const string Disabled = "disabled";
    public const string Configured = "configured";
    public const string Starting = "starting";
    public const string Ready = "ready";
    public const string Unavailable = "unavailable";
    public const string Incompatible = "incompatible";
}

public static class AgentLoginMethods
{
    public const string DeviceCode = "device-code";
    public const string ApiKey = "api-key";
    // A browser sign-in the CLI drives itself: CommandDeck harvests the URL
    // (and, for Claude Code, waits for a pasted code) without owning the flow.
    public const string Browser = "browser";
}

public static class AgentConversationStates
{
    public const string Idle = "idle";
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class AgentTurnOutcomes
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class AgentEventKinds
{
    public const string Status = "status";
    public const string Text = "text";
    public const string Reasoning = "reasoning";
    public const string Tool = "tool";
    public const string Request = "request";
    public const string Usage = "usage";
    public const string Done = "done";
}

public static class AgentPendingRequestKinds
{
    public const string Question = "question";
    public const string Context = "context";
    public const string Action = "action";
    public const string Command = "command";
}

public static class AgentRequestDecisions
{
    public const string Allow = "allow";
    public const string AllowFolder = "allow-folder";
    public const string AllowAlways = "allow-always";
    public const string Deny = "deny";
    public const string CancelTurn = "cancel-turn";
}

public static class AgentContextOperations
{
    public const string ListDirectory = "list-directory";
    public const string SearchText = "search-text";
    public const string ReadFile = "read-file";
}

public static class AgentToolNames
{
    public const string ListDirectory = "commanddeck_list_directory";
    public const string SearchText = "commanddeck_search_text";
    public const string ReadFile = "commanddeck_read_file";
    public const string Search = "commanddeck_search";
    public const string Invoke = "commanddeck_invoke";
    public const string RunCommand = "commanddeck_run_command";
    public const string SearchPackages = "commanddeck_search_packages";
    public const string ProposePackageChange = "commanddeck_propose_package_change";
}

public static class AgentGrantKinds
{
    public const string Folder = "folder";
    public const string Command = "command";
}

public static class AgentGrantStates
{
    public const string Active = "active";
    public const string Stale = "stale";
    public const string Revoked = "revoked";
}

public static class AgentActionRisks
{
    public const string Immediate = "immediate";
    public const string Confirm = "confirm";
    public const string Denied = "denied";
}

public static class AgentActionDraftKinds
{
    public const string Invoke = "invoke";
    public const string Command = "command";
    public const string PackageChange = "package-change";
}

public static class AgentToolActivityStates
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Denied = "denied";
    public const string Failed = "failed";
}

public static class AgentTranscriptRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public static class PackageOperations
{
    public const string Install = "install";
    public const string Remove = "remove";
}

public static class PackageOperationStates
{
    public const string Queued = "queued";
    public const string Authorizing = "authorizing";
    public const string Running = "running";
    public const string Verifying = "verifying";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
