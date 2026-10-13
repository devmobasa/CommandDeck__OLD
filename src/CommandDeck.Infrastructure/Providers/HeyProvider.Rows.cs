using System.Globalization;

namespace CommandDeck;

// Rows for HEY listings and every state a hey: search can end in. None can be
// customized: a favorite, alias, or hidden item would persist mail text.
public sealed partial class HeyProvider
{
    public const string OpenActionId = "open";
    public const string CopyLinkActionId = "copy-link";
    public const string ReadActionId = "read";
    public const string SignInActionId = "sign-in";
    public const string InstallActionId = "install";
    public const string TopicIdMetadata = "heyTopicId";
    public const string PostingIdMetadata = "heyPostingId";
    public const string BoxIdMetadata = "heyBoxId";
    public const string SeenMetadata = "heySeen";
    public const string UnreadIcon = "mail-unread";
    public const string ThreadIcon = "internet-mail";
    public const string RefineHint = "narrow it with from:, in:, has:, or date:";

    private List<SearchResult> Project( HeyListing listing, SearchRequest request )
    {
        if ( listing.Failure is not null )
        {
            return [FailureRow( listing.Failure, RetryAction( request ) )];
        }

        if ( listing.Threads.Count == 0 )
        {
            return [StateRow( "no-matches", "No HEY threads match", "Try fewer words, or " + RefineHint + "." )];
        }

        var bounded = Math.Clamp( request.Limit, 1, HeyClient.MaximumThreads );
        List<SearchResult> rows = [.. listing.Threads.Take( bounded ).Select( ( thread, index ) => ThreadRow( thread, index ) )];
        if ( listing.HasMore || listing.Threads.Count > bounded )
        {
            rows.Add( StateRow( "more", "More matches in HEY",
                $"Showing the first {rows.Count.ToString( CultureInfo.InvariantCulture )}: {RefineHint}.", score: 0 ) );
        }

        return rows;
    }

    private SearchResult ThreadRow( HeyThread thread, int index, HeyBox? box = null )
    {
        var topicId = thread.TopicId.ToString( CultureInfo.InvariantCulture );
        var local = thread.At is { } at ? TimeZoneInfo.ConvertTime( at, _time.LocalTimeZone ) : ( DateTimeOffset? ) null;
        var subtitle = string.Join( " · ", new[] { thread.Sender, local is null ? "" : ShortDate( local.Value ), thread.Summary }
            .Where( static part => part.Length > 0 ) );
        Dictionary<string, string> metadata = new( StringComparer.Ordinal )
        {
            [TopicIdMetadata] = topicId
        };
        if ( thread.PostingId is { } postingId )
        {
            metadata[PostingIdMetadata] = postingId.ToString( CultureInfo.InvariantCulture );
        }

        if ( thread.BoxId is { } boxId )
        {
            metadata[BoxIdMetadata] = boxId.ToString( CultureInfo.InvariantCulture );
        }

        if ( thread.Seen is { } seen )
        {
            metadata[SeenMetadata] = seen ? "true" : "false";
        }

        List<DetailField> details = [];
        var from = thread.SenderAddress is null || thread.SenderAddress == thread.Sender
            ? thread.Sender
            : $"{thread.Sender} <{thread.SenderAddress}>";
        if ( from.Length > 0 )
        {
            details.Add( new DetailField( "From", from ) );
        }

        if ( local is not null )
        {
            details.Add( new DetailField( "Date", local.Value.ToString( "ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture ) ) );
        }

        if ( thread.Seen == false )
        {
            details.Add( new DetailField( "Status", "Unseen" ) );
        }

        if ( thread.Summary.Length > 0 )
        {
            details.Add( new DetailField( "Preview", thread.Summary ) );
        }

        return new SearchResult
        {
            Id = "hey:topic:" + topicId,
            Kind = ResultKinds.Mail,
            Title = thread.Subject,
            Subtitle = subtitle,
            Icon = thread.Seen == false ? UnreadIcon : ThreadIcon,
            ProviderId = ProviderIds.Hey,
            SourceId = SourceIds.Hey,
            // HEY's order is the ranking; the coordinator keeps this score.
            Score = 1.0 - (index * 0.001),
            CanCustomize = false,
            Details = details,
            Metadata = metadata,
            Actions =
            [
                new CommandAction
                {
                    Id = OpenActionId,
                    Title = "Open in HEY",
                    Kind = ActionKinds.HeyOpen,
                    Arguments = new( StringComparer.Ordinal ) { ["topicId"] = topicId, ["appUrl"] = thread.AppUrl }
                },
                new CommandAction
                {
                    Id = ReadActionId,
                    Title = "Read Thread",
                    Kind = ActionKinds.HeyReadThread,
                    Arguments = new( StringComparer.Ordinal ) { ["topicId"] = topicId }
                },
                new CommandAction
                {
                    Id = CopyLinkActionId,
                    Title = "Copy Link",
                    Kind = ActionKinds.CopyText,
                    Arguments = new( StringComparer.Ordinal ) { ["text"] = thread.AppUrl }
                },
                .. TriageActions( thread, box )
            ]
        };
    }

    // Today: 14:05; this year: 21 Sep; earlier: 21 Sep 2025.
    private string ShortDate( DateTimeOffset local )
    {
        var today = TimeZoneInfo.ConvertTime( _time.GetUtcNow( ), _time.LocalTimeZone );
        if ( local.Date == today.Date )
        {
            return local.ToString( "HH:mm", CultureInfo.InvariantCulture );
        }

        return local.ToString( local.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture );
    }

    // A passing problem (offline, rate limited, slow) offers the same search again.
    private static SearchResult FailureRow( HeyFailure failure, CommandAction retry ) => failure.Kind switch
    {
        HeyFailureKind.NotInstalled => InstallRow( ),
        HeyFailureKind.SignedOut => new SearchResult
        {
            Id = "hey:state:signed-out",
            Kind = ResultKinds.Status,
            Title = "Sign in to HEY",
            Subtitle = "Opens a terminal for hey login; your browser finishes signing in.",
            Icon = ThreadIcon,
            ProviderId = ProviderIds.Hey,
            SourceId = SourceIds.Hey,
            Score = 1,
            CanCustomize = false,
            Actions = [new CommandAction { Id = SignInActionId, Title = "Sign in to HEY", Kind = ActionKinds.HeySignIn }]
        },
        HeyFailureKind.Network => StateRow( "offline", "HEY is unreachable", "Check your connection, then search again.", failure.Message, action: retry ),
        HeyFailureKind.RateLimited => StateRow( "rate-limited", "HEY asked for a pause", "Wait a moment, then search again.", failure.Message, action: retry ),
        HeyFailureKind.TimedOut => StateRow( "timed-out", "HEY took too long to answer", "Search again, or " + RefineHint + ".", action: retry ),
        HeyFailureKind.Rejected => StateRow( "rejected", "HEY can't run that search", failure.Message ),
        _ => StateRow( "failed", "HEY search failed", failure.Message, action: retry )
    };

    private static SearchResult InstallRow( ) => new( )
    {
        Id = "hey:state:not-installed",
        Kind = ResultKinds.Status,
        Title = "Install the HEY CLI",
        Subtitle = "Search your HEY mail from here. Installs hey-cli with mise in a terminal.",
        Icon = ThreadIcon,
        ProviderId = ProviderIds.Hey,
        SourceId = SourceIds.Hey,
        Score = 1,
        CanCustomize = false,
        Actions = [new CommandAction { Id = InstallActionId, Title = "Install the HEY CLI", Kind = ActionKinds.HeyInstall }]
    };

    private static SearchResult StateRow(
        string state, string title, string subtitle, string? detail = null, double score = 1, CommandAction? action = null ) => new( )
        {
            Id = "hey:state:" + state,
            Kind = ResultKinds.Status,
            Title = title,
            Subtitle = subtitle,
            Icon = ThreadIcon,
            ProviderId = ProviderIds.Hey,
            SourceId = SourceIds.Hey,
            Score = score,
            CanCustomize = false,
            Details = detail is null || detail == subtitle ? [] : [new DetailField( "HEY said", detail )],
            Actions = action is null ? [] : [action]
        };
}
