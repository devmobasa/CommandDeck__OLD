using System.Globalization;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardPreviewPreservesSourceAndUnicode( )
    {
        var text = "prefix\r\ncafe\u0301 👨‍👩‍👧‍👦 你好 <script>&\" suffix cafe\u0301";
        var query = QueryParser.Parse( "café -suffix metadata:unrelated" );
        var preview = ClipboardTextPreview.Create( text, query.Expression );
        Equal( 2, preview.MatchCount );
        Equal( 0, preview.MatchIndex!.Value );
        Equal( 0, preview.SelectedRange );
        Equal( text, preview.Text );
        Equal( "cafe\u0301", text.Substring( preview.Ranges[0].StartUtf16, preview.Ranges[0].LengthUtf16 ) );
        True( preview.Text.Contains( "\r\n", StringComparison.Ordinal ), "line endings remain exact" );
        True( preview.Text.Contains( "<script>&\"", StringComparison.Ordinal ), "markup remains unchanged inert source" );
        var family = ClipboardTextPreview.Create( text, QueryParser.Parse( "👩" ).Expression );
        Equal( "👨‍👩‍👧‍👦", family.Text.Substring( family.Ranges.Single( ).StartUtf16, family.Ranges.Single( ).LengthUtf16 ) );
        var metadata = ClipboardTextPreview.Create( text, QueryParser.Parse( "metadata:café -café" ).Expression );
        Equal( 0, metadata.MatchCount );
        Equal( 0, metadata.Ranges.Count );
    }

    private static void ClipboardPreviewBoundsEscapedSlicesAndCounts( )
    {
        var text = string.Concat( Enumerable.Repeat( "😀\r\n<quoted>&\" cafe\u0301 ", 900 ) );
        var preview = ClipboardTextPreview.Create( text, QueryParser.Parse( "café" ).Expression, 800 );
        Equal( 800, preview.MatchIndex!.Value );
        var elements = StringInfo.ParseCombiningCharacters( text ).Append( text.Length ).ToHashSet( );
        True( elements.Contains( preview.SliceStartUtf16 ), "slice starts at a text element" );
        True( elements.Contains( preview.SliceStartUtf16 + preview.Text.Length ), "slice ends at a text element" );
        Equal( text.Substring( preview.SliceStartUtf16, preview.Text.Length ), preview.Text );
        using var json = JsonDocument.Parse( JsonSerializer.Serialize( preview, CommandDeckJsonContext.Default.ClipboardPreviewSlice ) );
        True( System.Text.Encoding.UTF8.GetByteCount( json.RootElement.GetProperty( "text" ).GetRawText( ) ) <= 8192,
            "actual serialized text value fits its encoded budget" );
        True( preview.Ranges.All( range => range.StartUtf16 >= 0 && range.StartUtf16 + range.LengthUtf16 <= preview.Text.Length ), "all ranges are slice-relative" );
        True( preview.HasTextBefore && preview.HasTextAfter, "middle slice reports source context" );

        var repeated = ClipboardTextPreview.Create( string.Concat( Enumerable.Repeat( "a ", 1200 ) ), QueryParser.Parse( "a" ).Expression, 500 );
        Equal( 1000, repeated.MatchCount );
        True( repeated.MatchCountLimited && repeated.RangeCountLimited, "count and rendered range caps are explicit" );
        Equal( 256, repeated.Ranges.Count );
        True( repeated.Ranges.Any( range => range.StartUtf16 + repeated.SliceStartUtf16 == 1000 ), "selected hit survives the range cap" );
        Equal( 1000, repeated.Ranges[repeated.SelectedRange!.Value].StartUtf16 + repeated.SliceStartUtf16 );
    }

    private static void ClipboardPreviewRejectsIndivisibleOversizeText( )
    {
        var giant = "a" + new string( '\u0301', 2000 );
        foreach ( var query in new[] { "", "a" } )
        {
            var preview = ClipboardTextPreview.Create( giant, QueryParser.Parse( query ).Expression );
            Equal( "", preview.Text );
            Equal( 0, preview.Ranges.Count );
            Equal( "TextElementOrMatchTooLarge", preview.SliceReason );
            True( preview.SelectedRange is null, "an unpreviewable hit has no drawable range" );
            Equal( 0, preview.SliceStartUtf16 );
            if ( query.Length > 0 )
            {
                Equal( 0, preview.MatchIndex!.Value );
            }
        }

        var overlap = ClipboardTextPreview.Create( "banana", QueryParser.Parse( "ana banana" ).Expression );
        Equal( 1, overlap.MatchCount );
        Equal( new ClipboardMatchRange( 0, 6 ), overlap.Ranges.Single( ) );
        Equal( "", ClipboardTextPreview.Create( "", new QueryExpression( ) ).Text );
    }
}
