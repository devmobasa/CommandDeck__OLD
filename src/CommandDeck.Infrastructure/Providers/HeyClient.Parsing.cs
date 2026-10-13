using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CommandDeck;

// hey answers every data command with an envelope: {"ok":true,"data":...} or
// {"ok":false,"error":...,"code":...}. The data is HEY's own payload, read
// field by field so unknown fields never matter and missing ones fail closed.
public sealed partial class HeyClient
{
    public const string DefaultWebOrigin = "https://app.hey.com";
    public const int MaximumSubjectLength = 300;
    public const int MaximumSummaryLength = 400;
    public const int MaximumSenderLength = 200;

    private static HeyStatus ParseStatus( ProcessResult result )
    {
        if ( result.TimedOut )
        {
            return new HeyStatus( HeyAvailability.Failed, "HEY took too long to report its sign-in." );
        }

        if ( result.ExitCode != 0 )
        {
            var failure = FailureOf( result );
            return failure.Kind == HeyFailureKind.SignedOut
                ? new HeyStatus( HeyAvailability.SignedOut, "Sign in to HEY to search your mail." )
                : new HeyStatus( HeyAvailability.Failed, failure.Message );
        }

        var envelope = ReadEnvelope( result.StandardOutput );
        if ( envelope.Failure is not null )
        {
            return new HeyStatus( HeyAvailability.Failed, envelope.Failure.Message );
        }

        using ( envelope.Document )
        {
            var authenticated = envelope.Data.ValueKind == JsonValueKind.Object &&
                envelope.Data.TryGetProperty( "authenticated", out var value ) &&
                value.ValueKind == JsonValueKind.True;
            return authenticated
                ? new HeyStatus( HeyAvailability.SignedIn, "Signed in to HEY." )
                : new HeyStatus( HeyAvailability.SignedOut, "Sign in to HEY to search your mail." );
        }
    }

    // One result per thread: {id?, topic_id, subject, updated_at, messages:[entry]}.
    private static HeyListing ParseSearch( string json )
    {
        var envelope = ReadEnvelope( json );
        if ( envelope.Failure is not null )
        {
            return new HeyListing { Failure = envelope.Failure };
        }

        using ( envelope.Document )
        {
            if ( envelope.Data.ValueKind != JsonValueKind.Array )
            {
                return Malformed( );
            }

            List<HeyThread> threads = [];
            foreach ( var match in envelope.Data.EnumerateArray( ).Take( MaximumThreads ) )
            {
                var topicId = ReadId( match, "topic_id" );
                if ( match.ValueKind != JsonValueKind.Object || topicId is null )
                {
                    continue;
                }

                var entry = FirstEntry( match );
                threads.Add( new HeyThread
                {
                    TopicId = topicId.Value,
                    PostingId = ReadId( match, "id" ),
                    Subject = Subject( ReadString( match, "subject" ) ),
                    Sender = entry is null ? "" : SenderName( entry.Value ),
                    SenderAddress = entry is null ? null : SenderAddress( entry.Value ),
                    Summary = Clean( entry is null ? null : ReadString( entry.Value, "summary" ), MaximumSummaryLength ),
                    At = (entry is null ? null : ReadTime( entry.Value, "created_at" )) ?? ReadTime( match, "updated_at" ),
                    AppUrl = ThreadUrl( topicId.Value )
                } );
            }

            return new HeyListing
            {
                Threads = threads,
                HasMore = IsPaginationNotice( envelope.Notice ) || envelope.Data.GetArrayLength( ) > MaximumThreads
            };
        }
    }

    // When hey stops paging with matches left, its notice says where to go on
    // ("Search stopped after 1 pages. Continue with --page 2."); a notice that
    // names no page says nothing about more matches.
    private static bool IsPaginationNotice( string? notice ) =>
        notice is not null && notice.Contains( "--page", StringComparison.Ordinal );

    // HEY's box payload with its postings: {postings:[posting + topic_id], next_page?}.
    private static HeyListing ParseBox( string json )
    {
        var envelope = ReadEnvelope( json );
        if ( envelope.Failure is not null )
        {
            return new HeyListing { Failure = envelope.Failure };
        }

        using ( envelope.Document )
        {
            if ( envelope.Data.ValueKind != JsonValueKind.Object ||
                !envelope.Data.TryGetProperty( "postings", out var postings ) ||
                postings.ValueKind != JsonValueKind.Array )
            {
                return Malformed( );
            }

            List<HeyThread> threads = [];
            foreach ( var posting in postings.EnumerateArray( ).Take( MaximumThreads ) )
            {
                // A bundle row stands for one sender's stream and has no thread.
                var topicId = ReadId( posting, "topic_id" );
                if ( posting.ValueKind != JsonValueKind.Object || topicId is null )
                {
                    continue;
                }

                threads.Add( new HeyThread
                {
                    TopicId = topicId.Value,
                    PostingId = ReadId( posting, "id" ),
                    BoxId = ReadId( posting, "box_id" ),
                    Subject = Subject( ReadString( posting, "name" ) ),
                    Sender = SenderName( posting ),
                    SenderAddress = SenderAddress( posting ),
                    Summary = Clean( ReadString( posting, "summary" ), MaximumSummaryLength ),
                    At = ReadTime( posting, "active_at" ) ?? ReadTime( posting, "created_at" ),
                    Seen = posting.TryGetProperty( "seen", out var seen ) && seen.ValueKind == JsonValueKind.True,
                    BubbledUp = IsBubbledUp( posting ),
                    AppUrl = ThreadUrl( topicId.Value )
                } );
            }

            return new HeyListing
            {
                Threads = threads,
                HasMore = !string.IsNullOrWhiteSpace( ReadString( envelope.Data, "next_page" ) ) ||
                    postings.GetArrayLength( ) > MaximumThreads
            };
        }
    }

    // HEY marks a posting that has bubbled up with bubbled_up, and one waiting
    // to with a bubble_up_schedule; hey leaves either out when it is not set.
    private static bool IsBubbledUp( JsonElement posting ) =>
        (posting.TryGetProperty( "bubbled_up", out var bubbled ) && bubbled.ValueKind == JsonValueKind.True) ||
        (posting.TryGetProperty( "bubble_up_schedule", out var schedule ) && schedule.ValueKind == JsonValueKind.Object);

    // A failing hey writes its error envelope to stderr when stdout is not a
    // terminal, which is always the case here; stdout is checked too, and the
    // exit code is the last resort. Error output past its limit was cut and
    // hey stopped, so its exit code explains nothing either.
    internal static HeyFailure FailureOf( ProcessResult result )
    {
        foreach ( var stream in new[] { result.StandardError, result.StandardOutput } )
        {
            var failure = ReadErrorEnvelope( stream );
            if ( failure is not null )
            {
                return failure;
            }
        }

        if ( result.StandardErrorLimitExceeded )
        {
            return new HeyFailure( HeyFailureKind.Failed, "HEY failed with more error output than CommandDeck reads." );
        }

        return new HeyFailure( HeyFailureKind.Failed,
            $"The HEY CLI exited with code {result.ExitCode.ToString( CultureInfo.InvariantCulture )}." );
    }

    // Only a JSON object with "ok": false is an error envelope.
    private static HeyFailure? ReadErrorEnvelope( string text )
    {
        if ( string.IsNullOrWhiteSpace( text ) )
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse( text );
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty( "ok", out var ok ) && ok.ValueKind == JsonValueKind.False
                ? ErrorFrom( root )
                : null;
        }
        catch ( JsonException )
        {
            return null;
        }
    }

    private static HeyFailure ErrorFrom( JsonElement root ) =>
        new( FailureKind( ReadString( root, "code" ) ),
            Clean( ReadString( root, "error" ), MaximumSummaryLength ) is { Length: > 0 } message ? message : "HEY refused the request." );

    private static HeyListing Malformed( ) =>
        HeyListing.Failed( HeyFailureKind.Failed, "HEY answered in a shape CommandDeck does not read." );

    private readonly record struct Envelope( JsonDocument? Document, JsonElement Data, string? Notice, HeyFailure? Failure );

    private static Envelope ReadEnvelope( string json )
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse( json );
        }
        catch ( JsonException )
        {
            return new Envelope( null, default, null, new HeyFailure( HeyFailureKind.Failed, "HEY answered with malformed JSON." ) );
        }

        var root = document.RootElement;
        if ( root.ValueKind != JsonValueKind.Object || !root.TryGetProperty( "ok", out var ok ) )
        {
            document.Dispose( );
            return new Envelope( null, default, null, new HeyFailure( HeyFailureKind.Failed, "HEY answered with malformed JSON." ) );
        }

        if ( ok.ValueKind != JsonValueKind.True )
        {
            var failure = ErrorFrom( root );
            document.Dispose( );
            return new Envelope( null, default, null, failure );
        }

        var data = root.TryGetProperty( "data", out var value ) ? value : default;
        return new Envelope( document, data, ReadString( root, "notice" ), null );
    }

    private static HeyFailureKind FailureKind( string? code ) => code switch
    {
        "auth" => HeyFailureKind.SignedOut,
        "network" => HeyFailureKind.Network,
        "rate_limit" => HeyFailureKind.RateLimited,
        "usage" or "validation" or "not_found" or "ambiguous" or "forbidden" or "conflict" => HeyFailureKind.Rejected,
        _ => HeyFailureKind.Failed
    };

    private static JsonElement? FirstEntry( JsonElement match ) =>
        match.TryGetProperty( "messages", out var messages ) &&
        messages.ValueKind == JsonValueKind.Array &&
        messages.GetArrayLength( ) > 0 &&
        messages[0].ValueKind == JsonValueKind.Object
            ? messages[0]
            : null;

    // HEY names a sender by the address it sent as when that differs from
    // the contact, the way its own rows do.
    private static string SenderName( JsonElement item )
    {
        var alternative = Clean( ReadString( item, "alternative_sender_name" ), MaximumSenderLength );
        if ( alternative.Length > 0 )
        {
            return alternative;
        }

        if ( !item.TryGetProperty( "creator", out var creator ) || creator.ValueKind != JsonValueKind.Object )
        {
            return "";
        }

        var name = Clean( ReadString( creator, "name" ), MaximumSenderLength );
        return name.Length > 0 ? name : Clean( ReadString( creator, "email_address" ), MaximumSenderLength );
    }

    private static string? SenderAddress( JsonElement item ) =>
        item.TryGetProperty( "creator", out var creator ) && creator.ValueKind == JsonValueKind.Object &&
        Clean( ReadString( creator, "email_address" ), MaximumSenderLength ) is { Length: > 0 } address
            ? address
            : null;

    private static string Subject( string? value ) =>
        Clean( value, MaximumSubjectLength ) is { Length: > 0 } subject ? subject : "(no subject)";

    // A thread opens on HEY's own origin, never a host taken from a payload, the
    // same rule HEY's Omarchy plugin follows for its web actions.
    public static string ThreadUrl( long topicId ) =>
        DefaultWebOrigin + "/topics/" + topicId.ToString( CultureInfo.InvariantCulture );

    private static long? ReadId( JsonElement item, string name ) =>
        item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty( name, out var value ) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64( out var id ) && id > 0
            ? id
            : null;

    private static string? ReadString( JsonElement item, string name ) =>
        item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty( name, out var value ) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString( )
            : null;

    private static DateTimeOffset? ReadTime( JsonElement item, string name ) =>
        DateTimeOffset.TryParse( ReadString( item, name ), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value ) && value.Year > 1
            ? value
            : null;

    // Mail text is shown inert: control and format characters become spaces,
    // runs of whitespace collapse, and the length is bounded.
    internal static string Clean( string? value, int maximumLength )
    {
        if ( string.IsNullOrEmpty( value ) )
        {
            return "";
        }

        StringBuilder builder = new( Math.Min( value.Length, maximumLength ) );
        var pendingSpace = false;
        foreach ( var character in value )
        {
            if ( char.IsWhiteSpace( character ) || char.IsControl( character ) ||
                CharUnicodeInfo.GetUnicodeCategory( character ) == UnicodeCategory.Format )
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            // A space is kept only before another character, so both must fit.
            if ( pendingSpace )
            {
                if ( builder.Length + 2 > maximumLength )
                {
                    break;
                }

                builder.Append( ' ' );
                pendingSpace = false;
            }

            builder.Append( character );
            if ( builder.Length >= maximumLength )
            {
                break;
            }
        }

        // The bound must not cut a surrogate pair in half, nor end on the
        // space before it.
        if ( builder.Length > 0 && char.IsHighSurrogate( builder[^1] ) )
        {
            builder.Length--;
        }

        if ( builder.Length > 0 && builder[^1] == ' ' )
        {
            builder.Length--;
        }

        return builder.ToString( );
    }
}
