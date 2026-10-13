using System.Text.RegularExpressions;

namespace CommandDeck;

public sealed partial class CalculatorProvider : ICommandProvider
{
    private readonly QalcEvaluator _qalc;
    private readonly bool _useQalc;

    public CalculatorProvider( )
        : this( runner: null, available: null, options: null, clock: null )
    {
    }

    public CalculatorProvider(
        ExternalProcessRunner? runner,
        Func<bool>? available,
        CalculatorOptions? options = null,
        TimeProvider? clock = null )
    {
        var resolved = options ?? new( );
        _useQalc = resolved.AllowsQalc( );
        var qalcAvailable = resolved.RequiresQalc( )
            ? static ( ) => true
            : available;
        _qalc = new QalcEvaluator( runner, qalcAvailable, clock );
    }

    public string Id => ProviderIds.Calculator;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !query.IsCalculator || string.IsNullOrWhiteSpace( query.Text ) )
        {
            return [];
        }

        // Rewrite bare temperature shorthand (105F in C) to qalc's canonical form
        // (105 °F to °C) before evaluating; otherwise qalc reads F/C as farad and
        // coulomb and `in` as inches. Non-temperature input is returned unchanged,
        // and the normalized text is what we display so the interpretation is
        // transparent to the user.
        var expression = NormalizeTemperatureConversion( query.Text );

        if ( Calculator.TryEvaluate( expression, out var value, out _ ) )
        {
            return [CreateResult( expression, Calculator.Format( value ) )];
        }

        if ( !_useQalc )
        {
            return [];
        }

        var qalcValue = await _qalc.TryEvaluateAsync( expression, cancellationToken )
            .ConfigureAwait( false );
        return qalcValue is null ? [] : [CreateResult( expression, qalcValue )];
    }

    // Matches a whole-string temperature conversion of the form
    // "<number>[°]<letter> (in|to) [°]<letter>" where each letter is F, C, or K.
    // The full-string anchor plus the single-letter units keep it from touching
    // farad/coulomb math (5 farad in coulomb), metric length (5m in cm), a lone
    // "100C", or plain arithmetic.
    [GeneratedRegex(
        @"^\s*([-+]?\d+(?:\.\d+)?)\s*°?\s*([FCK])\s+(?:in|to)\s+°?\s*([FCK])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )]
    private static partial Regex TemperatureConversionRegex( );

    internal static string NormalizeTemperatureConversion( string expression )
    {
        var match = TemperatureConversionRegex( ).Match( expression );
        if ( !match.Success )
        {
            return expression;
        }
        var number = match.Groups[1].Value;
        var from = CanonicalTemperatureUnit( match.Groups[2].Value );
        var to = CanonicalTemperatureUnit( match.Groups[3].Value );
        return $"{number} {from} to {to}";
    }

    // qalc spells Fahrenheit and Celsius with a degree sign but Kelvin without one.
    private static string CanonicalTemperatureUnit( string letter ) => char.ToUpperInvariant( letter[0] ) switch
    {
        'F' => "°F",
        'C' => "°C",
        'K' => "K",
        _ => letter
    };

    private SearchResult CreateResult( string expression, string formatted ) => new( )
    {
        Id = $"calculator:{expression}",
        Kind = ResultKinds.Calculator,
        Title = formatted,
        Subtitle = expression,
        Icon = "accessories-calculator",
        ProviderId = Id,
        SourceId = SourceIds.Calculator,
        Score = 1,
        Details = [new DetailField( "Expression", expression, Monospace: true )],
        Actions =
        [
            new CommandAction
            {
                Id = "copy",
                Title = "Copy Result",
                Kind = ActionKinds.CopyText,
                Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["text"] = formatted
                }
            }
        ]
    };
}
