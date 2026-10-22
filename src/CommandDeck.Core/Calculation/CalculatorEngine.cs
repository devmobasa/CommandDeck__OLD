namespace CommandDeck;

public static class CalculatorEngine
{
    public const int MaximumExpressionLength = 512;

    public static bool IsConversion( string expression )
    {
        // Placeholder rates validate compatibility only; their values never become search results.
        var result = Evaluate( expression, static ( ) => CalculationCurrencies.PlaceholderRates );

        return result.Status == CalculationStatus.Success && result.IsConversion;
    }

    public static CalculationResult Evaluate( string expression, Func<CurrencyRateSnapshot?>? rates = null )
    {
        var conversion = false;

        try
        {
            if ( string.IsNullOrWhiteSpace( expression ) || expression.Length > MaximumExpressionLength )
            {
                return new( ) { Status = CalculationStatus.Invalid };
            }

            var tokens = CalculationLexer.Tokenize( expression );
            NormalizeTemperatureShorthand( tokens );
            var context = new CalculationContext( rates );
            CalculationQuantity? target = null;
            string? numberBase = null;
            var split = -1;
            var depth = 0;
            List<int> candidates = [];

            for ( var i = 0; i < tokens.Length; i++ )
            {
                depth += tokens[i].Text == "(" ? 1 : tokens[i].Text == ")" ? -1 : 0;
                if ( depth == 0 && tokens[i].Lower is "to" or "in" or "as" or "->" or "→" )
                {
                    candidates.Add( i );
                }
            }

            // The rightmost complete target wins; the parser restricts remaining in to inches.
            foreach ( var candidate in candidates.AsEnumerable( ).Reverse( ) )
            {
                var remainder = tokens[(candidate + 1)..];
                if ( remainder.Length == 1 && CalculationFormatter.IsBase( remainder[0].Lower ) )
                {
                    split = candidate;
                    numberBase = remainder[0].Lower;
                    break;
                }

                try
                {
                    // Set before parsing so a target that needs missing rates still counts.
                    conversion = true;
                    target = new CalculationParser( remainder, context, target: true ).Parse( );
                    split = candidate;
                    break;
                }
                catch ( Exception exception ) when ( exception is FormatException or ArithmeticException )
                {
                    conversion = false;
                }
            }

            if ( split >= 0 )
            {
                conversion = true;
                tokens = tokens[..split];
            }

            var quantity = new CalculationParser( tokens, context ).Parse( );

            if ( target is not null )
            {
                if ( quantity.Units is [{ Unit.Symbol: "mpg", Power: 1 }] && target.Units is [{ Unit.Symbol: "L/100km", Power: 1 }] ||
                    quantity.Units is [{ Unit.Symbol: "L/100km", Power: 1 }] && target.Units is [{ Unit.Symbol: "mpg", Power: 1 }] )
                {
                    var reciprocal = CalculationNumber.Binary( new( 1m ), quantity.BaseValue, '/' );
                    quantity = target with { Value = CalculationNumber.Binary( reciprocal, target.Factor, '/' ), UnitOnly = false };
                }
                else
                {
                    quantity = quantity.ConvertTo( target );
                }
            }

            var text = numberBase is not null ? CalculationFormatter.Base( quantity, numberBase ) : CalculationFormatter.Format( quantity );

            return new( )
            {
                Status = CalculationStatus.Success,
                Text = text,
                PreciseText = CalculationFormatter.CurrencyCode( quantity ) is { } code
                    ? CalculationFormatter.Number( quantity.Value, preserveInteger: false ) + " " + code : null,
                RatesDate = context.Snapshot?.Date,
                IsConversion = conversion
            };
        }
        catch ( CalculationRatesUnavailableException )
        {
            return new( ) { Status = CalculationStatus.RatesUnavailable, IsConversion = conversion };
        }
        catch ( Exception )
        {
            return new( ) { Status = CalculationStatus.Invalid, IsConversion = conversion };
        }
    }

    private static void NormalizeTemperatureShorthand( CalculationToken[] tokens )
    {
        var offset = tokens.Length > 0 && tokens[0].Text is "+" or "-" ? 1 : 0;
        if ( tokens.Length != offset + 4 || !tokens[offset].Number || tokens[offset + 2].Lower is not ("to" or "in") )
        {
            return;
        }

        static string? Temperature( string text ) => text.TrimStart( '°' ).ToLowerInvariant( ) switch
        {
            "c" => "°C",
            "f" => "°F",
            "k" => "K",
            _ => null
        };

        var source = Temperature( tokens[offset + 1].Text );
        var target = Temperature( tokens[offset + 3].Text );
        if ( source is not null && target is not null )
        {
            tokens[offset + 1] = new( source );
            tokens[offset + 3] = new( target );
        }
    }
}

public enum CalculationStatus { Success, Invalid, RatesUnavailable }

public sealed record CalculationResult
{
    public required CalculationStatus Status { get; init; }
    public string? Text { get; init; }
    public string? PreciseText { get; init; }
    public DateOnly? RatesDate { get; init; }
    // True when a to/in/as target was parsed; inline detection promotes only conversions.
    public bool IsConversion { get; init; }
}

public sealed record CurrencyRateSnapshot( DateOnly Date, IReadOnlyDictionary<string, decimal> PerEuro );
