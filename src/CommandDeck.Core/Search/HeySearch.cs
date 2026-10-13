namespace CommandDeck;

/// <summary>
/// Search rules for HEY mail. HEY answers over the network, so it runs only
/// when a query asks for it (hey:, mail:, or @hey) and gets a longer deadline
/// than local sources; its rows keep HEY's own order.
/// </summary>
public static class HeySearch
{
    public const string FilterId = SourceIds.Hey;

    // Covers the provider's typing pause and the client's read timeout.
    public const int ScopedDeadlineMilliseconds = 5000;

    public static bool IsRequested( ParsedQuery query ) =>
        string.Equals( query.ProviderFilter, ProviderIds.Hey, StringComparison.OrdinalIgnoreCase ) ||
        query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Hey );

    // Words typed without a filter can be sent to HEY with one row at the end of
    // the list; the row runs nothing until it is chosen.
    public const int FallbackMinimumLength = 3;

    public static bool OffersFallback( ParsedQuery query ) =>
        !query.HasExplicitFilter && !query.IsCommandMode && !query.IsAiMode && !query.IsCalculator &&
        query.Text.Trim( ).Length >= FallbackMinimumLength;
}
