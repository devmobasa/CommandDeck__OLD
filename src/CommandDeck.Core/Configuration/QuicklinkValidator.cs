namespace CommandDeck;

public static class QuicklinkValidator
{
    public const string TitleMessage = "Quicklink title must be 1-64 characters without controls.";
    public const string UrlLengthMessage = "Quicklink URL must be 1-2048 characters without controls.";
    public const string UrlMessage = "Quicklink URL must be an absolute HTTP or HTTPS URL without credentials; only {query} placeholders are supported.";
    public const string IconMessage = "Quicklink icon must be 1-256 characters without controls.";

    public static void Validate( QuicklinkOptions link )
    {
        if ( link is null )
        {
            throw new InvalidOperationException( "quicklinks must contain valid objects." );
        }
        if ( string.IsNullOrEmpty( link.Id ) || link.Id.Length > 64 ||
            !char.IsAsciiLetterOrDigit( link.Id[0] ) || link.Id.Any( character =>
                !char.IsAsciiLetterOrDigit( character ) && character is not '.' and not '_' and not '-' ) )
        {
            throw new InvalidOperationException( "Quicklink ID must use 1-64 ASCII letters, digits, dots, underscores, or hyphens, and must start with a letter or digit." );
        }
        if ( string.IsNullOrWhiteSpace( link.Title ) || link.Title.Length > 64 || link.Title.Any( char.IsControl ) )
        {
            throw new InvalidOperationException( TitleMessage );
        }
        ValidateUrl( link.UrlTemplate );
        if ( link.Alias is not null )
        {
            var alias = AliasValidator.NormalizeAndValidate( link.Alias );
            if ( alias != link.Alias )
            {
                throw new InvalidOperationException( AliasValidator.InvalidMessage );
            }
        }
        if ( link.Icon is not null && (link.Icon.Length is < 1 or > 256 || link.Icon.Any( char.IsControl )) )
        {
            throw new InvalidOperationException( IconMessage );
        }
    }

    private static void ValidateUrl( string template )
    {
        if ( string.IsNullOrWhiteSpace( template ) || template.Length > 2048 ||
            template.Any( char.IsControl ) )
        {
            throw new InvalidOperationException( UrlLengthMessage );
        }
        var expanded = template.Replace( "{query}", "sample", StringComparison.OrdinalIgnoreCase );
        if ( expanded.Contains( '{' ) || expanded.Contains( '}' ) ||
            !Uri.TryCreate( expanded, UriKind.Absolute, out var uri ) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty( uri.Host ) ||
            !string.IsNullOrEmpty( uri.UserInfo ) )
        {
            throw new InvalidOperationException( UrlMessage );
        }
    }

    public static void ValidateUnique( IEnumerable<QuicklinkOptions> links )
    {
        HashSet<string> ids = new( StringComparer.OrdinalIgnoreCase );
        HashSet<string> aliases = new( StringComparer.OrdinalIgnoreCase );
        foreach ( var link in links )
        {
            Validate( link );
            if ( !ids.Add( link.Id ) )
            {
                throw new InvalidOperationException( $"Quicklink ID '{link.Id}' is configured more than once." );
            }
            if ( link.Alias is not null && !aliases.Add( link.Alias ) )
            {
                throw new InvalidOperationException( $"Quicklink alias '{link.Alias}' is configured more than once." );
            }
        }
    }
}
