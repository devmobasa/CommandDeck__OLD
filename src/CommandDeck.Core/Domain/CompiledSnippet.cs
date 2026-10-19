using System.Globalization;
using System.Text;

namespace CommandDeck;

/// <summary>A validated snippet template. Each expansion inserts values once, literally.</summary>
public sealed class CompiledSnippet
{
    public const int MaximumExpandedLength = 128 * 1024;
    private static readonly Dictionary<int, string> NoClipboard = [];
    private readonly IReadOnlyList<SnippetPart> _parts;

    internal CompiledSnippet( IReadOnlyList<SnippetPart> parts, IReadOnlyList<ActionInput> inputs, IReadOnlyList<int> clipboardOffsets )
    {
        _parts = parts;
        Inputs = inputs;
        ClipboardOffsets = clipboardOffsets;
    }

    public IReadOnlyList<ActionInput> Inputs { get; }

    /// <summary>Distinct clipboard offsets in ascending order; 0 is the current clipboard.</summary>
    public IReadOnlyList<int> ClipboardOffsets { get; }

    public bool ReadsClipboardHistory => ClipboardOffsets.Any( static offset => offset > 0 );

    public string Expand( IReadOnlyDictionary<string, string> arguments, TimeProvider time, IReadOnlyDictionary<int, string>? clipboard = null )
    {
        ArgumentNullException.ThrowIfNull( arguments );
        ArgumentNullException.ThrowIfNull( time );

        SnippetScope scope = new( arguments, time.GetLocalNow( ), time.LocalTimeZone, clipboard ?? NoClipboard );
        StringBuilder output = new( );
        foreach ( var part in _parts )
        {
            output.Append( part.Render( scope ) );
            if ( output.Length > MaximumExpandedLength )
            {
                throw new InvalidOperationException( "Expanded snippet exceeds 128 KiB." );
            }
        }

        return output.ToString( );
    }
}

internal sealed record SnippetScope(
    IReadOnlyDictionary<string, string> Arguments,
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    IReadOnlyDictionary<int, string> Clipboard );

internal abstract class SnippetPart( string[] modifiers )
{
    public string Render( SnippetScope scope )
    {
        var value = Value( scope );
        foreach ( var modifier in modifiers )
        {
            value = modifier switch
            {
                "uppercase" => value.ToUpperInvariant( ),
                "lowercase" => value.ToLowerInvariant( ),
                "trim" => value.Trim( ),
                "percent-encode" => Uri.EscapeDataString( value ),
                "json-stringify" => JsonString( value ),
                _ => throw new InvalidOperationException( "Unknown snippet modifier '" + modifier + "'." )
            };
        }

        return value;
    }

    protected abstract string Value( SnippetScope scope );

    // A JSON string literal, quotes included. Non-ASCII text stays readable; control
    // characters and unpaired surrogates are escaped so the result is valid JSON.
    private static string JsonString( string value )
    {
        StringBuilder builder = new( value.Length + 2 );
        builder.Append( '"' );
        for ( var index = 0; index < value.Length; index++ )
        {
            var current = value[index];
            if ( char.IsHighSurrogate( current ) && index + 1 < value.Length && char.IsLowSurrogate( value[index + 1] ) )
            {
                builder.Append( current ).Append( value[++index] );
                continue;
            }

            builder.Append( current switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                _ when current < ' ' || char.IsSurrogate( current ) => "\\u" + (( int ) current).ToString( "x4", CultureInfo.InvariantCulture ),
                _ => current.ToString( )
            } );
        }

        return builder.Append( '"' ).ToString( );
    }
}

internal sealed class SnippetLiteralPart( string text ) : SnippetPart( [] )
{
    protected override string Value( SnippetScope scope ) => text;
}

internal sealed class SnippetInputPart( string name, string[] modifiers ) : SnippetPart( modifiers )
{
    protected override string Value( SnippetScope scope ) =>
        scope.Arguments.TryGetValue( "input:" + name, out var value )
            ? value
            : throw new InvalidOperationException( "Enter a value for " + name + "." );
}

internal sealed class SnippetUuidPart( string[] modifiers ) : SnippetPart( modifiers )
{
    protected override string Value( SnippetScope scope ) => Guid.NewGuid( ).ToString( "D" );
}

internal sealed class SnippetClipboardPart( int offset, string[] modifiers ) : SnippetPart( modifiers )
{
    protected override string Value( SnippetScope scope ) =>
        scope.Clipboard.TryGetValue( offset, out var value )
            ? value
            : throw new InvalidOperationException( "Clipboard text is unavailable." );
}

internal sealed class SnippetDatePart( string format, SnippetDateStep[] steps, CultureInfo culture, string[] modifiers )
    : SnippetPart( modifiers )
{
    protected override string Value( SnippetScope scope )
    {
        var value = scope.Now;
        try
        {
            foreach ( var step in steps )
            {
                value = step.Apply( value, scope.Zone );
            }
        }
        catch ( ArgumentOutOfRangeException )
        {
            throw new InvalidOperationException( "A snippet date offset is out of range." );
        }

        return value.ToString( format, culture );
    }
}

// Minutes and hours are elapsed time; days, months and years keep the local
// wall-clock time across daylight-saving changes.
internal readonly record struct SnippetDateStep( int Amount, char Unit )
{
    public DateTimeOffset Apply( DateTimeOffset value, TimeZoneInfo zone ) => Unit switch
    {
        'm' => TimeZoneInfo.ConvertTime( value.AddMinutes( Amount ), zone ),
        'h' => TimeZoneInfo.ConvertTime( value.AddHours( Amount ), zone ),
        'd' => AtWallClock( value.DateTime.AddDays( Amount ), zone ),
        'M' => AtWallClock( value.DateTime.AddMonths( Amount ), zone ),
        _ => AtWallClock( value.DateTime.AddYears( Amount ), zone )
    };

    private static DateTimeOffset AtWallClock( DateTime wall, TimeZoneInfo zone ) =>
        TimeZoneInfo.ConvertTime( new DateTimeOffset( wall, zone.GetUtcOffset( wall ) ), zone );
}
