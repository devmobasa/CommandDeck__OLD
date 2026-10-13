namespace CommandDeck;

public sealed class ApplicationInvocation
{
    public required string ApplicationQuery { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public static bool TryParse( ParsedQuery query, out ApplicationInvocation? invocation )
    {
        invocation = null;
        if ( query.IsCalculator || query.IsAiMode || query.IsCommandMode || query.Terms.Count < 2 )
        {
            return false;
        }

        var explicitApplication = query.Original.StartsWith(
                                      "app:",
                                      StringComparison.OrdinalIgnoreCase ) ||
                                  query.IncludesFilter( "applications" );
        if ( !explicitApplication && !string.IsNullOrWhiteSpace( query.ProviderFilter ) )
        {
            return false;
        }

        var arguments = query.Terms.Skip( 1 ).ToArray( );
        if ( arguments.Any( static argument => argument.Contains( '\u001f', StringComparison.Ordinal ) ) )
        {
            return false;
        }

        if ( !LooksLikeArgument( arguments[0] ) )
        {
            return false;
        }

        invocation = new ApplicationInvocation
        {
            ApplicationQuery = query.Terms[0],
            Arguments = arguments
        };
        return true;
    }

    private static bool LooksLikeArgument( string value )
    {
        if ( value.StartsWith( '-', StringComparison.Ordinal ) ||
            value.StartsWith( '~' ) ||
            value.StartsWith( '.' ) ||
            value.Contains( Path.DirectorySeparatorChar ) ||
            value.Contains( Path.AltDirectorySeparatorChar ) )
        {
            return true;
        }

        return Uri.TryCreate( value, UriKind.Absolute, out var uri ) &&
            !string.IsNullOrWhiteSpace( uri.Scheme );
    }
}
