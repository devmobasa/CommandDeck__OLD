using System.Globalization;

namespace CommandDeck;

public static partial class FuzzyMatcher
{
    /// <summary>All contiguous matches using the launcher's existing folding, with whole text-element spans.</summary>
    public static IEnumerable<MatchedRange> FindContiguousRanges( string query, string candidate )
    {
        var term = Fold( query.Trim( ) ).Text;
        if ( term.Length == 0 )
        {
            yield break;
        }

        var folded = Fold( candidate );
        var boundaries = StringInfo.ParseCombiningCharacters( candidate ).Append( candidate.Length ).ToArray( );
        var offset = 0;
        while ( offset <= folded.Text.Length - term.Length )
        {
            var index = folded.Text.IndexOf( term, offset, StringComparison.OrdinalIgnoreCase );
            if ( index < 0 )
            {
                yield break;
            }

            var start = folded.OriginalIndices is null ? index : folded.OriginalIndices[index];
            var finalIndex = index + term.Length - 1;
            var final = folded.OriginalIndices is null ? finalIndex : folded.OriginalIndices[finalIndex];
            var startElement = Array.BinarySearch( boundaries, start );
            startElement = startElement < 0 ? ~startElement - 1 : startElement;
            var endElement = Array.BinarySearch( boundaries, final );
            endElement = endElement < 0 ? ~endElement : endElement + 1;
            yield return new( boundaries[startElement], boundaries[endElement] - boundaries[startElement] );
            offset = index + 1;
        }
    }
}
