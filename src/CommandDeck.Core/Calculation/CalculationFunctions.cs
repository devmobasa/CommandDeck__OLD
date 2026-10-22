using System.Globalization;

namespace CommandDeck;

internal static class CalculationFunctions
{
    internal static bool IsFunction( string name ) => name is "sqrt" or "cbrt" or "sin" or "cos" or "tan" or
        "asin" or "acos" or "atan" or "log" or "log10" or "log2" or "ln" or "exp" or "abs" or
        "round" or "floor" or "ceil" or "gcd" or "lcm" or "min" or "max";

    internal static CalculationQuantity? Constant( string name ) => name switch
    {
        "pi" or "π" => CalculationQuantity.Scalar( 3.1415926535897932384626433833m ),
        "tau" or "τ" => CalculationQuantity.Scalar( 6.2831853071795864769252867666m ),
        "e" => CalculationQuantity.Scalar( 2.7182818284590452353602874714m ),
        "phi" or "φ" => CalculationQuantity.Scalar( 1.6180339887498948482045868344m ),
        "c" => new( new( 299792458m ), [new( CalculationUnits.Canonical( "m" ), 1 ), new( CalculationUnits.Canonical( "s" ), -1 )] ),
        "g" => new( new( 9.80665m ), [new( CalculationUnits.Canonical( "m" ), 1 ), new( CalculationUnits.Canonical( "s" ), -2 )] ),
        _ => null
    };

    internal static CalculationQuantity Call( string name, List<CalculationQuantity> arguments )
    {
        if ( arguments.Count == 0 )
        {
            throw new FormatException( );
        }

        var first = arguments[0];
        first.RejectOffset( );

        if ( name is "min" or "max" )
        {
            if ( arguments.Count < 2 )
            {
                throw new FormatException( );
            }

            var chosen = first;

            foreach ( var argument in arguments.Skip( 1 ) )
            {
                argument.RejectOffset( );

                var compared = argument.ConvertTo( chosen ).Value.CompareTo( chosen.Value );
                if ( name == "min" ? compared < 0 : compared > 0 )
                {
                    chosen = argument;
                }
            }

            return chosen with { BarePercent = false, UnitOnly = false };
        }

        if ( name is "gcd" or "lcm" )
        {
            return IntegerFunction( name, arguments );
        }

        if ( arguments.Count > (name is "round" or "log" ? 2 : 1) )
        {
            throw new FormatException( );
        }

        if ( name is "sqrt" or "cbrt" )
        {
            return first.Root( name == "sqrt" ? 2 : 3 );
        }

        if ( name is "abs" or "round" or "floor" or "ceil" )
        {
            return Round( name, arguments );
        }

        if ( name is "sin" or "cos" or "tan" )
        {
            var radians = first.Dimension == new CalculationDimension( Angle: 1 ) ? first.BaseValue.Double :
                ScalarValue( first ).Double * Math.PI / 180;
            var sin = Snap( Math.Sin( radians ) );
            var cos = Snap( Math.Cos( radians ) );
            if ( name == "tan" && cos == 0 )
            {
                throw new ArithmeticException( );
            }

            return new( new CalculationNumber( name switch { "sin" => sin, "cos" => cos, _ => sin / cos } ), [] );
        }

        var x = ScalarValue( first );
        var answer = name switch
        {
            "asin" or "acos" => InverseTrig( name, x ),
            "atan" => Math.Atan( x.Double ) * 180 / Math.PI,
            "log" or "log10" or "log2" or "ln" => Logarithm( name, x, arguments ),
            "exp" => Math.Exp( x.Double ),
            _ => throw new FormatException( )
        };
        if ( name == "exp" && answer == 0 )
        {
            throw new ArithmeticException( );
        }

        return new( new CalculationNumber( answer ), name is "asin" or "acos" or "atan"
            ? [new( CalculationUnits.Canonical( "°" ), 1 )] : [] );
    }

    private static CalculationQuantity Round( string name, List<CalculationQuantity> arguments )
    {
        var first = arguments[0];
        var digits = 0;
        if ( arguments.Count == 2 )
        {
            arguments[1].RequireScalar( );
            if ( !arguments[1].Value.IsInteger || arguments[1].Value.CompareTo( new( 0m ) ) < 0 ||
                arguments[1].Value.CompareTo( new( 28m ) ) > 0 )
            {
                throw new ArithmeticException( );
            }

            digits = ( int ) arguments[1].Value.Double;
        }

        var value = first.Value;
        if ( name == "round" && !value.IsExact && Math.Abs( value.Double ) >= 1e-12 &&
            decimal.TryParse( CalculationFormatter.Number( value ),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var displayed ) )
        {
            value = new( displayed );
        }

        var scale = Math.Pow( 10, digits );
        var scaled = name == "round" && !value.IsExact ? double.Parse(
            (value.Double * scale).ToString( "G15", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture ) : 0;

        var result = value.IsExact ? new CalculationNumber( name switch
        {
            "abs" => decimal.Abs( value.Exact ),
            "floor" => decimal.Floor( value.Exact ),
            "ceil" => decimal.Ceiling( value.Exact ),
            _ => decimal.Round( value.Exact, digits, MidpointRounding.AwayFromZero )
        } ) : new CalculationNumber( name switch
        {
            "abs" => Math.Abs( value.Double ),
            "floor" => Math.Floor( value.Double ),
            "ceil" => Math.Ceiling( value.Double ),
            _ => double.IsFinite( scaled ) ? Math.Round( scaled, MidpointRounding.AwayFromZero ) / scale : value.Double
        } );

        return first with { Value = result, BarePercent = false, UnitOnly = false };
    }

    private static double Snap( double value ) => Math.Abs( value ) < 1e-12 ? 0 : value;
    private static double InverseTrig( string name, CalculationNumber value )
    {
        if ( value.CompareTo( new( -1m ) ) < 0 || value.CompareTo( new( 1m ) ) > 0 )
        {
            throw new ArithmeticException( );
        }

        return (name == "asin" ? Math.Asin( value.Double ) : Math.Acos( value.Double )) * 180 / Math.PI;
    }

    private static double Logarithm( string name, CalculationNumber value, List<CalculationQuantity> arguments )
    {
        if ( value.CompareTo( new( 0m ) ) <= 0 )
        {
            throw new ArithmeticException( );
        }

        if ( name != "log" || arguments.Count == 1 )
        {
            return name switch { "log2" => Math.Log2( value.Double ), "ln" => Math.Log( value.Double ), _ => Math.Log10( value.Double ) };
        }

        var numberBase = ScalarValue( arguments[1] );
        if ( numberBase.CompareTo( new( 0m ) ) <= 0 || numberBase.CompareTo( new( 1m ) ) == 0 )
        {
            throw new ArithmeticException( );
        }

        // When an exact base rounds to 1, ln(base) is base - 1 to double precision.
        var logarithm = numberBase.Double == 1 ? ( double ) (numberBase.Exact - 1m) : Math.Log( numberBase.Double );

        return Math.Log( value.Double ) / logarithm;
    }

    private static CalculationNumber ScalarValue( CalculationQuantity quantity )
    {
        quantity.RequireScalar( );

        return quantity.Value;
    }

    private static CalculationQuantity IntegerFunction( string name, List<CalculationQuantity> arguments )
    {
        if ( arguments.Count < 2 )
        {
            throw new FormatException( );
        }

        var result = new CalculationNumber( 0m );
        var first = true;

        foreach ( var argument in arguments )
        {
            argument.RequireScalar( );
            if ( !argument.Value.IsInteger )
            {
                throw new ArithmeticException( );
            }

            var number = argument.Value.CompareTo( new( 0m ) ) < 0 ? argument.Value.Negate( ) : argument.Value;
            if ( first && name == "lcm" )
            {
                result = number;
                first = false;
                continue;
            }

            var a = result;
            var b = number;

            while ( !b.IsZero )
            {
                (a, b) = (b, CalculationNumber.Binary( a, b, '%' ));
            }

            result = name == "gcd" ? a : a.IsZero ? new( 0m ) :
                CalculationNumber.Binary( CalculationNumber.Binary( result, a, '/' ), number, '*' );
        }

        return new( result, [] );
    }
}
