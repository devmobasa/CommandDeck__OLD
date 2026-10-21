using System.Globalization;

namespace CommandDeck;

internal readonly struct CalculationNumber
{
    // Keep decimal's 28-29 significant digits while values fit, otherwise use double.
    // Literals beyond 28 significant digits round on input; cancellation beyond that
    // precision (for example (1e28 + 0.1 - 1e28) * 10) loses rounded digits: a known limit.
    // Display results to 15 significant digits (exact integers keep all digits).
    // Nonzero decimal results below 1e-12 move to double; tiny or underflowed
    // decimal products and quotients recompute in double.
    // Transcendental functions (sin, cos, tan, asin, acos, atan, ln, log, log2, log10, exp),
    // roots and non-integer powers compute in double after rounding inputs to about 16
    // significant digits. Results involving an approximate value have double precision.
    // Inputs differing only beyond that precision can coincide (ln(1.00000000000000000001) = 0).
    // Domain checks still use the exact value.
    internal decimal Exact { get; }
    internal double Approximate { get; }
    internal bool IsExact { get; }
    internal double Double => IsExact ? ( double ) Exact : Approximate;
    internal bool IsZero => IsExact ? Exact == 0 : Approximate == 0;
    internal bool IsInteger => IsExact ? decimal.Truncate( Exact ) == Exact : Math.Truncate( Approximate ) == Approximate;

    internal CalculationNumber( decimal value )
    {
        Exact = value;
        IsExact = true;
    }

    internal CalculationNumber( double value )
    {
        if ( !double.IsFinite( value ) || double.IsSubnormal( value ) )
        {
            throw new ArithmeticException( );
        }

        Approximate = value == 0 ? 0 : value;
    }

    internal static CalculationNumber Parse( string text )
    {
        if ( text.Length > 2 && text[0] == '0' && "xbo".Contains( char.ToLowerInvariant( text[1] ) ) )
        {
            var radix = char.ToLowerInvariant( text[1] ) switch { 'x' => 16, 'b' => 2, _ => 8 };
            var value = new CalculationNumber( 0m );

            foreach ( var character in text.AsSpan( 2 ) )
            {
                var digit = character is >= '0' and <= '9' ? character - '0' : char.ToLowerInvariant( character ) - 'a' + 10;
                if ( digit < 0 || digit >= radix )
                {
                    throw new FormatException( );
                }

                value = Binary( Binary( value, new( ( decimal ) radix ), '*' ), new( ( decimal ) digit ), '+' );
            }

            return value;
        }

        var floating = double.Parse( text, NumberStyles.Float, CultureInfo.InvariantCulture );
        if ( floating == 0 && text.Split( 'e', 'E' )[0].Any( character => character is >= '1' and <= '9' ) )
        {
            throw new ArithmeticException( );
        }

        if ( decimal.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact ) &&
            (floating == 0 || Math.Abs( floating ) >= 1e-12) )
        {
            return new( exact );
        }

        return new( floating );
    }

    internal static CalculationNumber Binary( CalculationNumber left, CalculationNumber right, char operation )
    {
        if ( operation is '/' or '%' && right.IsZero )
        {
            throw new ArithmeticException( );
        }

        if ( left.IsExact && right.IsExact )
        {
            try
            {
                var result = operation switch
                {
                    '+' => left.Exact + right.Exact,
                    '-' => left.Exact - right.Exact,
                    '*' => left.Exact * right.Exact,
                    '/' => left.Exact / right.Exact,
                    '%' => left.Exact % right.Exact,
                    _ => throw new FormatException( )
                };

                // Keep the decimal cancellation result; recompute only tiny products and quotients.
                if ( operation is '*' or '/' && decimal.Abs( result ) < 0.000000000001m && !left.IsZero && !right.IsZero )
                {
                    return Floating( left, right, operation );
                }

                if ( result != 0 && decimal.Abs( result ) < 0.000000000001m )
                {
                    return new( ( double ) result );
                }

                return new( result );
            }
            catch ( OverflowException ) { }
        }

        return Floating( left, right, operation );
    }

    private static CalculationNumber Floating( CalculationNumber a, CalculationNumber b, char op )
    {
        var result = op switch
        {
            '+' => a.Double + b.Double,
            '-' => a.Double - b.Double,
            '*' => a.Double * b.Double,
            '/' => a.Double / b.Double,
            '%' => a.Double % b.Double,
            _ => throw new FormatException( )
        };
        if ( op is '*' or '/' && result == 0 && !a.IsZero && !b.IsZero )
        {
            throw new ArithmeticException( );
        }

        return new( result );
    }

    internal CalculationNumber Negate( ) => IsExact ? new( -Exact ) : new( -Approximate );
    internal int CompareTo( CalculationNumber other )
    {
        if ( IsExact && other.IsExact )
        {
            return Exact.CompareTo( other.Exact );
        }

        var approximate = IsExact ? other.Approximate : Approximate;
        if ( IsExact != other.IsExact && (approximate == 0 || Math.Abs( approximate ) >= 1e-28) &&
            Math.Abs( approximate ) < ( double ) decimal.MaxValue )
        {
            return IsExact ? Exact.CompareTo( ( decimal ) approximate ) : (( decimal ) approximate).CompareTo( other.Exact );
        }

        return Double.CompareTo( other.Double );
    }

    internal CalculationNumber Power( CalculationNumber exponent )
    {
        if ( IsZero && exponent.CompareTo( new( 0m ) ) < 0 || exponent.IsInteger &&
            (exponent.CompareTo( new( -4096m ) ) < 0 || exponent.CompareTo( new( 4096m ) ) > 0) ||
            CompareTo( new( 0m ) ) < 0 && !exponent.IsInteger )
        {
            throw new ArithmeticException( );
        }

        if ( IsExact && exponent.IsInteger )
        {
            var power = ( int ) exponent.Double;
            var factor = power < 0 ? Binary( new( 1m ), this, '/' ) : this;
            var result = new CalculationNumber( 1m );

            for ( var remaining = Math.Abs( power ); remaining > 0; remaining >>= 1 )
            {
                if ( (remaining & 1) != 0 )
                {
                    result = Binary( result, factor, '*' );
                }

                if ( remaining > 1 )
                {
                    factor = Binary( factor, factor, '*' );
                }
            }

            return result;
        }

        var approximate = Math.Pow( Double, exponent.Double );
        if ( approximate == 0 && !IsZero )
        {
            throw new ArithmeticException( );
        }

        return new( approximate );
    }

    internal CalculationNumber Factorial( )
    {
        if ( !IsInteger || CompareTo( new( 0m ) ) < 0 || CompareTo( new( 170m ) ) > 0 )
        {
            throw new ArithmeticException( );
        }

        var result = 1m;

        for ( var i = 2; i <= Double; i++ )
        {
            try
            {
                result *= i;
            }
            catch ( OverflowException )
            {
                return FloatingFactorial( ( int ) Double );
            }
        }

        return new( result );
    }

    private static CalculationNumber FloatingFactorial( int limit )
    {
        var value = 1d;
        var correction = 0d;

        for ( var i = 2; i <= limit; i++ )
        {
            var product = value * i;
            correction = correction * i + Math.FusedMultiplyAdd( value, i, -product );
            value = product;
        }

        return new( value + correction );
    }
}
