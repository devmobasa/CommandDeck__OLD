namespace CommandDeck.Specs;

public static partial class Program
{
    private static TimezoneProvider TimezoneAt( DateTimeOffset now ) =>
        new( available: ( ) => true, clock: new FixedTimeProvider( now ) );

    private static async Task TimezoneProviderReturnsCurrentTimeThroughFilterAsync( )
    {
        DateTimeOffset now = new( 2026, 9, 3, 12, 0, 0, TimeSpan.Zero );
        var provider = TimezoneAt( now );

        var activating = QueryParser.Parse( "tz: tokyo" );
        True( activating.IncludedFilters.Any( filter => filter.SourceId == SourceIds.Timezone ),
            "the tz: token targets the timezone source" );

        var results = await provider.SearchAsync(
            activating, new SearchRequest { Query = "", Limit = 20 }, default );
        Equal( 1, results.Count );
        var city = results[0];
        Equal( SourceIds.Timezone, city.SourceId );
        Equal( "timezone:Asia/Tokyo", city.Id );
        Equal( ResultKinds.Timezone, city.Kind );
        True( city.Title.Contains( "Tokyo", StringComparison.Ordinal ), "title names the city" );
        True( city.Title.Contains( "9:00 PM", StringComparison.Ordinal ),
            "Tokyo is nine hours ahead of noon UTC" );
        True( city.Subtitle!.Contains( "UTC+09:00", StringComparison.Ordinal ),
            "the subtitle shows the UTC offset" );
        True( city.DetailMarkdown!.Contains( "Daylight saving: no", StringComparison.Ordinal ),
            "Tokyo observes no daylight saving" );
        Equal( ActionKinds.CopyText, city.Actions[0].Kind );
        True( city.Actions[0].Arguments["text"].Contains( "9:00 PM", StringComparison.Ordinal ),
            "the copy action carries the formatted time" );

        // The time: alias resolves to the same source.
        var viaAlias = await provider.SearchAsync(
            QueryParser.Parse( "time: tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default );
        Equal( 1, viaAlias.Count );

        // Filter-gated: an untargeted query does no work and returns nothing.
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( "tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );

        // Absent database (injected) yields nothing without touching the clock.
        TimezoneProvider unavailable = new( available: ( ) => false );
        Equal( 0, (await unavailable.SearchAsync(
            activating, new SearchRequest { Query = "", Limit = 20 }, default )).Count );

        await AssertProviderDiscoveryContractAsync(
            ( _, available ) => new TimezoneProvider( available ),
            activating,
            QueryParser.Parse( "tokyo" ),
            listingJson: string.Empty );
    }

    private static async Task TimezoneProviderConvertsAcrossZonesAsync( )
    {
        // 1 July 2026: Winnipeg is on Central Daylight Time (UTC-5).
        var provider = TimezoneAt( new DateTimeOffset( 2026, 7, 1, 12, 0, 0, TimeSpan.Zero ) );

        var results = await provider.SearchAsync(
            QueryParser.Parse( "tz: 9am winnipeg to tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default );
        Equal( 1, results.Count );
        var conversion = results[0];
        Equal( SourceIds.Timezone, conversion.SourceId );
        Equal( "timezone:America/Winnipeg->Asia/Tokyo", conversion.Id );
        True( conversion.Title.Contains( "Winnipeg 9:00 AM", StringComparison.Ordinal ),
            "the source local time is shown" );
        True( conversion.Title.Contains( "Tokyo 11:00 PM", StringComparison.Ordinal ),
            "9am CDT converts to 11pm the same day in Tokyo" );
        True( conversion.Subtitle!.Contains( "Winnipeg UTC-05:00 DST", StringComparison.Ordinal ),
            "the summer source offset and DST state are shown" );
        True( conversion.Subtitle!.Contains( "Tokyo UTC+09:00", StringComparison.Ordinal ),
            "the target offset is shown" );
        Equal( "9:00 AM Winnipeg = 11:00 PM Tokyo", conversion.Actions[0].Arguments["text"] );

        // Case-insensitive tokens and a two-token time ("9 am") both parse.
        Equal( 1, (await provider.SearchAsync(
            QueryParser.Parse( "tz: 9AM Winnipeg to Tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );
        Equal( 1, (await provider.SearchAsync(
            QueryParser.Parse( "tz: 9 am winnipeg to tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );

        // A conversion with no explicit time falls back to the current time.
        Equal( 1, (await provider.SearchAsync(
            QueryParser.Parse( "tz: london to tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );
    }

    private static async Task TimezoneProviderHonorsDaylightSavingAsync( )
    {
        // Same city, same wall query, opposite sides of the US DST boundary.
        var summer = TimezoneAt( new DateTimeOffset( 2026, 7, 1, 16, 0, 0, TimeSpan.Zero ) );
        var summerCity = (await summer.SearchAsync(
            QueryParser.Parse( "tz: new york" ),
            new SearchRequest { Query = "", Limit = 20 }, default ))[0];
        True( summerCity.Subtitle!.Contains( "UTC-04:00", StringComparison.Ordinal ),
            "New York is UTC-4 while daylight saving is in effect" );
        True( summerCity.Subtitle!.Contains( "DST", StringComparison.Ordinal ),
            "the summer result flags DST" );
        True( summerCity.DetailMarkdown!.Contains( "Daylight saving: yes", StringComparison.Ordinal ),
            "the detail confirms DST is active" );

        var winter = TimezoneAt( new DateTimeOffset( 2026, 1, 15, 16, 0, 0, TimeSpan.Zero ) );
        var winterCity = (await winter.SearchAsync(
            QueryParser.Parse( "tz: new york" ),
            new SearchRequest { Query = "", Limit = 20 }, default ))[0];
        True( winterCity.Subtitle!.Contains( "UTC-05:00", StringComparison.Ordinal ),
            "New York returns to UTC-5 in standard time" );
        True( !winterCity.Subtitle!.Contains( "DST", StringComparison.Ordinal ),
            "the winter result does not flag DST" );
        True( winterCity.DetailMarkdown!.Contains( "Daylight saving: no", StringComparison.Ordinal ),
            "the detail confirms DST is inactive" );

        // A conversion that crosses midnight when the source leaves DST: 9am CST
        // (UTC-6) lands at 12:00 AM the following day in Tokyo.
        var crossing = (await winter.SearchAsync(
            QueryParser.Parse( "tz: 9am winnipeg to tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default ))[0];
        True( crossing.Subtitle!.Contains( "Winnipeg UTC-06:00", StringComparison.Ordinal ),
            "Winnipeg is UTC-6 in standard time" );
        True( crossing.Title.Contains( "Tokyo 12:00 AM (+1 day)", StringComparison.Ordinal ),
            "the standard-time conversion rolls over to the next day" );
    }

    private static async Task TimezoneProviderIgnoresUnknownAndStaysOffHomeAsync( )
    {
        var provider = TimezoneAt( new DateTimeOffset( 2026, 9, 3, 12, 0, 0, TimeSpan.Zero ) );

        // Unknown cities yield no result, whether alone or in a conversion.
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( "tz: atlantis" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( "tz: 9am atlantis to tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( "tz: 9am winnipeg to atlantis" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );

        // A query without the filter never targets the provider.
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( "9am winnipeg to tokyo" ),
            new SearchRequest { Query = "", Limit = 20 }, default )).Count );

        // Both tokens resolve to the timezone source in the filter registry.
        Equal( SourceIds.Timezone, SearchFilterRegistry.FindByToken( "tz" )!.SourceId );
        Equal( SourceIds.Timezone, SearchFilterRegistry.FindByToken( "time" )!.SourceId );

        // Off bare Home, on in search, by default.
        var preference = LauncherPreferences.CreateDefault( ).Sources
            .Single( source => source.SourceId == SourceIds.Timezone );
        True( preference.EnabledInSearch, "timezone participates in filtered search" );
        True( !preference.EnabledOnHome, "timezone is off the bare Home surface" );
    }
}
