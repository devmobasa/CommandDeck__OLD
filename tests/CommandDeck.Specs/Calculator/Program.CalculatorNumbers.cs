using System.Globalization;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static (string Expression, string Text)[] CalculatorNumberCases( ) =>
    [
        ("12", "12"), ("1.5", "1.5"), (".5", "0.5"), ("1e3", "1000"), ("1.5E-7", "1.5e-7"),
        ("0.1 + 0.2", "0.3"), ("1/3", "0.333333333333333"), ("2/3", "0.666666666666667"),
        ("2^70", "1180591620717411303424"), ("20!", "2432902008176640000"),
        ("2^100", "1.26765060022823e30"), ("28!", "3.04888344611714e29"), ("100!", "9.33262154439442e157"),
        ("170!", "7.257415615308e306"), ("1e-28^-1", "1e28"),
        ("1.00000000000000000001 - 1", "1e-20"), ("1.0000000000001 - 1", "1e-13"),
        ("1.00000000000000000001 + -1", "1e-20"),
        ("1.5e-28", "1.5e-28"), ("1.5e-28 * 1e28", "1.5"),
        ("0e-999", "0"), ("0 * 1e-300", "0"),
        ("1e-300 - 1e-300", "0"), ("sqrt(4)-2", "0"), ("sqrt(4) mod 2", "0"),
        ("999.9999999999994", "999.999999999999"), ("99999999999999.94", "99999999999999.9"),
        ("0.0000001", "1e-7"), ("0.000001", "0.000001"), ("1e-30 * 2", "2e-30"), ("1e-20 * 1e-20", "1e-40"),
        ("1e-10 / 1000", "1e-13"), ("1e29", "1e29"), ("-0", "0"), ("−0.0", "0"),
        ("-2^2", "-4"), ("2^-1", "0.5"), ("2^3^2", "512"), ("2**3**2", "512"), ("3²", "9"), ("2³", "8"),
        ("(-8)^3", "-512"), ("4^0.5", "2"), ("1^4096", "1"), ("1^-4096", "1"),
        ("2(3+4)", "14"), ("(1+2)(3+4)", "21"), ("2pi", "6.28318530717959"),
        ("1/2pi", "1.5707963267949"), ("1/2", "0.5"), ("1/2/3", "0.166666666666667"), ("1/2 + 1/4", "0.75"),
        ("-1/2", "-0.5"), ("2^1/2", "1.4142135623731"), ("6/2(1+2)", "9"), ("10/4", "2.5"), ("2 + 3 * 4", "14"), ("2 × 3 ÷ 2 · 4", "12"),
        ("10 % 3", "1"), ("10%3", "1"), ("-7 mod 3", "-1"), ("7 MOD -3", "1"),
        ("pi", "3.14159265358979"), ("π", "3.14159265358979"), ("tau", "6.28318530717959"),
        ("τ", "6.28318530717959"), ("e", "2.71828182845905"), ("phi", "1.61803398874989"), ("φ", "1.61803398874989"),
        ("4k", "4 K"), ("2048", "2048"), ("5!", "120"), ("0!", "1"),
        ("79228162514264337593543950335 + 1", "7.92281625142643e28"),
        ("1000000000000000.5", "1e15"), ("-1e-30", "-1e-30")
    ];

    private static string[] CalculatorInvalidCases( ) =>
    [
        "", " ", "2 3", "1/0", "0^-1", "(-8)^(1/3)", "171!", "(-1)!", "1.5!", "1e999",
        "(-1)^9007199254740993", "1.00000000000000000001^100000000000000000000", "2^4096", "2^-4097",
        "1^4097", "1^-4097", "(-8)^1.00000000000000001",
        "1 m^32 * m^33", "1 m^64 * m^64", "1 m^32 / m^-33", "1 m^-64 / m^64",
        "1 m^64 * (m/kph)", "1 m^64 / (kph/m)", "(1 m^64/kph^32)^2",
        "1 kph * m^-64 / m", "(1 m^-64*kph^32)^2",
        "asin(1.00000000000000000001)", "asin(-1.00000000000000000001)",
        "acos(1.00000000000000000001)", "acos(-1.00000000000000000001)",
        "5 ft 3 in", "1 h 30 min", "2 lb 4 oz", "100 km in", "in", "in to cm", "k", "5 1/2", "2 1/2 cups", "1/2 3",
        "1e-999", "1e-300 * 1e-300", "1e-310", "1e-300 / 1e10", "2^-1024", "(1e-200)^2", "(1e-200)^2.1",
        "1 mpg + 1 L/100km", "min(1 mpg, 1 L/100km)", "1 mpg mod 1 L/100km",
        "1 m to 1 m", "1 m to m*2/2", "1 m to sqrt(m²)", "1 m to pi m", "1 m to m%",
        "1 m to m!", "1 m to m+m-m", "1 m to (m/s) 1 s", "1 m to m^pi",
        "unknown", "2 +", "2 + 3 garbage", "2 ; 3", "1,000", "1..2", "0x", "0b102", "0o19", "0xFG",
        "(1+2", "1+2)", "5 to km", "5 km to s", "1 m + 1 s", "1 km - 2 kg", "2 to hex to bin",
        "sqrt(3 m)", "cbrt(8 m²)", "sqrt(-1)", "sin(3 m)", "tan(90)", "asin(2)", "acos(-2)",
        "ln(0)", "log(-1)", "log(10,1)", "log(10,0)", "log(1,0)", "exp(1000)", "exp(-1000)", "round(1,1.5)", "round(1,29)",
        "log2(0)", "log10(-1)", "log(1,-0.000000000001)", "sqrt(-0.000000000001)",
        "170.00000000000000000001!", "round(1,28.00000000000000000001)",
        "gcd(1.00000000000000000001,2)", "lcm(1.00000000000000000001,2)",
        "0^-0.000000000001", "(-8)^3.00000000000000000001",
        "1.00000000000000000001 to hex", "9223372036854775807.1 to hex",
        "gcd(1.5,2)", "gcd(1)", "lcm(2 m,4 m)", "min(1 m,2 s)", "max(1 km,2 kg)",
        "sin()", "sin(1,2)", "round(1,2,3)", "min()", "min(1)", "1 to hex garbage", "5 m to hex",
        "1.5 to bin", "9223372036854775808 to hex", "-9223372036854775809 to hex", "10 °C + 5 °C",
        "10 °C - 5 °C", "2 * 10 °C", "10 °F / 2", "abs(-10 °C)", "10 °C^2", "°C·m",
        "5 M", "5 pa", "2\n+3", "2\t+3", "2\u001b+3", "(5 km to m)", "10 Hz to s"
    ];

    private static void CalculatorInputBounds( )
    {
        AssertCalculationInvalid( new string( '1', CalculatorEngine.MaximumExpressionLength + 1 ) );
        AssertCalculationInvalid( new string( '(', 100 ) + "1" + new string( ')', 100 ) );
        AssertCalculationInvalid( string.Concat( Enumerable.Repeat( "1+", 128 ) ) + "1" );
        AssertCalculationInvalid( new string( '-', 100 ) + "1" );
        AssertCalculationInvalid( string.Join( '^', Enumerable.Repeat( "1", 100 ) ) );
        AssertCalculation( new string( '(', 64 ) + "1" + new string( ')', 64 ), "1" );

        Equal( CalculationStatus.Invalid, CalculatorEngine.Evaluate( null! ).Status );

        var random = new Random( 712 );
        const string alphabet = "0123456789+-*/^%(), m°C$xyz\u0001";

        for ( var sample = 0; sample < 1000; sample++ )
        {
            var text = new string( Enumerable.Range( 0, random.Next( 1, 513 ) ).Select( _ => alphabet[random.Next( alphabet.Length )] ).ToArray( ) );
            var result = CalculatorEngine.Evaluate( text );

            True( result.Status != CalculationStatus.Success || result.Text is not null );
        }
    }

    private static void CalculatorConversionFlag( )
    {
        foreach ( var expression in new[] { "5 km to mi", "1 km² to m^2", "1 km² to m²", "2 + sqrt(4) to hex", "255 to hex", "100 km in miles" } )
        {
            var result = CalculatorEngine.Evaluate( expression );

            Equal( CalculationStatus.Success, result.Status );
            True( result.IsConversion );
        }

        foreach ( var expression in new[] { "100 * 5", "sqrt(2)", "15% of 80", "2048", "4k", "5 km", "2pi", "-3", "50%", "5!", "0xff", "pi", "5 in" } )
        {
            var result = CalculatorEngine.Evaluate( expression );

            Equal( CalculationStatus.Success, result.Status );
            True( !result.IsConversion );
        }
    }

    private static void CalculatorInvariantAndConcurrent( )
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "de-DE" );

            Parallel.For( 0, 100, _ =>
            {
                AssertCalculation( "0.1 + 0.2", "0.3" );
                AssertCalculation( "12 in to cm", "30.48 cm" );
            } );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
