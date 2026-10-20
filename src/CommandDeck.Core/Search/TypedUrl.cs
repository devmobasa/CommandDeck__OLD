using System.Diagnostics.CodeAnalysis;

namespace CommandDeck;

/// <summary>
/// Recognizes a query that is one typed web address, so search can offer to
/// open it. Detection is conservative because the row ranks first:
/// <list type="bullet">
///   <item>The text is one ASCII token: no whitespace, user info, braces, or
///   characters a URL cannot hold, and valid percent-encoding.</item>
///   <item><c>http://</c> and <c>https://</c> are the only schemes. Any other
///   <c>scheme://</c> is rejected; text without a scheme gets <c>https://</c>.</item>
///   <item><see cref="Uri.TryCreate(string, UriKind, out Uri)"/> must accept it
///   with the host exactly as typed, so shorthand such as <c>1.5</c> (read as
///   IPv4 1.0.0.5) is not reinterpreted.</item>
///   <item>The host is a dotted name whose last label is two or more letters,
///   <c>localhost</c>, or a four-part IPv4 address. With an explicit scheme, a
///   single-label name or a bracketed IPv6 address is also accepted.</item>
///   <item>Without a scheme, the host must be followed by a port or a path, or
///   start with <c>www.</c>: <c>github.com/devmobasa</c> and <c>localhost:8080</c>
///   open, while <c>notes.md</c>, <c>file.txt</c>, and <c>example.com</c> stay
///   searches.</item>
/// </list>
/// The result is the system's absolute form of the address. Nothing is fetched.
/// </summary>
public static class TypedUrl
{
    private const string HttpsPrefix = "https://";

    public static bool TryParse( string? text, [NotNullWhen( true )] out string? url )
    {
        url = null;
        if ( !TryAddScheme( text?.Trim( ) ?? string.Empty, out var candidate, out var bare ) ||
            !ClipboardHttpUrl.TryParseSingleHttpUrl( candidate, out _, out _ ) ||
            !Uri.TryCreate( candidate, UriKind.Absolute, out var uri ) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Port <= 0 )
        {
            return false;
        }

        var authorityStart = candidate.IndexOf( "://", StringComparison.Ordinal ) + 3;
        var host = TypedHost( candidate, authorityStart );
        if ( !string.Equals( host, uri.Host, StringComparison.OrdinalIgnoreCase ) || !AcceptedHost( uri, host, bare ) ||
            (bare && !HasAddressShape( host, candidate[(authorityStart + host.Length)..] )) )
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    // Only http:// and https:// count as schemes; text without "://" gets https://.
    private static bool TryAddScheme( string text, out string candidate, out bool bare )
    {
        bare = !text.StartsWith( "http://", StringComparison.OrdinalIgnoreCase ) &&
            !text.StartsWith( HttpsPrefix, StringComparison.OrdinalIgnoreCase );
        candidate = bare ? HttpsPrefix + text : text;
        return text.Length > 0 && !(bare && text.Contains( "://", StringComparison.Ordinal ));
    }

    // The host as typed; Uri.Host is its canonical form.
    private static string TypedHost( string candidate, int authorityStart )
    {
        var authorityEnd = candidate.IndexOfAny( ['/', '?', '#'], authorityStart );
        var authority = authorityEnd < 0 ? candidate[authorityStart..] : candidate[authorityStart..authorityEnd];
        return authority.StartsWith( '[' ) ? authority[..(authority.IndexOf( ']' ) + 1)] : authority.Split( ':' )[0];
    }

    // Without a scheme, a port, a path, or a www. name marks text as an address
    // rather than a file name such as notes.md.
    private static bool HasAddressShape( string host, string afterHost ) =>
        afterHost.StartsWith( ':' ) || afterHost.StartsWith( '/' ) ||
        host.StartsWith( "www.", StringComparison.OrdinalIgnoreCase );

    private static bool AcceptedHost( Uri uri, string host, bool bare )
    {
        if ( uri.HostNameType == UriHostNameType.IPv6 )
        {
            return !bare;
        }

        var labels = host.Split( '.' );
        if ( uri.HostNameType == UriHostNameType.IPv4 )
        {
            return labels.Length == 4;
        }

        if ( uri.HostNameType != UriHostNameType.Dns || labels.Any( static label => label.Length == 0 ) )
        {
            return false;
        }

        if ( labels.Length == 1 )
        {
            return !bare || host.Equals( "localhost", StringComparison.OrdinalIgnoreCase );
        }

        return labels[^1].Length >= 2 && labels[^1].All( char.IsAsciiLetter );
    }
}
