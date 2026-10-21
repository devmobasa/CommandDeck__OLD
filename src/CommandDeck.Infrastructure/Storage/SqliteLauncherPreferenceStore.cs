using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore : ILauncherPreferenceStore
{
    private readonly Func<CancellationToken, ValueTask> _initialize;
    private readonly Func<CancellationToken, ValueTask<SqliteConnection>> _open;
    private readonly TimeProvider _timeProvider;
    private readonly Func<ContributionRegistrySnapshot> _contributions;
    private readonly IAiPreferenceStore _aiPreferences;
    private readonly Func<IReadOnlyList<QuicklinkOptions>> _configuredQuicklinks;
    private readonly Func<SqliteConnection, SqliteTransaction, CancellationToken, ValueTask> _publishQuicklinks;

    public SqliteLauncherPreferenceStore( Func<CancellationToken, ValueTask> initialize,
        Func<CancellationToken, ValueTask<SqliteConnection>> open, TimeProvider timeProvider,
        Func<ContributionRegistrySnapshot> contributions, IAiPreferenceStore aiPreferences,
        Func<IReadOnlyList<QuicklinkOptions>> configuredQuicklinks,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, ValueTask> publishQuicklinks )
    {
        _initialize = initialize;
        _open = open;
        _timeProvider = timeProvider;
        _contributions = contributions;
        _aiPreferences = aiPreferences;
        _configuredQuicklinks = configuredQuicklinks;
        _publishQuicklinks = publishQuicklinks;
    }

    internal Func<SqliteConnection, SqliteTransaction, string, CancellationToken, ValueTask>? ValidateFallback { get; set; }

    private ContributionRegistrySnapshot ContributionSnapshot => _contributions( );
    private ValueTask InitializeAsync( CancellationToken token ) => _initialize( token );
    private ValueTask<SqliteConnection> OpenAsync( CancellationToken token ) => _open( token );
}
