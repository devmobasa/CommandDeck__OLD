using System.Text;
using System.Text.RegularExpressions;

namespace CommandDeck;

public static partial class SnippetTemplate
{
    [GeneratedRegex( @"\{([A-Za-z][A-Za-z0-9_-]{0,63})\}", RegexOptions.CultureInvariant )]
    private static partial Regex Placeholder( );

    public static List<ActionInput> Inputs( string text ) => Placeholder( ).Matches( text )
        .Select( static match => match.Groups[1].Value )
        .Where( static name => name is not "date" and not "time" and not "clipboard" )
        .Distinct( StringComparer.Ordinal ).Select( static name => new ActionInput { Name = name } ).ToList( );

    public static string Expand( string text, IReadOnlyDictionary<string, string> arguments, DateTimeOffset now, string clipboard )
    {
        ArgumentNullException.ThrowIfNull( arguments );
        return Placeholder( ).Replace( text, match => match.Groups[1].Value switch
        {
            "date" => now.ToString( "yyyy-MM-dd" ),
            "time" => now.ToString( "HH:mm" ),
            "clipboard" => clipboard,
            var name => arguments.TryGetValue( "input:" + name, out var value )
                ? value : throw new InvalidOperationException( "Enter a value for " + name + "." )
        } );
    }

    public static void ValidateOptions( List<SnippetOptions>? snippets )
    {
        if ( snippets is null || snippets.Count > 1000 )
        {
            throw new InvalidOperationException( "Configure at most 1000 snippets." );
        }
        HashSet<string> ids = new( StringComparer.Ordinal );
        foreach ( var snippet in snippets )
        {
            if ( snippet is null || string.IsNullOrWhiteSpace( snippet.Id ) || snippet.Id.Length > 64 ||
                !ids.Add( snippet.Id ) || string.IsNullOrWhiteSpace( snippet.Title ) ||
                string.IsNullOrEmpty( snippet.Text ) || Encoding.UTF8.GetByteCount( snippet.Text ) > 64 * 1024 ||
                Inputs( snippet.Text ).Count > 16 )
            {
                throw new InvalidOperationException( "Snippets need unique IDs, a title, text up to 64 KiB, and at most 16 named inputs." );
            }
        }
    }
}
