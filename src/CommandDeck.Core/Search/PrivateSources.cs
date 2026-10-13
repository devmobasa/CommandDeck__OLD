namespace CommandDeck;

/// <summary>
/// Sources whose rows and queries stay inside the interactive launcher:
/// clipboard history and HEY mail. A private row is never recorded in usage or
/// query history, offered as a suggestion, saved as a search, shown on Home,
/// logged, or given to the assistant, AI attachments, or assistant tools; a
/// query that names a private source is private as a whole.
/// </summary>
public static class PrivateSources
{
    public static bool IsPrivate( SearchResult result ) =>
        ClipboardPrivacy.IsHistory( result ) || IsMail( result );

    public static bool IsMail( SearchResult result ) =>
        result.SourceId == SourceIds.Hey || result.ProviderId == ProviderIds.Hey ||
        result.Id.StartsWith( "hey:", StringComparison.Ordinal );

    public static bool ReferencesPrivateSource( ParsedQuery query ) =>
        ClipboardPrivacy.ReferencesHistory( query ) || ReferencesMail( query );

    public static bool ReferencesPrivateSource( QueryExpression expression ) =>
        ClipboardPrivacy.ReferencesHistory( expression ) || ReferencesMail( expression );

    public static bool ReferencesMail( ParsedQuery query ) =>
        string.Equals( query.ProviderFilter, ProviderIds.Hey, StringComparison.OrdinalIgnoreCase ) ||
        query.IncludedFilters.Concat( query.ExcludedFilters ).Any( static filter =>
            filter.SourceId == SourceIds.Hey || filter.ProviderId == ProviderIds.Hey );

    public static bool ReferencesMail( QueryExpression expression ) =>
        expression.IncludeFilterIds.Concat( expression.ExcludeFilterIds )
            .Contains( SourceIds.Hey, StringComparer.OrdinalIgnoreCase );

    // Providers that read private content never run for suggestions or the
    // assistant's search tool, so their content is never even produced.
    public static bool ReadsPrivateContent( string providerId ) =>
        providerId is ProviderIds.Clipboard or ProviderIds.Hey;
}
