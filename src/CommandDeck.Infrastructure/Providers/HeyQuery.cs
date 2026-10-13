namespace CommandDeck;

/// <summary>
/// Reads HEY refinements out of a <c>hey:</c> query. Plain words are the search
/// text; a quoted phrase becomes HEY's exact phrase; <c>-word</c> excludes a
/// word. Refinements take one value each: <c>from:</c>, <c>to:</c>,
/// <c>subject:</c>, <c>label:</c>, and the fixed vocabularies of <c>in:</c>,
/// <c>has:</c>, and <c>date:</c>, which accept a few friendlier spellings.
/// </summary>
public sealed record HeyQuery
{
    public HeySearchQuery Search { get; init; } = new( );
    // Why the query cannot be sent: an unknown in:, has:, or date: value.
    public string? Problem { get; init; }
    // Whether the query named any refinement, such as from: or in:.
    public bool HasRefinements { get; init; }
    // box: lists one box instead of searching; the words then filter its threads.
    public HeyBox? Box { get; init; }
    public IReadOnlyList<string> Words { get; init; } = [];

    public static IReadOnlyList<string> RefinementNames { get; } = ["from", "to", "subject", "in", "label", "has", "date", "box"];

    public static IReadOnlyDictionary<string, string> Boxes { get; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
    {
        ["imbox"] = "imbox",
        ["feed"] = "feed",
        ["the-feed"] = "feed",
        ["papertrail"] = "papertrail",
        ["paper-trail"] = "papertrail",
        ["trail"] = "papertrail",
        ["trash"] = "trash"
    };

    public static IReadOnlyDictionary<string, string> Attachments { get; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
    {
        ["any"] = "any",
        ["attachment"] = "any",
        ["attachments"] = "any",
        ["images"] = "images",
        ["image"] = "images",
        ["pdfs"] = "pdfs",
        ["pdf"] = "pdfs",
        ["calendar_invites"] = "calendar_invites",
        ["invite"] = "calendar_invites",
        ["invites"] = "calendar_invites",
        ["documents"] = "documents",
        ["document"] = "documents",
        ["docs"] = "documents",
        ["spreadsheets"] = "spreadsheets",
        ["spreadsheet"] = "spreadsheets",
        ["sheets"] = "spreadsheets",
        ["presentations"] = "presentations",
        ["presentation"] = "presentations",
        ["slides"] = "presentations",
        ["media"] = "media",
        ["zip_files"] = "zip_files",
        ["zip"] = "zip_files"
    };

    public static IReadOnlyDictionary<string, string> Dates { get; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
    {
        ["last_7_days"] = "last_7_days",
        ["7d"] = "last_7_days",
        ["week"] = "last_7_days",
        ["last_30_days"] = "last_30_days",
        ["30d"] = "last_30_days",
        ["month"] = "last_30_days",
        ["last_90_days"] = "last_90_days",
        ["90d"] = "last_90_days",
        ["year"] = "year"
    };

    public static HeyQuery Parse( ParsedQuery query )
    {
        ArgumentNullException.ThrowIfNull( query );

        List<string> words = [];
        List<string> excluded = [];
        string? exact = null;
        string? problem = null;
        Dictionary<string, string> refinements = new( StringComparer.OrdinalIgnoreCase );
        foreach ( var clause in Clauses( query ) )
        {
            // title:, path:, and the other fields of local search mean nothing to HEY.
            if ( clause.Field is not null )
            {
                problem ??= $"HEY has no {clause.Field}: refinement. Use {SupportedRefinements}.";
                continue;
            }

            var (name, value) = SplitRefinement( clause.Text );
            if ( name is not null )
            {
                problem ??= AddRefinement( refinements, name, value, clause.Excluded );
                continue;
            }

            if ( clause.Excluded )
            {
                excluded.Add( clause.Text );
            }
            else if ( exact is null && clause.Text.Contains( ' ', StringComparison.Ordinal ) )
            {
                exact = clause.Text;
            }
            else
            {
                words.Add( clause.Text );
            }
        }

        problem ??= Vocabulary( refinements, "in", Boxes, "imbox, feed, papertrail, or trash" ) ??
            Vocabulary( refinements, "has", Attachments, "any, images, pdfs, invites, documents, spreadsheets, presentations, media, or zip" ) ??
            Vocabulary( refinements, "date", Dates, "7d, 30d, 90d, or year" );
        var box = refinements.TryGetValue( "box", out var boxToken ) ? HeyBoxes.FindByToken( boxToken ) : null;
        if ( boxToken is not null && box is null )
        {
            problem ??= $"HEY has no box:{boxToken}. Use {string.Join( ", ", HeyBoxes.All.Select( static value => value.Token ) )}.";
        }
        else if ( box is not null && refinements.Count > 1 )
        {
            problem ??= "box: lists one box. To search a box, use in:imbox, in:feed, or in:papertrail.";
        }


        return new HeyQuery
        {
            Problem = problem,
            HasRefinements = refinements.Count > 0,
            Box = box,
            Words = exact is null ? words : [.. words, exact],
            Search = new HeySearchQuery
            {
                Text = string.Join( ' ', words ),
                Exact = exact,
                None = excluded.Count == 0 ? null : string.Join( ' ', excluded ),
                From = refinements.GetValueOrDefault( "from" ),
                To = refinements.GetValueOrDefault( "to" ),
                Subject = refinements.GetValueOrDefault( "subject" ),
                Label = refinements.GetValueOrDefault( "label" ),
                Box = Canonical( refinements, "in", Boxes ),
                Attachment = Canonical( refinements, "has", Attachments ),
                Date = Canonical( refinements, "date", Dates )
            }
        };
    }

    // @hey keeps its text unparsed; read the same clauses a hey: query has.
    private static List<QueryClause> Clauses( ParsedQuery query )
    {
        if ( query.Expression.Clauses.Count > 0 || query.Text.Length == 0 )
        {
            return query.Expression.Clauses;
        }

        try
        {
            return QueryParser.Parse( query.Text ).Expression.Clauses;
        }
        catch ( InvalidOperationException )
        {
            return [new QueryClause { Text = query.Text }];
        }
    }

    // "from:jane" or from:"Jane Cooper"; anything else is ordinary text.
    public static (string? Name, string Value) SplitRefinement( string text )
    {
        var colon = text.IndexOf( ':', StringComparison.Ordinal );
        if ( colon <= 0 )
        {
            return (null, text);
        }

        var name = text[..colon];
        if ( !RefinementNames.Contains( name, StringComparer.OrdinalIgnoreCase ) )
        {
            return (null, text);
        }

        var value = text[(colon + 1)..].Trim( );
        if ( value.Length >= 2 && value[0] == '"' && value[^1] == '"' )
        {
            value = value[1..^1].Trim( );
        }

        return (name.ToLowerInvariant( ), value);
    }

    private const string SupportedRefinements = "subject:, from:, to:, label:, in:, has:, or date:";

    // A value still being typed (from: alone) waits. HEY cannot leave a
    // refinement out, and each takes one value.
    private static string? AddRefinement( Dictionary<string, string> refinements, string name, string value, bool excluded )
    {
        if ( value.Length == 0 )
        {
            return null;
        }

        if ( excluded )
        {
            return $"HEY can't leave out {name}:{value}; a minus only leaves out words.";
        }

        if ( refinements.TryGetValue( name, out var earlier ) && !string.Equals( earlier, value, StringComparison.OrdinalIgnoreCase ) )
        {
            return $"HEY takes one {name}: value; keep {name}:{earlier} or {name}:{value}.";
        }

        refinements.TryAdd( name, value );
        return null;
    }

    private static string? Vocabulary(
        Dictionary<string, string> refinements, string name, IReadOnlyDictionary<string, string> values, string accepted ) =>
        refinements.TryGetValue( name, out var value ) && !values.ContainsKey( value )
            ? $"HEY does not know {name}:{value}. Use {accepted}."
            : null;

    private static string? Canonical(
        Dictionary<string, string> refinements, string name, IReadOnlyDictionary<string, string> values ) =>
        refinements.TryGetValue( name, out var value ) && values.TryGetValue( value, out var canonical ) ? canonical : null;
}
