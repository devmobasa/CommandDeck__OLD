using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog :
    ISearchCatalog,
    ICatalogOperations,
    IApplicationIconResolver,
    IFileHandlerCatalog,
    ILauncherPreferenceStore
{
    private const long WalSizeLimitBytes = 16L * 1024 * 1024;
    private readonly string _connectionPragmas;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private readonly PathPolicy _pathPolicy;
    private readonly RankingOptions _rankingOptions;
    private readonly HistoryPolicy _history;
    private readonly PrivacyOptions _privacyOptions;
    private readonly IMimeTypeResolver _mimeTypeResolver;
    private readonly SemaphoreSlim _initializationLock = new( 1, 1 );
    private readonly SemaphoreSlim _fileHandlerCacheLock = new( 1, 1 );
    private IReadOnlyList<HandlerRecord>? _fileHandlerCache;
    private volatile bool _initialized;
    private readonly SqliteLauncherPreferenceStore _launcherPreferences;

    public SqliteCatalog(
        StorageOptions options,
        TimeProvider? timeProvider = null,
        PrivacyOptions? privacy = null,
        SearchOptions? searchOptions = null,
        IMimeTypeResolver? mimeTypeResolver = null,
        HistoryPolicy? history = null )
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _privacyOptions = privacy ?? new PrivacyOptions( );
        _pathPolicy = new PathPolicy( _privacyOptions );
        _history = history ?? new HistoryPolicy( _privacyOptions, searchOptions );
        _rankingOptions = (searchOptions ?? new SearchOptions( )).Ranking;
        _mimeTypeResolver = mimeTypeResolver ?? new SharedMimeTypeResolver( );
        _databasePath = PathExpansion.Expand( options.DatabasePath );
        SqliteConnectionStringBuilder builder = new( )
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // WAL provides cross-connection reader concurrency. SQLite shared-cache
            // table locks would instead reject reads while a staged generation is
            // being written, defeating the known-good snapshot guarantee.
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = Math.Max( 1, options.BusyTimeoutMilliseconds / 1000 )
        };
        _connectionString = builder.ToString( );
        _connectionPragmas = string.Create( System.Globalization.CultureInfo.InvariantCulture, $"""
            PRAGMA foreign_keys = ON;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = {Math.Max( 0, options.BusyTimeoutMilliseconds )};
            PRAGMA temp_store = FILE;
            PRAGMA journal_size_limit = {WalSizeLimitBytes};
            """ );
        AiPreferences = new SqliteAiPreferenceStore( InitializeAsync, OpenAsync, _timeProvider );
        AgentPreferences = new SqliteAgentPreferenceStore( InitializeAsync, OpenAsync, _timeProvider );
        _launcherPreferences = new( InitializeAsync, OpenAsync, _timeProvider,
            ( ) => ContributionSnapshot, AiPreferences );
        UsageHistory = new SqliteUsageHistoryStore(
            InitializeAsync, OpenAsync, _launcherPreferences.GetFilterAliasesAsync,
            ( ) => ContributionSnapshot, _timeProvider, _rankingOptions, _history, _privacyOptions );
    }

    public IUsageHistoryStore UsageHistory { get; }

    // What this catalog may remember and learn from; shared with the search
    // coordinator and the runtime so a Settings change reaches all of them.
    public HistoryPolicy History => _history;

    public IAiPreferenceStore AiPreferences { get; }

    public IAgentPreferenceStore AgentPreferences { get; }

    public string DatabasePath => _databasePath;
    public ILauncherPreferenceStore LauncherPreferences => _launcherPreferences;

    public async ValueTask InitializeAsync( CancellationToken cancellationToken )
    {
        if ( _initialized )
        {
            return;
        }

        await _initializationLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _initialized )
            {
                return;
            }

            Directory.CreateDirectory( Path.GetDirectoryName( _databasePath )! );
            await using var connection =
                await OpenAsync( cancellationToken ).ConfigureAwait( false );
            await RefuseNewerSchemaAsync( connection, cancellationToken ).ConfigureAwait( false );
            await using var command = connection.CreateCommand( );
            command.CommandText = BootstrapSchema;
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            await ApplyMigrationsAsync( connection, cancellationToken ).ConfigureAwait( false );
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release( );
        }
    }

    public async ValueTask<bool> VerifyFts5Async( CancellationToken cancellationToken )
    {
        try
        {
            await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
            await using var command = connection.CreateCommand( );
            command.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS temp.commanddeck_fts_probe USING fts5(value);";
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            return true;
        }
        catch ( SqliteException )
        {
            return false;
        }
    }

    public ValueTask DisposeAsync( ) => ValueTask.CompletedTask;

    internal async ValueTask<SqliteConnection> OpenAsync( CancellationToken cancellationToken )
    {
        SqliteConnection connection = new( _connectionString );
        try
        {
            await connection.OpenAsync( cancellationToken ).ConfigureAwait( false );
            await ApplyConnectionPragmasAsync( connection, cancellationToken ).ConfigureAwait( false );
        }
        catch
        {
            await connection.DisposeAsync( ).ConfigureAwait( false );
            throw;
        }

        connection.CreateFunction<long, long, long, double>(
            "commanddeck_frecency",
            static ( useCount, lastUsedUnixMilliseconds, nowUnixMilliseconds ) =>
                FrecencyPolicy.ScoreAtUnixMilliseconds(
                    useCount,
                    lastUsedUnixMilliseconds,
                    nowUnixMilliseconds ),
            isDeterministic: true );
        return connection;
    }

    // The WAL journal mode lives in the database file; these settings belong to one
    // connection. A pooled connection keeps whatever its last user set (a
    // generation's file-backed stage, a registry commit's FULL sync, bookkeeping's
    // short busy wait), so every open applies them again.
    // - synchronous NORMAL: in WAL mode a commit syncs the WAL only at checkpoints.
    // - busy_timeout: SQLite itself waits for another writer, polling every 1-100 ms,
    //   up to the configured milliseconds; Microsoft.Data.Sqlite's own retry counts
    //   whole seconds (DefaultTimeout) and polls every 150 ms.
    // - temp_store FILE: the compiled default; big sorts and VACUUM spill to files.
    // cache_size keeps SQLite's 2 MiB: 16 and 64 MiB saved at most 2 ms on a
    // 100k-file catalog, and every pooled connection would hold its own copy.
    private async ValueTask ApplyConnectionPragmasAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.CommandText = _connectionPragmas;
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    // Microsoft.Data.Sqlite checks cancellation only between rows, so a search
    // that was superseded mid-step (ranking every prefix match) keeps running and
    // holds its provider slot. Interrupting the statement ends it at once. Dispose
    // the registration before the connection: disposal waits for a running
    // callback, so an interrupt never reaches a pooled handle another caller owns.
    private static CancellationTokenRegistration InterruptOnCancellation(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        var handle = connection.Handle;
        if ( handle is null )
        {
            throw new InvalidOperationException( "The catalog connection is not open." );
        }

        return cancellationToken.UnsafeRegister(
            static state => SQLitePCL.raw.sqlite3_interrupt( ( SQLitePCL.sqlite3 ) state! ),
            handle );
    }

    private static bool WasInterrupted( SqliteException exception, CancellationToken cancellationToken ) =>
        exception.SqliteErrorCode == SQLitePCL.raw.SQLITE_INTERRUPT &&
        cancellationToken.IsCancellationRequested;

    internal static async ValueTask ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    private static void AddParameters( SqliteCommand command, params string[] names )
    {
        foreach ( var name in names )
        {
            command.Parameters.Add( new SqliteParameter( name, null ) );
        }
    }

}
