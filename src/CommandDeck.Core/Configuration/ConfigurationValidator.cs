using System.Globalization;

namespace CommandDeck;

public static class ConfigurationValidator
{
    public static void Validate( CommandDeckOptions options )
    {
        if ( options.Privacy.QueryHistoryRetentionDays is < 1 or > 3650 ||
            options.Privacy.MaximumQueryHistoryEntries is < 1 or > 100_000 )
        {
            throw new InvalidOperationException(
                "Query history retention must be 1-3650 days with 1-100000 entries." );
        }
        ArgumentNullException.ThrowIfNull( options );
        ValidateIndexing( options.Indexing );
        ValidateRanking( options.Search?.Ranking );
        ValidateCalculator( options.Search?.Calculator );
        ValidateAi( options.Ai );
        ValidateAgent( options.Agent );
        if ( options.Quicklinks is null )
        {
            throw new InvalidOperationException( "quicklinks must be an array." );
        }
        QuicklinkValidator.ValidateUnique( options.Quicklinks );
        ValidateWebEngines( options.Search?.WebEngines );
        ValidateProjects( options.Projects );
        if ( options.ClipboardHistory is null || options.ClipboardHistory.MaximumEntries is < 10 or > 1000 ||
            options.ClipboardHistory.RetentionHours is < 1 or > 8760 )
        {
            throw new InvalidOperationException( "Clipboard history requires 10–1000 entries and 1–8760 retention hours." );
        }
        if ( options.ScriptDirectories is null || options.ScriptDirectories.Count > 16 ||
                    options.ScriptDirectories.Any( string.IsNullOrWhiteSpace ) )
        {
            throw new InvalidOperationException( "Configure at most 16 nonempty script directories." );
        }
        SnippetTemplate.ValidateOptions( options.Snippets );
    }

    private static void ValidateProjects( List<ProjectOptions>? projects )
    {
        if ( projects is null || projects.Count > 100 || projects.Any( project =>
            project is null || string.IsNullOrWhiteSpace( project.Title ) ||
            string.IsNullOrWhiteSpace( project.Path ) ||
            !(System.IO.Path.IsPathFullyQualified( project.Path ) || project.Path.StartsWith( "~/", StringComparison.Ordinal )) ||
            project.Path.Any( char.IsControl ) || string.IsNullOrWhiteSpace( project.Editor ) ||
            project.Editor.Any( char.IsControl ) || project.EditorArguments is null ||
            project.EditorArguments.Any( argument => argument is null || argument.Any( char.IsControl ) ) ) )
        {
            throw new InvalidOperationException( "Projects require a title, absolute or ~/ path, and valid editor arguments (maximum 100 projects)." );
        }
    }

    private static void ValidateWebEngines( List<WebEngineOptions>? engines )
    {
        if ( engines is null )
        {
            throw new InvalidOperationException( "search.webEngines must be an array." );
        }
        HashSet<string> ids = new( StringComparer.OrdinalIgnoreCase );
        HashSet<string> aliases = new( StringComparer.OrdinalIgnoreCase );
        foreach ( var engine in engines )
        {
            if ( engine is null )
            {
                throw new InvalidOperationException( "search.webEngines must contain valid objects." );
            }
            ValidateIdentifier( engine.Id, "Web engine ID" );
            if ( !ids.Add( engine.Id ) )
            {
                throw new InvalidOperationException(
                    $"Web engine ID '{engine.Id}' is configured more than once." );
            }
            if ( string.IsNullOrWhiteSpace( engine.Name ) || engine.Name.Length > 64 ||
                engine.Name.Any( char.IsControl ) )
            {
                throw new InvalidOperationException(
                    $"Web engine '{engine.Id}' name must be 1-64 characters without controls." );
            }
            if ( string.IsNullOrWhiteSpace( engine.UrlTemplate ) || engine.UrlTemplate.Length > 2048 ||
                engine.UrlTemplate.Any( char.IsControl ) ||
                !engine.UrlTemplate.Contains( "{query}", StringComparison.OrdinalIgnoreCase ) )
            {
                throw new InvalidOperationException(
                    $"Web engine '{engine.Id}' urlTemplate must be 1-2048 characters and contain the " +
                    "{query} placeholder." );
            }
            var alias = ValidateWebEngineAlias( engine.Id, engine.Alias );
            if ( !aliases.Add( alias ) )
            {
                throw new InvalidOperationException(
                    $"Web engine alias '{alias}' is configured more than once." );
            }
        }
    }

    private static string ValidateWebEngineAlias( string engineId, string alias )
    {
        var value = (alias ?? string.Empty).Trim( ).TrimEnd( ':' );
        if ( value.Length is < 1 or > 32 || !IsAsciiLetterOrDigit( value[0] ) ||
            value.Any( static character =>
                !IsAsciiLetterOrDigit( character ) && character is not '.' and not '_' and not '-' ) )
        {
            throw new InvalidOperationException(
                $"Web engine '{engineId}' alias must use 1-32 ASCII letters, digits, dots, " +
                "underscores, or hyphens, and must start with a letter or digit." );
        }
        if ( SearchFilterRegistry.IsReserved( value ) )
        {
            throw new InvalidOperationException(
                $"Web engine alias '{value}' conflicts with built-in launcher syntax." );
        }
        return value.ToLowerInvariant( );
    }

    public static void ValidateAi( AiOptions? ai )
    {
        if ( ai?.Providers is null || ai.Providers.Count > AiLimits.MaximumProviders ||
            ai.Providers.Any( static provider => provider is null ) )
        {
            throw new InvalidOperationException( "ai.providers must contain at most eight objects." );
        }

        HashSet<string> ids = new( StringComparer.OrdinalIgnoreCase );
        foreach ( var provider in ai.Providers )
        {
            ValidateAiProvider( provider );
            if ( !ids.Add( provider.Id ) )
            {
                throw new InvalidOperationException( $"AI provider ID '{provider.Id}' is duplicated." );
            }
        }
    }

    /// <summary>
    /// Validates one provider definition: identifier, reserved backend IDs,
    /// display name, adapter kind, endpoint policy, model, context limit, and the
    /// credential reference. Duplicate detection belongs to the enclosing set.
    /// </summary>
    public static void ValidateAiProvider( AiProviderOptions? provider )
    {
        if ( provider is null )
        {
            throw new InvalidOperationException( "AI provider definition is required." );
        }
        ValidateIdentifier( provider.Id, "AI provider ID" );
        if ( AgentBackendIds.IsReserved( provider.Id ) )
        {
            throw new InvalidOperationException(
                $"AI provider ID '{provider.Id}' is reserved for an assistant backend." );
        }
        var displayName = provider.DisplayName ?? provider.Id;
        if ( string.IsNullOrWhiteSpace( displayName ) ||
            System.Text.Encoding.UTF8.GetByteCount( displayName ) > 96 ||
            displayName.Any( char.IsControl ) )
        {
            throw new InvalidOperationException( "AI provider display name is invalid." );
        }
        if ( provider.Kind is not AiAdapterKinds.Ollama and
            not AiAdapterKinds.OpenAiChatCompletions )
        {
            throw new InvalidOperationException(
                $"AI provider '{provider.Id}' has an unsupported adapter kind." );
        }
        if ( provider.BaseUrl is null )
        {
            throw new InvalidOperationException( $"AI provider '{provider.Id}' baseUrl is invalid." );
        }
        ValidateAiEndpoint( provider );
        AiContractValidator.ValidateUtf8(
            provider.Model, AiLimits.MaximumModelIdBytes, "AI model", false,
            allowLineBreaks: false );
        if ( provider.ContextLimit is <= 0 or > 10_000_000 )
        {
            throw new InvalidOperationException(
                "AI provider contextLimit must be 1-10000000 when configured." );
        }
        ValidateAiCredential( provider );
    }

    private static void ValidateAiEndpoint( AiProviderOptions provider )
    {
        var configuredWithoutOptionalSlash = provider.BaseUrl.EndsWith( '/', StringComparison.Ordinal )
            ? provider.BaseUrl[..^1]
            : provider.BaseUrl;
        if ( provider.BaseUrl.Length > 2048 ||
            !Uri.TryCreate( provider.BaseUrl, UriKind.Absolute, out var uri ) ||
            uri.Scheme is not "http" and not "https" ||
            !string.IsNullOrEmpty( uri.UserInfo ) || !string.IsNullOrEmpty( uri.Query ) ||
            !string.IsNullOrEmpty( uri.Fragment ) || uri.Host.Contains( '*' ) ||
            (uri.AbsolutePath.Length > 1 && uri.AbsolutePath.Contains( "//", StringComparison.Ordinal )) ||
            !string.Equals(
                configuredWithoutOptionalSlash,
                RemoveOptionalTrailingSlash(
                    uri.GetComponents( UriComponents.AbsoluteUri, UriFormat.UriEscaped ) ),
                StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( $"AI provider '{provider.Id}' baseUrl is invalid." );
        }
        var loopback = AiEndpointPolicy.IsLiteralLoopback( uri );
        if ( !loopback && (uri.Scheme != Uri.UriSchemeHttps || !provider.AllowRemoteEndpoint) )
        {
            throw new InvalidOperationException(
                $"Remote AI provider '{provider.Id}' requires HTTPS and allowRemoteEndpoint=true." );
        }
        if ( provider.Kind == AiAdapterKinds.Ollama && !loopback && !provider.AllowRemoteEndpoint )
        {
            throw new InvalidOperationException( "Remote Ollama requires explicit permission." );
        }
    }

    private static string RemoveOptionalTrailingSlash( string value ) =>
        value.EndsWith( '/', StringComparison.Ordinal ) ? value[..^1] : value;

    private static void ValidateAiCredential( AiProviderOptions provider ) =>
        ValidateCredentialReference( provider.Credential, "AI credential" );

    private static void ValidateCredentialReference( AiCredentialOptions? credential, string label )
    {
        if ( credential is null || credential.Kind is not
            AiCredentialKinds.None and not AiCredentialKinds.Environment and not AiCredentialKinds.Systemd )
        {
            throw new InvalidOperationException( $"{label} kind is invalid." );
        }
        if ( credential.Kind == AiCredentialKinds.None )
        {
            if ( credential.Name is not null )
            {
                throw new InvalidOperationException( $"{label} name must be absent for kind none." );
            }
            return;
        }
        AiContractValidator.ValidateIdentifier( credential.Name, 64, label + " name" );
    }

    private static void ValidateCalculator( CalculatorOptions? calculator )
    {
        if ( calculator is null || string.IsNullOrWhiteSpace( calculator.UseQalc ) )
        {
            throw new InvalidOperationException( "search.calculator.useQalc is required." );
        }

        var mode = calculator.UseQalc.Trim( );
        if ( !string.Equals( mode, CalculatorOptions.UseQalcAuto, StringComparison.OrdinalIgnoreCase ) &&
            !string.Equals( mode, CalculatorOptions.UseQalcNever, StringComparison.OrdinalIgnoreCase ) &&
            !string.Equals( mode, CalculatorOptions.UseQalcAlways, StringComparison.OrdinalIgnoreCase ) )
        {
            throw new InvalidOperationException(
                "search.calculator.useQalc must be auto, never, or always." );
        }
    }

    private static void ValidateRanking( RankingOptions? ranking )
    {
        if ( ranking is null || ranking.SourceWeights is null )
        {
            throw new InvalidOperationException( "search.ranking and sourceWeights are required." );
        }
        if ( ranking.Adaptive is null || ranking.TimeAware is null || ranking.Context is null || ranking.TopMatches is null )
        {
            throw new InvalidOperationException(
                "Adaptive, Top Matches, time-aware and context ranking controls are required." );
        }

        (string Name, double Value, double Minimum, double Maximum)[] bounds =
        [
            ("search.ranking.titleWeight", ranking.TitleWeight, 0.01, 100),
            ("search.ranking.pathWeight", ranking.PathWeight, 0.01, 100),
            ("search.ranking.metadataWeight", ranking.MetadataWeight, 0.01, 100),
            ("search.ranking.contentWeight", ranking.ContentWeight, 0.01, 100),
            ("search.ranking.configWeight", ranking.ConfigWeight, 0.01, 100),
            ("search.ranking.maximumResultsPerSource", ranking.MaximumResultsPerSource, 1, 500),
            ("search.ranking.firstPageSize", ranking.FirstPageSize, 1, 200),
            ("search.ranking.minimumSourcesOnFirstPage", ranking.MinimumSourcesOnFirstPage, 1, ranking.FirstPageSize),
            ("search.ranking.adaptive.retentionDays", ranking.Adaptive.RetentionDays, 1, 3650),
            ("search.ranking.adaptive.maximumEvents", ranking.Adaptive.MaximumEvents, 1, 1_000_000),
            ("search.ranking.timeAware.applicationMultiplier", ranking.TimeAware.ApplicationMultiplier, 0, 5),
            ("search.ranking.timeAware.fileMultiplier", ranking.TimeAware.FileMultiplier, 0, 5),
            ("search.ranking.timeAware.workspaceMultiplier", ranking.TimeAware.WorkspaceMultiplier, 0, 5),
            ("search.ranking.timeAware.actionMultiplier", ranking.TimeAware.ActionMultiplier, 0, 5),
            ("search.ranking.context.activeApplicationBoost", ranking.Context.ActiveApplicationBoost, 0, 0.5),
            ("search.ranking.context.activeWorkspaceBoost", ranking.Context.ActiveWorkspaceBoost, 0, 0.5),
            ("search.ranking.context.currentDirectoryBoost", ranking.Context.CurrentDirectoryBoost, 0, 0.5),
            ("search.ranking.context.selectedProjectBoost", ranking.Context.SelectedProjectBoost, 0, 0.5)
        ];
        foreach ( (var name, var value, var minimum, var maximum) in bounds )
        {
            RequireRankingRange( name, value, minimum, maximum );
        }

        foreach ( (var sourceId, var weight) in ranking.SourceWeights )
        {
            if ( string.IsNullOrWhiteSpace( sourceId ) )
            {
                throw new InvalidOperationException( "search.ranking.sourceWeights keys must name a source." );
            }

            RequireRankingRange( $"search.ranking.sourceWeights.{sourceId}", weight, 0.1, 5 );
        }
    }

    private static void RequireRankingRange( string name, double value, double minimum, double maximum )
    {
        if ( !double.IsFinite( value ) || value < minimum || value > maximum )
        {
            throw new InvalidOperationException( string.Create(
                CultureInfo.InvariantCulture,
                $"{name} must be between {minimum} and {maximum}; it is {value}." ) );
        }
    }

    private static void ValidateIndexing( IndexingOptions indexing )
    {
        if ( indexing?.Roots is null )
        {
            throw new InvalidOperationException( "indexing.roots must be an array." );
        }
        foreach ( var root in indexing.Roots )
        {
            ValidateIndexRoot( root );
        }
        if ( indexing.MaximumExtractedBytes is < 1 or > 64 * 1024 * 1024 ||
            indexing.ExtractorTimeoutMilliseconds is < 50 or > 120_000 ||
            indexing.ExtractorMemoryLimitBytes is < 16 * 1024 * 1024 or > 4L * 1024 * 1024 * 1024 ||
            indexing.ExtractorStandardErrorBytes is < 1 or > 1024 * 1024 ||
            indexing.MaximumConcurrentExtractors is < 1 or > 16 ||
            indexing.MaintenanceIntervalMinutes is < 1 or > 7 * 24 * 60 )
        {
            throw new InvalidOperationException( "Index extractor limits are outside supported bounds." );
        }
    }

    private static void ValidateIndexRoot( IndexRootOptions root )
    {
        if ( root is null || string.IsNullOrWhiteSpace( root.Path ) )
        {
            throw new InvalidOperationException(
                "indexing.roots must contain objects with a non-empty path." );
        }
        if ( root.IncludeGlobs is null || root.ExcludeGlobs is null ||
            root.IncludeGlobs.Concat( root.ExcludeGlobs ).Any( static pattern =>
                string.IsNullOrWhiteSpace( pattern ) || pattern.Contains( '\0' ) ) )
        {
            throw new InvalidOperationException(
                $"Inclusion and exclusion globs for root '{root.Path}' must be arrays of non-empty patterns without NUL characters." );
        }
        if ( root.FileTypePolicies is null )
        {
            throw new InvalidOperationException(
                $"File type policies for root '{root.Path}' must be an array." );
        }
        foreach ( var policy in root.FileTypePolicies )
        {
            if ( policy is null || string.IsNullOrWhiteSpace( policy.Pattern ) ||
                Path.IsPathRooted( policy.Pattern ) ||
                policy.Pattern.Contains( Path.DirectorySeparatorChar ) ||
                policy.Pattern.Contains( Path.AltDirectorySeparatorChar ) )
            {
                throw new InvalidOperationException(
                    $"File type policies for root '{root.Path}' require a filename glob." );
            }
            ValidatePositiveLimit( policy.MaximumFileBytes, "maximumFileBytes", root.Path );
            ValidatePositiveLimit(
                policy.MaximumExtractedBytes,
                "maximumExtractedBytes",
                root.Path );
            ValidatePositiveLimit(
                policy.ExtractorTimeoutMilliseconds,
                "extractorTimeoutMilliseconds",
                root.Path );
            ValidatePositiveLimit(
                policy.ExtractorMemoryLimitBytes,
                "extractorMemoryLimitBytes",
                root.Path );
        }
    }

    private static void ValidatePositiveLimit( long? value, string name, string root )
    {
        if ( value is <= 0 )
        {
            throw new InvalidOperationException(
                $"File type policy {name} for root '{root}' must be positive." );
        }
    }

    private static void ValidateIdentifier( string value, string label )
    {
        if ( string.IsNullOrEmpty( value ) || value.Length > 64 || !IsAsciiLetterOrDigit( value[0] ) ||
            value.Any( static character =>
                !IsAsciiLetterOrDigit( character ) && character is not '.' and not '_' and not '-' ) )
        {
            throw new InvalidOperationException(
                $"{label} must use 1-64 ASCII letters, digits, dots, underscores, or hyphens, " +
                "and must start with a letter or digit." );
        }
    }

    private static bool IsAsciiLetterOrDigit( char value ) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static void ValidateAgent( AgentOptions agent )
    {
        ArgumentNullException.ThrowIfNull( agent );
        ValidateAbsoluteOrNullPath( agent.CodexBinaryPath, "agent.codexBinaryPath" );
        ValidateAbsoluteOrNullPath( agent.ClaudeBinaryPath, "agent.claudeBinaryPath" );
        ValidateAbsoluteOrNullPath( agent.CursorBinaryPath, "agent.cursorBinaryPath" );
        ValidateAbsoluteOrNullPath( agent.GrokBinaryPath, "agent.grokBinaryPath" );
        ValidateAbsoluteOrNullPath( agent.BubblewrapPath, "agent.bubblewrapPath" );
        ValidateAbsoluteOrNullPath( agent.SystemdRunPath, "agent.systemdRunPath" );
        ValidateCredentialReference( agent.ApiKeyCredential, "agent.apiKeyCredential" );
        if ( string.IsNullOrWhiteSpace( agent.RuntimeDirectory ) ||
            string.IsNullOrWhiteSpace( agent.StateDirectory ) )
        {
            throw new InvalidOperationException(
                "agent.runtimeDirectory and agent.stateDirectory must not be empty." );
        }
        if ( agent.MinimumCodexMajorVersion < 0 || agent.MinimumCodexMinorVersion < 0 ||
            agent.MinimumCodexPatchVersion < 0 || agent.MaximumCodexMajorVersion < 0 ||
            agent.MaximumCodexMinorVersion < 0 || agent.MaximumCodexPatchVersion < 0 )
        {
            throw new InvalidOperationException( "agent Codex version parts must not be negative." );
        }
        Version minimum = new(
            agent.MinimumCodexMajorVersion,
            agent.MinimumCodexMinorVersion,
            agent.MinimumCodexPatchVersion );
        Version maximum = new(
            agent.MaximumCodexMajorVersion,
            agent.MaximumCodexMinorVersion,
            agent.MaximumCodexPatchVersion );
        if ( maximum < minimum )
        {
            throw new InvalidOperationException( "agent maximum Codex version must not precede its minimum." );
        }
    }

    private static void ValidateAbsoluteOrNullPath( string? value, string label )
    {
        if ( value is null )
        {
            return;
        }
        if ( string.IsNullOrWhiteSpace( value ) || !value.StartsWith( '/' ) ||
            value.Contains( '\0', StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( $"{label} must be an absolute path." );
        }
    }
}
