using System.Globalization;

namespace CommandDeck;

public sealed class EditorActionPayload
{
    public string Path { get; }
    public int Line { get; }
    public int Column { get; }
    public string Key { get; }

    public EditorActionPayload( string path, int line = 1, int column = 1, string? key = null )
    {
        if ( string.IsNullOrWhiteSpace( path ) || path.Contains( '\0' ) )
        {
            throw new InvalidOperationException( "An editor action requires a valid path." );
        }
        if ( line <= 0 || column <= 0 )
        {
            throw new InvalidOperationException( "Editor line and column must be positive integers." );
        }

        Path = path;
        Line = line;
        Column = column;
        Key = key ?? string.Empty;
    }

    public Dictionary<string, string> ToArguments( ) => new( StringComparer.Ordinal )
    {
        ["path"] = Path,
        ["line"] = Line.ToString( CultureInfo.InvariantCulture ),
        ["column"] = Column.ToString( CultureInfo.InvariantCulture ),
        ["key"] = Key
    };

    public static EditorActionPayload Read( IReadOnlyDictionary<string, string> arguments ) => new(
        arguments.GetValueOrDefault( "path", string.Empty ),
        ReadPosition( arguments, "line" ), ReadPosition( arguments, "column" ), arguments.GetValueOrDefault( "key" ) );

    private static int ReadPosition( IReadOnlyDictionary<string, string> arguments, string name )
    {
        if ( !arguments.TryGetValue( name, out var value ) )
        {
            return 1;
        }
        if ( !int.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out var position ) || position <= 0 )
        {
            throw new InvalidOperationException( $"Editor {name} must be a positive integer." );
        }

        return position;
    }
}
