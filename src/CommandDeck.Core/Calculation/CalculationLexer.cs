namespace CommandDeck;

// UnitGroup and Fraction mark the opening parenthesis the lexer adds around `km/h` and `1/2`.
internal readonly record struct CalculationToken( string Text, bool Number = false, bool UnitGroup = false, bool Fraction = false )
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

                var fraction = FractionEnd( expression, start, i );
                if ( fraction > i )
                {
                    // An integer fraction written without spaces is one number: 1/2 cup, 3/4 h, 1/2pi = π/2.
                    tokens.AddRange( [new( "(", Fraction: true ), new( expression[start..i], true ), new( "/" ),
                        new( expression[(i + 1)..fraction], true ), new( ")" )] );
                    i = fraction;
                }
                else
                {
                    tokens.Add( new( expression[start..i].Replace( '−', '-' ), true ) );
                }
            }
            else if ( IsWordStart( character ) )
            {
                i = WordEnd( expression, start );

                var word = expression[start..i];
                var compound = CompoundEnd( expression, i, word );
                if ( tokens.Count > 0 &&
                    (tokens[^1].Lower, word.ToLowerInvariant( )) is ("fl", "oz" ) or ("fluid", "ounce" or "ounces" ) )
                {
                    tokens[^1] = new( "floz" );
                }
                else if ( compound > i )
                {
                    // Two units written as a/b without spaces are one unit: 100 km / 50 km/h = 2 h.
                    tokens.AddRange( [new( "(", UnitGroup: true ), new( word ), new( "/" ), .. Tokenize( expression[(i + 1)..compound] ), new( ")" )] );
                    i = compound;
                }
                else
                {
                    tokens.Add( new( word ) );
                }
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

    private static bool IsWordStart( char character ) => char.IsLetter( character ) || character is 'µ' or '°';

    private static int WordEnd( string expression, int start )
    {
        var i = start + 1;

        while ( i < expression.Length && char.IsLetterOrDigit( expression[i] ) && expression[i] is not '²' and not '³' )
        {
            i++;
        }

        return i;
    }

    // The end of `/denominator` when both sides are plain integers, otherwise -1.
    private static int FractionEnd( string expression, int start, int end )
    {
        if ( end + 1 >= expression.Length || expression[end] != '/' || !char.IsAsciiDigit( expression[end + 1] ) ||
            expression.AsSpan( start, end - start ).ContainsAnyExceptInRange( '0', '9' ) )
        {
            return -1;
        }

        var denominator = NumberEnd( expression, end + 1 );

        return expression.AsSpan( end + 1, denominator - end - 1 ).ContainsAnyExceptInRange( '0', '9' ) ? -1 : denominator;
    }

    // The end of `/unit` (with an attached power) after a unit, otherwise -1.
    private static int CompoundEnd( string expression, int slash, string numerator )
    {
        if ( slash + 1 >= expression.Length || expression[slash] != '/' || !IsWordStart( expression[slash + 1] ) )
        {
            return -1;
        }

        var end = WordEnd( expression, slash + 1 );
        if ( !IsUnit( numerator ) || !IsUnit( expression[(slash + 1)..end] ) )
        {
            return -1;
        }

        if ( end < expression.Length && expression[end] is '²' or '³' )
        {
            return end + 1;
        }

        if ( end + 1 >= expression.Length || expression[end] != '^' )
        {
            return end;
        }

        var digits = expression[end + 1] is '-' or '−' or '+' ? end + 2 : end + 1;
        var power = digits;

        while ( power < expression.Length && char.IsAsciiDigit( expression[power] ) )
        {
            power++;
        }

        return power > digits ? power : end;
    }

    private static bool IsUnit( string word ) =>
        CalculationUnits.Find( word ) is not null || CalculationCurrencies.IsCurrency( word ) ||
        word.Length > 1 && word[^1] is '2' or '3' && CalculationUnits.Find( word[..^1] ) is not null;

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
