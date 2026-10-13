namespace CommandDeck;

public static partial class QueryParser
{
    // Prompts and invocation argv are payloads, not search clauses. Decide at
    // their leading boundary before interpreting '-' or field/filter prefixes.
    private static bool TryParseLiteralPayload(
        string original,
        QueryExpression seed,
        IReadOnlyDictionary<string, string>? aliases,
        ContributionRegistrySnapshot? registry,
        out LiteralPayload payload )
    {
        payload = null!;
        var text = original;
        List<string> includes = [.. seed.IncludeFilterIds];
        if ( !TryReadPayloadPrefix( ref text, includes, aliases, registry ) )
        {
            return false;
        }

        var ai = includes.Count == 1 && string.Equals( includes[0], "ai", StringComparison.OrdinalIgnoreCase );
        if ( !ai && (includes.Count > 1 || (includes.Count == 1 &&
            !string.Equals( includes[0], "applications", StringComparison.OrdinalIgnoreCase ))) )
        {
            return false;
        }

        string[] terms;
        if ( ai )
        {
            terms = text.Length == 0 ? [] : [text];
        }
        else
        {
            terms = TokenizeSyntax( text ).Select( Unquote ).ToArray( );
            if ( terms.Length < 2 || IsSearchSyntax( terms[1], aliases, registry ) ||
                (includes.Count == 0 && terms[1].StartsWith( '-', StringComparison.Ordinal ) &&
                 !terms[1].StartsWith( "--", StringComparison.Ordinal )) )
            {
                return false;
            }

            ParsedQuery invocationQuery = new( ) { Original = original, Text = text, Terms = terms };
            if ( !ApplicationInvocation.TryParse( invocationQuery, out _ ) )
            {
                return false;
            }
        }

        var expression = Validate( new QueryExpression
        {
            IncludeFilterIds = includes,
            ExcludeFilterIds = [.. seed.ExcludeFilterIds]
        } );
        payload = new LiteralPayload( text, terms, expression, ai );
        return true;
    }

    private static bool TryReadPayloadPrefix(
        ref string text,
        List<string> includes,
        IReadOnlyDictionary<string, string>? aliases,
        ContributionRegistrySnapshot? registry )
    {
        var colon = text.IndexOf( ':' );
        if ( colon <= 0 || text[..colon].Any( char.IsWhiteSpace ) )
        {
            return true;
        }

        var seededAi = includes.Count == 1 &&
            string.Equals( includes[0], "ai", StringComparison.OrdinalIgnoreCase );
        var filter = seededAi
            ? SearchFilterRegistry.FindByToken( text[..colon] )
            : ResolveToken( text[..colon], aliases, registry );
        if ( !seededAi && (filter is null || filter.Id is not ("ai" or "applications")) )
        {
            return false;
        }

        if ( filter is not null && (filter.Id == "ai" || !seededAi) )
        {
            AddUnique( includes, filter.Id );
            text = text[(colon + 1)..].TrimStart( );
        }
        return true;
    }

    private static bool IsSearchSyntax(
        string value,
        IReadOnlyDictionary<string, string>? aliases,
        ContributionRegistrySnapshot? registry )
    {
        var token = value.TrimStart( '-' );
        var colon = token.IndexOf( ':' );
        return colon > 0 && (QueryFieldIds.All.Contains( token[..colon] ) ||
            ResolveToken( token[..colon], aliases, registry ) is not null);
    }

    private sealed record LiteralPayload(
        string Text, IReadOnlyList<string> Terms, QueryExpression Expression, bool IsAi );
}
