namespace CommandDeck;

public static class AgentToolCatalog
{
    public static IReadOnlyList<AgentToolDefinition> ForTrustLevel( int trustLevel, bool packagesAvailable )
    {
        List<AgentToolDefinition> tools = [];
        if ( trustLevel >= AgentTrustLevels.Launcher )
        {
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.Search,
                Description = "Search the CommandDeck launcher for applications, windows, files, configuration keys, commands, and quicklinks. Returns result IDs, invocation references and available action IDs. Use before commanddeck_invoke.",
                InputSchemaJson = """{"type":"object","properties":{"query":{"type":"string","description":"Launcher query text; supports prefixes like app:, win:, file:, cfg:"},"limit":{"type":"integer","minimum":1,"maximum":16}},"required":["query"]}"""
            } );
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.Invoke,
                Description = "Propose invoking one action of a search result by resultId, actionId and invocationReference from the same search response. Benign actions (launch, focus, copy) run at once; others require the user's confirmation card. Never assume success until the tool returns.",
                InputSchemaJson = """{"type":"object","properties":{"resultId":{"type":"string"},"actionId":{"type":"string"},"invocationReference":{"type":"string"},"rationale":{"type":"string"}},"required":["resultId","actionId","invocationReference"]}"""
            } );
        }
        if ( trustLevel >= AgentTrustLevels.ApprovedContext )
        {
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.ListDirectory,
                Description = "List a directory under the user's home. Paths may use ~. Access is brokered by CommandDeck and may require the user's approval.",
                InputSchemaJson = """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""
            } );
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.ReadFile,
                Description = "Read a UTF-8 text file under the user's home (bounded to 64 KiB). Access is brokered and may require approval.",
                InputSchemaJson = """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""
            } );
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.SearchText,
                Description = "Search text files under a directory of the user's home for a literal string (case-insensitive). Bounded results.",
                InputSchemaJson = """{"type":"object","properties":{"path":{"type":"string"},"query":{"type":"string"}},"required":["path","query"]}"""
            } );
        }
        if ( trustLevel >= AgentTrustLevels.Packages && packagesAvailable )
        {
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.SearchPackages,
                Description = "Search the local Arch package databases (official repositories only).",
                InputSchemaJson = """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}"""
            } );
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.ProposePackageChange,
                Description = "Propose installing or removing official Arch packages. CommandDeck builds an exact preview and the user confirms with Polkit; the tool returns the outcome.",
                InputSchemaJson = """{"type":"object","properties":{"operation":{"type":"string","enum":["install","remove"]},"packages":{"type":"array","items":{"type":"string"},"maxItems":16},"rationale":{"type":"string"}},"required":["operation","packages"]}"""
            } );
        }
        if ( trustLevel >= AgentTrustLevels.ConfirmedCommands )
        {
            tools.Add( new AgentToolDefinition
            {
                Name = AgentToolNames.RunCommand,
                Description = "Propose running one literal command (argv array, no shell) as the user. CommandDeck shows the exact command and waits for confirmation; bounded output is returned.",
                InputSchemaJson = """{"type":"object","properties":{"argv":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":64},"cwd":{"type":"string"},"rationale":{"type":"string"}},"required":["argv"]}"""
            } );
        }
        return tools;
    }

    public static string DeveloperInstructions( int trustLevel, IReadOnlyList<AgentToolDefinition> tools )
    {
        if ( tools.Count == 0 )
        {
            return "You are the CommandDeck assistant inside a Linux launcher on Omarchy (Arch Linux with Hyprland). " +
                "Answer concisely in Markdown. The user can only see this conversation window. " +
                "You have no tools in this conversation and no direct access to the filesystem, a shell, or the network; " +
                "answer from knowledge and say plainly when a tool you do not have would be needed. " +
                "Do not ask for passwords or credentials. Current trust level: " + AgentTrustLevels.Name( trustLevel ) + ".";
        }
        var hasContext = tools.Any( static tool =>
            tool.Name is AgentToolNames.ReadFile or AgentToolNames.ListDirectory or AgentToolNames.SearchText );
        var filesystem = hasContext
            ? "To read a file use commanddeck_read_file, to list a folder use commanddeck_list_directory, and to search files use commanddeck_search_text. "
            : "You cannot read files, list folders, or search file contents at this trust level; say so if asked. ";
        return "You are the CommandDeck assistant inside a Linux launcher on Omarchy (Arch Linux with Hyprland). " +
            "Answer concisely in Markdown. The user can only see this conversation window. " +
            "Available CommandDeck tools: " + string.Join( ", ", tools.Select( static tool => tool.Name ) ) + ". " +
            "These tools are your only way to reach the user's system: you have no direct or native access to the filesystem, " +
            "a shell, or the network, and any built-in file, shell, or web capability is unavailable here. " +
            "Never report a failure of your own built-in access; use the CommandDeck tool for the task instead. " +
            filesystem +
            "To act on apps, windows, settings, or quicklinks, call commanddeck_search first and then commanddeck_invoke. " +
            "Every tool call is mediated by CommandDeck; access is under the user's home and the user may deny it. " +
            "Never claim an action happened unless the tool result says so. " +
            "Do not ask for passwords or credentials. Current trust level: " + AgentTrustLevels.Name( trustLevel ) + ".";
    }
}
