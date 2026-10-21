using System.Text.RegularExpressions;

namespace CommandDeck;

// Bare search only opts into a bounded calculation grammar. Explicit '=' input
// keeps qalc's wider grammar for constants, functions and compound units.
public static partial class CalculatorInput
{
    public static bool IsInlineCandidate( ParsedQuery query ) =>
        query.ProviderFilter is null && !query.IsCommandMode &&
        query.IncludedFilters.Count == 0 && query.ExcludedFilters.Count == 0 &&
        !query.Expression.Clauses.Any( static clause => clause.Field is not null ) &&
        IsInlineCandidate( query.Original );

    private static bool IsInlineCandidate( string text )
    {
        if ( text.Length is 0 or > 512 )
        {
            return false;
        }

        return DateCounting.IsCandidate( text ) || IsArithmetic( text );
    }

    private static bool IsArithmetic( string text )
    {
        if ( text.Any( static c => !char.IsAsciiDigit( c ) && !char.IsWhiteSpace( c ) && !".+-*/%^()".Contains( c ) ) ||
            DigitGroupsRegex( ).IsMatch( text ) )
        {
            return false;
        }

        return BinaryOperatorRegex( ).IsMatch( text );
    }

    [GeneratedRegex( @"\A[+-]?[0-9]+(?:[-/][0-9]+)+\z" )]
    private static partial Regex DigitGroupsRegex( );

    [GeneratedRegex( @"[0-9)]\s*[-+*/%^]\s*(?:[-+]\s*)*[0-9.(]" )]
    private static partial Regex BinaryOperatorRegex( );
}
