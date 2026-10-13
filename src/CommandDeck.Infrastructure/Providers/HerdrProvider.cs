namespace CommandDeck;

/// <summary>
/// Opportunistic live search over Herdr spaces (workspaces) and tabs. Runs no
/// process unless the Herdr source is enabled, the query can include Herdr
/// rows, and the CLI is on PATH. Matching and ranking are local; Herdr only
/// supplies the inventory and, on activation, the focus switch.
/// </summary>
public sealed partial class HerdrProvider( HerdrClient client ) : ICommandProvider
{
    public const string Icon = "utilities-terminal";
    public const string SwitchSpaceActionId = "switch-space";
    public const string SwitchTabActionId = "switch-tab";
    public const string TargetKindMetadata = "herdrTargetKind";
    public const string TargetIdMetadata = "herdrTargetId";
    public const string WorkspaceIdMetadata = HerdrSearch.WorkspaceIdMetadata;
    public const string WorkspaceLabelMetadata = "herdrWorkspaceLabel";
    public const string FocusedMetadata = "herdrFocused";
    public const string AgentStatusMetadata = HerdrSearch.AgentStatusMetadata;
    public const string UnknownSpaceLabel = "Unknown space";
    public const int MaximumResults = 512;

    private const double ParentSpaceWeight = 0.84;
    private const double FocusedBonus = 0.03;

    public string Id => SearchCoordinator.HerdrProviderId;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );

        if ( request.EnabledSearchSources?.Contains( SourceIds.Herdr, StringComparer.Ordinal ) != true )
        {
            return [];
        }

        (var includeSpaces, var includeTabs) = IncludedKinds( query );
        if ( request.HerdrWorkspaceId is not null )
        {
            if ( !HerdrClient.IsValidIdentifier( request.HerdrWorkspaceId ) )
            {
                return [];
            }

            includeSpaces = false;
        }

        if ( !includeSpaces && !includeTabs )
        {
            return [];
        }

        // An empty query is only a browse through herdr: or the Home section; a
        // query that is empty because it only excludes something is not.
        var text = query.Text.Trim( );
        var explicitlyScoped = request.HerdrWorkspaceId is not null || query.ProviderFilter == SearchCoordinator.HerdrProviderId ||
            query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Herdr );
        var browse = text.Length == 0 && (explicitlyScoped || query.ExcludedFilters.Count == 0);
        if ( text.Length == 0 && !browse )
        {
            return [];
        }

        if ( !client.IsInstalled )
        {
            return [];
        }

        var inventory = await client.ListAsync( cancellationToken ).ConfigureAwait( false );
        cancellationToken.ThrowIfCancellationRequested( );
        if ( inventory is null )
        {
            return [];
        }

        var results = Project( inventory, query, includeSpaces, includeTabs, request.HerdrWorkspaceId );
        return results
            .OrderByDescending( static result => result.Score )
            .ThenBy( static result => result.Title, StringComparer.OrdinalIgnoreCase )
            .ThenBy( static result => result.Id, StringComparer.Ordinal )
            .Take( Math.Clamp( request.Limit, 1, MaximumResults ) )
            .ToArray( );
    }

    private static (bool Spaces, bool Tabs) IncludedKinds( ParsedQuery query )
    {
        var herdrIncluded = query.IncludedFilters.Where( static filter => filter.SourceId == SourceIds.Herdr ).ToArray( );
        if ( query.IncludedFilters.Count > 0 && herdrIncluded.Length == 0 )
        {
            return (false, false);
        }

        var spaces = herdrIncluded.Length == 0 ||
            herdrIncluded.Any( static filter => filter.Kind is null or ResultKinds.Workspace );
        var tabs = herdrIncluded.Length == 0 ||
            herdrIncluded.Any( static filter => filter.Kind is null or ResultKinds.Tab );

        foreach ( var filter in query.ExcludedFilters.Where( static filter =>
            filter.SourceId == SourceIds.Herdr && filter.Id != HerdrSearch.AttentionFilterId ) )
        {
            spaces &= filter.Kind is not null and not ResultKinds.Workspace;
            tabs &= filter.Kind is not null and not ResultKinds.Tab;
        }

        return (spaces, tabs);
    }

    private static List<SearchResult> Project(
        HerdrInventory inventory,
        ParsedQuery query,
        bool includeSpaces,
        bool includeTabs,
        string? workspaceId )
    {
        var browse = query.Text.Trim( ).Length == 0;
        var clauses = query.Expression.Clauses;
        if ( clauses.Count == 0 && !browse )
        {
            clauses = [new QueryClause { Text = query.Text }];
        }

        var labels = inventory.Workspaces
            .GroupBy( static workspace => workspace.Id, StringComparer.Ordinal )
            .ToDictionary( static group => group.Key, static group => group.First( ).Label, StringComparer.Ordinal );
        List<Candidate> candidates = [];

        if ( includeSpaces )
        {
            foreach ( var workspace in inventory.Workspaces )
            {
                var result = MatchClauses( SpaceResult( workspace, inventory.Tabs, 0 ), clauses, workspace.Focused );
                if ( result is not null )
                {
                    candidates.Add( new Candidate(
                        result,
                        workspace.Focused,
                        Kind: 0,
                        workspace.Number,
                        TabNumber: 0,
                        workspace.Label,
                        Orphan: false ) );
                }
            }
        }

        if ( includeTabs )
        {
            foreach ( var tab in inventory.Tabs )
            {
                if ( workspaceId is not null && tab.WorkspaceId != workspaceId )
                {
                    continue;
                }

                var parentLabel = labels.GetValueOrDefault( tab.WorkspaceId );
                var parent = inventory.Workspaces.FirstOrDefault( workspace => workspace.Id == tab.WorkspaceId );
                var result = MatchClauses( TabResult( tab, parentLabel, 0 ), clauses, tab.Focused );
                if ( result is not null && HerdrSearch.MatchesAttention( query, result ) )
                {
                    candidates.Add( new Candidate(
                        result,
                        tab.Focused,
                        Kind: 1,
                        parent?.Number ?? long.MaxValue,
                        tab.Number,
                        tab.Label,
                        Orphan: parent is null ) );
                }
            }
        }

        if ( !browse )
        {
            return candidates.Select( static candidate => candidate.Result ).ToList( );
        }

        // Browse order: focused first, then spaces before tabs, space number, tab
        // number, label, ID. One ordinal encodes it in the score so the order
        // survives the coordinator's re-sort.
        var ordered = candidates
            .OrderByDescending( static candidate => candidate.Focused )
            .ThenBy( static candidate => candidate.Orphan )
            .ThenBy( static candidate => candidate.Kind )
            .ThenBy( static candidate => candidate.SpaceNumber )
            .ThenBy( static candidate => candidate.TabNumber )
            .ThenBy( static candidate => candidate.Label, StringComparer.OrdinalIgnoreCase )
            .ThenBy( static candidate => candidate.Result.Id, StringComparer.Ordinal )
            .ToArray( );

        return ordered
            .Select( ( candidate, ordinal ) => new SearchResult( candidate.Result )
            {
                Score = BrowseBaseline - (ordinal * BrowseStep)
            } )
            .ToList( );
    }

    private static SearchResult? MatchClauses(
        SearchResult result,
        List<QueryClause> clauses,
        bool focused )
    {
        var total = 0.0;
        var positiveCount = 0;
        foreach ( var clause in clauses )
        {
            var score = ClauseScore( result, clause );
            if ( clause.Excluded )
            {
                if ( score > 0 )
                {
                    return null;
                }

                continue;
            }

            if ( score <= 0 )
            {
                return null;
            }

            total += score;
            positiveCount++;
        }

        return new SearchResult( result )
        {
            Score = (positiveCount == 0 ? BrowseBaseline : total / positiveCount) +
                (focused ? FocusedBonus : 0)
        };
    }

    private static double ClauseScore( SearchResult result, QueryClause clause )
    {
        if ( clause.Field is null && !clause.Excluded )
        {
            var title = LabelScore( clause.Text, result.Title );
            var parent = result.Kind == ResultKinds.Tab
                ? LabelScore( clause.Text, result.Metadata[WorkspaceLabelMetadata] ) * ParentSpaceWeight
                : 0;
            return Math.Max( title, parent );
        }

        // Field constraints and exclusions use the same literal values as the
        // coordinator, before the provider's candidate limit can discard rows.
        IEnumerable<string?> values = clause.Field switch
        {
            QueryFieldIds.Title => [result.Title],
            QueryFieldIds.Metadata => result.Metadata.Values.Prepend( result.Subtitle ),
            null => result.Metadata.Values.Prepend( result.Subtitle ).Prepend( result.Title ),
            _ => []
        };
        var score = values
            .Where( value => value?.Contains( clause.Text, StringComparison.OrdinalIgnoreCase ) == true )
            .Select( value => FuzzyMatcher.Score( clause.Text, value! ) )
            .DefaultIfEmpty( 0 )
            .Max( );
        return clause.Field == QueryFieldIds.Metadata ? score * ParentSpaceWeight : score;
    }

    private static double LabelScore( string text, string label )
    {
        // Phrase matching shares the matcher's Unicode folding but cannot
        // skip intervening words or match across labels.
        return text.Any( char.IsWhiteSpace )
            ? FuzzyMatcher.ScorePhrase( text, label )
            : FuzzyMatcher.Score( text, label );
    }

    private const double BrowseBaseline = 0.36;
    private const double BrowseStep = 0.0001;

    private sealed record Candidate(
        SearchResult Result,
        bool Focused,
        int Kind,
        long SpaceNumber,
        long TabNumber,
        string Label,
        bool Orphan );

    private static SearchResult SpaceResult( HerdrWorkspace workspace, IReadOnlyList<HerdrTab> tabs, double score )
    {
        var number = workspace.Number.ToString( System.Globalization.CultureInfo.InvariantCulture );
        var title = workspace.Label.Length > 0 ? workspace.Label : $"Space {number}";
        var subtitle = $"Space {number} · {Plural( workspace.TabCount, "tab" )} · {Plural( workspace.PaneCount, "pane" )}" +
            (workspace.Focused ? " · focused" : string.Empty);
        List<DetailField> details =
        [
            new("Number", number),
            new("Tabs", workspace.TabCount.ToString( System.Globalization.CultureInfo.InvariantCulture )),
            new("Panes", workspace.PaneCount.ToString( System.Globalization.CultureInfo.InvariantCulture ))
        ];
        if ( workspace.AgentStatus.Length > 0 )
        {
            details.Add( new DetailField( "Agent status", workspace.AgentStatus ) );
        }

        details.Add( new DetailField( "Herdr ID", workspace.Id, Monospace: true ) );
        AddTabPreview( details, workspace.Id, tabs );

        return new SearchResult
        {
            Id = $"herdr:workspace:{workspace.Id}",
            Kind = ResultKinds.Workspace,
            Title = title,
            Subtitle = subtitle,
            Icon = Icon,
            ProviderId = SearchCoordinator.HerdrProviderId,
            SourceId = SourceIds.Herdr,
            Score = score,
            Details = details,
            Actions =
            [
                FocusAction( SwitchSpaceActionId, "Switch Herdr Space", HerdrTargetKinds.Workspace, workspace.Id ),
                SpaceAction( "browse-tabs", "Browse tabs", ActionKinds.BrowseHerdrTabs, workspace.Id ),
                SpaceAction( "new-tab", "New tab in this space", ActionKinds.HerdrCreateTab, workspace.Id )
            ],
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                [TargetKindMetadata] = HerdrTargetKinds.Workspace,
                [TargetIdMetadata] = workspace.Id,
                [WorkspaceIdMetadata] = workspace.Id,
                [WorkspaceLabelMetadata] = workspace.Label,
                [FocusedMetadata] = workspace.Focused ? "true" : "false",
                [AgentStatusMetadata] = workspace.AgentStatus
            }
        };
    }

    private static SearchResult TabResult( HerdrTab tab, string? parentLabel, double score )
    {
        var number = tab.Number.ToString( System.Globalization.CultureInfo.InvariantCulture );
        var title = tab.Label.Length > 0 ? tab.Label : $"Tab {number}";
        var space = parentLabel is null
            ? UnknownSpaceLabel
            : parentLabel.Length > 0 ? parentLabel : "Unnamed space";
        var subtitle = $"In {space} · Tab {number} · {Plural( tab.PaneCount, "pane" )}" +
            (tab.Focused ? " · focused" : string.Empty);
        List<DetailField> details =
        [
            new("Space", space),
            new("Number", number),
            new("Panes", tab.PaneCount.ToString( System.Globalization.CultureInfo.InvariantCulture ))
        ];
        if ( tab.AgentStatus.Length > 0 )
        {
            details.Add( new DetailField( "Agent status", tab.AgentStatus ) );
        }

        details.Add( new DetailField( "Herdr ID", tab.Id, Monospace: true ) );

        return new SearchResult
        {
            Id = $"herdr:tab:{tab.Id}",
            Kind = ResultKinds.Tab,
            Title = title,
            Subtitle = subtitle,
            Icon = Icon,
            ProviderId = SearchCoordinator.HerdrProviderId,
            SourceId = SourceIds.Herdr,
            Score = score,
            Details = details,
            Actions =
            [
                FocusAction( SwitchTabActionId, "Switch Herdr Tab", HerdrTargetKinds.Tab, tab.Id ),
                new CommandAction
                {
                    Id = "rename-tab",
                    Title = "Rename tab",
                    Kind = ActionKinds.HerdrRenameTab,
                    Arguments = new( StringComparer.Ordinal ) { ["targetId"] = tab.Id },
                    Inputs = [new ActionInput { Name = "Name", DefaultValue = tab.Label }]
                }
            ],
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                [TargetKindMetadata] = HerdrTargetKinds.Tab,
                [TargetIdMetadata] = tab.Id,
                [WorkspaceIdMetadata] = tab.WorkspaceId,
                [WorkspaceLabelMetadata] = parentLabel ?? string.Empty,
                [FocusedMetadata] = tab.Focused ? "true" : "false",
                [AgentStatusMetadata] = tab.AgentStatus
            }
        };
    }

    private static CommandAction FocusAction( string id, string title, string targetKind, string targetId ) => new( )
    {
        Id = id,
        Title = title,
        Kind = ActionKinds.HerdrFocus,
        Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
        {
            ["targetKind"] = targetKind,
            ["targetId"] = targetId
        }
    };

    private static string Plural( long count, string noun ) =>
        $"{count.ToString( System.Globalization.CultureInfo.InvariantCulture )} {noun}{(count == 1 ? string.Empty : "s")}";
}
