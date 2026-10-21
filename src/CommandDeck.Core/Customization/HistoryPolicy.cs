namespace CommandDeck;

// History and search behavior: the configuration's defaults,
// overridden by any choice made in Settings. Readers ask on every use, so a
// changed choice applies to the next search or invocation without a restart.
// The runtime applies preferences it saves or resets; the search coordinator,
// the runtime and startup apply the preferences they read, unless a choice was
// applied while they read, so a search that started before a change never
// restores the old choice.
public sealed class HistoryPolicy
{
    private readonly PrivacyOptions _privacy;
    private readonly SearchOptions _search;
    private readonly object _gate = new( );
    private volatile LauncherPreferences? _preferences;
    private long _version;

    public HistoryPolicy( PrivacyOptions? privacy = null, SearchOptions? search = null )
    {
        _privacy = privacy ?? new PrivacyOptions( );
        _search = search ?? new SearchOptions( );
    }

    public PreferenceDefaults Defaults => new( )
    {
        RememberSearches = _privacy.StoreQueryHistory,
        LearnFromUse = _search.LearnFromInvocations && _privacy.StoreUsageHistory,
        OmarchyMenuState = _search.OmarchyMenuState,
        TopMatches = _search.Ranking.TopMatches.Enabled,
        InlineCalculator = _search.Calculator.Inline
    };

    // Query history: the searches a result was accepted after.
    public bool RememberSearches => _preferences?.RememberSearches ?? _privacy.StoreQueryHistory;

    // Usage learning: accepted uses and the ranking built from them. Adaptive
    // ranking (search.ranking.adaptive.enabled) still gates both.
    public bool LearnFromUse => _preferences?.LearnFromUse ?? (_search.LearnFromInvocations && _privacy.StoreUsageHistory);

    public bool OmarchyMenuState => _preferences?.OmarchyMenuState ?? _search.OmarchyMenuState;

    public bool TopMatches => _preferences?.TopMatches ?? _search.Ranking.TopMatches.Enabled;

    public bool InlineCalculator => _preferences?.InlineCalculator ?? _search.Calculator.Inline;

    // Taken before reading preferences, for ApplyRead.
    public long Version => Interlocked.Read( ref _version );

    // Preferences just saved or reset.
    public void Apply( LauncherPreferences preferences )
    {
        lock ( _gate )
        {
            _preferences = preferences;
            _version++;
        }
    }

    // Preferences read after Version returned `version`; ignored when a choice
    // was applied since.
    public void ApplyRead( LauncherPreferences preferences, long version )
    {
        lock ( _gate )
        {
            if ( _version != version )
            {
                return;
            }

            _preferences = preferences;
            _version++;
        }
    }
}
