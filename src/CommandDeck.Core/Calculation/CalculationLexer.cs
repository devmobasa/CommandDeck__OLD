namespace CommandDeck;

// UnitGroup and Fraction mark the parentheses the lexer adds around `km/h` and `1/2`.
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

                var fraction = FractionEnd( expression, start, i, tokens is [.., { Text: "^" }] or [.., { Text: "^" }, { Text: "+" or "-" }] );
                var rate = tokens is [.., var previous] && CalculationCurrencies.IsCurrency( previous.Text ) ? DenominatorEnd( expression, i ) : -1;
                if ( fraction > i )
                {
                    // An integer fraction before an operand is one number: 1/2 cup, 3/4 h, 1/2pi = π/2.
                    tokens.AddRange( [new( "(", Fraction: true ), new( expression[start..i], true ), new( "/" ),
                        new( expression[(i + 1)..fraction], true ), new( ")", Fraction: true )] );
                    i = fraction;
                }
                else if ( rate > i )
                {
                    // $20/h is twenty dollars per hour, so $800 / $20/h divides by the whole rate.
                    var currency = tokens[^1];

                    tokens.RemoveAt( tokens.Count - 1 );
                    AddUnitGroup( tokens, [currency, new( expression[start..i].Replace( '−', '-' ), true )], expression, i, rate );
                    i = rate;
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
                    AddUnitGroup( tokens, [new( word )], expression, i, compound );
                    i = compound;
                }
                else
                {
                    tokens.Add( new( word ) );
                }
            }
            else
            {
                var symbol = Operator( expression, start, ref i );
                var compound = CalculationCurrencies.IsCurrency( symbol ) ? DenominatorEnd( expression, i ) : -1;
                if ( compound > i )
                {
                    // A currency symbol joins a compound unit like its code does: 10 €/h.
                    AddUnitGroup( tokens, [new( symbol )], expression, i, compound );
                    i = compound;
                }
                else
                {
                    tokens.Add( new( symbol ) );
                }
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

    private static void AddUnitGroup( List<CalculationToken> tokens, CalculationToken[] numerator, string expression, int slash, int end ) =>
        tokens.AddRange( [new( "(", UnitGroup: true ), .. numerator, new( "/" ), .. Tokenize( expression[(slash + 1)..end] ), new( ")", UnitGroup: true )] );

    // The end of `/denominator` when both sides are plain integers and an operand follows, otherwise -1.
    // Without an operand (10/2^2, 3/2!, 100/5%) or after ^ (27^1/3) normal precedence applies.
    private static int FractionEnd( string expression, int start, int end, bool afterPower )
    {
        if ( afterPower || end + 1 >= expression.Length || expression[end] != '/' || !char.IsAsciiDigit( expression[end + 1] ) ||
            expression.AsSpan( start, end - start ).ContainsAnyExceptInRange( '0', '9' ) )
        {
            return -1;
        }

        var denominator = NumberEnd( expression, end + 1 );
        var next = denominator;

        while ( next < expression.Length && char.IsWhiteSpace( expression[next] ) )
        {
            next++;
        }

        return !expression.AsSpan( end + 1, denominator - end - 1 ).ContainsAnyExceptInRange( '0', '9' ) &&
            next < expression.Length && (IsWordStart( expression[next] ) || expression[next] == '(' ||
            CalculationCurrencies.IsCurrency( expression[next].ToString( ) )) ? denominator : -1;
    }

    private static int CompoundEnd( string expression, int slash, string numerator ) =>
        IsUnit( numerator ) ? DenominatorEnd( expression, slash ) : -1;

    // The end of `/unit` with an attached power when it names a unit, otherwise -1.
    private static int DenominatorEnd( string expression, int slash )
    {
        if ( slash + 1 >= expression.Length || expression[slash] != '/' || !IsWordStart( expression[slash + 1] ) )
        {
            return -1;
        }

        var end = WordEnd( expression, slash + 1 );
        if ( !IsUnit( expression[(slash + 1)..end] ) )
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
