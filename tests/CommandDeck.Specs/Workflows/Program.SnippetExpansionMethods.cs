using System.Globalization;
using System.Text.RegularExpressions;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void SnippetDatesUseTheClockFormatsLocalesAndOffsets( )
    {
        // Local time is Saturday 2026-03-28 23:30 (+01:00); summer time starts that night at 02:00.
        SnippetClock clock = new( new DateTimeOffset( 2026, 3, 28, 22, 30, 0, TimeSpan.Zero ), SnippetDaylightZone( ) );
        string Expand( string text ) => SnippetTemplate.Expand( text, NoSnippetArguments, clock );
        const string zoned = " format=\"yyyy-MM-dd HH:mm zzz\"}";

        Equal( "2026-03-28|23:30|2026-03-28 23:30|Saturday", Expand( "{date}|{time}|{datetime}|{day}" ) );
        Equal( "28.03.2026 23:30:00 +01:00", Expand( "{datetime format=\"dd.MM.yyyy HH:mm:ss zzz\"}" ) );
        Equal( "23:30:00|Sat", Expand( "{time format=\"HH:mm:ss\"}|{day format=ddd}" ) );

        // Days, months and years keep the wall-clock time; minutes and hours are elapsed time.
        Equal( "2026-03-29 23:30 +02:00", Expand( "{datetime offset=\"+1d\"" + zoned ) );
        Equal( "2026-03-30 00:30 +02:00", Expand( "{datetime offset=\"+24h\"" + zoned ) );
        Equal( "2026-03-29 21:30 +02:00", Expand( "{datetime offset=\"+1d -2h\"" + zoned ) );
        Equal( "2026-02-28 23:30 +01:00", Expand( "{datetime offset=-1M" + zoned ) );
        Equal( "2027-03-28 23:30 +02:00", Expand( "{datetime offset=+1y" + zoned ) );
        Equal( "2026-03-29 Sunday", Expand( "{date offset=\"+30m\"} {day offset=+30m}" ) );
        Equal( "2026-03-29", Expand( "{date offset='+1d   +0m'}" ) );

        // Steps apply in order, so month-end clamping depends on it.
        clock.UtcNow = new DateTimeOffset( 2026, 1, 31, 11, 0, 0, TimeSpan.Zero );
        Equal( "2026-02-27|2026-02-28", Expand( "{date offset=\"+1M -1d\"}|{date offset=\"-1d +1M\"}" ) );

        // A step into the skipped hour lands on the equivalent instant after the change.
        clock.UtcNow = new DateTimeOffset( 2026, 3, 28, 1, 30, 0, TimeSpan.Zero );
        Equal( "2026-03-29 03:30 +02:00", Expand( "{datetime offset=+1d" + zoned ) );

        var outOfRange = SnippetFailure( ( ) => Expand( "{date offset=\"+999999y\"}" ) );
        Equal( "A snippet date offset is out of range.", outOfRange );

        // Formatting is invariant unless a locale is named, whatever the daemon's culture is.
        clock.UtcNow = new DateTimeOffset( 2026, 3, 28, 22, 30, 0, TimeSpan.Zero );
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "th-TH" );
            Equal( "2026-03-28 Saturday 03/28/2026", Expand( "{date} {day} {date format=d}" ) );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Equal( "Samstag, 28. März 2026", Expand( "{date locale=\"de-DE\" format=\"dddd, d. MMMM yyyy\"}" ) );
        Equal( "samedi", Expand( "{day locale=fr-FR}" ) );
    }

    private static void SnippetUuidsAreFreshVersionFourValues( )
    {
        SnippetClock clock = new( DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc );
        var template = SnippetTemplate.Compile( "{uuid} {uuid} {uuid | uppercase}" );
        Equal( 0, template.Inputs.Count );
        var first = template.Expand( NoSnippetArguments, clock ).Split( ' ' );
        var second = template.Expand( NoSnippetArguments, clock ).Split( ' ' );
        Regex version4 = new( "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant );
        True( version4.IsMatch( first[0] ) && version4.IsMatch( first[1] ) && version4.IsMatch( first[2].ToLowerInvariant( ) ) );
        Equal( first[2].ToUpperInvariant( ), first[2] );
        Equal( 6, first.Concat( second ).Select( static value => value.ToLowerInvariant( ) ).Distinct( ).Count( ) );
    }

    private static async Task SnippetInputDefaultsReachPreviewAndCopyAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        const string template = "Review {topic default=\"review\"} for {owner}: {topic | uppercase} by {date offset=\"+1d\"}";
        SnippetProvider provider = new( [new( ) { Id = "review", Title = "Review prompt", Text = template }] );
        var rows = await provider.SearchAsync( QueryParser.Parse( "snip: review" ), new SearchRequest { Query = "snip: review" }, default );
        var row = rows.Single( );
        var preview = row.Actions.Single( static action => action.Kind == ActionKinds.SnippetPreview );
        Equal( "topic=review,owner=", string.Join( ',', preview.Inputs.Select( static input => input.Name + "=" + input.DefaultValue ) ) );
        True( !PrivateSources.IsPrivate( row ), "snippets without history placeholders stay ordinary rows" );
        Equal( "Enter a value for owner.", SnippetFailure( ( ) => ActionInputValues.Bind( preview, null ) ) );

        SnippetClock clock = new( new DateTimeOffset( 2026, 9, 6, 14, 30, 0, TimeSpan.Zero ), TimeZoneInfo.Utc );
        LinuxActionExecutor executor = new( timeProvider: clock );
        var bound = ActionInputValues.Bind( preview, new Dictionary<string, string> { ["owner"] = "Ada" } );
        var previewed = await executor.ExecuteAsync( row, bound, "", default );
        True( previewed.Succeeded, previewed.Message );
        Equal( "Review review for Ada: REVIEW by 2026-09-07", previewed.DetailMarkdown );

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-action-payload-", preservePrefix: true ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        var originalFixture = Environment.GetEnvironmentVariable( ActionPayloadFixtureDirectory );
        try
        {
            File.CreateSymbolicLink( Path.Combine( root, "wl-copy" ), Environment.ProcessPath! );
            Environment.SetEnvironmentVariable( "PATH", root );
            Environment.SetEnvironmentVariable( ActionPayloadFixtureDirectory, root );
            var copy = row.Actions.Single( static action => action.Kind == ActionKinds.SnippetCopy );
            var copied = await executor.ExecuteAsync( row, ActionInputValues.Bind( copy, new Dictionary<string, string>
            {
                ["topic"] = "{owner} api",
                ["owner"] = "Lin"
            } ), "", default );
            True( copied.Succeeded, copied.Message );
            Equal( "Review {owner} api for Lin: {OWNER} API by 2026-09-07", await File.ReadAllTextAsync( Path.Combine( root, "stdin" ) ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Environment.SetEnvironmentVariable( ActionPayloadFixtureDirectory, originalFixture );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task SnippetClipboardHistoryOffsetsArePrivateAndNeedConsentAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-snippet-history-" ).FullName;
        try
        {
            SnippetClock clock = new( new DateTimeOffset( 2026, 9, 6, 14, 30, 0, TimeSpan.Zero ), TimeZoneInfo.Utc );
            using ClipboardHistoryStore store = new( Path.Combine( directory, "history.db" ),
                new ClipboardHistoryOptions { MaximumEntries = 10, RetentionHours = 1 }, clock );
            BoundedProcessRunner live = ( _, _, _, _, _ ) => ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "live text",
                StandardError = string.Empty
            } );
            LinuxActionExecutor executor = new( clipboardHistory: new( store ), snippetClipboardRunner: live, timeProvider: clock );
            SnippetProvider provider = new( [
                new( ) { Id = "stack", Title = "Stack", Text = "now={clipboard} prev={clipboard offset=1 | uppercase} before={clipboard offset=2}" },
                new( ) { Id = "far", Title = "Far", Text = "{clipboard offset=5}" },
                new( ) { Id = "live", Title = "Live", Text = "{clipboard offset=0 | trim}" }] );
            var rows = await provider.SearchAsync( QueryParser.Parse( "snip:" ), new SearchRequest { Query = "snip:" }, default );
            var stack = rows.Single( static row => row.Title == "Stack" );
            var far = rows.Single( static row => row.Title == "Far" );
            var current = rows.Single( static row => row.Title == "Live" );
            True( PrivateSources.IsPrivate( stack ) && PrivateSources.IsPrivate( far ), "history placeholders make the row private" );
            True( !PrivateSources.IsPrivate( current ), "the current clipboard alone is not history" );
            True( stack.Actions.All( action => AgentLauncherTools.ClassifyRisk( stack, action ) == AgentActionRisks.Denied ) );

            var preview = stack.Actions.Single( static action => action.Kind == ActionKinds.SnippetPreview );
            var disabled = await executor.ExecuteAsync( stack, preview, "", default );
            Equal( "Enable clipboard capture before using clipboard history here.", disabled.Message );

            store.Enabled = true;
            foreach ( var text in new[] { "first", "second", "third" } )
            {
                store.Capture( text );
                clock.UtcNow += TimeSpan.FromSeconds( 1 );
            }

            True( store.Pin( store.Search( "first", 1 ).Single( ).Id, true ), "pinning does not reorder offsets" );
            Equal( "now=live text prev=SECOND before=first", (await executor.ExecuteAsync( stack, preview, "", default )).DetailMarkdown );
            store.Capture( "second" );
            Equal( "now=live text prev=THIRD before=first", (await executor.ExecuteAsync( stack, preview, "", default )).DetailMarkdown );
            Equal( "live text", (await executor.ExecuteAsync( current, current.Actions[1], "", default )).DetailMarkdown );
            Equal( "Clipboard history has no text entry 5 places back.", (await executor.ExecuteAsync( far, far.Actions[1], "", default )).Message );

            // An action without the private-row marker cannot read history.
            CommandAction forged = new( )
            {
                Id = "preview",
                Title = "Preview",
                Kind = ActionKinds.SnippetPreview,
                Arguments = new SnippetActionPayload( "{clipboard offset=1}" ).ToArguments( )
            };
            Equal( "Search for this snippet again before it reads clipboard history.", (await executor.ExecuteAsync( current, forged, "", default )).Message );

            store.Enabled = false;
            Equal( "Enable clipboard capture before using clipboard history here.", (await executor.ExecuteAsync( stack, preview, "", default )).Message );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }

    private static string SnippetFailure( Action action )
    {
        try
        {
            action( );
        }
        catch ( InvalidOperationException exception )
        {
            return exception.Message;
        }

        throw new InvalidOperationException( "Expected InvalidOperationException." );
    }

    // Fixed rules keep daylight-saving cases independent of the host's time zone data.
    private static TimeZoneInfo SnippetDaylightZone( )
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule( DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours( 1 ),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule( new DateTime( 1, 1, 1, 2, 0, 0 ), 3, 5, DayOfWeek.Sunday ),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule( new DateTime( 1, 1, 1, 3, 0, 0 ), 10, 5, DayOfWeek.Sunday ) );
        return TimeZoneInfo.CreateCustomTimeZone( "CommandDeck/SnippetSpec", TimeSpan.FromHours( 1 ), "Snippet spec",
            "Snippet spec", "Snippet spec summer", [rule] );
    }
}
