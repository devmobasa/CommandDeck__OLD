using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore : ILauncherPreferenceStore
{
    private readonly Func<CancellationToken, ValueTask> _initialize;
    private readonly Func<CancellationToken, ValueTask<SqliteConnection>> _open;
    private readonly TimeProvider _timeProvider;
    private readonly Func<ContributionRegistrySnapshot> _contributions;
    private readonly IAiPreferenceStore _aiPreferences;

    public SqliteLauncherPreferenceStore( Func<CancellationToken, ValueTask> initialize,
        Func<CancellationToken, ValueTask<SqliteConnection>> open, TimeProvider timeProvider,
        Func<ContributionRegistrySnapshot> contributions, IAiPreferenceStore aiPreferences )
    {
        _initialize = initialize;
        _open = open;
        _timeProvider = timeProvider;
        _contributions = contributions;
        _aiPreferences = aiPreferences;
    }

    private ContributionRegistrySnapshot ContributionSnapshot => _contributions( );
    private ValueTask InitializeAsync( CancellationToken token ) => _initialize( token );
    private ValueTask<SqliteConnection> OpenAsync( CancellationToken token ) => _open( token );
}
