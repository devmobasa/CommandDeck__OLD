using System.Text.Json.Nodes;

namespace CommandDeck;

public interface IAgentLauncherHost
{
    ValueTask<IReadOnlyList<SearchResult>> SearchAsync( SearchRequest request, CancellationToken cancellationToken );
    SearchResult? ResolveCachedResult( string resultId, string invocationReference );
    ValueTask<ActionExecutionResult> InvokeAsync(
        string resultId,
        string actionId,
        string invocationReference,
        string query,
        bool confirmed,
        CancellationToken cancellationToken );
}

public static class AgentLauncherTools
{
    public static async ValueTask<string> SearchAsync(
        IAgentLauncherHost host,
        string query,
        int limit,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( host );
        var bounded = Math.Clamp( limit, 1, AgentLimits.MaximumSearchResults );
        var results = await host.SearchAsync(
            new SearchRequest { Query = query, Limit = bounded },
            cancellationToken ).ConfigureAwait( false );
        JsonArray array = [];
        foreach ( var result in results.Where( static result => !PrivateSources.IsPrivate( result ) ).Take( bounded ) )
        {
            JsonArray actions = [];
            foreach ( var action in result.Actions )
            {
                var risk = ClassifyRisk( result, action );
                if ( risk == AgentActionRisks.Denied )
                {
                    continue;
                }
                actions.Add( ( JsonNode ) new JsonObject
                {
                    ["actionId"] = action.Id,
                    ["title"] = action.Title,
                    ["kind"] = action.Kind,
                    ["requiresConfirmation"] = risk == AgentActionRisks.Confirm
                } );
            }
            array.Add( ( JsonNode ) new JsonObject
            {
                ["resultId"] = result.Id,
                ["invocationReference"] = result.InvocationReference,
                ["title"] = result.Title,
                ["subtitle"] = result.Subtitle,
                ["kind"] = result.Kind,
                ["actions"] = actions
            } );
        }
        return new JsonObject { ["results"] = array }.ToJsonString( );
    }

    public static string ClassifyRisk( SearchResult result, CommandAction action )
    {
        ArgumentNullException.ThrowIfNull( result );
        ArgumentNullException.ThrowIfNull( action );
        if ( PrivateSources.IsPrivate( result ) || action.Kind is ActionKinds.LaunchArgv or ActionKinds.ClipboardHistory or ActionKinds.AiPrompt or ActionKinds.AgentPrompt or ActionKinds.AgentOpen or ActionKinds.ApplySavedSearch or ActionKinds.ApplyQuery or ActionKinds.BrowseHerdrTabs or ActionKinds.BrowseOmarchyMenu )
        {
            return AgentActionRisks.Denied;
        }
        if ( action.IsDestructive || action.RequiresConfirmation )
        {
            return AgentActionRisks.Confirm;
        }
        return action.Kind switch
        {
            ActionKinds.LaunchDesktop => AgentActionRisks.Immediate,
            ActionKinds.CopyText => AgentActionRisks.Immediate,
            ActionKinds.HyprlandDispatch when result.Kind is ResultKinds.Window or ResultKinds.Workspace => AgentActionRisks.Immediate,
            ActionKinds.HerdrFocus when result.SourceId == SourceIds.Herdr && result.Kind is ResultKinds.Workspace or ResultKinds.Tab => AgentActionRisks.Immediate,
            _ => AgentActionRisks.Confirm
        };
    }

    // Commands that only read state, so they are safe to run without a per-command
    // card when AutoApproveReadOnlyCommands is on. Deny-by-default: anything not
    // listed here — writers, package managers, network tools, interpreters — still
    // confirms. None of these write, delete, install, or spawn another program.
    private static readonly HashSet<string> ReadOnlyCommands = new( StringComparer.Ordinal )
    {
        "ls", "cat", "head", "tail", "pwd", "whoami", "id", "date", "uname",
        "hostname", "uptime", "df", "du", "free", "which", "type",
        "file", "stat", "wc", "echo", "printf", "printenv", "basename", "dirname",
        "realpath", "readlink", "cut", "sort", "uniq", "nl", "grep"
    };

    private static readonly HashSet<string> TrustedCommandDirectories = new( StringComparer.Ordinal )
    {
        "/usr/bin",
        "/bin"
    };

    // A shell can run an arbitrary script, so a shell is never auto-approved even
    // when its script only reads state — the script cannot be classified safely.
    private static readonly HashSet<string> ShellExecutables = new( StringComparer.Ordinal )
    {
        "bash", "sh", "zsh", "dash", "fish", "ksh", "csh", "tcsh", "ash", "busybox"
    };

    // Immediate only for a direct invocation (never a shell) of a known read-only
    // tool whose canonical path is under /usr/bin or /bin. Basename matching is
    // not enough: a script named ls in a temp directory still confirms. Arguments
    // are allow-listed: unclassified flags, unique-prefix abbreviations such as
    // sort --out, uniq's output operand, and file -C all confirm. The caller
    // still gates this behind the option, so the default — confirm every
    // command — is unchanged.
    public static string ClassifyCommandRisk( AgentCommandPlan plan )
    {
        ArgumentNullException.ThrowIfNull( plan );
        if ( !TryGetTrustedCommandName( plan.Executable, out var name ) ||
            ShellExecutables.Contains( name ) ||
            !ReadOnlyCommands.Contains( name ) ||
            !ArgumentsAreReadOnly( name, CommandArguments( plan ) ) )
        {
            return AgentActionRisks.Confirm;
        }

        return AgentActionRisks.Immediate;
    }

    private static bool TryGetTrustedCommandName( string? executable, out string name )
    {
        name = string.Empty;
        if ( string.IsNullOrEmpty( executable ) || executable.Contains( '\0', StringComparison.Ordinal ) )
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath( executable );
        }
        catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException )
        {
            return false;
        }

        var directory = Path.GetDirectoryName( full );
        name = Path.GetFileName( full );
        return directory is not null &&
            TrustedCommandDirectories.Contains( directory ) &&
            name.Length > 0;
    }

    private static IReadOnlyList<string> CommandArguments( AgentCommandPlan plan )
    {
        var argv = plan.Argv;
        if ( argv.Count == 0 )
        {
            return argv;
        }

        var first = argv[0];
        if ( string.Equals( first, plan.Executable, StringComparison.Ordinal ) ||
            string.Equals( Path.GetFileName( first ), Path.GetFileName( plan.Executable ), StringComparison.Ordinal ) )
        {
            return argv.Skip( 1 ).ToArray( );
        }

        return argv;
    }

    private sealed record ReadOnlyArgumentPolicy(
        int MaximumPositionals,
        bool AllowAllOptions,
        string AllowedShorts,
        HashSet<string> AllowedLongs,
        string ShortsWithValues,
        HashSet<string> LongsWithValues );

    private static readonly HashSet<string> SortLongOptions = new( StringComparer.Ordinal )
    {
        "reverse", "unique", "numeric-sort", "general-numeric-sort", "human-numeric-sort",
        "month-sort", "random-sort", "version-sort", "ignore-case", "ignore-leading-blanks",
        "dictionary-order", "ignore-nonprinting", "stable", "zero-terminated", "merge",
        "check", "key", "field-separator", "ignore-blanks"
    };

    private static readonly HashSet<string> UniqLongOptions = new( StringComparer.Ordinal )
    {
        "count", "repeated", "unique", "ignore-case", "skip-fields", "skip-chars",
        "check-chars", "all-repeated", "zero-terminated", "group"
    };

    private static readonly HashSet<string> FileLongOptions = new( StringComparer.Ordinal )
    {
        "brief", "mime", "mime-type", "mime-encoding", "keep-going", "list", "magic-file",
        "exclude", "raw", "version", "help", "no-dereference", "dereference", "print0",
        "separator"
    };

    private static readonly HashSet<string> DateLongOptions = new( StringComparer.Ordinal )
    {
        "date", "file", "iso-8601", "rfc-email", "rfc-3339", "reference", "universal", "utc",
        "universal-time", "debug"
    };

    private static readonly HashSet<string> HostnameLongOptions = new( StringComparer.Ordinal )
    {
        "fqdn", "long", "short", "ip-address", "all-ip-addresses", "domain", "alias",
        "yp", "nis", "verbose"
    };

    private static readonly Dictionary<string, ReadOnlyArgumentPolicy> ArgumentPolicies =
        new( StringComparer.Ordinal )
        {
            ["sort"] = new( int.MaxValue, false, "bcdfgihHMkmnrsuVzRtcC", SortLongOptions,
                "kt", ["key", "field-separator"] ),
            ["uniq"] = new( 1, false, "cduifswzD", UniqLongOptions,
                "fsw", ["skip-fields", "skip-chars", "check-chars"] ),
            ["file"] = new( int.MaxValue, false, "beFiklnNprsv0hLm", FileLongOptions,
                "eFm", ["magic-file", "exclude", "separator"] ),
            ["date"] = new( 1, false, "udfIRRc", DateLongOptions,
                "dfr", ["date", "file", "reference", "rfc-3339"] ),
            ["hostname"] = new( 0, false, "fsidvyaA", HostnameLongOptions, "", [] )
        };

    private static readonly ReadOnlyArgumentPolicy DefaultArgumentPolicy =
        new( int.MaxValue, true, "", new HashSet<string>( StringComparer.Ordinal ), "", [] );

    private static bool ArgumentsAreReadOnly( string name, IReadOnlyList<string> arguments )
    {
        var policy = ArgumentPolicies.GetValueOrDefault( name, DefaultArgumentPolicy );
        var positionals = 0;
        var operandsOnly = false;
        for ( var index = 0; index < arguments.Count; index++ )
        {
            var argument = arguments[index];
            if ( !operandsOnly && argument == "--" )
            {
                operandsOnly = true;
                continue;
            }

            if ( operandsOnly || IsOperand( argument ) )
            {
                // date's legacy numeric operand sets the clock; its only
                // read-only positional form is an output format prefixed '+'.
                if ( name == "date" && !argument.StartsWith( '+' ) )
                {
                    return false;
                }
                positionals++;
                if ( positionals > policy.MaximumPositionals )
                {
                    return false;
                }

                continue;
            }

            if ( policy.AllowAllOptions )
            {
                continue;
            }

            if ( !OptionIsReadOnly( argument, policy, out var consumesNext ) )
            {
                return false;
            }

            // Required values are literal even when they equal "--". Consume
            // them before recognizing an option terminator on the next pass.
            if ( consumesNext && ++index >= arguments.Count )
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOperand( string argument ) =>
        argument.Length == 0 || argument == "-" || argument[0] != '-';

    private static string LongOptionName( string argument )
    {
        var body = argument[2..];
        var equals = body.IndexOf( '=' );
        return equals < 0 ? body : body[..equals];
    }

    private static bool OptionIsReadOnly(
        string argument,
        ReadOnlyArgumentPolicy policy,
        out bool consumesNext )
    {
        if ( argument.StartsWith( "--", StringComparison.Ordinal ) )
        {
            var name = LongOptionName( argument );
            consumesNext = policy.LongsWithValues.Contains( name ) && !argument.Contains( '=' );
            return policy.AllowedLongs.Contains( name );
        }

        return ShortOptionsAllowed( argument, policy, out consumesNext );
    }

    private static bool ShortOptionsAllowed(
        string argument,
        ReadOnlyArgumentPolicy policy,
        out bool consumesNext )
    {
        consumesNext = false;
        if ( argument.Length < 2 )
        {
            return false;
        }

        for ( var index = 1; index < argument.Length; index++ )
        {
            if ( !policy.AllowedShorts.Contains( argument[index] ) )
            {
                return false;
            }

            if ( policy.ShortsWithValues.Contains( argument[index] ) )
            {
                // Everything after a value-taking short option is its value,
                // not more flags. Otherwise the next argv token is required.
                consumesNext = index == argument.Length - 1;
                return true;
            }
        }

        return true;
    }

    public static AgentActionCard BuildCard( string cardId, SearchResult result, CommandAction action, string risk, string? rationale )
    {
        ArgumentNullException.ThrowIfNull( result );
        ArgumentNullException.ThrowIfNull( action );
        List<DetailField> details =
        [
            new DetailField("Result", result.Title),
            new DetailField("Kind", result.Kind),
            new DetailField("Action", action.Title),
            new DetailField("Action kind", action.Kind, Monospace: true)
        ];
        foreach ( (var key, var value) in action.Arguments.Take( 8 ) )
        {
            if ( key is "path" or "url" or "desktopId" or "desktopFile" or "exec" or "command" or "dispatch" )
            {
                details.Add( new DetailField( key, value.Length > 240 ? value[..240] : value, Monospace: true ) );
            }
        }
        return new AgentActionCard
        {
            Id = cardId,
            Kind = AgentActionDraftKinds.Invoke,
            Title = $"{action.Title}: {result.Title}",
            Risk = risk,
            ResultTitle = result.Title,
            ActionTitle = action.Title,
            ActionKind = action.Kind,
            Destructive = action.IsDestructive,
            CanRemember = false,
            Details = details,
            Rationale = rationale
        };
    }
}
