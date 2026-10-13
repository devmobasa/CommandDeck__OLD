namespace CommandDeck;

/// <summary>Searches only accepted manifest metadata. It has no process, broker, or network dependency.</summary>
public sealed class ExtensionCommandProvider( IContributionRegistry registry, ILauncherSearchPreferences preferences ) : ICommandProvider
{
    public string Id => ProviderIds.Extensions;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        var snapshot = request.ContributionSnapshot ?? registry.Snapshot;
        List<SearchResult> results = [];
        if ( query.Terms.Count > 0 )
        {
            var alias = await preferences.FindAliasAsync( query.Terms[0], cancellationToken ).ConfigureAwait( false );
            if ( alias?.Kind == ResultKinds.ExtensionCommand && snapshot.Commands.Any( command => command.Available && command.StableId == alias.Id && (!command.Development || query.IncludesFilter( command.SourceId )) ) )
            {
                results.Add( alias );
            }
        }
        foreach ( var command in snapshot.Commands )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            if ( !command.Available || (command.Development && !query.IncludesFilter( command.SourceId )) || results.Any( result => result.Id == command.StableId ) || !Matches( command, query ) )
            {
                continue;
            }
            results.Add( new SearchResult
            {
                Id = command.StableId,
                Kind = ResultKinds.ExtensionCommand,
                Title = command.Title,
                Subtitle = command.Description,
                SourceId = command.SourceId,
                ProviderId = Id,
                Icon = "application-x-addon",
                CanCustomize = true,
                Actions = [new CommandAction { Id = "open", Title = "Open command", Kind = ActionKinds.ExtensionOpen }],
                Score = string.Equals( command.Title, query.Text, StringComparison.OrdinalIgnoreCase ) ? 1 : 0.65,
                Metadata = new( StringComparer.Ordinal ) { ["keywords"] = string.Join( ' ', command.Keywords ) }
            } );
        }
        // Reserve candidates fairly across packages before the coordinator applies its final source budgets.
        return results.GroupBy( result => result.SourceId ).SelectMany( group => group.OrderByDescending( result => result.Score )
                .ThenBy( result => result.Title, StringComparer.OrdinalIgnoreCase ).Select( ( result, index ) => (Result: result, Position: index) ) )
            .OrderBy( value => value.Position ).ThenByDescending( value => value.Result.Score ).ThenBy( value => value.Result.Id, StringComparer.Ordinal )
            .Take( request.Limit ).Select( value => value.Result ).ToArray( );
    }

    private static bool Matches( ExtensionCommandContribution command, ParsedQuery query )
    {
        var metadata = command.Description + " " + string.Join( ' ', command.Keywords );
        foreach ( var clause in query.Expression.Clauses )
        {
            var text = clause.Field switch
            {
                null => command.Title + " " + metadata,
                QueryFieldIds.Title => command.Title,
                QueryFieldIds.Metadata => metadata,
                _ => ""
            };
            var found = text.Contains( clause.Text, StringComparison.OrdinalIgnoreCase );
            if ( found == clause.Excluded )
            {
                return false;
            }
        }
        return true;
    }
}
