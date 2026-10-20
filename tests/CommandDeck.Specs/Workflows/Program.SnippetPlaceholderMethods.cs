namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly IReadOnlyDictionary<string, string> NoSnippetArguments =
        new Dictionary<string, string>( StringComparer.Ordinal );

    private static void SnippetSyntaxKeepsMalformedPlaceholdersLiteral( )
    {
        SnippetClock clock = new( new DateTimeOffset( 2026, 9, 6, 14, 30, 0, TimeSpan.Zero ), TimeZoneInfo.Utc );
        string Expand( string text ) => SnippetTemplate.Expand( text, NoSnippetArguments, clock );

        // Braces that never form a placeholder stay literal text, as before.
        const string literal = "{\"a\": 1} {see below} { date} {date:x} {date format=} {date | } {date format=\"dd\"offset=\"+1d\"} {";
        Equal( literal, Expand( literal ) );
        Equal( "{2026-09-06} {a 2026-09-06", Expand( "{{date}} {a {date}" ) );

        // A line break anywhere inside a placeholder, even quoted, abandons it.
        const string broken = "{date\n} {date format=\"dd\nMM\"} {date format='dd\\\nMM'} {date format=\"dd}";
        Equal( broken, Expand( broken ) );

        // Options follow the name and precede modifiers; names and placeholders are bounded.
        Equal( "{date | trim format=\"dd\"}", Expand( "{date | trim format=\"dd\"}" ) );
        var longName = "{" + new string( 'a', 65 ) + "}";
        Equal( longName, Expand( longName ) );
        var longPlaceholder = "{topic default=\"" + new string( 'x', 1100 ) + "\"}";
        Equal( longPlaceholder, Expand( longPlaceholder ) );
        Equal( 0, SnippetTemplate.Inputs( longName + longPlaceholder ).Count );

        // Quoted values accept either quote and backslash escapes; bare values end at whitespace.
        Equal( "06.09.2026|06'09|2026-09-06 14:30", Expand( """{date format='dd.MM.yyyy'}|{date format='dd\\\'MM'}|{datetime}""" ) );
        var inputs = SnippetTemplate.Inputs( """{a default="say \"hi\" \\ o'k"} {b default='it\'s "fine"'} {c default=plain}""" );
        Equal( "say \"hi\" \\ o'k", inputs[0].DefaultValue );
        Equal( "it's \"fine\"", inputs[1].DefaultValue );
        Equal( "plain", inputs[2].DefaultValue );
        Equal( "2026-09-06", Expand( "{date\t|\tlowercase}" ) );
    }

    private static async Task SnippetPlaceholderErrorsRejectConfigurationAsync( )
    {
        static string Error( string text )
        {
            try
            {
                SnippetTemplate.ValidateOptions( [new( ) { Id = "bad", Title = "Bad", Text = text }] );
            }
            catch ( InvalidOperationException exception )
            {
                return exception.Message;
            }

            throw new InvalidOperationException( "Expected a configuration error for " + text );
        }

        Equal( "Snippet 'bad': {topic | shout} uses unknown modifier 'shout'; use uppercase, lowercase, trim, percent-encode, json-stringify.",
            Error( "Hello {topic | shout}" ) );
        Equal( "Snippet 'bad': {topic format=\"x\"} does not support option 'format'; use default.", Error( "{topic format=\"x\"}" ) );
        Equal( "Snippet 'bad': {date zone=\"x\"} does not support option 'zone'; use format, offset, locale.", Error( "{date zone=\"x\"}" ) );
        Equal( "Snippet 'bad': {date format=\"dd\" format=\"MM\"} repeats option 'format'.", Error( "{date format=\"dd\" format=\"MM\"}" ) );
        Equal( "Snippet 'bad': {uuid format=\"N\"} takes no options.", Error( "{uuid format=\"N\"}" ) );
        Equal( "Snippet 'bad': {time format=\"'abc\"} needs a valid .NET custom date and time format.", Error( "{time format=\"'abc\"}" ) );
        Equal( "Snippet 'bad': {date format=%} needs a valid .NET custom date and time format.", Error( "{date format=%}" ) );
        Equal( "Snippet 'bad': {day locale=\"zz-ZZ\"} uses locale 'zz-ZZ', which is not available on this system.",
            Error( "{day locale=\"zz-ZZ\"}" ) );
        Equal( "Snippet 'bad': {day locale=\"de_DE@x\"} uses locale 'de_DE@x', which is not available on this system.",
            Error( "{day locale=\"de_DE@x\"}" ) );
        foreach ( var offset in new[] { "1d", "+1w", "+d", "+1d,-2h", "+1234567d", "+1d +1d +1d +1d +1d +1d +1d +1d +1d" } )
        {
            True( Error( "{date offset=\"" + offset + "\"}" ).EndsWith(
                "needs an offset of up to 8 signed steps such as \"+1d -2h\" (units m, h, d, M, y).", StringComparison.Ordinal ), offset );
        }

        foreach ( var offset in new[] { "-1", "+1", "1000", "x", "1.5" } )
        {
            True( Error( "{clipboard offset=" + offset + "}" ).EndsWith( "needs a whole-number offset from 0 to 999.", StringComparison.Ordinal ), offset );
        }

        Equal( "Snippet 'bad': {topic default=\"\"} needs a nonempty default.", Error( "{topic default=\"\"}" ) );
        Equal( "Snippet 'bad': {topic default=\"b\"} gives input 'topic' a different default than an earlier placeholder.",
            Error( "{topic default=\"a\"} {topic} {topic default=\"b\"}" ) );
        Equal( "Snippets need unique IDs, a title, text up to 64 KiB, and at most 16 named inputs.",
            Error( string.Concat( Enumerable.Range( 0, 17 ).Select( static index => "{input" + index + " | trim}" ) ) ) );
        var repeated = string.Concat( Enumerable.Range( 0, 16 ).Select( static index => "{input" + index + "} {input" + index + " | uppercase}" ) );
        SnippetTemplate.ValidateOptions( [new( ) { Id = "ok", Title = "OK", Text = repeated }] );

        var directory = Directory.CreateTempSubdirectory( "commanddeck-snippet-config-" ).FullName;
        try
        {
            var path = Path.Combine( directory, "config.json" );
            await File.WriteAllTextAsync( path, """{ "snippets": [{ "id": "review", "title": "Review", "text": "{topic | upper}" }] }""" );
            var failure = await ConfigurationLoaderFailureAsync( path );
            Equal( "Snippet 'review': {topic | upper} uses unknown modifier 'upper'; " +
                "use uppercase, lowercase, trim, percent-encode, json-stringify.", failure );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }

    private static void SnippetModifiersTransformEachValueInOrder( )
    {
        SnippetClock clock = new( new DateTimeOffset( 2026, 9, 6, 14, 30, 0, TimeSpan.Zero ), TimeZoneInfo.Utc );
        var template = SnippetTemplate.Compile(
            "{topic | uppercase}|{topic | trim | lowercase}|{topic | percent-encode}|{topic | json-stringify}|" +
            "{topic}|{day|uppercase}|{date | json-stringify}" );
        Equal( 1, template.Inputs.Count );
        var expanded = template.Expand( new Dictionary<string, string> { ["input:topic"] = "  Café & \"Tea\"/\\\n\u0001 " }, clock );
        Equal( "  CAFÉ & \"TEA\"/\\\n\u0001 |café & \"tea\"/\\\n\u0001|%20%20Caf%C3%A9%20%26%20%22Tea%22%2F%5C%0A%01%20|" +
            "\"  Café & \\\"Tea\\\"/\\\\\\n\\u0001 \"|  Café & \"Tea\"/\\\n\u0001 |SUNDAY|\"2026-09-06\"", expanded );

        // Modifiers apply to the inserted value only; the result is never expanded again.
        var literal = SnippetTemplate.Expand( "{topic | uppercase}", new Dictionary<string, string> { ["input:topic"] = "{date} {uuid}" }, clock );
        Equal( "{DATE} {UUID}", literal );
        Equal( "\"\\ud800x\"", SnippetTemplate.Expand( "{topic | json-stringify}", new Dictionary<string, string> { ["input:topic"] = "\ud800x" }, clock ) );
        Equal( "\"🙂\"", SnippetTemplate.Expand( "{topic | json-stringify}", new Dictionary<string, string> { ["input:topic"] = "🙂" }, clock ) );
    }

    private static async Task<string> ConfigurationLoaderFailureAsync( string path )
    {
        try
        {
            await ConfigurationLoader.LoadAsync( path, CancellationToken.None );
        }
        catch ( InvalidOperationException exception )
        {
            return exception.Message;
        }

        throw new InvalidOperationException( "Expected configuration loading to fail." );
    }

    private sealed class SnippetClock( DateTimeOffset utcNow, TimeZoneInfo zone ) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow( ) => UtcNow;
        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
