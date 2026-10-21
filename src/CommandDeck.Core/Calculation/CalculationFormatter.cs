using System.Globalization;

namespace CommandDeck;

internal static class CalculationFormatter
{
    internal static string? CurrencyCode( CalculationQuantity quantity ) => quantity.Units is
        [{ Unit.Dimension.Currency: 1, Power: 1 }] ? quantity.Units[0].Unit.Symbol : null;

    internal static string Format( CalculationQuantity quantity )
    {
        var code = CurrencyCode( quantity );
        if ( code is not null )
        {
            var value = quantity.Value;
            var negative = value.Double < 0;
            var magnitude = negative ? value.Negate( ) : value;
            var displayed = Number( magnitude, preserveInteger: false );
            var amount = magnitude.CompareTo( new( 1000000000000000m ) ) >= 0 ? displayed
                : decimal.Round( decimal.Parse( displayed, NumberStyles.Float, CultureInfo.InvariantCulture ),
                    2, MidpointRounding.AwayFromZero ).ToString( "F2", CultureInfo.InvariantCulture );
            var symbol = CalculationCurrencies.Symbol( code );
            var sign = negative && amount != "0.00" ? "-" : "";

            return sign + (symbol is not null ? symbol + amount : code + " " + amount);
        }

        var unit = Units( quantity.Units );

        return Number( quantity.Value ) + (unit.Length == 0 || unit == "°" || unit.StartsWith( '/' ) ? "" : " ") + unit;
    }

    internal static string Number( CalculationNumber number, bool preserveInteger = true )
    {
        if ( number.IsZero )
        {
            return "0";
        }

        if ( preserveInteger && number.IsExact && number.IsInteger )
        {
            return number.Exact.ToString( "0", CultureInfo.InvariantCulture );
        }

        string formatted;
        if ( number.IsExact )
        {
            formatted = number.Exact.ToString( "G15", CultureInfo.InvariantCulture );
        }
        else
        {
            formatted = number.Double.ToString( "G15", CultureInfo.InvariantCulture );
        }

        if ( formatted.Contains( 'E' ) )
        {
            var pieces = formatted.Split( 'E' );
            var exponent = int.Parse( pieces[1], CultureInfo.InvariantCulture );
            if ( exponent is >= -6 and < 15 )
            {
                return double.Parse( formatted, CultureInfo.InvariantCulture ).ToString( "0.####################", CultureInfo.InvariantCulture );
            }

            return pieces[0] + "e" + exponent.ToString( CultureInfo.InvariantCulture );
        }

        return formatted == "-0" ? "0" : formatted;
    }

    private static string Units( CalculationUnitPower[] terms )
    {
        static string Term( CalculationUnitPower term, int index )
        {
            var power = Math.Abs( term.Power );
            var symbol = term.Unit.Symbol;
            if ( index > 0 || term.Power < 0 )
            {
                symbol = symbol switch { "g" => "gram", "in" => "inch", _ => symbol };
            }

            if ( symbol.Contains( '/' ) && (power > 1 || term.Power < 0) )
            {
                symbol = term.Unit.Symbols.Split( '|' )[0];
                if ( symbol.Contains( '/' ) )
                {
                    symbol = "(" + symbol + ")";
                }
            }

            return symbol + (power switch { 1 => "", 2 => "²", 3 => "³", _ => "^" + power.ToString( CultureInfo.InvariantCulture ) });
        }

        var numerator = string.Join( '·', terms.Where( term => term.Power > 0 ).Select( Term ) );
        var denominator = terms.Where( term => term.Power < 0 ).ToArray( );
        if ( denominator.Length == 0 )
        {
            return numerator;
        }

        return numerator + "/" +
            (denominator.Length > 1 ? "(" : "") + string.Join( '·', denominator.Select( Term ) ) + (denominator.Length > 1 ? ")" : "");
    }

    internal static bool IsBase( string name ) => name is "hex" or "hexadecimal" or "bin" or "binary" or "oct" or "octal" or "dec" or "decimal";

    internal static string Base( CalculationQuantity quantity, string name )
    {
        quantity.RequireScalar( );

        var integer = quantity.Value.IsExact ? quantity.Value.Exact : checked(( decimal ) quantity.Value.Double);
        if ( !quantity.Value.IsInteger || integer < long.MinValue || integer > long.MaxValue )
        {
            throw new ArithmeticException( );
        }

        var value = ( long ) integer;
        if ( name is "dec" or "decimal" )
        {
            return value.ToString( CultureInfo.InvariantCulture );
        }

        var radix = name is "hex" or "hexadecimal" ? 16u : name is "bin" or "binary" ? 2u : 8u;
        var prefix = radix == 16 ? "0x" : radix == 2 ? "0b" : "0o";
        var remaining = ( ulong ) decimal.Abs( integer );
        var digits = "";
        do
        {
            digits = "0123456789ABCDEF"[( int ) (remaining % radix)] + digits;
            remaining /= radix;
        } while ( remaining > 0 );

        return (value < 0 ? "-" : "") + prefix + digits;
    }
}
