namespace CommandDeck;

// While the last word of a hey: query is a refinement with an unfinished value
// (in:, has:pd, date:, box:re), the rows are its values instead of a search.
// They run nothing; Enter or Tab puts the value into the query.
public sealed partial class HeyProvider
{
    public const string CompleteActionId = "complete";

    private static readonly Dictionary<string, (string Value, string Description)[]> CompletionValues = new( StringComparer.Ordinal )
    {
        ["in"] =
        [
            ("imbox", "Search the Imbox"), ("feed", "Search The Feed"),
            ("papertrail", "Search the Paper Trail"), ("trash", "Search the Trash")
        ],
        ["has"] =
        [
            ("any", "Any attachment"), ("images", "Images"), ("pdfs", "PDFs"), ("invites", "Calendar invites"),
            ("documents", "Documents"), ("spreadsheets", "Spreadsheets"), ("presentations", "Presentations"),
            ("media", "Audio and video"), ("zip", "Zip files")
        ],
        ["date"] = [("7d", "The last 7 days"), ("30d", "The last 30 days"), ("90d", "The last 90 days"), ("year", "This year")],
        ["box"] = [.. HeyBoxes.All.Select( static box => (box.Token, "List " + box.Label) )]
    };

    private static List<SearchResult>? CompletionRows( SearchRequest request )
    {
        var query = request.Query;
        if ( query.Length == 0 || char.IsWhiteSpace( query[^1] ) )
        {
            return null;
        }

        var start = query.LastIndexOfAny( [' ', '\t'] ) + 1;
        var word = query[start..];
        var colon = word.IndexOf( ':', StringComparison.Ordinal );
        if ( colon <= 0 || !CompletionValues.TryGetValue( word[..colon].ToLowerInvariant( ), out var values ) )
        {
            return null;
        }

        var name = word[..colon].ToLowerInvariant( );
        var typed = word[(colon + 1)..];
        if ( IsComplete( name, typed ) )
        {
            return null;
        }

        var matching = values.Where( value => value.Value.StartsWith( typed, StringComparison.OrdinalIgnoreCase ) ).ToArray( );
        if ( matching.Length == 0 )
        {
            return null;
        }

        var filterId = request.Expression?.IncludeFilterIds.Contains( HeySearch.FilterId, StringComparer.OrdinalIgnoreCase ) == true
            ? HeySearch.FilterId
            : "";
        return [.. matching.Select( ( value, index ) =>
        {
            var completed = query[..start] + name + ":" + value.Value + " ";
            return new SearchResult
            {
                Id = "hey:complete:" + name + ":" + value.Value,
                Kind = ResultKinds.Status,
                Title = name + ":" + value.Value,
                Subtitle = value.Description,
                Icon = name == "box" ? BoxIcon : ThreadIcon,
                ProviderId = ProviderIds.Hey,
                SourceId = SourceIds.Hey,
                Score = HomeScore( index ),
                CanCustomize = false,
                Metadata = new( StringComparer.Ordinal ) { [ResultMetadataKeys.CompletionText] = completed },
                Actions = [ApplyQueryAction( CompleteActionId, "Use " + name + ":" + value.Value, completed, filterId )]
            };
        } )];
    }

    // A value the parser already accepts is finished: the query searches.
    private static bool IsComplete( string name, string typed ) => name switch
    {
        "in" => HeyQuery.Boxes.ContainsKey( typed ),
        "has" => HeyQuery.Attachments.ContainsKey( typed ),
        "date" => HeyQuery.Dates.ContainsKey( typed ),
        _ => HeyBoxes.FindByToken( typed ) is not null
    };
}
