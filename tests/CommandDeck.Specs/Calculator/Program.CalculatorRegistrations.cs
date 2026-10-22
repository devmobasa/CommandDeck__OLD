namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunCalculatorRegistrationsAsync( )
    {
        foreach ( var (expression, expected) in CalculatorNumberCases( ).Concat( CalculatorUnitCases( ) ).Concat( CalculatorFunctionCases( ) ) )
        {
            Run( "calculator evaluates " + expression, ( ) => AssertCalculation( expression, expected ) );
        }

        foreach ( var expression in CalculatorInvalidCases( ) )
        {
            Run( "calculator rejects " + expression, ( ) => AssertCalculationInvalid( expression ) );
        }

        Run( "calculator bounds nesting tokens and input without throwing", CalculatorInputBounds );
        Run( "calculator reports whether the input is a conversion", CalculatorConversionFlag );
        Run( "calculator stays invariant and thread safe", CalculatorInvariantAndConcurrent );
        Run( "calculator compound unit text round trips", CalculatorUnitTextRoundTrips );

        foreach ( var (expression, text, precise) in CalculatorCurrencyCases( ) )
        {
            Run( "calculator currency evaluates " + expression, ( ) => CalculatorCurrencyEvaluation( expression, text, precise ) );
        }

        Run( "calculator currency rounds midpoint amounts away from zero", CalculatorCurrencyRounding );
        Run( "calculator currency distinguishes unavailable unsupported and incompatible", CalculatorCurrencyFailures );
        Run( "calculator currency title text round trips", CalculatorCurrencyTextRoundTrips );
        await RunAsync( "calculator rates refresh lazily with single flight and six hour cache", CalculatorRatesRefreshAsync );
        await RunAsync( "calculator rates preserve old data and back off after failure", CalculatorRatesFailureAsync );
        await RunAsync( "calculator rates validate bounded XML and transport failures", CalculatorRatesInvalidAsync );
        await RunAsync( "calculator rates persist private cache across instances", CalculatorRatesCacheAsync );
        await RunAsync( "calculator rates timeout and dispose in flight", CalculatorRatesCancellationAsync );
        await RunAsync( "calculator rates overlapping disposals both await shutdown", CalculatorRatesOverlappingDisposalAsync );
        Run( "calculator mixed workload stays below 250 microseconds", CalculatorPerformance );
    }

    private static void AssertCalculation( string expression, string expected )
    {
        var result = CalculatorEngine.Evaluate( expression );

        Equal( CalculationStatus.Success, result.Status );
        Equal( expected, result.Text );
        Equal<string?>( null, result.PreciseText );
        Equal<DateOnly?>( null, result.RatesDate );
    }

    private static void AssertCalculationInvalid( string expression, Func<CurrencyRateSnapshot?>? rates = null )
    {
        var result = CalculatorEngine.Evaluate( expression, rates );

        Equal( CalculationStatus.Invalid, result.Status );
        Equal<string?>( null, result.Text );
        Equal<string?>( null, result.PreciseText );
        Equal<DateOnly?>( null, result.RatesDate );
    }
}
