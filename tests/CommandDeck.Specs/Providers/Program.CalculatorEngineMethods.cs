namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly (string Query, string Expected)[] InlineConversionCases =
    [
        ("5 km in miles", "3.10685596118667 mi"), ("100 usd to eur", "€89.02"),
        ("10 mins to hours", "0.166666666666667 h"),
        ("5 lbs to kg", "2.26796185 kg"), ("105F in C", "40.5555555555556 °C"),
        ("3 days in hours", "72 h"), ("60 mph to km/h", "96.56064 km/h"),
        ("$100 to €", "€89.02"), ("255 to hex", "0xFF"),
        ("2 L to fl oz", "67.628045403686 floz"), ("2 L to fluid ounce", "67.628045403686 floz"),
        ("2 L to fluid ounces", "67.628045403686 floz"),
        (".5 km as m", "500 m"), ("-5 km -> m", "-5000 m"), ("100km→mi", "62.1371192237334 mi")
    ];

    private static readonly string[] OrdinaryConversionSearches =
    [
        "5 km in paris", "1 in a million", "2 in 1 laptop", "10 years in prison", "3 days in rome",
        "5 m to go", "4 to 5", "1 to 1", "km to miles", "100 km in", "1 in to", "7zip to rar",
        "x264 to x265", "4k to 1080p", "2026-09-11 to 2026-10-11", "100 usd to km", "5 kg to km"
    ];

    private static async Task InlineCalculatorConversionsAsync( )
    {
        foreach ( var (input, expected) in InlineConversionCases )
        {
            True( CalculatorEngine.IsConversion( input ), input );
            foreach ( var enabled in new[] { true, false } )
            {
                CalculatorProvider provider = new( options: new( ) { Inline = enabled }, rates: CalculatorTestRates );
                foreach ( var prefix in new[] { "", "=" } )
                {
                    var query = prefix + input;
                    var rows = await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest { Query = query }, default );
                    Equal( enabled || prefix == "=", rows.Count == 1 );
                    if ( rows.Count == 0 )
                    {
                        continue;
                    }

                    var row = rows.Single( );
                    Equal( ResultKinds.Calculator, row.Kind );
                    Equal( expected, row.Title );
                    Equal( input, row.Subtitle );
                    Equal( input, row.Details.Single( field => field.Label == "Expression" ).Value );
                    Equal( "Copy Result", row.Actions.Single( ).Title );
                    Equal( expected, row.Actions.Single( ).Arguments["text"] );
                }
            }
        }

        foreach ( var query in OrdinaryConversionSearches.Concat( OrdinaryCalculatorSearches ).Where( static query => !query.StartsWith( '=' ) ) )
        {
            CalculatorProvider provider = new( rates: CalculatorTestRates );
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest { Query = query }, default )).Count );
        }

    }

    private static async Task CalculatorProviderCurrencyAsync( )
    {
        CalculatorProvider provider = new( rates: CalculatorTestRates );
        foreach ( var (input, title, exact) in new[]
        {
            ("100 USD to EUR", "€89.02", "89.0154886950329 EUR"),
            ("100000000 USD to EUR", "€89015488.70", "89015488.6950329 EUR"),
            ("-100000000 USD to EUR", "-€89015488.70", "-89015488.6950329 EUR")
        } )
        {
            var rows = await provider.SearchAsync( QueryParser.Parse( "=" + input ), new SearchRequest( ), default );
            var row = rows.Single( );

            Equal( title, row.Title );
            Equal( title, row.Actions.Single( ).Arguments["text"] );
            Equal( exact, row.Details.Single( field => field.Label == "Exact value" ).Value );
            Equal( "ECB reference rates, 2026-10-21", row.Details.Single( field => field.Label == "Exchange rates" ).Value );
        }
        foreach ( var (input, expected) in new[]
        {
            ("2 + 3 * 4", "14"), ("105f to c", "40.5555555555556 °C"), ("0C in F", "32 °F"),
            ("300K in C", "26.85 °C"), ("-40°F to °C", "-40 °C"), ("98.6 F to C", "37 °C"),
            ("sin(30)", "0.5"), ("2pi", "6.28318530717959"), ("15% of 80", "12"), ("1 GiB to MB", "1073.741824 MB")
        } )
        {
            var row = (await provider.SearchAsync( QueryParser.Parse( "=" + input ), new SearchRequest( ), default )).Single( );
            Equal( expected, row.Title );
            True( row.Details.All( field => field.Label is not "Exact value" and not "Exchange rates" ) );
            Equal( input, row.Subtitle );
        }
        foreach ( var input in new[] { "1 / 0", "5 kg to km", "100 usd to km", "100 USD to EUR to hex" } )
        {
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "=" + input ), new SearchRequest( ), default )).Count );
        }
    }

    private static async Task CalculatorProviderLazyRatesAsync( )
    {
        foreach ( var prefix in new[] { "", "=" } )
        {
            using CalculatorRateHandler handler = new( _ => Task.FromResult( CalculatorXmlResponse( ) ) );
            await using CurrencyRateService rates = new( CalculatorCachePath( ), true, handler: handler );
            CalculatorProvider provider = new( rates: rates.GetRates );

            foreach ( var input in new[] { "2 + 2", "5 km in miles", "5 km in paris" } )
            {
                await provider.SearchAsync( QueryParser.Parse( input ), new SearchRequest( ), default );
            }
            Equal( 0, handler.Calls );
            var query = prefix + "100 usd to eur";
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest( ), default )).Count );

            await rates.WhenIdleAsync( );

            var row = (await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest( ), default )).Single( );
            Equal( "€89.02", row.Title );
            Equal( 1, handler.Calls );
        }
    }
}
