namespace CommandDeck;

internal readonly record struct CalculationToken( string Text, bool Number = false )
{
    internal string Lower => Text.ToLowerInvariant( );
}

internal static class CalculationLexer
{
    internal static CalculationToken[] Tokenize( string expression )
    {
        List<CalculationToken> tokens = [];

        for ( var i = 0; i < expression.Length; )
        {
            var character = expression[i];
            if ( char.IsControl( character ) )
            {
                throw new FormatException( );
            }

            if ( char.IsWhiteSpace( character ) )
            {
                i++;
                continue;
            }

            var start = i++;
            if ( expression.AsSpan( start ).StartsWith( "L/100km", StringComparison.Ordinal ) )
            {
                tokens.Add( new( "L/100km" ) );
                i = start + 7;
            }
            else if ( char.IsAsciiDigit( character ) || character == '.' )
            {
                i = NumberEnd( expression, start );
                tokens.Add( new( expression[start..i].Replace( '−', '-' ), true ) );
            }
            else if ( char.IsLetter( character ) || character is 'µ' or '°' )
            {
                while ( i < expression.Length && char.IsLetterOrDigit( expression[i] ) && expression[i] is not '²' and not '³' )
                {
                    i++;
                }

                tokens.Add( new( expression[start..i] ) );
            }
            else
            {
                tokens.Add( new( Operator( expression, start, ref i ) ) );
            }

            if ( tokens.Count > 256 )
            {
                throw new FormatException( );
            }
        }

        return tokens.ToArray( );
    }

    private static int NumberEnd( string expression, int start )
    {
        var i = start + 1;
        if ( expression[start] == '0' && i < expression.Length && "xXbBoO".Contains( expression[i] ) )
        {
            i++;
            var digits = i;

            while ( i < expression.Length && char.IsAsciiLetterOrDigit( expression[i] ) )
            {
                i++;
            }

            if ( i == digits )
            {
                throw new FormatException( );
            }

            return i;
        }

        while ( i < expression.Length && (char.IsAsciiDigit( expression[i] ) || expression[i] == '.') )
        {
            i++;
        }

        if ( i + 1 < expression.Length && expression[i] is 'e' or 'E' &&
            (char.IsAsciiDigit( expression[i + 1] ) || expression[i + 1] is '+' or '-' or '−') )
        {
            i++;
            if ( expression[i] is '+' or '-' or '−' )
            {
                i++;
            }

            while ( i < expression.Length && char.IsAsciiDigit( expression[i] ) )
            {
                i++;
            }
        }

        return i;
    }

    private static string Operator( string expression, int start, ref int end )
    {
        var character = expression[start];
        if ( end < expression.Length && (character == '*' && expression[end] == '*' || character == '-' && expression[end] == '>') )
        {
            end++;

            return character == '*' ? "^" : "->";
        }

        return character switch { '−' => "-", '×' or '·' => "*", '÷' => "/", _ => character.ToString( ) };
    }
}
