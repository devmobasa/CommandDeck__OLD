namespace CommandDeck.Specs;

public static partial class Program
{
    private static CurrencyRateSnapshot CalculatorTestRates( ) => new( new( 2026, 10, 21 ), new Dictionary<string, decimal>
    {
        ["USD"] = 1.1234m,
        ["JPY"] = 177.36m,
        ["GBP"] = 0.83825m,
        ["SEK"] = 11.0975m,
        ["NOK"] = 10.5120m,
        ["CHF"] = 0.9256m,
        ["INR"] = 106.1265m
    } );

    private static (string Expression, string Text, string? Precise)[] CalculatorCurrencyCases( ) =>
        [
            ("100 USD to EUR", "€89.02", "89.0154886950329 EUR"),
            ("100 eur in usd", "$112.34", "112.34 USD"), ("$100 to €", "€89.02", "89.0154886950329 EUR"),
            ("100 € to £", "£83.83", "83.825 GBP"), ("100 JPY to EUR", "€0.56", null),
            ("1000 SEK to NOK", "NOK 947.24", null), ("10 GBP + 5 EUR", "£14.19", "14.19125 GBP"),
            ("-5 eur", "-€5.00", "-5 EUR"), ("€ 5", "€5.00", "5 EUR"), ("5€", "€5.00", "5 EUR"),
            ("-5 NOK", "-NOK 5.00", "-5 NOK"),
            ("-$5", "-$5.00", "-5 USD"), ("USD + 5 USD", "$6.00", "6 USD"),
            ("USD 100", "$100.00", "100 USD"), ("$ 100", "$100.00", "100 USD"),
            ("1051.25256 NOK to EUR", "€100.01", "100.005 EUR"),
            ("-1051.25256 NOK to EUR", "-€100.01", "-100.005 EUR"),
            ("1051.25256 NOK / EUR * EUR", "€100.01", "100.005 EUR"),
            ("-1051.25256 NOK / EUR * EUR", "-€100.01", "-100.005 EUR"),
            ("100.00499999999999999999999 EUR", "€100.01", "100.005 EUR"),
            ("-100.00499999999999999999999 EUR", "-€100.01", "-100.005 EUR"),
            ("1e30 usd", "$1e30", "1e30 USD"), ("1e30 NOK", "NOK 1e30", "1e30 NOK"),
            ("-1e30 usd", "-$1e30", "-1e30 USD"), ("-1e30 NOK", "-NOK 1e30", "-1e30 NOK"),
            ("1e15 usd", "$1e15", "1e15 USD"), ("sqrt(2) USD", "$1.41", "1.4142135623731 USD"),
            ("sqrt(4 usd^2)", "$2.00", "2 USD"), ("-sqrt(2) USD", "-$1.41", "-1.4142135623731 USD"),
            ("sqrt(2) NOK", "NOK 1.41", "1.4142135623731 NOK"),
            ("999999999999999 usd", "$999999999999999.00", "999999999999999 USD"),
            ("999999999999999.99 usd", "$1000000000000000.00", "1e15 USD"),
            ("-999999999999999.99 usd", "-$1000000000000000.00", "-1e15 USD"),
            ("100$", "$100.00", "100 USD"), ("100 $", "$100.00", "100 USD"),
            ("1 dollar + 2 dollars", "$3.00", "3 USD"), ("1 euro + 2 euros", "€3.00", "3 EUR"),
            ("1 yen + 2 ¥", "¥3.00", "3 JPY"), ("1 rupee + 2 rupees", "₹3.00", "3 INR"),
            ("1 ₹", "₹1.00", "1 INR"), ("10 USD / 5 USD", "2", null),
            ("5.005 eur", "€5.01", "5.005 EUR"), ("-5.005 eur", "-€5.01", "-5.005 EUR"),
            ("-0.001 eur", "€0.00", "-0.001 EUR")
        ];

    private static void CalculatorCurrencyEvaluation( string expression, string text, string? precise )
    {
        var rates = CalculatorTestRates( );
        var calls = 0;

        var result = CalculatorEngine.Evaluate( expression, ( ) => { calls++; return rates; } );

        Equal( CalculationStatus.Success, result.Status );
        Equal( text, result.Text );
        Equal<DateOnly?>( rates.Date, result.RatesDate );
        Equal( 1, calls );

        if ( precise is not null || expression.Contains( '/' ) )
        {
            Equal( precise, result.PreciseText );
        }
        else
        {
            True( result.PreciseText is not null );
        }
    }

    private static void CalculatorCurrencyRounding( )
    {
        var rates = CalculatorTestRates( );

        var large = CalculatorEngine.Evaluate( "100000000000000000000 EUR", ( ) => rates );

        Equal( "€1e20", large.Text );
        Equal( "1e20 EUR", large.PreciseText );

        foreach ( var expression in new[] { "1 yuan", "1 ₩", "1 ₪" } )
        {
            var complete = new CurrencyRateSnapshot( rates.Date, new Dictionary<string, decimal> { ["CNY"] = 2m, ["KRW"] = 3m, ["ILS"] = 4m } );

            Equal( CalculationStatus.Success, CalculatorEngine.Evaluate( expression, ( ) => complete ).Status );
        }

        var midpoint = new CurrencyRateSnapshot( rates.Date, new Dictionary<string, decimal> { ["USD"] = 1.125m } );

        Equal( "$1.13", CalculatorEngine.Evaluate( "1 EUR to USD", ( ) => midpoint ).Text );
        Equal( "-$1.13", CalculatorEngine.Evaluate( "-1 EUR to USD", ( ) => midpoint ).Text );

        for ( var rate = 1.005m; rate < 2m; rate += 0.01m )
        {
            var rounded = decimal.Round( rate, 2, MidpointRounding.AwayFromZero ).ToString( "F2", System.Globalization.CultureInfo.InvariantCulture );
            var boundary = new CurrencyRateSnapshot( rates.Date, new Dictionary<string, decimal> { ["USD"] = rate } );

            Equal( "$" + rounded, CalculatorEngine.Evaluate( "1 EUR to USD", ( ) => boundary ).Text );
        }
    }

    private static void CalculatorCurrencyTextRoundTrips( )
    {
        var rates = CalculatorTestRates( );
        (string Expression, string Text)[] cases =
        [
            ("-5 NOK", "-NOK 5.00"), ("5 NOK", "NOK 5.00"),
            ("-1e30 NOK", "-NOK 1e30"), ("1e30 NOK", "NOK 1e30"),
            ("-5 USD", "-$5.00"), ("5 USD", "$5.00"),
            ("-1e30 USD", "-$1e30"), ("1e30 USD", "$1e30"),
            ("-5.005 EUR", "-€5.01"), ("5.005 EUR", "€5.01"),
            ("-0.001 NOK", "NOK 0.00"), ("-0.001 EUR", "€0.00")
        ];

        foreach ( var (expression, text) in cases )
        {
            var result = CalculatorEngine.Evaluate( expression, ( ) => rates );

            Equal( CalculationStatus.Success, result.Status );
            Equal( text, result.Text );

            var copied = CalculatorEngine.Evaluate( result.Text!, ( ) => rates );

            Equal( CalculationStatus.Success, copied.Status );
            Equal( text, copied.Text );
        }
    }

    private static void CalculatorUnavailableRateFeatures( )
    {
        (string Expression, CalculationFeatures Features)[] cases =
        [
            ("2 * USD", CalculationFeatures.Operator), ("sqrt(4) USD", CalculationFeatures.Function),
            ("100 USD to EUR", CalculationFeatures.Conversion),
            ("sqrt(4) * USD to m", CalculationFeatures.Operator | CalculationFeatures.Function | CalculationFeatures.Conversion),
            ("100 to m/USD", CalculationFeatures.Operator | CalculationFeatures.Conversion),
            ("5 USD", CalculationFeatures.None)
        ];

        foreach ( var (expression, features) in cases )
        {
            foreach ( var rates in new Func<CurrencyRateSnapshot?>?[] { null, ( ) => null } )
            {
                var result = CalculatorEngine.Evaluate( expression, rates );

                Equal( CalculationStatus.RatesUnavailable, result.Status );
                Equal( features, result.Features );
                Equal<string?>( null, result.Text );
                Equal<string?>( null, result.PreciseText );
                Equal<DateOnly?>( null, result.RatesDate );
            }
        }
    }

    private static void CalculatorCurrencyFailures( )
    {
        foreach ( var expression in new[] { "100 usd to eur", "$5", "1 eur", "100 usd to km", "1 USD / (1 EUR - 1 USD)" } )
        {
            Equal( CalculationStatus.RatesUnavailable, CalculatorEngine.Evaluate( expression ).Status );

            var calls = 0;

            var result = CalculatorEngine.Evaluate( expression, ( ) => { calls++; return null; } );

            Equal( CalculationStatus.RatesUnavailable, result.Status );
            Equal( 1, calls );
            Equal<string?>( null, result.Text );
            Equal<string?>( null, result.PreciseText );
            Equal<DateOnly?>( null, result.RatesDate );
        }

        foreach ( var expression in new[] { "$-5", "NOK -5", "100 usd to km", "100 btc to eur", "1 CHF + 2 m", "1 AUD", "1 btc", "1 bitcoin", "1 bitcoins", "1 eth", "1 ethereum", "1 rub", "1 ₽", "1 ₿", "1 aed", "1 bam", "1 twd", "1 usd garbage" } )
        {
            AssertCalculationInvalid( expression, CalculatorTestRates );
        }

        Equal( CalculationStatus.Invalid, CalculatorEngine.Evaluate( "1 USD", ( ) => throw new InvalidOperationException( ) ).Status );
        Equal( CalculationStatus.Invalid, CalculatorEngine.Evaluate( "1 USD", ( ) => new( new( 2026, 10, 21 ), new Dictionary<string, decimal> { ["USD"] = 0 } ) ).Status );

        foreach ( var expression in CalculatorNumberCases( ).Concat( CalculatorUnitCases( ) ).Concat( CalculatorFunctionCases( ) ).Select( pair => pair.Expression ) )
        {
            var calls = 0;

            var result = CalculatorEngine.Evaluate( expression, ( ) => { calls++; return CalculatorTestRates( ); } );

            Equal( CalculationStatus.Success, result.Status );
            Equal( 0, calls );
        }
    }
}
