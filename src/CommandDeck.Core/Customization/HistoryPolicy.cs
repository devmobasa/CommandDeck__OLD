namespace CommandDeck;

// What the launcher may remember and learn from: the configuration's defaults,
// overridden by any choice made in Settings. Readers ask on every use, so a
// changed choice applies to the next search or invocation without a restart.
// The search coordinator applies the preferences it reads for each search, and
// the runtime applies them whenever it reads or saves them.
public sealed class HistoryPolicy
{
    private readonly PrivacyOptions _privacy;
    private readonly SearchOptions _search;
    private volatile LauncherPreferences? _preferences;

    public HistoryPolicy( PrivacyOptions? privacy = null, SearchOptions? search = null )
    {
        _privacy = privacy ?? new PrivacyOptions( );
        _search = search ?? new SearchOptions( );
    }

    public PreferenceDefaults Defaults => new( )
    {
        RememberSearches = _privacy.StoreQueryHistory,
        LearnFromUse = _search.LearnFromInvocations && _privacy.StoreUsageHistory,
        OmarchyMenuState = _search.OmarchyMenuState
    };

    // Query history: the searches a result was accepted after.
    public bool RememberSearches => _preferences?.RememberSearches ?? _privacy.StoreQueryHistory;

    // Usage learning: accepted uses and the ranking built from them. Adaptive
    // ranking (search.ranking.adaptive.enabled) still gates both.
    public bool LearnFromUse => _preferences?.LearnFromUse ?? (_search.LearnFromInvocations && _privacy.StoreUsageHistory);

    public bool OmarchyMenuState => _preferences?.OmarchyMenuState ?? _search.OmarchyMenuState;

    public void Apply( LauncherPreferences preferences ) => _preferences = preferences;
}
