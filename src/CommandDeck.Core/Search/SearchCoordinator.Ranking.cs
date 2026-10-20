
namespace CommandDeck;

public sealed partial class SearchCoordinator
{

    private SearchResult[] AssembleSections(
        IReadOnlyList<SearchResult> results,
        ParsedQuery query,
        LauncherPreferences preferences,
        int limit )
    {
        var positions = preferences.Sources.ToDictionary(
            static value => value.SourceId,
            static value => value.Position,
            StringComparer.Ordinal );
        List<SearchResult> exact = [];
        List<SearchResult> remaining = [];
        foreach ( var result in results )
        {
            if ( IsTopMatch( result, query ) )
            {
                exact.Add( WithSection(
                    result,
                    new ResultSection { Id = "top-match", Title = "Top Match", Order = 0 } ) );
            }
            else
            {
                remaining.Add( result );
            }
        }

        List<SearchResult> assembled = [.. exact.OrderByDescending( static value => value.Score )];
        var groups = remaining
            .GroupBy( static value => value.SourceId )
            .OrderBy( group => positions.GetValueOrDefault( group.Key, 100 ) )
            .ToArray( );
        var fairSourceCount = Math.Min(
            _options.Ranking.MinimumSourcesOnFirstPage,
            groups.Length );
        for ( var groupIndex = 0; groupIndex < groups.Length; groupIndex++ )
        {
            var group = groups[groupIndex];
            var position = positions.GetValueOrDefault( group.Key, 100 );
            ResultSection section = new( )
            {
                Id = group.Key,
                Title = SourceLabel( group.Key ),
                Order = position
            };
            var sourceLimit = _options.Ranking.MaximumResultsPerSource;
            sourceLimit = Math.Max(
                0,
                sourceLimit - assembled.Count( result => result.SourceId == group.Key ) );
            if ( groupIndex == 0 && fairSourceCount > 1 )
            {
                sourceLimit = Math.Min(
                    sourceLimit,
                    Math.Max( 1, _options.Ranking.FirstPageSize - (fairSourceCount - 1) ) );
            }
            assembled.AddRange( group
                .OrderByDescending( static value => value.Score )
                .ThenBy( static value => ResultTitleOrderKey( value ), StringComparer.OrdinalIgnoreCase )
                .Take( sourceLimit )
                .Select( value => WithSection( value, section ) ) );
        }

        return assembled.Take( limit ).ToArray( );
    }

    private static bool IsTopMatch( SearchResult result, ParsedQuery query )
    {
        if ( result.SourceId == SourceIds.Hey )
        {
            return false;
        }

        if ( IsExplicitIntent( result ) )
        {
            return true;
        }

        var normalized = query.Text.Trim( );
        if ( normalized.Length == 0 )
        {
            return false;
        }

        if ( string.Equals( result.Title.Trim( ), normalized, StringComparison.OrdinalIgnoreCase ) )
        {
            return true;
        }

        return result.Metadata.TryGetValue( "path", out var path ) &&
            string.Equals( Path.GetFileName( path ), normalized, StringComparison.OrdinalIgnoreCase );
    }

    private static string ResultTitleOrderKey( SearchResult result )
    {
        if ( result.Kind == ResultKinds.Workspace &&
            result.ProviderId == ProviderIds.Hyprland &&
            result.SourceId == SourceIds.WindowsWorkspaces &&
            result.Metadata.TryGetValue( "workspaceName", out var name ) &&
            int.TryParse( name, out var number ) && number > 0 )
        {
            return $"Workspace {number:D10}";
        }

        return result.Title;
    }

    private SearchResult ApplyFinalScore(
        SearchResult result,
        ParsedQuery query,
        SearchRequest request )
    {
        // HEY's own order (relevance for a search, recency for a box) is the
        // ranking: local field and context scores cannot see a mail body.
        if ( result.SourceId == SourceIds.Hey )
        {
            return result;
        }

        var prior = result.Kind switch
        {
            ResultKinds.Application => 0.09,
            ResultKinds.Omarchy => 0.08,
            ResultKinds.Calculator => 0.10,
            ResultKinds.Configuration => 0.02,
            ResultKinds.Window => 0.10,
            ResultKinds.Workspace => 0.05,
            ResultKinds.Tab => 0.05,
            ResultKinds.Quicklink => 0.04,
            ResultKinds.CustomCommand => 0.04,
            ResultKinds.AiCommand => query.IsAiMode ? 0.15 : -0.02,
            _ => 0
        };

        var lexical = BestFieldScore( query.Text, result );
        var sourceScore = result.Score <= 0 ? lexical : result.Score;
        var contextContributions = ContextContributions( result, request );
        var context = contextContributions.Sum( static value => value.Value );
        var baseScore = (sourceScore * 0.82) + lexical * 0.18 + prior + context;
        var sourceWeight = _options.Ranking.SourceWeights.GetValueOrDefault(
            result.SourceId,
            1 );
        var sourceAdjustment = baseScore * (sourceWeight - 1);
        var final = result.Metadata.ContainsKey( ResultMetadataKeys.ExactAlias )
            ? 1.5
            : Math.Clamp( baseScore + sourceAdjustment, 0, 1.5 );
        var contributions = ScaleProviderContributions(
            result,
            sourceScore );
        contributions.Add( new(
            RankingContributionKinds.Field,
            "Best matching displayed field",
            lexical * 0.18 ) );
        if ( prior != 0 )
        {
            contributions.Add( new( RankingContributionKinds.KindPrior, "Result type prior", prior ) );
        }
        contributions.AddRange( contextContributions );
        contributions.Add( new(
            RankingContributionKinds.SourceWeight,
            $"Source weight ×{sourceWeight:0.##}",
            sourceAdjustment ) );
        if ( result.Metadata.ContainsKey( ResultMetadataKeys.ExactAlias ) )
        {
            contributions =
            [
                new(RankingContributionKinds.ExactAlias, "Exact alias", 1.5)
            ];
        }
        else
        {
            var explained = contributions.Sum( static contribution => contribution.Value );
            var policyAdjustment = final - explained;
            if ( Math.Abs( policyAdjustment ) > 0.000_001 )
            {
                contributions.Add( new(
                    RankingContributionKinds.Policy,
                    "Score bounds",
                    policyAdjustment ) );
            }
        }
        var matches = result.Ranking.Matches.Count > 0
            ? [.. result.Ranking.Matches]
            : BuildFallbackMatches( query.Text, result );
        var titleRanges = matches.FirstOrDefault( static match =>
                              match.Field == RankingFieldNames.Title )?.Ranges ??
                          FuzzyMatcher.FindRanges( query.Text, result.Title );
        return Copy(
            result,
            score: final,
            ranges: titleRanges,
            ranking: new RankingExplanation
            {
                FinalScore = final,
                Contributions = contributions,
                Matches = matches,
                Policies = [.. result.Ranking.Policies]
            } );
    }

    private double BestFieldScore( string query, SearchResult result )
    {
        var ranking = _options.Ranking;
        var titleWeight = Math.Max( 0.01, ranking.TitleWeight );
        var score = FuzzyMatcher.Score( query, result.Title );
        score = Math.Max( score, FuzzyMatcher.Score( query, result.Subtitle ?? string.Empty ) *
            ranking.MetadataWeight / titleWeight );
        if ( result.Metadata.TryGetValue( "path", out var path ) )
        {
            score = Math.Max( score, FuzzyMatcher.Score( query, path ) *
                ranking.PathWeight / titleWeight );
        }
        foreach ( var match in result.Ranking.Matches )
        {
            var weight = match.Field switch
            {
                RankingFieldNames.Path => ranking.PathWeight,
                RankingFieldNames.Metadata => ranking.MetadataWeight,
                RankingFieldNames.Content => ranking.ContentWeight,
                RankingFieldNames.Config => ranking.ConfigWeight,
                _ => ranking.TitleWeight
            };
            score = Math.Max( score, FuzzyMatcher.Score( query, match.Value ) * weight / titleWeight );
        }
        return score;
    }

    private static List<RankingContribution> ScaleProviderContributions(
        SearchResult result,
        double sourceScore )
    {
        if ( result.Ranking.Contributions.Count == 0 )
        {
            return [new( RankingContributionKinds.Lexical, "Provider match", sourceScore * 0.82 )];
        }
        return result.Ranking.Contributions.Select( static contribution => contribution with
        {
            Value = contribution.Value * 0.82
        } ).ToList( );
    }

    private List<RankingContribution> ContextContributions(
        SearchResult result,
        SearchRequest request )
    {
        var options = _options.Ranking.Context;
        if ( !options.Enabled )
        {
            return [];
        }
        List<RankingContribution> contributions = [];
        var applicationMatch = !string.IsNullOrWhiteSpace( request.ActiveApplication ) &&
                               ApplicationContextKeys.Any( key =>
                                   result.Metadata.TryGetValue( key, out var value ) &&
                                   string.Equals( value, request.ActiveApplication, StringComparison.OrdinalIgnoreCase ) );
        var workspaceMatch = !string.IsNullOrWhiteSpace( request.ActiveWorkspace ) &&
                             result.Metadata.TryGetValue( "workspace", out var workspace ) &&
                             string.Equals( workspace, request.ActiveWorkspace, StringComparison.OrdinalIgnoreCase );
        if ( applicationMatch && options.ActiveApplicationBoost > 0 )
        {
            contributions.Add( new(
                RankingContributionKinds.Context,
                "Active application context",
                options.ActiveApplicationBoost ) );
        }
        if ( workspaceMatch && options.ActiveWorkspaceBoost > 0 )
        {
            contributions.Add( new(
                RankingContributionKinds.Context,
                "Active workspace context",
                options.ActiveWorkspaceBoost ) );
        }
        if ( PathMatchesContext( result, request.CurrentDirectory ) &&
            options.CurrentDirectoryBoost > 0 )
        {
            contributions.Add( new(
                RankingContributionKinds.Context,
                "Current directory context",
                options.CurrentDirectoryBoost ) );
        }
        if ( PathMatchesContext( result, request.SelectedProject ) &&
            options.SelectedProjectBoost > 0 )
        {
            contributions.Add( new(
                RankingContributionKinds.Context,
                "Selected project context",
                options.SelectedProjectBoost ) );
        }
        return contributions;
    }

    private static bool PathMatchesContext( SearchResult result, string? contextPath )
    {
        if ( string.IsNullOrWhiteSpace( contextPath ) ||
            !result.Metadata.TryGetValue( "path", out var resultPath ) ||
            string.IsNullOrWhiteSpace( resultPath ) )
        {
            return false;
        }
        var context = contextPath.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
        if ( context.Length == 0 || !Path.IsPathRooted( context ) || !Path.IsPathRooted( resultPath ) )
        {
            return false;
        }
        return string.Equals( resultPath, context, StringComparison.Ordinal ) ||
            resultPath.StartsWith( context + Path.DirectorySeparatorChar, StringComparison.Ordinal );
    }

    private async ValueTask<IReadOnlyDictionary<string, UsageSignal>> ReadLiveUsageAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken cancellationToken )
    {
        if ( _usageHistory is null || !_options.LearnFromInvocations || !_privacy.StoreUsageHistory || !_options.Ranking.Adaptive.Enabled ||
            !_options.Ranking.TimeAware.Enabled )
        {
            return new Dictionary<string, UsageSignal>( StringComparer.Ordinal );
        }
        var ids = results
            .Where( static result => result.Kind is ResultKinds.Workspace or ResultKinds.Window or ResultKinds.Tab )
            .Select( static result => result.Id )
            .Distinct( StringComparer.Ordinal )
            .ToArray( );
        return ids.Length == 0
            ? new Dictionary<string, UsageSignal>( StringComparer.Ordinal )
            : await _usageHistory.GetUsageSignalsAsync( ids, cancellationToken ).ConfigureAwait( false );
    }

    private SearchResult ApplyLiveTimeAwareScore(
        SearchResult result,
        IReadOnlyDictionary<string, UsageSignal> usage,
        DateTimeOffset? now = null,
        ParsedQuery? query = null )
    {
        // Herdr inventory browsing keeps the provider's focused/ordinal order.
        // Accepted usage affects its text matches only.
        if ( result.ProviderId == HerdrProviderId && query?.Text.Length == 0 )
        {
            return result;
        }

        if ( result.Kind is not ResultKinds.Window and not ResultKinds.Workspace and not ResultKinds.Tab ||
            result.Ranking.Contributions.Any( static contribution =>
                contribution.Kind == RankingContributionKinds.Frecency ) ||
            !usage.TryGetValue( result.Id, out var signal ) )
        {
            return result;
        }
        var boost = TimeAwareRankingPolicy.Score(
            result.Kind,
            signal.UseCount,
            signal.LastUsedUnixMilliseconds,
            now ?? _timeProvider.GetUtcNow( ),
            _options.Ranking.TimeAware );
        if ( boost <= 0 )
        {
            return result;
        }
        var multiplier = TimeAwareRankingPolicy.Multiplier(
            result.Kind,
            _options.Ranking.TimeAware );
        var score = Math.Clamp( result.Score + boost, 0, 1.2 );
        List<RankingContribution> contributions = [.. result.Ranking.Contributions];
        contributions.Add( new(
            RankingContributionKinds.Frecency,
            $"Recent {TimeAwareRankingPolicy.Category( result.Kind )} use ×{multiplier:0.##}",
            boost ) );
        contributions.Add( new(
            RankingContributionKinds.Adaptive,
            $"Adaptive history expires after {_options.Ranking.Adaptive.RetentionDays} days",
            0 ) );
        return Copy(
            result,
            score: score,
            ranking: new RankingExplanation
            {
                FinalScore = score,
                Contributions = contributions,
                Matches = [.. result.Ranking.Matches],
                Policies = [.. result.Ranking.Policies]
            } );
    }

    private static List<RankingFieldMatch> BuildFallbackMatches(
        string query,
        SearchResult result )
    {
        List<RankingFieldMatch> matches = [];
        AddFallbackMatch( matches, RankingFieldNames.Title, query, result.Title );
        AddFallbackMatch( matches, RankingFieldNames.Metadata, query, result.Subtitle );
        if ( result.Metadata.TryGetValue( "path", out var path ) )
        {
            AddFallbackMatch( matches, RankingFieldNames.Path, query, path );
        }
        return matches;
    }

    private static void AddFallbackMatch(
        List<RankingFieldMatch> matches,
        string field,
        string query,
        string? value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return;
        }
        var ranges = FuzzyMatcher.FindRanges( query, value );
        if ( ranges.Count > 0 )
        {
            matches.Add( new RankingFieldMatch { Field = field, Value = value, Ranges = ranges } );
        }
    }

    private static IEnumerable<SearchResult> CollapseConfigurationResults(
        IEnumerable<SearchResult> ranked )
    {
        var results = ranked.ToArray( );
        var totals = results
            .Where( HasConfigurationSource )
            .GroupBy( static result => result.Metadata["path"], StringComparer.Ordinal )
            .ToDictionary( static group => group.Key, static group => group.Count( ), StringComparer.Ordinal );
        Dictionary<string, int> retained = new( StringComparer.Ordinal );
        foreach ( var result in results )
        {
            if ( !HasConfigurationSource( result ) )
            {
                yield return result;
                continue;
            }

            var path = result.Metadata["path"];
            var retainedCount = retained.GetValueOrDefault( path );
            if ( retainedCount >= 2 )
            {
                continue;
            }

            retained[path] = retainedCount + 1;
            var omitted = totals[path] - 2;
            if ( retainedCount == 0 && omitted > 0 )
            {
                var note = omitted == 1 ? "1 more match" : $"{omitted} more matches";
                List<DetailField> details = [.. result.Details, new DetailField( "Related", $"{note} in this file" )];
                Dictionary<string, string> metadata = new( result.Metadata, StringComparer.Ordinal )
                {
                    ["collapsedCount"] = omitted.ToString( System.Globalization.CultureInfo.InvariantCulture )
                };
                yield return Copy(
                    result,
                    subtitle: string.IsNullOrWhiteSpace( result.Subtitle ) ? note : $"{note} · {result.Subtitle}",
                    details: details,
                    metadata: metadata );
            }
            else
            {
                yield return result;
            }
        }
    }

    private static bool HasConfigurationSource( SearchResult result ) =>
        result.Kind == ResultKinds.Configuration &&
        result.Metadata.TryGetValue( "path", out var path ) &&
        !string.IsNullOrWhiteSpace( path );

    private static SearchResult WithSection( SearchResult result, ResultSection section ) =>
        Copy( result, section: section );

    private static SearchResult Copy(
        SearchResult result,
        double? score = null,
        IReadOnlyList<MatchedRange>? ranges = null,
        string? subtitle = null,
        IReadOnlyList<DetailField>? details = null,
        Dictionary<string, string>? metadata = null,
        ResultSection? section = null,
        RankingExplanation? ranking = null ) => new( )
        {
            Id = result.Id,
            Kind = result.Kind,
            Title = result.Title,
            Subtitle = subtitle ?? result.Subtitle,
            Icon = result.Icon,
            DetailMarkdown = result.DetailMarkdown,
            Details = details is null ? result.Details : [.. details],
            ProviderId = result.ProviderId,
            SourceId = result.SourceId,
            Section = section ?? result.Section,
            IsFavorite = result.IsFavorite,
            Aliases = result.Aliases,
            CanCustomize = result.CanCustomize,
            Score = score ?? result.Score,
            MatchedRanges = ranges is null ? result.MatchedRanges : [.. ranges],
            Ranking = ranking ?? result.Ranking,
            Actions = result.Actions,
            Metadata = metadata ?? result.Metadata
        };
}
