namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly (string Query, string Expected)[] InlineArithmeticCases =
    [
        ("100 * 5", "500"), ("100*5", "500"), ("2^10", "1024"), ("(3+4)*2", "14"),
        ("10 / 4", "2.5"), ("(1/2)*8", "4"), ("1/2+1", "1.5"), ("10-4*2", "2"), ("1.5 * 3", "4.5"), ("-2 + 7", "5"), ("10 - 4", "6"),
        ("10 * -2", "-20"), ("10 * - 2", "-20"), ("10 * + 2", "20"), ("10 * - - 2", "20"), ("10 * - (2)", "-20")
    ];

    private static readonly string[] OrdinaryCalculatorSearches =
    [
        "2048", "7zip", "1password", "x264", "4k", "v1.2.3", "1.5", "100%", "555-1234",
        "2026-09-11", "+1-555-1234", "5/5", "1/2", "3/4", "2026/09/11", "-1/2", "-v", "c++", "gaps", "node 20", "100 usd", "100 usd to", "5 km in",
        "5 unknown in miles", "5 km in miles extra", "2 +", "app: 100 * 5", "=oops"
    ];

    private static void InlineCalculatorParserPreservesSearchAsync( )
    {
        foreach ( var query in new[] { "100 * 5", "2 + 2", "-2 + 7", "days until 11 sep" } )
        {
            var parsed = QueryParser.Parse( query );
            True( !parsed.IsCalculator, query );
            True( parsed.Expression.LiteralQuery is null, query );
            True( QueryParser.Parse( "=" + query ).IsCalculator );
        }
        var date = QueryParser.Parse( "days until 11 sep -launch" );
        Equal( "days|until|11|sep|launch", string.Join( '|', date.Expression.Clauses.Select( static clause => clause.Text ) ) );
        True( date.Expression.Clauses[^1].Excluded );
        Equal( 2, QueryParser.Parse( "2 + 2" ).Expression.Clauses.Count );
    }

    private static async Task InlineCalculatorArithmeticAsync( )
    {
        CalculatorProvider provider = new( rates: ( ) => throw new InvalidOperationException( "Arithmetic must stay offline." ) );
        foreach ( var (input, expected) in InlineArithmeticCases )
        {
            foreach ( var prefix in new[] { "", "=" } )
            {
                var query = prefix + input;
                var rows = await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest { Query = query }, default );
                Equal( expected, rows.Single( ).Title );
                Equal( expected, rows.Single( ).Actions[0].Arguments["text"] );
            }
        }
        foreach ( var query in OrdinaryCalculatorSearches.Where( static query => !query.StartsWith( '=' ) )
                     .Concat( ["1 / 0", "2 + (3", "10 * -", "10 * - )", "days until invalid",
                         "file: days until 11 sep", "days until 11 sep -files:", "title:days until tomorrow"] ) )
        {
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest { Query = query }, default )).Count );
        }
    }

    private static async Task DateCalculatorUsesLocalClockAndResolvesYearsAsync( )
    {
        DateCalculatorClock clock = new( new DateTimeOffset( 2026, 10, 21, 12, 0, 0, TimeSpan.Zero ), TimeZoneInfo.Utc );
        CalculatorProvider provider = new( clock: clock, rates: ( ) => throw new InvalidOperationException( "Date counts must stay offline." ) );
        foreach ( var (phrase, title, subtitle, copied) in new[]
        {
            ("days until 11 sep", "325 days", "Today → Sat 11 Sep 2027", "325"),
            ("days until sep 11", "325 days", "Today → Sat 11 Sep 2027", "325"),
            ("days till 11 september 2027", "325 days", "Today → Sat 11 Sep 2027", "325"),
            ("days until 2027-09-11", "325 days", "Today → Sat 11 Sep 2027", "325"),
            ("days since 10 may", "164 days", "Sun 10 May 2026 → Today", "164"),
            ("days from 10 may", "164 days", "Sun 10 May 2026 → Today", "164"),
            ("days from 10 may to sep 19", "132 days", "Sun 10 May 2026 → Sat 19 Sep 2026", "132"),
            ("days between 10 may and 19 sep", "132 days", "Sun 10 May 2026 → Sat 19 Sep 2026", "132"),
            ("weeks until september 11 2027", "46 weeks 3 days", "Today → Sat 11 Sep 2027", "325"),
            ("days until 11 september", "325 days", "Today → Sat 11 Sep 2027", "325"),
            ("DAYS UNTIL 11 SEP 2027", "325 days", "Today → Sat 11 Sep 2027", "325"),
            ("weeks until yesterday", "-1 day", "Today → Tue 20 Oct 2026", "-1"),
            ("weeks since tomorrow", "-1 day", "Thu 22 Oct 2026 → Today", "-1"),
            ("weeks until tomorrow", "1 day", "Today → Thu 22 Oct 2026", "1"),
            ("weeks since yesterday", "1 day", "Tue 20 Oct 2026 → Today", "1"),
            ("weeks until 13 oct 2026", "-1 week 1 day", "Today → Tue 13 Oct 2026", "-8"),
            ("weeks until 14 oct 2026", "-1 week 0 days", "Today → Wed 14 Oct 2026", "-7"),
            ("weeks until 28 oct 2026", "1 week 0 days", "Today → Wed 28 Oct 2026", "7"),
            ("weeks until 29 oct 2026", "1 week 1 day", "Today → Thu 29 Oct 2026", "8"),
            ("weeks until today", "0 days (today)", "Today → Today", "0"),
            ("days until today", "0 days (today)", "Today → Today", "0"),
            ("days until tomorrow", "1 day", "Today → Thu 22 Oct 2026", "1"),
            ("days since yesterday", "1 day", "Tue 20 Oct 2026 → Today", "1"),
            ("days since 11 dec", "314 days", "Thu 11 Dec 2025 → Today", "314"),
            ("days from 10 dec to 10 jan", "31 days", "Wed 10 Dec 2025 → Sat 10 Jan 2026", "31"),
            ("days until 29 feb", "496 days", "Today → Tue 29 Feb 2028", "496"),
            ("days since 29 feb", "965 days", "Thu 29 Feb 2024 → Today", "965")
        } )
        {
            foreach ( var prefix in new[] { "", "=" } )
            {
                var query = prefix + phrase;
                var parsed = QueryParser.Parse( query );
                var rows = await provider.SearchAsync( parsed, new SearchRequest { Query = query }, default );
                True( rows.Count == 1, query );
                var row = rows[0];
                Equal( title, row.Title );
                Equal( subtitle, row.Subtitle );
                Equal( copied, row.Actions[0].Arguments["text"] );
            }
        }
        foreach ( var query in new[] { "days until 31 apr", "days until 29 feb 2027", "days between 10 may", "days until", "days from tomorrow to", "days until 2027-02-30" } )
        {
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest( ), default )).Count );
        }

        clock.Now = new DateTimeOffset( 2026, 12, 31, 23, 30, 0, TimeSpan.Zero );
        clock.Zone = TimeZoneInfo.CreateCustomTimeZone( "spec-local", TimeSpan.FromHours( 2 ), "spec-local", "spec-local" );
        var local = (await provider.SearchAsync( QueryParser.Parse( "days until 1 jan" ), new SearchRequest( ), default )).Single( );
        Equal( "0 days (today)", local.Title );
        clock.Zone = TimeZoneInfo.Utc;
        Equal( "1 day", (await provider.SearchAsync( QueryParser.Parse( "days until 1 jan" ), new SearchRequest( ), default )).Single( ).Title );
        clock.Now = new DateTimeOffset( 2028, 2, 29, 12, 0, 0, TimeSpan.Zero );
        Equal( "0 days (today)", (await provider.SearchAsync( QueryParser.Parse( "days until 29 feb" ), new SearchRequest( ), default )).Single( ).Title );
    }

    private sealed class DateCalculatorClock( DateTimeOffset now, TimeZoneInfo zone ) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public TimeZoneInfo Zone { get; set; } = zone;
        public override DateTimeOffset GetUtcNow( ) => Now;
        public override TimeZoneInfo LocalTimeZone => Zone;
    }
}
