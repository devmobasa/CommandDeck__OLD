using System.Globalization;
using System.Text;

namespace CommandDeck;

public static partial class FuzzyMatcher
{
    private const int MaximumTypoTokenLength = 48;

    public static double Score( string query, string candidate ) =>
        Evaluate( query, candidate, includeRanges: false ).Score;

    public static double ScorePhrase( string query, string candidate ) =>
        Evaluate( query, candidate, includeRanges: false, requireContiguous: true ).Score;

    public static List<MatchedRange> FindRanges( string query, string candidate ) =>
        Evaluate( query, candidate, includeRanges: true ).Ranges ?? [];

    internal static int SearchTextLength( string query ) =>
        new StringInfo( Fold( query.Trim( ) ).Text ).LengthInTextElements;

    // Uses exactly the same folding and word boundaries as lexical matching.
    public static bool IsStrongPrefix( string query, string candidate )
    {
        var term = Fold( query.Trim( ) ).Text;
        var text = Fold( candidate ).Text;
        if ( term.Length == 0 )
        {
            return false;
        }
        if ( text.StartsWith( term, StringComparison.OrdinalIgnoreCase ) )
        {
            return true;
        }
        for ( var index = 0; index <= text.Length - term.Length; index++ )
        {
            if ( IsWordStart( text, index ) &&
                text.AsSpan( index ).StartsWith( term, StringComparison.OrdinalIgnoreCase ) )
            {
                return true;
            }
        }
        return false;
    }

    private static MatchEvaluation Evaluate(
        string query,
        string candidate,
        bool includeRanges,
        bool requireContiguous = false )
    {
        if ( string.IsNullOrWhiteSpace( query ) )
        {
            return new MatchEvaluation( 0.15, null );
        }

        if ( string.IsNullOrEmpty( candidate ) )
        {
            return default;
        }

        var foldedQuery = Fold( query.Trim( ) );
        var foldedCandidate = Fold( candidate );
        if ( foldedQuery.Text.Length == 0 )
        {
            return default;
        }

        var match = EvaluateFolded(
            foldedQuery.Text,
            foldedCandidate.Text,
            includeRanges,
            requireContiguous );
        return includeRanges && match.Ranges is not null
            ? match with
            {
                Ranges = foldedCandidate.MapRanges( match.Ranges )
            }
            : match;
    }

    private static MatchEvaluation EvaluateFolded(
        string normalizedQuery,
        string candidate,
        bool includeRanges,
        bool requireContiguous )
    {
        var direct = candidate.IndexOf( normalizedQuery, StringComparison.OrdinalIgnoreCase );
        if ( direct >= 0 )
        {
            if ( candidate.Length == normalizedQuery.Length )
            {
                return new MatchEvaluation(
                    1,
                    includeRanges ? [new MatchedRange( 0, normalizedQuery.Length )] : null );
            }

            var prefix = direct == 0;
            var boundary = !prefix && IsWordStart( candidate, direct );
            var directScore = prefix ? 0.94 : boundary ? 0.86 : 0.78;
            var lengthPenalty = Math.Min(
                0.06,
                (candidate.Length - normalizedQuery.Length) * 0.002 );
            var minimumScore = prefix ? 0.88 : boundary ? 0.80 : 0.70;
            return new MatchEvaluation(
                Math.Clamp(
                    directScore - lengthPenalty - (direct * 0.001),
                    minimumScore,
                    0.95 ),
                includeRanges ? [new MatchedRange( direct, normalizedQuery.Length )] : null );
        }

        if ( requireContiguous )
        {
            return default;
        }

        if ( TryMatchAcronym(
            normalizedQuery,
            candidate,
            includeRanges,
            out var acronym ) )
        {
            return acronym;
        }

        if ( TryMatchTypo(
            normalizedQuery,
            candidate,
            includeRanges,
            out var typo ) )
        {
            return typo;
        }

        return MatchSubsequence( normalizedQuery, candidate, includeRanges );
    }

    private static bool TryMatchAcronym(
        string query,
        string candidate,
        bool includeRanges,
        out MatchEvaluation match )
    {
        match = default;
        if ( query.Length < 2 || query.Any( static character => !char.IsLetterOrDigit( character ) ) )
        {
            return false;
        }

        List<MatchedRange>? ranges = includeRanges ? [] : null;
        var queryIndex = 0;
        var boundaryCount = 0;
        for ( var index = 0; index < candidate.Length; index++ )
        {
            if ( !IsWordStart( candidate, index ) )
            {
                continue;
            }

            boundaryCount++;
            if ( char.ToUpperInvariant( candidate[index] ) !=
                char.ToUpperInvariant( query[queryIndex] ) )
            {
                continue;
            }

            ranges?.Add( new MatchedRange( index, 1 ) );
            queryIndex++;
            if ( queryIndex == query.Length )
            {
                var coverage = query.Length / ( double ) Math.Max( query.Length, boundaryCount );
                match = new MatchEvaluation(
                    Math.Clamp( 0.66 + (coverage * 0.04), 0.66, 0.70 ),
                    ranges );
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchTypo(
        string query,
        string candidate,
        bool includeRanges,
        out MatchEvaluation match )
    {
        match = default;
        if ( query.Length < 4 || query.Length > MaximumTypoTokenLength ||
            query.Any( static character => !char.IsLetterOrDigit( character ) ) )
        {
            return false;
        }

        var maximumDistance = query.Length >= 8 ? 2 : 1;
        var bestDistance = maximumDistance + 1;
        var bestStart = -1;
        var bestLength = 0;

        var spanStart = -1;
        for ( var index = 0; index <= candidate.Length; index++ )
        {
            var atEnd = index == candidate.Length;
            var startsNewWord = !atEnd && IsWordStart( candidate, index ) && spanStart >= 0;
            var isWordCharacter = !atEnd && char.IsLetterOrDigit( candidate[index] );

            if ( spanStart < 0 && isWordCharacter )
            {
                spanStart = index;
                continue;
            }

            if ( spanStart < 0 || (isWordCharacter && !startsNewWord) )
            {
                continue;
            }

            var spanLength = index - spanStart;
            if ( Math.Abs( spanLength - query.Length ) <= maximumDistance )
            {
                var distance = BoundedDamerauLevenshtein(
                    query,
                    candidate.AsSpan( spanStart, spanLength ),
                    maximumDistance );
                if ( distance < bestDistance )
                {
                    bestDistance = distance;
                    bestStart = spanStart;
                    bestLength = spanLength;
                }
            }

            spanStart = isWordCharacter ? index : -1;
        }

        if ( bestStart < 0 || bestDistance > maximumDistance )
        {
            return false;
        }

        var score = 0.63 - (bestDistance * 0.04) -
                    (Math.Abs( bestLength - query.Length ) * 0.01);
        match = new MatchEvaluation(
            Math.Clamp( score, 0.53, 0.59 ),
            includeRanges ? [new MatchedRange( bestStart, bestLength )] : null );
        return true;
    }

    private static MatchEvaluation MatchSubsequence(
        string query,
        string candidate,
        bool includeRanges )
    {
        List<int>? positions = includeRanges ? [] : null;
        var queryIndex = 0;
        var previousMatch = -2;
        var consecutive = 0;
        double score = 0;

        for ( var candidateIndex = 0;
             candidateIndex < candidate.Length && queryIndex < query.Length;
             candidateIndex++ )
        {
            if ( char.ToUpperInvariant( candidate[candidateIndex] ) !=
                char.ToUpperInvariant( query[queryIndex] ) )
            {
                continue;
            }

            positions?.Add( candidateIndex );
            score += 1;
            if ( candidateIndex == previousMatch + 1 )
            {
                consecutive++;
                score += 0.7 + (consecutive * 0.08);
            }
            else
            {
                consecutive = 0;
            }

            if ( IsWordStart( candidate, candidateIndex ) )
            {
                score += 0.9;
            }

            previousMatch = candidateIndex;
            queryIndex++;
        }

        if ( queryIndex != query.Length )
        {
            return default;
        }

        var normalized = score /
                         Math.Max( query.Length * 2.8, candidate.Length * 0.25 );
        return new MatchEvaluation(
            Math.Clamp( normalized, 0.05, 0.52 ),
            includeRanges ? CollapseRanges( positions! ) : null );
    }

    private static List<MatchedRange> CollapseRanges( List<int> positions )
    {
        List<MatchedRange> ranges = [];
        var start = positions[0];
        var length = 1;
        for ( var index = 1; index < positions.Count; index++ )
        {
            if ( positions[index] == positions[index - 1] + 1 )
            {
                length++;
            }
            else
            {
                ranges.Add( new MatchedRange( start, length ) );
                start = positions[index];
                length = 1;
            }
        }

        ranges.Add( new MatchedRange( start, length ) );
        return ranges;
    }

    private static int BoundedDamerauLevenshtein(
        ReadOnlySpan<char> left,
        ReadOnlySpan<char> right,
        int maximumDistance )
    {
        if ( Math.Abs( left.Length - right.Length ) > maximumDistance )
        {
            return maximumDistance + 1;
        }

        Span<int> previousPrevious = stackalloc int[right.Length + 1];
        Span<int> previous = stackalloc int[right.Length + 1];
        Span<int> current = stackalloc int[right.Length + 1];
        for ( var rightIndex = 0; rightIndex <= right.Length; rightIndex++ )
        {
            previous[rightIndex] = rightIndex;
        }

        for ( var leftIndex = 1; leftIndex <= left.Length; leftIndex++ )
        {
            current[0] = leftIndex;
            for ( var rightIndex = 1; rightIndex <= right.Length; rightIndex++ )
            {
                var substitutionCost = char.ToUpperInvariant( left[leftIndex - 1] ) ==
                                       char.ToUpperInvariant( right[rightIndex - 1] )
                    ? 0
                    : 1;
                var distance = Math.Min(
                    previous[rightIndex] + 1,
                    Math.Min(
                        current[rightIndex - 1] + 1,
                        previous[rightIndex - 1] + substitutionCost ) );

                if ( leftIndex > 1 && rightIndex > 1 &&
                    char.ToUpperInvariant( left[leftIndex - 1] ) ==
                    char.ToUpperInvariant( right[rightIndex - 2] ) &&
                    char.ToUpperInvariant( left[leftIndex - 2] ) ==
                    char.ToUpperInvariant( right[rightIndex - 1] ) )
                {
                    distance = Math.Min( distance, previousPrevious[rightIndex - 2] + 1 );
                }

                current[rightIndex] = distance;
            }

            var oldPreviousPrevious = previousPrevious;
            previousPrevious = previous;
            previous = current;
            current = oldPreviousPrevious;
        }

        var result = previous[right.Length];
        return result <= maximumDistance ? result : maximumDistance + 1;
    }

    private static bool IsWordStart( string value, int index )
    {
        if ( index < 0 || index >= value.Length || !char.IsLetterOrDigit( value[index] ) )
        {
            return false;
        }

        if ( index == 0 || !char.IsLetterOrDigit( value[index - 1] ) )
        {
            return true;
        }

        var current = value[index];
        var previous = value[index - 1];
        if ( char.IsDigit( current ) != char.IsDigit( previous ) )
        {
            return true;
        }

        if ( char.IsUpper( current ) && char.IsLower( previous ) )
        {
            return true;
        }

        return char.IsUpper( current ) && char.IsUpper( previous ) &&
            index + 1 < value.Length && char.IsLower( value[index + 1] );
    }

    private static FoldedText Fold( string value )
    {
        if ( value.All( static character => character <= 0x7f ) )
        {
            return new FoldedText( value, null, value.Length );
        }

        StringBuilder text = new( value.Length );
        List<int> originalIndices = new( value.Length );
        var elements = StringInfo.GetTextElementEnumerator( value );
        while ( elements.MoveNext( ) )
        {
            var originalIndex = elements.ElementIndex;
            var decomposed = elements.GetTextElement( ).Normalize( NormalizationForm.FormD );
            var foldLatinDiacritics = IsLatinBase( decomposed );
            for ( var index = 0; index < decomposed.Length; index++ )
            {
                var category = CharUnicodeInfo.GetUnicodeCategory( decomposed, index );
                var characterLength = char.IsHighSurrogate( decomposed[index] ) &&
                                      index + 1 < decomposed.Length && char.IsLowSurrogate( decomposed[index + 1] )
                        ? 2
                        : 1;
                if ( foldLatinDiacritics && category is UnicodeCategory.NonSpacingMark )
                {
                    index += characterLength - 1;
                    continue;
                }

                for ( var offset = 0; offset < characterLength; offset++ )
                {
                    text.Append( decomposed[index + offset] );
                    originalIndices.Add( originalIndex );
                }

                index += characterLength - 1;
            }
        }

        return new FoldedText( text.ToString( ), originalIndices.ToArray( ), value.Length );
    }

    private static bool IsLatinBase( string value )
    {
        if ( !Rune.TryGetRuneAt( value, 0, out var rune ) )
        {
            return false;
        }

        var scalar = rune.Value;
        return scalar is >= 0x0041 and <= 0x024f or
            >= 0x1e00 and <= 0x1eff or
            >= 0x2c60 and <= 0x2c7f or
            >= 0xa720 and <= 0xa7ff or
            >= 0xab30 and <= 0xab6f;
    }

    private readonly record struct MatchEvaluation( double Score, List<MatchedRange>? Ranges );

    private readonly record struct FoldedText(
        string Text,
        int[]? OriginalIndices,
        int OriginalLength )
    {
        public List<MatchedRange> MapRanges( List<MatchedRange> ranges )
        {
            if ( OriginalIndices is null )
            {
                return [.. ranges];
            }

            List<MatchedRange> mapped = new( ranges.Count );
            foreach ( var range in ranges )
            {
                var start = OriginalIndices[range.Start];
                var foldedEnd = range.Start + range.Length;
                var end = foldedEnd < OriginalIndices.Length
                    ? OriginalIndices[foldedEnd]
                    : OriginalLength;
                mapped.Add( new MatchedRange( start, Math.Max( 1, end - start ) ) );
            }

            return mapped;
        }
    }
}
