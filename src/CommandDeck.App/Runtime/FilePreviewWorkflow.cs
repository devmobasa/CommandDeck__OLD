namespace CommandDeck;

internal sealed class FilePreviewWorkflow(
    SearchResultCache cache,
    FilePreviewService previews,
    ILauncherSearchPreferences preferences,
    Func<ContributionRegistrySnapshot> contributionSnapshot )
{
    public async ValueTask<FilePreviewOutcome> PreviewAsync(
        string resultId,
        string? query,
        QueryExpression? expression,
        int matchIndex,
        string? owner,
        string? invocationReference,
        CancellationToken cancellationToken )
    {
        CachedSearchResult? cached = null;
        var found = owner is null
            ? cache.TryGet( resultId, out cached )
            : invocationReference is not null &&
              cache.TryGetInvocation( invocationReference, owner, resultId, out cached );
        if ( !found || cached is null )
        {
            return FilePreviewOutcome.Failure(
                "The result expired. Search again before previewing it." );
        }

        if ( !string.Equals( cached.Result.Kind, ResultKinds.File, StringComparison.Ordinal ) ||
            !cached.Result.Metadata.TryGetValue( "path", out var path ) )
        {
            return FilePreviewOutcome.Failure(
                "Preview is only available for indexed file results." );
        }

        var aliases = await preferences.GetFilterAliasesAsync( cancellationToken ).ConfigureAwait( false );
        var aliasMap = aliases.ToDictionary( alias => alias.Alias, alias => alias.FilterId, StringComparer.OrdinalIgnoreCase );
        var parsed = QueryParser.Parse( query, expression, aliasMap, contributionSnapshot( ) );
        var outcome = await previews.ReadAsync( path, cancellationToken, parsed.Expression, matchIndex )
            .ConfigureAwait( false );
        if ( outcome.Preview is { MatchLine: > 0 } preview )
        {
            var openId = $"preview:{resultId}:{preview.MatchLine}:{preview.MatchColumn}";
            SearchResult matchingLine = new( cached.Result )
            {
                Id = openId,
                Metadata = new Dictionary<string, string>( cached.Result.Metadata, StringComparer.Ordinal )
                {
                    [ResultMetadataKeys.CatalogResultId] = cached.Result.Metadata.GetValueOrDefault( ResultMetadataKeys.CatalogResultId, resultId )
                },
                Actions =
                [
                    new CommandAction
                    {
                        Id = "edit",
                        Title = "Open matching line",
                        Kind = ActionKinds.EditAtLine,
                        Arguments = new EditorActionPayload( path, preview.MatchLine, preview.MatchColumn ).ToArguments( )
                    }
                ]
            };

            // The derived row keeps the originating search's query and privacy
            // classification, so opening it records exactly what the row would.
            var openResult = cache.Store( [matchingLine], cached.Query, cached.PrivateQuery, owner ?? string.Empty )[0];
            preview.OpenResultId = openId;
            preview.OpenInvocationReference = openResult.InvocationReference;
        }

        return outcome;
    }
}
