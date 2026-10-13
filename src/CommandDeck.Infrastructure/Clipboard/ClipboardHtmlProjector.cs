using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace CommandDeck;

/// <summary>Called only by the sandbox worker. Produces inert text, never sanitized HTML.</summary>
public static class ClipboardHtmlProjector
{
    public const int MaximumInputBytes = 1024 * 1024;
    public const int MaximumTextBytes = 32768;
    private static readonly UTF8Encoding StrictUtf8 = new( false, true );
    private static readonly HashSet<string> Excluded = new( StringComparer.OrdinalIgnoreCase )
    {
        "head", "script", "style", "template", "object", "iframe"
    };
    private static readonly HashSet<string> Blocks = new( StringComparer.OrdinalIgnoreCase )
    {
        "address", "article", "aside", "blockquote", "div", "dl", "dt", "dd", "fieldset", "figcaption", "figure",
        "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "nav", "ol", "p", "pre",
        "section", "table", "tr", "ul"
    };

    public static ClipboardHtmlProjection Project( ReadOnlySpan<byte> bytes )
    {
        if ( bytes.Length is 0 or > MaximumInputBytes )
        {
            return Unavailable( "InputLimit" );
        }

        string html;
        try
        {
            html = StrictUtf8.GetString( bytes.StartsWith( "\uFEFF"u8 ) ? bytes[3..] : bytes );
        }
        catch ( DecoderFallbackException )
        {
            return Unavailable( "InvalidEncoding" );
        }

        using var context = BrowsingContext.New( Configuration.Default );
        HtmlParser parser = new( new HtmlParserOptions { IsScripting = false }, context );
        using var document = parser.ParseDocument( html );
        return ProjectDocument( document );
    }

    private static ClipboardHtmlProjection ProjectDocument( IDocument document )
    {
        Stack<Frame> pending = new( );
        pending.Push( new( document, 0, false, false, false ) );
        TextOutput output = new( );
        var visited = 0;
        while ( pending.TryPop( out var frame ) )
        {
            if ( frame.Exit )
            {
                output.Break( );
                continue;
            }

            if ( ++visited > 100000 || frame.Depth > 256 )
            {
                return Unavailable( "StructureLimit" );
            }

            if ( frame.Node.NextSibling is { } sibling )
            {
                pending.Push( frame with
                {
                    Node = sibling
                } );
            }

            var suppressed = frame.Suppressed;
            var preserve = frame.Preserve;
            if ( frame.Node is IElement element )
            {
                if ( element.LocalName.Equals( "meta", StringComparison.OrdinalIgnoreCase ) && !HasCompatibleCharset( element ) )
                {
                    return Unavailable( "ConflictingCharset" );
                }

                suppressed |= Excluded.Contains( element.LocalName ) || element.HasAttribute( "hidden" ) ||
                    string.Equals( element.GetAttribute( "aria-hidden" )?.Trim( ), "true", StringComparison.OrdinalIgnoreCase );
                preserve |= element.LocalName.Equals( "pre", StringComparison.OrdinalIgnoreCase );
                if ( !suppressed && (Blocks.Contains( element.LocalName ) || element.LocalName.Equals( "br", StringComparison.OrdinalIgnoreCase )) )
                {
                    output.Break( );
                    if ( Blocks.Contains( element.LocalName ) )
                    {
                        pending.Push( frame with
                        {
                            Exit = true
                        } );
                    }
                }
            }
            else if ( !suppressed && frame.Node is IText text )
            {
                output.Append( text.Data, preserve );
            }

            if ( frame.Node.FirstChild is { } child )
            {
                pending.Push( new( child, frame.Depth + 1, suppressed, preserve, false ) );
            }
        }

        return new( true, output.Text, output.Truncated );
    }

    private static bool HasCompatibleCharset( IElement element )
    {
        var charset = element.GetAttribute( "charset" );
        if ( charset is not null && !charset.Trim( ).Equals( "utf-8", StringComparison.OrdinalIgnoreCase ) )
        {
            return false;
        }

        if ( !string.Equals( element.GetAttribute( "http-equiv" )?.Trim( ), "content-type", StringComparison.OrdinalIgnoreCase ) )
        {
            return true;
        }

        var content = element.GetAttribute( "content" ) ?? "";
        foreach ( var parameter in content.Split( ';' ).Skip( 1 ) )
        {
            var pair = parameter.Split( '=', 2, StringSplitOptions.TrimEntries );
            if ( pair[0].Equals( "charset", StringComparison.OrdinalIgnoreCase ) &&
                (pair.Length != 2 || !pair[1].Trim( '\'', '"' ).Equals( "utf-8", StringComparison.OrdinalIgnoreCase )) )
            {
                return false;
            }
        }

        return true;
    }

    private static ClipboardHtmlProjection Unavailable( string reason ) => new( false, "", false, reason );
    private sealed record Frame( INode Node, int Depth, bool Suppressed, bool Preserve, bool Exit );

    private sealed class TextOutput
    {
        private readonly StringBuilder _text = new( );
        private int _bytes;
        private bool _space;
        private bool _break;
        internal bool Truncated { get; private set; }
        internal string Text => _text.ToString( );

        internal void Break( )
        {
            _space = false;
            _break = _text.Length > 0 && _text[^1] != '\n';
        }

        internal void Append( string text, bool preserve )
        {
            foreach ( var rune in text.EnumerateRunes( ) )
            {
                if ( !preserve && rune.Value is ' ' or '\t' or '\r' or '\n' or '\f' )
                {
                    _space = true;
                    continue;
                }

                if ( _break )
                {
                    Add( new Rune( '\n' ) );
                    _break = false;
                    _space = false;
                }

                if ( _space && _text.Length > 0 && _text[^1] != '\n' )
                {
                    Add( new Rune( ' ' ) );
                }

                _space = false;
                Add( rune );
            }
        }

        private void Add( Rune rune )
        {
            if ( Truncated || _bytes + rune.Utf8SequenceLength > MaximumTextBytes )
            {
                Truncated = true;
                return;
            }

            Span<char> encoded = stackalloc char[2];
            var length = rune.EncodeToUtf16( encoded );
            _text.Append( encoded[..length] );
            _bytes += rune.Utf8SequenceLength;
        }
    }
}
