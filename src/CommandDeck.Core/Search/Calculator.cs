using System.Globalization;

namespace CommandDeck;

public static class Calculator
{
    public static bool TryEvaluate( string expression, out double result, out string? error )
    {
        try
        {
            Parser parser = new( expression );
            result = parser.Parse( );
            error = null;
            return double.IsFinite( result );
        }
        catch ( FormatException exception )
        {
            result = 0;
            error = exception.Message;
            return false;
        }
        catch ( DivideByZeroException )
        {
            result = 0;
            error = "Division by zero.";
            return false;
        }
    }

    public static string Format( double value ) =>
        value.ToString( "G15", CultureInfo.InvariantCulture );

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;

        public Parser( string text )
        {
            _text = text;
        }

        public double Parse( )
        {
            if ( string.IsNullOrWhiteSpace( _text ) )
            {
                throw new FormatException( "Enter an expression after '='." );
            }

            var value = ParseExpression( );
            SkipWhiteSpace( );
            if ( _position != _text.Length )
            {
                throw new FormatException( $"Unexpected character '{_text[_position]}'." );
            }

            return value;
        }

        private double ParseExpression( )
        {
            var value = ParseTerm( );
            while ( true )
            {
                SkipWhiteSpace( );
                if ( Consume( '+' ) )
                {
                    value += ParseTerm( );
                }
                else if ( Consume( '-' ) )
                {
                    value -= ParseTerm( );
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm( )
        {
            var value = ParsePower( );
            while ( true )
            {
                SkipWhiteSpace( );
                if ( Consume( '*' ) )
                {
                    value *= ParsePower( );
                }
                else if ( Consume( '/' ) )
                {
                    var divisor = ParsePower( );
                    if ( divisor == 0 )
                    {
                        throw new DivideByZeroException( );
                    }

                    value /= divisor;
                }
                else if ( Consume( '%' ) )
                {
                    var divisor = ParsePower( );
                    if ( divisor == 0 )
                    {
                        throw new DivideByZeroException( );
                    }

                    value %= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParsePower( )
        {
            var value = ParseUnary( );
            SkipWhiteSpace( );
            if ( Consume( '^' ) )
            {
                value = Math.Pow( value, ParsePower( ) );
            }

            return value;
        }

        private double ParseUnary( )
        {
            SkipWhiteSpace( );
            if ( Consume( '+' ) )
            {
                return ParseUnary( );
            }

            if ( Consume( '-' ) )
            {
                return -ParseUnary( );
            }

            return ParsePrimary( );
        }

        private double ParsePrimary( )
        {
            SkipWhiteSpace( );
            if ( Consume( '(' ) )
            {
                var groupedValue = ParseExpression( );
                SkipWhiteSpace( );
                if ( !Consume( ')' ) )
                {
                    throw new FormatException( "Missing closing parenthesis." );
                }

                return groupedValue;
            }

            var start = _position;
            var hasDecimal = false;
            while ( _position < _text.Length )
            {
                var current = _text[_position];
                if ( char.IsDigit( current ) )
                {
                    _position++;
                    continue;
                }

                if ( current == '.' && !hasDecimal )
                {
                    hasDecimal = true;
                    _position++;
                    continue;
                }

                break;
            }

            if ( start == _position ||
                !double.TryParse(
                    _text[start.._position],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value ) )
            {
                throw new FormatException( $"Expected a number at position {start + 1}." );
            }

            return value;
        }

        private void SkipWhiteSpace( )
        {
            while ( _position < _text.Length && char.IsWhiteSpace( _text[_position] ) )
            {
                _position++;
            }
        }

        private bool Consume( char expected )
        {
            if ( _position < _text.Length && _text[_position] == expected )
            {
                _position++;
                return true;
            }

            return false;
        }
    }
}
