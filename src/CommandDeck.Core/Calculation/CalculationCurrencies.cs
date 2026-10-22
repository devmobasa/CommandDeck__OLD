using System.Collections.Frozen;

namespace CommandDeck;

internal static class CalculationCurrencies
{
    private static readonly FrozenSet<string> Supported =
        "EUR USD JPY CZK DKK GBP HUF PLN RON SEK CHF ISK NOK TRY AUD BRL CAD CNY HKD IDR ILS INR KRW MXN MYR NZD PHP SGD THB ZAR"
            .Split( ' ' ).ToFrozenSet( StringComparer.OrdinalIgnoreCase );
    private static readonly FrozenSet<string> Unsupported =
        "btc bitcoin bitcoins eth ethereum rub ₽ ₿ aed bam twd".Split( ' ' ).ToFrozenSet( StringComparer.OrdinalIgnoreCase );
    private static readonly FrozenDictionary<string, string> Aliases = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
    {
        ["$"] = "USD",
        ["dollar"] = "USD",
        ["dollars"] = "USD",
        ["€"] = "EUR",
        ["euro"] = "EUR",
        ["euros"] = "EUR",
        ["£"] = "GBP",
        ["yen"] = "JPY",
        ["¥"] = "JPY",
        ["yuan"] = "CNY",
        ["rupee"] = "INR",
        ["rupees"] = "INR",
        ["₹"] = "INR",
        ["₩"] = "KRW",
        ["₪"] = "ILS"
    }.ToFrozenDictionary( StringComparer.OrdinalIgnoreCase );

    internal static readonly CurrencyRateSnapshot PlaceholderRates = new(
        DateOnly.MinValue, Supported.ToFrozenDictionary( code => code, _ => 1m, StringComparer.OrdinalIgnoreCase ) );

    internal static bool IsCurrency( string spelling ) => Aliases.ContainsKey( spelling ) || Supported.Contains( spelling );

    internal static string? Resolve( string spelling )
    {
        if ( Unsupported.Contains( spelling ) )
        {
            throw new FormatException( );
        }

        return Aliases.TryGetValue( spelling, out var code ) ? code :
            Supported.Contains( spelling ) ? spelling.ToUpperInvariant( ) : null;
    }

    internal static string? Symbol( string code ) => code switch
    {
        "USD" => "$",
        "EUR" => "€",
        "GBP" => "£",
        "JPY" => "¥",
        "INR" => "₹",
        "KRW" => "₩",
        "ILS" => "₪",
        _ => null
    };
}
