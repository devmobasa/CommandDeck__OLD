using System.Text;

namespace CommandDeck;

public sealed partial class FilePreviewService
{
    private const int MaximumScanBytes = 1024 * 1024;
    private const int MaximumScanLines = 10000;
    private const int MaximumMatches = 256;
    private const int ContextLines = 5;

    private static async ValueTask<FilePreviewOutcome> ReadMatchesAsync(
        SafeRegularFile opened,
        string[] terms,
        int matchIndex,
        CancellationToken cancellationToken )
    {
        if ( matchIndex is < 0 or >= MaximumMatches )
        {
            return FilePreviewOutcome.Failure( "The preview match is out of range." );
        }

        var buffer = new byte[MaximumScanBytes + 1];
        var bytesRead = await opened.ReadPrefixAsync( buffer, cancellationToken ).ConfigureAwait( false );
        var length = Math.Min( bytesRead, MaximumScanBytes );
        if ( buffer.AsSpan( 0, length ).Contains( ( byte ) 0 ) )
        {
            return FilePreviewOutcome.Failure( "Preview is unavailable for binary files." );
        }

        var scanTruncated = bytesRead > MaximumScanBytes;
        var text = Encoding.UTF8.GetString( buffer, 0, length );
        using var reader = new StringReader( text );
        List<string> lines = [];
        List<(int Line, int Column)> matches = [];
        while ( lines.Count < MaximumScanLines && reader.ReadLine( ) is { } line )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            lines.Add( line );
            var column = FirstMatchColumn( line, terms );
            if ( column > 0 )
            {
                if ( matches.Count == MaximumMatches )
                {
                    scanTruncated = true;
                    break;
                }

                matches.Add( (lines.Count, column) );
            }
        }

        scanTruncated |= reader.Peek( ) >= 0;
        if ( matches.Count == 0 )
        {
            var prefix = LimitPreviewBytes( text );
            (prefix, var lineTruncated) = TruncateLines( prefix );
            return FilePreviewOutcome.Success( new FilePreview
            {
                Text = prefix,
                Language = LanguageFor( opened.CanonicalPath ),
                Truncated = lineTruncated || prefix.Length < text.Length || scanTruncated,
                ScanTruncated = scanTruncated
            } );
        }

        // A file can change between navigation requests. Clamp to its last remaining match.
        matchIndex = Math.Min( matchIndex, matches.Count - 1 );
        var match = matches[matchIndex];
        var start = Math.Max( 0, match.Line - 1 - ContextLines );
        var end = Math.Min( lines.Count, match.Line + ContextLines );
        var context = string.Join( '\n', lines.GetRange( start, end - start ) );
        while ( Encoding.UTF8.GetByteCount( context ) > ProtocolLimits.MaximumPreviewBytes &&
            end - start > 1 )
        {
            if ( match.Line - 1 - start > end - match.Line )
            {
                start++;
            }
            else
            {
                end--;
            }

            context = string.Join( '\n', lines.GetRange( start, end - start ) );
        }

        if ( Encoding.UTF8.GetByteCount( context ) > ProtocolLimits.MaximumPreviewBytes )
        {
            // Keep the match visible even on a minified or unusually long line.
            var offset = Math.Max( 0, match.Column - 1 - 128 );
            if ( offset > 0 && char.IsLowSurrogate( context[offset] ) )
            {
                offset--;
            }

            context = (offset > 0 ? "…" : "") + context[offset..];
            context = LimitPreviewBytes( context );
        }

        return FilePreviewOutcome.Success( new FilePreview
        {
            Text = context,
            Language = LanguageFor( opened.CanonicalPath ),
            Truncated = start > 0 || end < lines.Count || context.Length < text.Length || scanTruncated,
            StartLine = start + 1,
            MatchLine = match.Line,
            MatchColumn = match.Column,
            MatchIndex = matchIndex,
            MatchCount = matches.Count,
            ScanTruncated = scanTruncated
        } );
    }

    private static int FirstMatchColumn( string line, string[] terms )
    {
        var first = int.MaxValue;
        foreach ( var term in terms )
        {
            if ( FuzzyMatcher.ScorePhrase( term, line ) <= 0 )
            {
                continue;
            }

            var ranges = FuzzyMatcher.FindRanges( term, line );
            if ( ranges.Count > 0 )
            {
                first = Math.Min( first, ranges[0].Start + 1 );
            }
        }

        return first == int.MaxValue ? 0 : first;
    }

    private static string LimitPreviewBytes( string text )
    {
        if ( Encoding.UTF8.GetByteCount( text ) <= ProtocolLimits.MaximumPreviewBytes )
        {
            return text;
        }

        var bytes = 0;
        var characters = 0;
        foreach ( var rune in text.EnumerateRunes( ) )
        {
            if ( bytes + rune.Utf8SequenceLength > ProtocolLimits.MaximumPreviewBytes )
            {
                break;
            }

            bytes += rune.Utf8SequenceLength;
            characters += rune.Utf16SequenceLength;
        }

        return text[..characters];
    }
}
