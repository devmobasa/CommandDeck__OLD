using System.Globalization;
using System.Text.Json;

namespace CommandDeck;

public sealed record ClipboardMatchRange( int StartUtf16, int LengthUtf16 );
public sealed record ClipboardPreviewSlice(
    string Text,
    int SliceStartUtf16,
    IReadOnlyList<ClipboardMatchRange> Ranges,
    int MatchCount,
    bool MatchCountLimited,
    int? MatchIndex,
    bool RangeCountLimited,
    bool HasTextBefore,
    bool HasTextAfter,
    string? SliceReason,
    int? SelectedRange = null );

public static class ClipboardTextPreview
{
    public static ClipboardPreviewSlice Create( string text, QueryExpression expression, int requestedMatch = 0 )
    {
        ArgumentNullException.ThrowIfNull( text );
        var terms = expression.Clauses.Where( static clause => !clause.Excluded &&
            clause.Field is null or QueryFieldIds.Title or QueryFieldIds.Content ).Select( static clause => clause.Text );
        var matches = FindMatches( text, terms, out var limited );
        int? selected = matches.Count == 0 ? null : Math.Clamp( requestedMatch, 0, matches.Count - 1 );
        var anchor = selected.HasValue ? matches[selected.Value] : new MatchedRange( 0, 0 );
        var boundaries = StringInfo.ParseCombiningCharacters( text ).Append( text.Length ).ToArray( );
        var first = ElementAt( boundaries, anchor.Start );
        var after = selected.HasValue ? ElementAt( boundaries, anchor.Start + anchor.Length ) : Math.Min( 1, boundaries.Length - 1 );
        var left = ElementAt( boundaries, Math.Max( 0, anchor.Start - 1024 ) );
        var right = Math.Min( boundaries.Length - 1, ElementAt( boundaries, Math.Min( text.Length, anchor.Start + anchor.Length + 1024 ) ) + 1 );
        left = Math.Min( left, first );
        right = Math.Max( right, after );

        while ( JsonEncodedText.Encode( text[boundaries[left]..boundaries[right]] ).EncodedUtf8Bytes.Length + 2 > 8192 )
        {
            if ( left == first && right == after )
            {
                return new( "", anchor.Start, [], matches.Count, limited, selected, false,
                    anchor.Start > 0, anchor.Start < text.Length, "TextElementOrMatchTooLarge" );
            }

            if ( left < first && (right == after || anchor.Start - boundaries[left] > boundaries[right] - anchor.Start - anchor.Length) )
            {
                left++;
            }
            else
            {
                right--;
            }
        }

        var start = boundaries[left];
        var end = boundaries[right];
        var fitting = matches.Where( match => match.Start >= start && match.Start + match.Length <= end ).ToArray( );
        var ranges = fitting.OrderBy( match => selected.HasValue && match == anchor ? 0 : 1 )
            .ThenBy( static match => match.Start ).Take( 256 ).OrderBy( static match => match.Start )
            .Select( match => new ClipboardMatchRange( match.Start - start, match.Length ) ).ToArray( );
        int? selectedRange = selected.HasValue
            ? Array.FindIndex( ranges, range => range.StartUtf16 == anchor.Start - start && range.LengthUtf16 == anchor.Length ) : null;
        return new( text[start..end], start, ranges, matches.Count, limited, selected, fitting.Length > ranges.Length,
            start > 0, end < text.Length, null, selectedRange is >= 0 ? selectedRange : null );
    }

    private static int ElementAt( int[] boundaries, int position )
    {
        var index = Array.BinarySearch( boundaries, position );
        return index < 0 ? ~index - 1 : index;
    }

    private static List<MatchedRange> FindMatches( string text, IEnumerable<string> terms, out bool limited )
    {
        List<IEnumerator<MatchedRange>> iterators = [];
        PriorityQueue<IEnumerator<MatchedRange>, int> queue = new( );
        List<MatchedRange> merged = [];
        limited = false;
        try
        {
            foreach ( var term in terms.Distinct( StringComparer.OrdinalIgnoreCase ) )
            {
                var iterator = FuzzyMatcher.FindContiguousRanges( term, text ).GetEnumerator( );
                iterators.Add( iterator );
                if ( iterator.MoveNext( ) )
                {
                    queue.Enqueue( iterator, iterator.Current.Start );
                }
            }

            while ( queue.TryDequeue( out var iterator, out _ ) )
            {
                var range = iterator.Current;
                if ( merged.Count > 0 && range.Start < merged[^1].Start + merged[^1].Length )
                {
                    var previous = merged[^1];
                    merged[^1] = new( previous.Start, Math.Max( previous.Start + previous.Length, range.Start + range.Length ) - previous.Start );
                }
                else if ( merged.Count == 1000 )
                {
                    limited = true;
                    break;
                }
                else
                {
                    merged.Add( range );
                }

                if ( iterator.MoveNext( ) )
                {
                    queue.Enqueue( iterator, iterator.Current.Start );
                }
            }
        }
        finally
        {
            foreach ( var iterator in iterators )
            {
                iterator.Dispose( );
            }
        }

        return merged;
    }
}
