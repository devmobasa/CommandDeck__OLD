using System.Globalization;

namespace CommandDeck;

// An empty hey: offers HEY's boxes and pages instead of a blank list, and
// box: lists one box. No row carries a count: HEY's bar plugin owns new-mail
// awareness, and HEY itself avoids unread numbers.
public sealed partial class HeyProvider
{
    public const string BrowseActionId = "browse";
    public const string RetryActionId = "retry";
    public const string OpenPageActionId = "open-page";
    public const string BoxIcon = "mail-folder-inbox";
    public const string SearchActionId = "search";
    public const int FallbackTitleLength = 80;

    private static List<SearchResult> HomeRows( )
    {
        List<SearchResult> rows = [.. HeyBoxes.All.Select( static ( box, index ) => BoxRow( box, index ) )];
        rows.Add( PageRow( HeyPages.Screener, "The Screener", "First-time senders waiting for a yes or no.", "contact-new", rows.Count ) );
        rows.Add( PageRow( HeyPages.Compose, "Write an Email", "Opens HEY's compose page.", "mail-message-new", rows.Count ) );
        rows.Add( PageRow( HeyPages.Home, "Open HEY", "Opens HEY where Settings says threads open.", ThreadIcon, rows.Count ) );
        rows.Add( StateRow( "hint", "Search HEY",
            "Type words to search your mail; refine with from:, to:, subject:, in:, has:, date:, or label:.", score: 0 ) );
        return rows;
    }

    private static SearchResult BoxRow( HeyBox box, int index ) => new( )
    {
        Id = "hey:box:" + box.Token,
        Kind = ResultKinds.Mail,
        Title = box.Label,
        Subtitle = "Browse " + box.Label + " · box:" + box.Token,
        Icon = BoxIcon,
        ProviderId = ProviderIds.Hey,
        SourceId = SourceIds.Hey,
        Score = HomeScore( index ),
        CanCustomize = false,
        Actions = [ApplyQueryAction( BrowseActionId, "Browse " + box.Label, "box:" + box.Token, HeySearch.FilterId )]
    };

    private static SearchResult PageRow( string page, string title, string subtitle, string icon, int index ) => new( )
    {
        Id = "hey:page:" + page,
        Kind = ResultKinds.Mail,
        Title = title,
        Subtitle = subtitle,
        Icon = icon,
        ProviderId = ProviderIds.Hey,
        SourceId = SourceIds.Hey,
        Score = HomeScore( index ),
        CanCustomize = false,
        Actions =
        [
            new CommandAction
            {
                Id = OpenPageActionId,
                Title = title,
                Kind = ActionKinds.HeyOpenPage,
                Arguments = new( StringComparer.Ordinal ) { ["page"] = page }
            }
        ]
    };

    private static double HomeScore( int index ) => 1.0 - (index * 0.001);

    // Plain words get one row at the end of the list that moves them into hey:.
    // It runs nothing until chosen and takes no ranked slot.
    private static SearchResult FallbackRow( ParsedQuery query, SearchRequest request ) => new( )
    {
        Id = "hey:fallback",
        Kind = ResultKinds.Mail,
        Title = $"Search HEY for “{HeyClient.Clean( query.Text, FallbackTitleLength )}”",
        Subtitle = "Your HEY mail · hey:",
        Icon = ThreadIcon,
        ProviderId = ProviderIds.Hey,
        SourceId = SourceIds.Hey,
        Section = new ResultSection { Id = "hey-fallback", Title = "HEY", Order = 101 },
        Score = 0,
        CanCustomize = false,
        Metadata = new( StringComparer.Ordinal )
        {
            [ResultPlacements.MetadataKey] = ResultPlacements.Trailing
        },
        Actions = [ApplyQueryAction( SearchActionId, "Search HEY", request.Query, HeySearch.FilterId )]
    };

    // The words filter the box locally: every word must appear in the subject,
    // the sender, or the preview.
    private List<SearchResult> ProjectBox( HeyListing listing, HeyBox box, IReadOnlyList<string> words, SearchRequest request )
    {
        if ( listing.Failure is not null )
        {
            return [FailureRow( listing.Failure, RetryAction( request ) )];
        }

        var matching = listing.Threads.Where( thread => words.All( word =>
            thread.Subject.Contains( word, StringComparison.OrdinalIgnoreCase ) ||
            thread.Sender.Contains( word, StringComparison.OrdinalIgnoreCase ) ||
            thread.Summary.Contains( word, StringComparison.OrdinalIgnoreCase ) ) ).ToArray( );
        if ( matching.Length == 0 )
        {
            return [StateRow( "empty-box", words.Count == 0 ? "Nothing in " + box.Label : "No threads in " + box.Label + " match",
                words.Count == 0 ? "HEY shows this box as empty." : "Only the newest threads are listed; search HEY for older ones." )];
        }

        var bounded = Math.Clamp( request.Limit, 1, HeyClient.MaximumThreads );
        List<SearchResult> rows = [.. matching.Take( bounded ).Select( ( thread, index ) => ThreadRow( thread, index, box ) )];
        if ( listing.HasMore || matching.Length > bounded )
        {
            var older = PageRow( HeyPages.Home, "Older threads are in HEY",
                $"Showing the newest {rows.Count.ToString( CultureInfo.InvariantCulture )} in {box.Label}.", ThreadIcon, 0 );
            rows.Add( new SearchResult( older ) { Id = "hey:state:more-in-box", Score = 0 } );
        }

        return rows;
    }

    // Enter on a failure row runs the same search again, with the same chip.
    private static CommandAction RetryAction( SearchRequest request ) => ApplyQueryAction(
        RetryActionId, "Search Again", request.Query,
        request.Expression?.IncludeFilterIds.Contains( HeySearch.FilterId, StringComparer.OrdinalIgnoreCase ) == true ? HeySearch.FilterId : "" );

    private static CommandAction ApplyQueryAction( string id, string title, string query, string filterId ) => new( )
    {
        Id = id,
        Title = title,
        Kind = ActionKinds.ApplyQuery,
        Arguments = new( StringComparer.Ordinal )
        {
            ["query"] = query,
            ["filterId"] = filterId
        }
    };
}
