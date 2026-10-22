using System.Globalization;

namespace CommandDeck;

public sealed class CalculatorProvider : ICommandProvider
{
    private readonly Func<CurrencyRateSnapshot?>? _rates;
    private readonly Func<bool> _inlineEnabled;
    private readonly TimeProvider _clock;

    public CalculatorProvider(
        CalculatorOptions? options = null,
        TimeProvider? clock = null,
        Func<bool>? inlineEnabled = null,
        Func<CurrencyRateSnapshot?>? rates = null )
    {
        var resolved = options ?? new( );
        _inlineEnabled = inlineEnabled ?? (( ) => resolved.Inline);
        _clock = clock ?? TimeProvider.System;
        _rates = rates;
    }

    public string Id => ProviderIds.Calculator;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        var inline = CalculatorInput.IsInlineCandidate( query );
        var text = query.IsCalculator ? query.Text : query.Original;
        if ( (!query.IsCalculator && !(inline && _inlineEnabled( ))) || string.IsNullOrWhiteSpace( text ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        if ( DateCounting.TryEvaluate( text, _clock, out var date ) && date is not null )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [CreateResult( text, date.Title, date.Subtitle, date.CopyText )] );
        }
        if ( DateCounting.IsCandidate( text ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        var result = CalculatorEngine.Evaluate( text, _rates );
        if ( result.Status != CalculationStatus.Success )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [CreateResult( text, result.Text!, exact: result.PreciseText, ratesDate: result.RatesDate )] );
    }

    private SearchResult CreateResult( string expression, string formatted, string? subtitle = null, string? copyText = null, string? exact = null, DateOnly? ratesDate = null ) => new( )
    {
        Id = $"calculator:{expression}",
        Kind = ResultKinds.Calculator,
        Title = formatted,
        Subtitle = subtitle ?? expression,
        Icon = "accessories-calculator",
        ProviderId = Id,
        SourceId = SourceIds.Calculator,
        Score = 1,
        Details = [new DetailField( "Expression", expression, Monospace: true ),
            .. exact is null ? Array.Empty<DetailField>( ) : [new DetailField( "Exact value", exact, Monospace: true )],
            .. ratesDate is null ? Array.Empty<DetailField>( ) : [new DetailField( "Exchange rates", "ECB reference rates, " + ratesDate.Value.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture ) )]],
        Actions =
        [
            new CommandAction
            {
                Id = "copy",
                Title = "Copy Result",
                Kind = ActionKinds.CopyText,
                Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["text"] = copyText ?? formatted
                }
            }
        ]
    };
}
