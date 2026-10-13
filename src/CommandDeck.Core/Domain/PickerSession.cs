using System.Text;

namespace CommandDeck;

public sealed record PickerChoice( string Value, string Label );

public sealed class PickerSession
{
    public string Prompt { get; init; } = "Choose an item";
    public List<PickerChoice> Choices { get; init; } = [];

    public static PickerSession Parse( ReadOnlySpan<byte> input, string prompt, bool labelTab )
    {
        if ( input.Length > 1024 * 1024 )
        {
            throw new InvalidOperationException( "Picker input exceeds 1 MiB." );
        }
        if ( prompt.Length > 256 )
        {
            throw new InvalidOperationException( "Picker prompt exceeds 256 characters." );
        }
        var text = new UTF8Encoding( false, true ).GetString( input );
        if ( text.Contains( '\0' ) )
        {
            throw new InvalidOperationException( "Picker input cannot contain NUL bytes." );
        }
        List<PickerChoice> choices = [];
        using StringReader reader = new( text );
        while ( reader.ReadLine( ) is { } line )
        {
            if ( choices.Count >= 10000 || line.Length > 4096 )
            {
                throw new InvalidOperationException( "Picker input allows at most 10000 choices of 4096 characters each." );
            }
            var tab = labelTab ? line.IndexOf( '\t' ) : -1;
            choices.Add( tab < 0 ? new( line, line ) : new( line[..tab], line[(tab + 1)..] ) );
        }
        return new PickerSession { Prompt = prompt, Choices = choices };
    }
}
