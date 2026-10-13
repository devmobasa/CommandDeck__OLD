namespace CommandDeck;

public enum HeyAvailability
{
    NotInstalled,
    SignedOut,
    SignedIn,
    Failed
}

public sealed record HeyStatus( HeyAvailability Availability, string Detail );

/// <summary>
/// Why a HEY read produced no listing. The CLI's own error codes decide the
/// kind, so a signed-out account, an unreachable server, and a rate limit each
/// get their own row instead of one generic failure.
/// </summary>
public enum HeyFailureKind
{
    NotInstalled,
    SignedOut,
    Network,
    RateLimited,
    TimedOut,
    Rejected,
    Failed
}

public sealed record HeyFailure( HeyFailureKind Kind, string Message );

/// <summary>
/// One HEY thread as a search match or a box posting. <see cref="PostingId"/> is
/// the box item HEY organizes (seen, move, bubble up); it is absent for a search
/// match without an active box item. <see cref="Seen"/> and
/// <see cref="BubbledUp"/> are only known for box postings. Text fields are
/// sanitized: no control characters, bounded length.
/// </summary>
public sealed record HeyThread
{
    public required long TopicId { get; init; }
    public long? PostingId { get; init; }
    public long? BoxId { get; init; }
    public required string Subject { get; init; }
    public string Sender { get; init; } = "";
    public string? SenderAddress { get; init; }
    public string Summary { get; init; } = "";
    public DateTimeOffset? At { get; init; }
    public bool? Seen { get; init; }
    public required string AppUrl { get; init; }
    public bool? BubbledUp { get; init; }
}

public sealed record HeyListing
{
    public IReadOnlyList<HeyThread> Threads { get; init; } = [];
    // HEY answered with more than this page; the rest is one "More in HEY" away.
    public bool HasMore { get; init; }
    public HeyFailure? Failure { get; init; }

    public static HeyListing Failed( HeyFailureKind kind, string message ) =>
        new( )
        {
            Failure = new HeyFailure( kind, message )
        };
}

/// <summary>
/// A HEY box CommandDeck can list: its launcher token, the name HEY shows, and
/// the name <c>hey box view</c> accepts.
/// </summary>
public sealed record HeyBox( string Token, string Label, string CliName );

public static class HeyBoxes
{
    // Most actionable first, the order the empty hey: view offers them in.
    public static IReadOnlyList<HeyBox> All { get; } =
    [
        new( "imbox", "Imbox", "imbox" ),
        new( "reply-later", "Reply Later", "laterbox" ),
        new( "set-aside", "Set Aside", "asidebox" ),
        new( "feed", "The Feed", "feedbox" ),
        new( "paper-trail", "Paper Trail", "trailbox" ),
        new( "bubbled-up", "Bubbled Up", "bubblebox" )
    ];

    public static HeyBox? FindByToken( string? token ) =>
        All.FirstOrDefault( box => string.Equals( box.Token, token, StringComparison.OrdinalIgnoreCase ) );

    public static bool IsCliName( string? name ) =>
        All.Any( box => string.Equals( box.CliName, name, StringComparison.Ordinal ) );
}

/// <summary>
/// A search in HEY's own terms: free text plus the refinements <c>hey search</c>
/// accepts. Values are passed as single <c>--name=value</c> arguments, never
/// through a shell.
/// </summary>
public sealed record HeySearchQuery
{
    public string Text { get; init; } = "";
    // One phrase that must appear as written, and words that must not appear.
    public string? Exact { get; init; }
    public string? None { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Subject { get; init; }
    public string? Box { get; init; }
    public string? Date { get; init; }
    public string? Label { get; init; }
    public string? Attachment { get; init; }

    public bool IsEmpty => Text.Length == 0 && Exact is null && None is null && From is null && To is null && Subject is null &&
        Box is null && Date is null && Label is null && Attachment is null;
}

/// <summary>
/// HEY pages the launcher opens besides a thread. The web paths are the ones
/// HEY's own Omarchy pieces use: /clearances (its bar plugin's Screener link)
/// and /messages/new (Omarchy's HEY mailto handler).
/// </summary>
public static class HeyPages
{
    public const string Home = "home";
    public const string Screener = "screener";
    public const string Compose = "compose";

    public static string WebUrl( string page ) => page switch
    {
        Screener => HeyClient.DefaultWebOrigin + "/clearances",
        Compose => HeyClient.DefaultWebOrigin + "/messages/new",
        _ => HeyClient.DefaultWebOrigin
    };
}

/// <summary>A whole thread as the Markdown <c>hey thread read --markdown</c> writes.</summary>
public sealed record HeyThreadText
{
    public string Markdown { get; init; } = "";
    public bool Trimmed { get; init; }
    public HeyFailure? Failure { get; init; }
}
