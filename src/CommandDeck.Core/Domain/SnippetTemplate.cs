using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CommandDeck;

// Snippet placeholders. Built-in names are date, time, datetime, day, uuid and
// clipboard; any other name is a named input. A placeholder that parses but uses
// an unknown option, modifier, format, offset or locale is a configuration error.
public static partial class SnippetTemplate
{
    public const int MaximumSnippets = 1000;
    public const int MaximumInputs = 16;
    public const int MaximumClipboardOffset = 999;
    private const int MaximumFormatLength = 128;
    private const int MaximumOffsetSteps = 8;
    private static readonly string[] DateOptions = ["format", "offset", "locale"];
    private static readonly string[] Modifiers = ["uppercase", "lowercase", "trim", "percent-encode", "json-stringify"];

    [GeneratedRegex( "^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,4}$", RegexOptions.CultureInvariant )]
    private static partial Regex LocaleName( );

    public static CompiledSnippet Compile( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        List<SnippetPart> parts = [];
        List<ActionInput> inputs = [];
        SortedSet<int> clipboardOffsets = [];
        foreach ( var token in SnippetTemplateParser.Parse( text ) )
        {
            if ( token.Placeholder is not { } placeholder )
            {
                parts.Add( new SnippetLiteralPart( token.Literal! ) );
                continue;
            }

            var modifiers = CompileModifiers( placeholder );
            parts.Add( placeholder.Name switch
            {
                "date" => CompileDate( placeholder, "yyyy-MM-dd", modifiers ),
                "time" => CompileDate( placeholder, "HH:mm", modifiers ),
                "datetime" => CompileDate( placeholder, "yyyy-MM-dd HH:mm", modifiers ),
                "day" => CompileDate( placeholder, "dddd", modifiers ),
                "uuid" => CompileUuid( placeholder, modifiers ),
                "clipboard" => CompileClipboard( placeholder, modifiers, clipboardOffsets ),
                _ => CompileInput( placeholder, modifiers, inputs )
            } );
        }

        return new( parts, inputs, [.. clipboardOffsets] );
    }

    public static List<ActionInput> Inputs( string text ) => [.. Compile( text ).Inputs];

    public static string Expand( string text, IReadOnlyDictionary<string, string> arguments, TimeProvider time,
        IReadOnlyDictionary<int, string>? clipboard = null ) => Compile( text ).Expand( arguments, time, clipboard );

    public static void ValidateOptions( List<SnippetOptions>? snippets )
    {
        if ( snippets is null || snippets.Count > MaximumSnippets )
        {
            throw new InvalidOperationException( "Configure at most 1000 snippets." );
        }

        HashSet<string> ids = new( StringComparer.Ordinal );
        foreach ( var snippet in snippets )
        {
            if ( snippet is null || string.IsNullOrWhiteSpace( snippet.Id ) || snippet.Id.Length > 64 ||
                !ids.Add( snippet.Id ) || string.IsNullOrWhiteSpace( snippet.Title ) ||
                string.IsNullOrEmpty( snippet.Text ) || Encoding.UTF8.GetByteCount( snippet.Text ) > 64 * 1024 ||
                CompileConfigured( snippet ).Inputs.Count > MaximumInputs )
            {
                throw new InvalidOperationException( "Snippets need unique IDs, a title, text up to 64 KiB, and at most 16 named inputs." );
            }
        }
    }

    private static CompiledSnippet CompileConfigured( SnippetOptions snippet )
    {
        try
        {
            return Compile( snippet.Text );
        }
        catch ( InvalidOperationException exception )
        {
            throw new InvalidOperationException( $"Snippet '{snippet.Id}': {exception.Message}", exception );
        }
    }

    private static SnippetDatePart CompileDate( SnippetPlaceholderSyntax placeholder, string defaultFormat, string[] modifiers )
    {
        var options = Options( placeholder, DateOptions );
        var format = options.GetValueOrDefault( "format", defaultFormat );
        var culture = options.TryGetValue( "locale", out var locale ) ? Culture( placeholder, locale ) : CultureInfo.InvariantCulture;
        var steps = options.TryGetValue( "offset", out var offset ) ? Offset( placeholder, offset ) : [];
        if ( format.Length is 0 or > MaximumFormatLength || !IsDateFormat( format, culture ) )
        {
            throw Invalid( placeholder, "needs a valid .NET custom date and time format" );
        }

        return new SnippetDatePart( format, steps, culture, modifiers );
    }

    private static SnippetUuidPart CompileUuid( SnippetPlaceholderSyntax placeholder, string[] modifiers )
    {
        Options( placeholder );
        return new SnippetUuidPart( modifiers );
    }

    private static SnippetClipboardPart CompileClipboard( SnippetPlaceholderSyntax placeholder, string[] modifiers, SortedSet<int> offsets )
    {
        var options = Options( placeholder, "offset" );
        var offset = 0;
        if ( options.TryGetValue( "offset", out var value ) &&
            (!int.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out offset ) || offset > MaximumClipboardOffset) )
        {
            throw Invalid( placeholder, "needs a whole-number offset from 0 to 999" );
        }

        offsets.Add( offset );
        return new SnippetClipboardPart( offset, modifiers );
    }

    // A name used more than once is one input; its occurrences may not disagree on a default.
    private static SnippetInputPart CompileInput( SnippetPlaceholderSyntax placeholder, string[] modifiers, List<ActionInput> inputs )
    {
        var options = Options( placeholder, "default" );
        var defaultValue = options.GetValueOrDefault( "default" );
        if ( defaultValue is not null && (defaultValue.Length == 0 || defaultValue.Contains( '\0' )) )
        {
            throw Invalid( placeholder, "needs a nonempty default" );
        }

        var index = inputs.FindIndex( input => input.Name == placeholder.Name );
        if ( index < 0 )
        {
            inputs.Add( new( ) { Name = placeholder.Name, DefaultValue = defaultValue } );
        }
        else if ( defaultValue is not null && inputs[index].DefaultValue is null )
        {
            inputs[index] = new( ) { Name = placeholder.Name, DefaultValue = defaultValue };
        }
        else if ( defaultValue is not null && inputs[index].DefaultValue != defaultValue )
        {
            throw Invalid( placeholder, $"gives input '{placeholder.Name}' a different default than an earlier placeholder" );
        }

        return new SnippetInputPart( placeholder.Name, modifiers );
    }

    private static string[] CompileModifiers( SnippetPlaceholderSyntax placeholder )
    {
        foreach ( var modifier in placeholder.Modifiers )
        {
            if ( !Modifiers.Contains( modifier, StringComparer.Ordinal ) )
            {
                throw Invalid( placeholder, $"uses unknown modifier '{modifier}'; use {string.Join( ", ", Modifiers )}" );
            }
        }

        return [.. placeholder.Modifiers];
    }

    private static Dictionary<string, string> Options( SnippetPlaceholderSyntax placeholder, params string[] allowed )
    {
        Dictionary<string, string> options = new( StringComparer.Ordinal );
        foreach ( var (key, value) in placeholder.Options )
        {
            if ( !allowed.Contains( key, StringComparer.Ordinal ) )
            {
                throw Invalid( placeholder, allowed.Length == 0
                    ? "takes no options"
                    : $"does not support option '{key}'; use {string.Join( ", ", allowed )}" );
            }

            if ( !options.TryAdd( key, value ) )
            {
                throw Invalid( placeholder, $"repeats option '{key}'" );
            }
        }

        return options;
    }

    private static CultureInfo Culture( SnippetPlaceholderSyntax placeholder, string locale )
    {
        try
        {
            if ( LocaleName( ).IsMatch( locale ) )
            {
                return CultureInfo.GetCultureInfo( locale, predefinedOnly: true );
            }
        }
        catch ( CultureNotFoundException )
        {
        }

        throw Invalid( placeholder, $"uses locale '{locale}', which is not available on this system" );
    }

    // Signed, space-separated steps such as "+1d -2h", applied in order.
    private static SnippetDateStep[] Offset( SnippetPlaceholderSyntax placeholder, string value )
    {
        var tokens = value.Split( [' ', '\t'], StringSplitOptions.RemoveEmptyEntries );
        if ( tokens.Length is 0 or > MaximumOffsetSteps || !tokens.All( IsOffsetStep ) )
        {
            throw Invalid( placeholder, "needs an offset of up to 8 signed steps such as \"+1d -2h\" (units m, h, d, M, y)" );
        }

        return [.. tokens.Select( static token => new SnippetDateStep(
            int.Parse( token[..^1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture ), token[^1] ) )];
    }

    private static bool IsOffsetStep( string token ) =>
        token.Length is >= 3 and <= 8 && token[0] is '+' or '-' && token[^1] is 'm' or 'h' or 'd' or 'M' or 'y' &&
        token[1..^1].All( char.IsAsciiDigit );

    private static bool IsDateFormat( string format, CultureInfo culture )
    {
        try
        {
            _ = DateTimeOffset.UnixEpoch.ToString( format, culture );
            return true;
        }
        catch ( FormatException )
        {
            return false;
        }
    }

    private static InvalidOperationException Invalid( SnippetPlaceholderSyntax placeholder, string problem )
    {
        var source = placeholder.Source.Length <= 80 ? placeholder.Source : placeholder.Source[..77] + "...";
        return new( $"{source} {problem}." );
    }
}
