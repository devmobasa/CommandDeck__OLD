namespace CommandDeck;

public sealed partial class SearchCoordinator
{
    private const int MaximumTopMatches = 3;
    private const int MinimumPromotionUses = 2;
    private const int PromotionRecencyDays = 21;
    private enum PromotionTier
    {
        Explicit, Exact, Learned, Grouped
    }

    private bool LearningAvailable => _usageHistory is not null &&
        _options.LearnFromInvocations && _privacy.StoreUsageHistory &&
        _options.Ranking.Adaptive.Enabled && _options.Ranking.TimeAware.Enabled;

    private static string UsageId( SearchResult result ) =>
        result.Metadata.GetValueOrDefault( ResultMetadataKeys.CatalogResultId, result.Id );

    private static bool StrongTitleMatch( SearchResult result, ParsedQuery query )
    {
        var positive = query.Expression.Clauses.Where( static clause => !clause.Excluded ).ToArray( );
        if ( positive.Any( static clause => clause.Field is not null and not QueryFieldIds.Title ) )
        {
            return false;
        }        // Explicit provider/command modes retain their parser-owned text. Ordinary
        // clauses (including quoted phrases) are never flattened and reparsed.
        if ( positive.Length == 0 && (query.IsCommandMode || query.ProviderFilter is not null) )
        {
            positive = [new QueryClause { Text = query.Text }];
        }
        if ( positive.Sum( static clause => FuzzyMatcher.SearchTextLength( clause.Text ) ) < 2 )
        {
            return false;
        }
        var basename = result.Metadata.TryGetValue( "path", out var path ) ? Path.GetFileName( path ) : "";
        return positive.Length > 0 && positive.All( clause =>
            FuzzyMatcher.IsStrongPrefix( clause.Text, result.Title ) ||
            (clause.Field is null && FuzzyMatcher.IsStrongPrefix( clause.Text, basename )) );
    }

    private bool Learnable( SearchResult result, ParsedQuery query ) =>
        LearnableAction( result, query ) && StrongTitleMatch( result, query );

    private bool LearnableAction( SearchResult result, ParsedQuery query ) =>
        LearningAvailable && !query.IsAiMode && !query.IsCalculator &&
        result.ProviderId != ProviderIds.Fallback && result.SourceId != SourceIds.Folders &&
        TimeAwareRankingPolicy.Multiplier( result.Kind, _options.Ranking.TimeAware ) > 0 &&
        result.Actions.Count > 0 &&
        IsLearningAction( result.Actions[0] );

    // Learned queries need opt-in query history on top of every learning gate,
    // and apply only to the user's own interactive searches.
    private async ValueTask<IReadOnlyDictionary<string, LearnedQuery>> ReadLearnedQueriesAsync(
        SearchRequest request, SearchPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken )
    {
        if ( purpose != SearchPurpose.Interactive || !LearningAvailable || !_privacy.StoreQueryHistory ||
            _usageHistory is not ILearnedQueryStore store )
        {
            return new Dictionary<string, LearnedQuery>( StringComparer.Ordinal );
        }

        try
        {
            var since = now.ToUnixTimeMilliseconds( ) - PromotionRecencyDays * 86_400_000L;
            return await store.GetLearnedQueriesAsync( request.Query ?? string.Empty, since, cancellationToken )
                .ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch ( Exception )
        {
            return new Dictionary<string, LearnedQuery>( StringComparer.Ordinal );
        } // Unavailable history learns nothing, like missing usage.
    }

    private bool LearnedFromQuery( SearchResult result, ParsedQuery query,
        IReadOnlyDictionary<string, LearnedQuery> learnedQueries ) =>
        learnedQueries.ContainsKey( UsageId( result ) ) && LearnableAction( result, query );

    private static bool IsLearningAction( CommandAction action ) => action.Kind is
        ActionKinds.LaunchDesktop or ActionKinds.OpenPath or ActionKinds.OpenUrl or
        ActionKinds.RevealPath or ActionKinds.EditAtLine or ActionKinds.RunArgv or
        ActionKinds.RunTrustedShell or ActionKinds.RunScript or ActionKinds.HyprlandDispatch or
        ActionKinds.HerdrFocus;

    private async ValueTask<IReadOnlyDictionary<string, UsageSignal>> ReadSearchUsageAsync(
        IReadOnlyList<SearchResult> results, ParsedQuery query, bool browsing,
        IReadOnlyDictionary<string, LearnedQuery> learnedQueries, CancellationToken cancellationToken )
    {
        Dictionary<string, UsageSignal> usage = new( StringComparer.Ordinal );
        if ( !LearningAvailable )
        {
            return usage;
        }
        var ids = results.Where( result =>
                        result.Kind is ResultKinds.Window or ResultKinds.Workspace or ResultKinds.Tab ||
                        (_options.Ranking.TopMatches.Enabled && !browsing &&
                         (Learnable( result, query ) || LearnedFromQuery( result, query, learnedQueries ))) )
                    .Select( UsageId ).Distinct( StringComparer.Ordinal ).Order( StringComparer.Ordinal ).ToArray( );
        try
        {
            foreach ( var batch in ids.Chunk( 500 ) )
            {
                foreach ( var pair in await _usageHistory!.GetUsageSignalsAsync( batch, cancellationToken ).ConfigureAwait( false ) )
                {
                    usage[pair.Key] = pair.Value;
                }
            }
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch ( Exception )
        {
            usage.Clear( );
        } // Unavailable history is exact-only, never partial evidence.
        return usage;
    }

    private sealed record PromotionCandidate( SearchResult Result, PromotionTier Tier, string Reason, double Tie, double Age );

    private PromotionCandidate ClassifyPromotion( SearchResult result, ParsedQuery query,
        IReadOnlyDictionary<string, UsageSignal> usage, IReadOnlyDictionary<string, LearnedQuery> learnedQueries,
        DateTimeOffset now )
    {
        var tier = PromotionTier.Grouped;
        var reason = "No strong title match or learning disabled";
        var signal = usage.GetValueOrDefault( UsageId( result ) );
        var age = signal.LastUsedUnixMilliseconds > 0
            ? Math.Max( 0, (now.ToUnixTimeMilliseconds( ) - signal.LastUsedUnixMilliseconds) / 86_400_000d )
            : double.PositiveInfinity;
        if ( result.Metadata.ContainsKey( ResultMetadataKeys.ExactAlias ) || result.Kind == ResultKinds.Calculator )
        {
            tier = PromotionTier.Explicit;
            reason = "Explicit intent";
        }
        else if ( IsTopMatch( result, query ) )
        {
            tier = PromotionTier.Exact;
            reason = "Exact title or basename";
        }
        // The learned query itself is private history, so the reason never quotes it.
        else if ( LearnedFromQuery( result, query, learnedQueries ) &&
            signal.UseCount >= (learnedQueries[UsageId( result )].Kind == LearnedQueryKind.Exact ? 1 : MinimumPromotionUses) &&
            age <= PromotionRecencyDays )
        {
            tier = PromotionTier.Learned;
            reason = learnedQueries[UsageId( result )].Kind == LearnedQueryKind.Exact
                ? "Accepted after this same search"
                : "Accepted after a longer search that starts with this one";
        }
        else if ( Learnable( result, query ) )
        {
            reason = "Requires two accepted uses and last use within 21 days";
            if ( signal.UseCount >= MinimumPromotionUses && age <= PromotionRecencyDays )
            {
                tier = PromotionTier.Learned;
                reason = "Repeated recent use and strong title match";
            }
        }
        var tie = LearningAvailable ? TimeAwareRankingPolicy.Score( result.Kind, signal.UseCount,
            signal.LastUsedUnixMilliseconds, now, _options.Ranking.TimeAware ) : 0;
        return new( result, tier, reason, tie, age );
    }

    private SearchResult[] AssembleTopMatches(
        IReadOnlyList<SearchResult> results, ParsedQuery query, LauncherPreferences preferences,
        int limit, IReadOnlyDictionary<string, UsageSignal> usage, IReadOnlyDictionary<string, LearnedQuery> learnedQueries,
        DateTimeOffset now )
    {
        var positions = preferences.Sources.ToDictionary( static source => source.SourceId, static source => source.Position );
        var sourceCap = _options.Ranking.MaximumResultsPerSource;
        var pageSize = Math.Min( limit, _options.Ranking.FirstPageSize );
        var target = Math.Min( pageSize, Math.Min( _options.Ranking.MinimumSourcesOnFirstPage,
            results.Select( static result => result.SourceId ).Distinct( ).Count( ) ) );
        var entries = results.Select( result => ClassifyPromotion( result, query, usage, learnedQueries, now ) ).ToArray( );
        var ordered = entries.OrderBy( static entry => entry.Tier )
            .ThenByDescending( static entry => entry.Result.Score ).ThenByDescending( static entry => entry.Tie )
            .ThenBy( static entry => ResultTitleOrderKey( entry.Result ), StringComparer.OrdinalIgnoreCase )
            .ThenBy( static entry => entry.Result.Id, StringComparer.Ordinal ).ToArray( );
        HashSet<string> promoted = new( StringComparer.Ordinal );
        Dictionary<string, int> counts = new( StringComparer.Ordinal );
        foreach ( var entry in ordered.Where( static entry => entry.Tier != PromotionTier.Grouped ) )
        {
            var source = entry.Result.SourceId;
            if ( promoted.Count >= Math.Min( MaximumTopMatches, limit ) || counts.GetValueOrDefault( source ) >= sourceCap )
            {
                continue;
            }
            var distinct = counts.Count + (counts.ContainsKey( source ) ? 0 : 1);
            if ( entry.Tier == PromotionTier.Learned && promoted.Count + 1 + Math.Max( 0, target - distinct ) > pageSize )
            {
                continue;
            }
            promoted.Add( entry.Result.Id );
            counts[source] = counts.GetValueOrDefault( source ) + 1;
        }
        var entriesById = entries.ToDictionary( static entry => entry.Result.Id, StringComparer.Ordinal );
        SearchResult Explain( SearchResult result )
        {
            var entry = entriesById[result.Id];
            var selected = promoted.Contains( result.Id );
            return Copy( result, ranking: new RankingExplanation
            {
                FinalScore = result.Ranking.FinalScore,
                Contributions = result.Ranking.Contributions,
                Matches = result.Ranking.Matches,
                Policies = result.Ranking.Policies,
                Placement = new RankingPlacement
                {
                    Tier = entry.Tier switch { PromotionTier.Explicit => "explicit", PromotionTier.Exact => "exact", PromotionTier.Learned => "learned", _ => "grouped" },
                    Promoted = selected,
                    Reason = entry.Reason + (entry.Tier != PromotionTier.Grouped && !selected ? "; section/source cap or first-page diversity" : ""),
                    UsageId = entry.Tier == PromotionTier.Learned ? UsageId( result ) : null,
                    LastUseAgeDays = entry.Tier == PromotionTier.Learned ? entry.Age : null
                }
            } );
        }
        List<SearchResult> assembled = ordered.Where( entry => promoted.Contains( entry.Result.Id ) )
            .Select( entry => WithSection( Explain( entry.Result ), new ResultSection { Id = "top-match", Title = "Top Matches", Order = 0 } ) ).ToList( );
        var seen = counts.Keys.ToHashSet( StringComparer.Ordinal );
        var groups = results.Where( result => !promoted.Contains( result.Id ) )
            .GroupBy( static result => result.SourceId ).OrderBy( group => positions.GetValueOrDefault( group.Key, 100 ) )
            .ThenBy( static group => group.Key, StringComparer.Ordinal ).ToArray( );
        for ( var index = 0; index < groups.Length; index++ )
        {
            var group = groups[index];
            var allowance = Math.Min( sourceCap - counts.GetValueOrDefault( group.Key ), limit - assembled.Count );
            if ( allowance <= 0 )
            {
                continue;
            }
            var prospective = seen.Count + (seen.Contains( group.Key ) ? 0 : 1);
            var availableLater = groups.Skip( index + 1 ).Count( next => !seen.Contains( next.Key ) && next.Key != group.Key && counts.GetValueOrDefault( next.Key ) < sourceCap );
            var reserve = Math.Min( Math.Max( 0, target - prospective ), availableLater );
            // Allocate before emitting: no later duplicate group can push a
            // reserved source off the first page.
            if ( reserve > 0 && assembled.Count < pageSize )
            {
                allowance = Math.Min( allowance, Math.Max( 0, pageSize - assembled.Count - reserve ) );
            }
            if ( allowance <= 0 )
            {
                continue;
            }
            seen.Add( group.Key );
            var section = new ResultSection { Id = group.Key, Title = SourceLabel( group.Key ), Order = positions.GetValueOrDefault( group.Key, 100 ) };
            assembled.AddRange( group.OrderByDescending( static result => result.Score )
                .ThenByDescending( result => entriesById[result.Id].Tie )
                .ThenBy( static result => ResultTitleOrderKey( result ), StringComparer.OrdinalIgnoreCase )
                .ThenBy( static result => result.Id, StringComparer.Ordinal ).Take( allowance )
                .Select( result => WithSection( Explain( result ), section ) ) );
        }
        return assembled.ToArray( );
    }
}
