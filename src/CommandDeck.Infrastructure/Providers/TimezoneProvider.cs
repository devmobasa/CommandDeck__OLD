using System.Globalization;
using System.Text;

namespace CommandDeck;

/// <summary>
/// World-clock and timezone conversion, computed entirely in-process from the
/// system IANA tz database via <see cref="TimeZoneInfo"/> (no subprocess, no
/// network). Like the other optional providers it only answers when the query
/// targets it through the <c>tz:</c>/<c>time:</c> filter, so an ordinary
/// keystroke does no work. Two shapes are understood:
/// <list type="bullet">
///   <item><c>tz: &lt;city&gt;</c> - the current local time there, with its UTC
///   offset and daylight-saving state.</item>
///   <item><c>tz: &lt;time&gt; &lt;city A&gt; to &lt;city B&gt;</c> - convert
///   that wall-clock time from A to B (DST-aware). The time is optional and
///   defaults to the current time in A.</item>
/// </list>
/// A curated city -> IANA-zone alias table is shipped as static data (a
/// <see cref="Dictionary{TKey,TValue}"/>, AOT-safe: no reflection). An unknown
/// city, or a zone the tz database cannot resolve, yields no result.
/// </summary>
public sealed class TimezoneProvider : ICommandProvider
{
    private const string ConversionSeparator = " to ";

    private static readonly Lazy<bool> DatabaseAvailable = new( static ( ) =>
    {
        try
        {
            return TimeZoneInfo.GetSystemTimeZones( ).Count > 0;
        }
        catch ( Exception exception ) when (
            exception is InvalidTimeZoneException or TimeZoneNotFoundException or IOException )
        {
            return false;
        }
    } );

    private readonly Func<bool> _available;
    private readonly TimeProvider _clock;

    public TimezoneProvider( Func<bool>? available = null, TimeProvider? clock = null )
    {
        _available = available ?? (static ( ) => DatabaseAvailable.Value);
        _clock = clock ?? TimeProvider.System;
    }

    public string Id => ProviderIds.Timezone;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );

        // Filter-activated only: no result, and no work, unless the user typed
        // tz:/time:.
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Timezone ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }
        if ( !_available( ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        var result = Compute( query.Text );
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( result is null ? [] : [result] );
    }

    private SearchResult? Compute( string text )
    {
        var trimmed = text.Trim( );
        if ( trimmed.Length == 0 )
        {
            return null;
        }

        var separator = trimmed.IndexOf( ConversionSeparator, StringComparison.OrdinalIgnoreCase );
        return separator >= 0
            ? BuildConversion(
                trimmed[..separator],
                trimmed[(separator + ConversionSeparator.Length)..] )
            : BuildCurrentTime( trimmed );
    }

    private SearchResult? BuildCurrentTime( string cityText )
    {
        if ( !TryResolveCity( cityText, out var city, out var zone ) )
        {
            return null;
        }

        var utcNow = _clock.GetUtcNow( );
        DateTimeOffset local;
        TimeSpan offset;
        bool isDst;
        try
        {
            local = TimeZoneInfo.ConvertTime( utcNow, zone );
            offset = zone.GetUtcOffset( utcNow );
            isDst = zone.IsDaylightSavingTime( utcNow );
        }
        catch ( Exception exception ) when (
            exception is InvalidTimeZoneException or ArgumentException )
        {
            return null;
        }

        var time = FormatTime( local.DateTime );
        var date = FormatDate( local.DateTime );
        var offsetText = FormatOffset( offset );
        var dstBadge = isDst ? " · DST" : string.Empty;
        var copy = $"{time} {city.Display} ({offsetText}{(isDst ? " DST" : string.Empty)})";

        StringBuilder detail = new( );
        detail.Append( "**" ).Append( city.Display ).Append( "**\n\n" );
        detail.Append( "- Local time: " ).Append( time ).Append( '\n' );
        detail.Append( "- Date: " ).Append( date ).Append( '\n' );
        detail.Append( "- Zone: " ).Append( city.Iana ).Append( '\n' );
        detail.Append( "- UTC offset: " ).Append( offsetText ).Append( '\n' );
        detail.Append( "- Daylight saving: " ).Append( isDst ? "yes" : "no" ).Append( '\n' );

        return BuildResult(
            id: $"timezone:{city.Iana}",
            title: $"{city.Display} · {time}",
            subtitle: $"{offsetText}{dstBadge} · {date}",
            detail: detail.ToString( ),
            copyText: copy );
    }

    private SearchResult? BuildConversion( string leftText, string rightText )
    {
        (var timeOfDay, var sourceCity) = SplitLeadingTime( leftText );
        if ( !TryResolveCity( sourceCity, out var from, out var fromZone ) ||
            !TryResolveCity( rightText, out var to, out var toZone ) )
        {
            return null;
        }

        DateTime sourceLocal;
        DateTime targetLocal;
        TimeSpan fromOffset;
        TimeSpan toOffset;
        bool fromDst;
        bool toDst;
        try
        {
            var nowInSource = TimeZoneInfo.ConvertTime( _clock.GetUtcNow( ), fromZone );
            sourceLocal = timeOfDay is TimeSpan tod
                ? DateTime.SpecifyKind( nowInSource.Date + tod, DateTimeKind.Unspecified )
                : DateTime.SpecifyKind( nowInSource.DateTime, DateTimeKind.Unspecified );
            if ( fromZone.IsInvalidTime( sourceLocal ) )
            {
                return null;
            }
            targetLocal = TimeZoneInfo.ConvertTime( sourceLocal, fromZone, toZone );
            fromOffset = fromZone.GetUtcOffset( sourceLocal );
            toOffset = toZone.GetUtcOffset( targetLocal );
            fromDst = fromZone.IsDaylightSavingTime( sourceLocal );
            toDst = toZone.IsDaylightSavingTime( targetLocal );
        }
        catch ( Exception exception ) when (
            exception is InvalidTimeZoneException or ArgumentException )
        {
            return null;
        }

        var sourceTime = FormatTime( sourceLocal );
        var targetTime = FormatTime( targetLocal );
        var fromOffsetText = FormatOffset( fromOffset );
        var toOffsetText = FormatOffset( toOffset );
        var dayMarker = DayMarker( (targetLocal.Date - sourceLocal.Date).Days );

        var copy =
            $"{sourceTime} {from.Display} = {targetTime}{dayMarker} {to.Display}";

        StringBuilder detail = new( );
        detail.Append( "**" ).Append( from.Display ).Append( " → " ).Append( to.Display ).Append( "**\n\n" );
        detail.Append( "- " ).Append( from.Display ).Append( ": " ).Append( sourceTime )
            .Append( " · " ).Append( fromOffsetText ).Append( fromDst ? " · DST" : string.Empty )
            .Append( " · " ).Append( FormatDate( sourceLocal ) ).Append( '\n' );
        detail.Append( "- " ).Append( to.Display ).Append( ": " ).Append( targetTime )
            .Append( " · " ).Append( toOffsetText ).Append( toDst ? " · DST" : string.Empty )
            .Append( " · " ).Append( FormatDate( targetLocal ) ).Append( '\n' );

        return BuildResult(
            id: $"timezone:{from.Iana}->{to.Iana}",
            title: $"{from.Display} {sourceTime} → {to.Display} {targetTime}{dayMarker}",
            subtitle:
                $"{from.Display} {fromOffsetText}{(fromDst ? " DST" : string.Empty)} · " +
                $"{to.Display} {toOffsetText}{(toDst ? " DST" : string.Empty)}",
            detail: detail.ToString( ),
            copyText: copy );
    }

    private static SearchResult BuildResult(
        string id,
        string title,
        string subtitle,
        string detail,
        string copyText ) => new( )
        {
            Id = id,
            Kind = ResultKinds.Timezone,
            Title = title,
            Subtitle = subtitle,
            Icon = "preferences-system-time",
            ProviderId = ProviderIds.Timezone,
            SourceId = SourceIds.Timezone,
            Score = 1,
            DetailMarkdown = detail,
            Section = new ResultSection { Id = "timezone", Title = "Timezone", Order = 7 },
            Actions =
            [
                new CommandAction
                {
                    Id = "copy-time",
                    Title = "Copy time",
                    Kind = ActionKinds.CopyText,
                    Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = copyText
                    }
                }
            ]
        };

    // Consume the leading token(s) of the left side as a time-of-day, returning
    // the remainder as the source city. "9am winnipeg" -> (09:00, "winnipeg");
    // "9 am winnipeg" -> (09:00, "winnipeg"); "london" -> (null, "london").
    private static (TimeSpan? Time, string City) SplitLeadingTime( string left )
    {
        var parts = left.Trim( ).Split(
            ( char[]? ) null, StringSplitOptions.RemoveEmptyEntries );
        if ( parts.Length == 0 )
        {
            return (null, string.Empty);
        }
        // A bare number followed by a meridiem ("9 am") is one time spanning two
        // tokens; join them before the greedier single-token parse claims "9".
        if ( parts.Length >= 2 &&
            (parts[1].Equals( "am", StringComparison.OrdinalIgnoreCase ) ||
                parts[1].Equals( "pm", StringComparison.OrdinalIgnoreCase )) &&
            TryParseTime( parts[0] + parts[1], out var pair ) )
        {
            return (pair, string.Join( ' ', parts[2..] ));
        }
        if ( TryParseTime( parts[0], out var first ) )
        {
            return (first, string.Join( ' ', parts[1..] ));
        }
        return (null, left);
    }

    private static bool TryParseTime( string token, out TimeSpan time )
    {
        time = default;
        var value = token.Trim( ).ToLowerInvariant( );
        if ( value.Length == 0 )
        {
            return false;
        }
        if ( value == "noon" )
        {
            time = new TimeSpan( 12, 0, 0 );
            return true;
        }
        if ( value == "midnight" )
        {
            time = TimeSpan.Zero;
            return true;
        }

        var am = false;
        var pm = false;
        if ( value.EndsWith( "am", StringComparison.Ordinal ) )
        {
            am = true;
            value = value[..^2].Trim( );
        }
        else if ( value.EndsWith( "pm", StringComparison.Ordinal ) )
        {
            pm = true;
            value = value[..^2].Trim( );
        }
        if ( value.Length == 0 )
        {
            return false;
        }

        int hour;
        var minute = 0;
        var colon = value.IndexOf( ':', StringComparison.Ordinal );
        if ( colon >= 0 )
        {
            var mm = value[(colon + 1)..];
            if ( !int.TryParse( value[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out hour ) ||
                mm.Length is not (1 or 2) ||
                !int.TryParse( mm, NumberStyles.None, CultureInfo.InvariantCulture, out minute ) )
            {
                return false;
            }
        }
        else if ( !int.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out hour ) )
        {
            return false;
        }

        if ( minute is < 0 or > 59 )
        {
            return false;
        }
        if ( am || pm )
        {
            if ( hour is < 1 or > 12 )
            {
                return false;
            }
            if ( hour == 12 )
            {
                hour = 0;
            }
            if ( pm )
            {
                hour += 12;
            }
        }
        else if ( hour is < 0 or > 23 )
        {
            return false;
        }

        time = new TimeSpan( hour, minute, 0 );
        return true;
    }

    private static bool TryResolveCity( string cityText, out CityZone city, out TimeZoneInfo zone )
    {
        city = default;
        zone = default!;
        var key = Normalize( cityText );
        if ( key.Length == 0 || !CityAliases.TryGetValue( key, out city ) )
        {
            return false;
        }
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById( city.Iana );
            return true;
        }
        catch ( Exception exception ) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException or IOException )
        {
            return false;
        }
    }

    private static string Normalize( string value ) => string.Join(
        ' ',
        value.Trim( ).ToLowerInvariant( ).Split(
            ( char[]? ) null, StringSplitOptions.RemoveEmptyEntries ) );

    private static string FormatTime( DateTime value ) =>
        value.ToString( "h:mm tt", CultureInfo.InvariantCulture );

    private static string FormatDate( DateTime value ) =>
        value.ToString( "ddd, MMM d, yyyy", CultureInfo.InvariantCulture );

    private static string FormatOffset( TimeSpan offset )
    {
        if ( offset == TimeSpan.Zero )
        {
            return "UTC";
        }
        var magnitude = offset.Duration( );
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        return string.Create(
            CultureInfo.InvariantCulture,
            $"UTC{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}" );
    }

    private static string DayMarker( int dayDelta ) => dayDelta switch
    {
        > 0 => $" (+{dayDelta} day{(dayDelta > 1 ? "s" : string.Empty)})",
        < 0 => $" ({dayDelta} day{(dayDelta < -1 ? "s" : string.Empty)})",
        _ => string.Empty
    };

    private readonly record struct CityZone( string Display, string Iana );

    // (IANA id, display name, alias tokens). Several display names may share one
    // zone (Seattle and Los Angeles are both America/Los_Angeles). The display
    // name is itself a lookup key, so it need not be repeated in the aliases.
    private static readonly (string Iana, string Display, string[] Aliases)[] CityData =
    [
        // North America
        ("America/New_York", "New York", ["nyc", "new york city", "ny"]),
        ("America/New_York", "Boston", []),
        ("America/New_York", "Washington", ["washington dc", "dc"]),
        ("America/New_York", "Philadelphia", ["philly"]),
        ("America/New_York", "Atlanta", []),
        ("America/New_York", "Miami", []),
        ("America/Toronto", "Toronto", ["montreal", "ottawa"]),
        ("America/Chicago", "Chicago", []),
        ("America/Chicago", "Dallas", []),
        ("America/Chicago", "Houston", []),
        ("America/Chicago", "Austin", []),
        ("America/Winnipeg", "Winnipeg", []),
        ("America/Denver", "Denver", ["salt lake city"]),
        ("America/Phoenix", "Phoenix", []),
        ("America/Los_Angeles", "Los Angeles", ["la"]),
        ("America/Los_Angeles", "San Francisco", ["sf", "san fran"]),
        ("America/Los_Angeles", "Seattle", []),
        ("America/Los_Angeles", "Las Vegas", ["vegas"]),
        ("America/Los_Angeles", "Portland", []),
        ("America/Vancouver", "Vancouver", []),
        ("America/Edmonton", "Calgary", ["edmonton"]),
        ("America/Anchorage", "Anchorage", ["alaska"]),
        ("Pacific/Honolulu", "Honolulu", ["hawaii"]),
        ("America/Mexico_City", "Mexico City", ["mexico"]),
        // South America
        ("America/Sao_Paulo", "Sao Paulo", ["são paulo"]),
        ("America/Sao_Paulo", "Rio de Janeiro", ["rio"]),
        ("America/Argentina/Buenos_Aires", "Buenos Aires", ["argentina"]),
        ("America/Santiago", "Santiago", ["chile"]),
        ("America/Lima", "Lima", ["peru"]),
        ("America/Bogota", "Bogota", ["bogotá", "colombia"]),
        // Europe
        ("Europe/London", "London", ["uk", "britain", "england"]),
        ("Europe/Dublin", "Dublin", ["ireland"]),
        ("Europe/Lisbon", "Lisbon", ["portugal"]),
        ("Europe/Paris", "Paris", ["france"]),
        ("Europe/Madrid", "Madrid", ["spain", "barcelona"]),
        ("Europe/Berlin", "Berlin", ["germany", "munich", "frankfurt", "hamburg"]),
        ("Europe/Amsterdam", "Amsterdam", ["netherlands"]),
        ("Europe/Brussels", "Brussels", ["belgium"]),
        ("Europe/Zurich", "Zurich", ["zürich", "geneva", "switzerland"]),
        ("Europe/Rome", "Rome", ["italy", "milan"]),
        ("Europe/Vienna", "Vienna", ["austria"]),
        ("Europe/Stockholm", "Stockholm", ["sweden"]),
        ("Europe/Oslo", "Oslo", ["norway"]),
        ("Europe/Copenhagen", "Copenhagen", ["denmark"]),
        ("Europe/Helsinki", "Helsinki", ["finland"]),
        ("Europe/Warsaw", "Warsaw", ["poland"]),
        ("Europe/Prague", "Prague", ["czechia"]),
        ("Europe/Budapest", "Budapest", ["hungary"]),
        ("Europe/Athens", "Athens", ["greece"]),
        ("Europe/Istanbul", "Istanbul", ["turkey", "ankara"]),
        ("Europe/Moscow", "Moscow", ["russia"]),
        ("Europe/Kyiv", "Kyiv", ["kiev", "ukraine"]),
        ("Atlantic/Reykjavik", "Reykjavik", ["iceland"]),
        // Africa and the Middle East
        ("Africa/Cairo", "Cairo", ["egypt"]),
        ("Africa/Lagos", "Lagos", ["nigeria"]),
        ("Africa/Nairobi", "Nairobi", ["kenya"]),
        ("Africa/Johannesburg", "Johannesburg", ["south africa", "cape town"]),
        ("Africa/Casablanca", "Casablanca", ["morocco"]),
        ("Asia/Dubai", "Dubai", ["uae", "abu dhabi"]),
        ("Asia/Qatar", "Doha", ["qatar"]),
        ("Asia/Riyadh", "Riyadh", ["saudi arabia"]),
        ("Asia/Tehran", "Tehran", ["iran"]),
        ("Asia/Jerusalem", "Jerusalem", ["tel aviv", "israel"]),
        ("Asia/Baghdad", "Baghdad", ["iraq"]),
        // Asia
        ("Asia/Karachi", "Karachi", ["pakistan", "islamabad", "lahore"]),
        ("Asia/Kolkata", "Mumbai", ["bombay"]),
        ("Asia/Kolkata", "New Delhi", ["delhi", "india"]),
        ("Asia/Kolkata", "Bengaluru", ["bangalore"]),
        ("Asia/Kolkata", "Kolkata", ["calcutta"]),
        ("Asia/Kathmandu", "Kathmandu", ["nepal"]),
        ("Asia/Dhaka", "Dhaka", ["bangladesh"]),
        ("Asia/Colombo", "Colombo", ["sri lanka"]),
        ("Asia/Bangkok", "Bangkok", ["thailand"]),
        ("Asia/Jakarta", "Jakarta", ["indonesia"]),
        ("Asia/Ho_Chi_Minh", "Ho Chi Minh City", ["ho chi minh", "saigon", "hanoi", "vietnam"]),
        ("Asia/Kuala_Lumpur", "Kuala Lumpur", ["kl", "malaysia"]),
        ("Asia/Singapore", "Singapore", []),
        ("Asia/Manila", "Manila", ["philippines"]),
        ("Asia/Hong_Kong", "Hong Kong", ["hk"]),
        ("Asia/Shanghai", "Shanghai", ["beijing", "china"]),
        ("Asia/Taipei", "Taipei", ["taiwan"]),
        ("Asia/Seoul", "Seoul", ["korea", "south korea"]),
        ("Asia/Tokyo", "Tokyo", ["japan", "osaka", "kyoto"]),
        ("Asia/Ulaanbaatar", "Ulaanbaatar", ["mongolia"]),
        ("Asia/Almaty", "Almaty", ["kazakhstan"]),
        ("Asia/Tashkent", "Tashkent", ["uzbekistan"]),
        // Oceania
        ("Australia/Sydney", "Sydney", []),
        ("Australia/Melbourne", "Melbourne", []),
        ("Australia/Brisbane", "Brisbane", []),
        ("Australia/Perth", "Perth", []),
        ("Australia/Adelaide", "Adelaide", []),
        ("Pacific/Auckland", "Auckland", ["new zealand", "wellington"]),
        ("Pacific/Fiji", "Suva", ["fiji"]),
        // Reference
        ("UTC", "UTC", ["gmt", "zulu"])
    ];

    private static readonly Dictionary<string, CityZone> CityAliases = BuildAliases( );

    private static Dictionary<string, CityZone> BuildAliases( )
    {
        Dictionary<string, CityZone> map = new( StringComparer.Ordinal );
        foreach ( (var iana, var display, var aliases) in CityData )
        {
            CityZone zone = new( display, iana );
            map[Normalize( display )] = zone;
            foreach ( var alias in aliases )
            {
                map[Normalize( alias )] = zone;
            }
        }
        return map;
    }
}
