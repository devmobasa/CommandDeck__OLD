using System.Threading.Channels;

namespace CommandDeck.PackageHelper;

internal delegate Task<PackageProcessResult> PackageProcessRunner(
    string fileName,
    IReadOnlyList<string> arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken );

internal sealed class PackageHelperOptions
{
    public required IPolkitAuthority Authority { get; init; }
    public required PackageProcessRunner Runner { get; init; }
    public string DatabaseRoot { get; init; } = PacmanDatabaseIdentity.DefaultRoot;
    public string PacmanExecutable { get; init; } = "/usr/bin/pacman";
    public Action<string, string, string>? Progress { get; init; }
    public Func<string>? OperationIdFactory { get; init; }
    public TimeSpan CommitTimeout { get; init; } = TimeSpan.FromMinutes( 30 );
    public TimeSpan QueryTimeout { get; init; } = TimeSpan.FromMinutes( 2 );
}

internal sealed class PackageHelperException( string errorName, string message )
    : Exception( message )
{
    public string ErrorName { get; } = errorName;
}

internal sealed record PackageHelperOperationState
{
    public required string OperationId { get; init; }
    public required string State { get; init; }
    public required string Message { get; init; }
    public required IReadOnlyList<string> VerifiedNames { get; init; }
    public required IReadOnlyList<string> FailedNames { get; init; }
}

internal sealed record PackageHelperStatus(
    string ProtocolVersion,
    string PacmanVersion,
    bool DatabaseLocked );

/// <summary>
/// Every decision the privileged helper makes, with the D-Bus transport and
/// the process launcher supplied from outside. Validation, authorization, and
/// the operation state machine live here so they can be exercised directly.
/// </summary>
internal sealed partial class PackageHelperCore( PackageHelperOptions options )
{
    private readonly PackageHelperOptions _options = options;
    private readonly Lock _gate = new( );
    private readonly Dictionary<string, MutableOperation> _operations =
        new( StringComparer.Ordinal );
    private readonly Channel<string> _pending =
        Channel.CreateUnbounded<string>( new UnboundedChannelOptions { SingleReader = true } );
    private string? _activeOperationId;

    public static IReadOnlyList<string> BuildCommitArguments(
        string operation,
        IReadOnlyList<string> names )
    {
        ArgumentNullException.ThrowIfNull( names );
        List<string> arguments = operation switch
        {
            PackageOperations.Install =>
                ["-S", "--needed", "--noconfirm", "--noprogressbar", "--color", "never", "--"],
            PackageOperations.Remove =>
                ["-Rns", "--noconfirm", "--noprogressbar", "--color", "never", "--"],
            _ => throw new ArgumentOutOfRangeException( nameof( operation ) )
        };
        arguments.AddRange( names );
        return arguments;
    }

    public static IReadOnlyList<string> BuildVerifyArguments( IReadOnlyList<string> names )
    {
        ArgumentNullException.ThrowIfNull( names );
        List<string> arguments = ["-Q", "--"];
        arguments.AddRange( names );
        return arguments;
    }

    public async ValueTask<PackageHelperStatus> GetStatusAsync(
        CancellationToken cancellationToken )
    {
        var version = "unknown";
        try
        {
            var result = await _options.Runner(
                _options.PacmanExecutable,
                ["--version"],
                _options.QueryTimeout,
                cancellationToken ).ConfigureAwait( false );
            if ( result.ExitCode == 0 )
            {
                version = ParsePacmanVersion( result.StandardOutput );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            version = "unknown";
        }

        return new PackageHelperStatus(
            PackageHelperProtocol.ProtocolVersion,
            version,
            IsDatabaseLocked( ) );
    }

    public async ValueTask<string> CommitAsync(
        string senderBusName,
        string operation,
        IReadOnlyList<string> names,
        string databaseIdentity,
        string transactionDigest,
        CancellationToken cancellationToken )
    {
        var normalized = ValidateRequest( operation, names );
        ValidateToken( databaseIdentity );
        ValidateToken( transactionDigest );
        EnsureNotBusy( );
        EnsureDatabaseUnchanged( databaseIdentity );
        var expectedVersions = await EnsureTransactionUnchangedAsync(
            operation,
            normalized,
            transactionDigest,
            cancellationToken ).ConfigureAwait( false );
        EnsureDatabaseUnlocked( );
        await AuthorizeAsync( senderBusName, operation, normalized, cancellationToken )
            .ConfigureAwait( false );
        EnsureDatabaseUnchanged( databaseIdentity );
        expectedVersions = await EnsureTransactionUnchangedAsync(
            operation, normalized, transactionDigest, cancellationToken ).ConfigureAwait( false );
        EnsureDatabaseUnlocked( );
        return Enqueue( operation, normalized, databaseIdentity, transactionDigest, expectedVersions );
    }

    public PackageHelperOperationState GetOperation( string operationId )
    {
        ArgumentException.ThrowIfNullOrEmpty( operationId );
        lock ( _gate )
        {
            if ( !_operations.TryGetValue( operationId, out var operation ) )
            {
                throw new PackageHelperException(
                    PackageHelperErrors.UnknownOperation,
                    "The operation is not known." );
            }

            return operation.Snapshot( );
        }
    }

    public bool Cancel( string operationId )
    {
        ArgumentException.ThrowIfNullOrEmpty( operationId );
        MutableOperation? cancelled = null;
        lock ( _gate )
        {
            if ( _operations.TryGetValue( operationId, out var operation ) &&
                string.Equals( operation.State, PackageOperationStates.Queued, StringComparison.Ordinal ) )
            {
                operation.State = PackageOperationStates.Cancelled;
                operation.Message = "The operation was cancelled before it started.";
                _activeOperationId = null;
                cancelled = operation;
            }
        }

        if ( cancelled is null )
        {
            return false;
        }

        Publish( cancelled );
        return true;
    }

    private static string ParsePacmanVersion( string output )
    {
        foreach ( var line in output.Split( '\n' ) )
        {
            var marker = line.IndexOf( "Pacman v", StringComparison.Ordinal );
            if ( marker < 0 )
            {
                continue;
            }

            var tail = line[(marker + "Pacman v".Length)..];
            var end = 0;
            while ( end < tail.Length && (char.IsAsciiLetterOrDigit( tail[end] ) || tail[end] is '.' or '-') )
            {
                end++;
            }

            if ( end > 0 )
            {
                return tail[..end];
            }
        }

        return "unknown";
    }

    private static IReadOnlyList<string> ValidateRequest(
        string operation,
        IReadOnlyList<string> names )
    {
        if ( !string.Equals( operation, PackageOperations.Install, StringComparison.Ordinal ) &&
            !string.Equals( operation, PackageOperations.Remove, StringComparison.Ordinal ) )
        {
            throw new PackageHelperException(
                PackageHelperErrors.InvalidOperation,
                "The operation is not supported." );
        }

        try
        {
            return AgentContractValidator.NormalizePackageNames( names );
        }
        catch ( InvalidOperationException exception )
        {
            throw new PackageHelperException( PackageHelperErrors.InvalidNames, exception.Message );
        }
    }

    private static void ValidateToken( string value )
    {
        if ( string.IsNullOrEmpty( value ) || value.Length != 64 )
        {
            throw new PackageHelperException(
                PackageHelperErrors.InvalidRequest,
                "The request identity is malformed." );
        }

        foreach ( var character in value )
        {
            if ( !char.IsAsciiHexDigitLower( character ) )
            {
                throw new PackageHelperException(
                    PackageHelperErrors.InvalidRequest,
                    "The request identity is malformed." );
            }
        }
    }

    private bool IsDatabaseLocked( ) =>
        File.Exists( PacmanDatabaseIdentity.LockFilePath( _options.DatabaseRoot ) );

    private void EnsureDatabaseUnchanged( string databaseIdentity )
    {
        if ( !string.Equals(
            PacmanDatabaseIdentity.Compute( _options.DatabaseRoot ),
            databaseIdentity,
            StringComparison.Ordinal ) )
        {
            throw new PackageHelperException(
                PackageHelperErrors.DatabaseChanged,
                "The package databases changed since the preview." );
        }
    }

    private async ValueTask<IReadOnlyDictionary<string, string>> EnsureTransactionUnchangedAsync(
        string operation,
        IReadOnlyList<string> names,
        string transactionDigest,
        CancellationToken cancellationToken )
    {
        var result = await _options.Runner(
            _options.PacmanExecutable,
            PacmanTransactionDigest.BuildPrintArguments( operation, names ),
            _options.QueryTimeout,
            cancellationToken ).ConfigureAwait( false );
        if ( result.ExitCode != 0 || result.TimedOut || result.OutputLimitExceeded )
        {
            throw new PackageHelperException(
                PackageHelperErrors.TransactionChanged,
                "The transaction could not be recomputed." );
        }

        var recomputed = PacmanTransactionDigest.Compute( operation, result.StandardOutput );
        if ( !string.Equals( recomputed, transactionDigest, StringComparison.Ordinal ) )
        {
            throw new PackageHelperException(
                PackageHelperErrors.TransactionChanged,
                "The transaction changed since the preview." );
        }
        return PacmanTransactionDigest.ParseVersions( result.StandardOutput );
    }

    private void EnsureDatabaseUnlocked( )
    {
        if ( IsDatabaseLocked( ) )
        {
            throw new PackageHelperException(
                PackageHelperErrors.DatabaseLocked,
                "Another package transaction holds the database lock." );
        }
    }

    private async ValueTask AuthorizeAsync(
        string senderBusName,
        string operation,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken )
    {
        List<KeyValuePair<string, string>> details =
        [
            new("polkit.message", "CommandDeck wants to change installed packages"),
            new("package.operation", operation),
            new("package.names", string.Join(' ', names))
        ];
        var decision = await _options.Authority
            .CheckAuthorizationAsync(
                senderBusName,
                PackageHelperProtocol.ActionPackageChange,
                details,
                cancellationToken )
            .ConfigureAwait( false );
        if ( decision.IsAuthorized )
        {
            return;
        }

        throw decision.IsChallenge
            ? new PackageHelperException(
                PackageHelperErrors.NoAuthenticationAgent,
                "No authentication agent answered the request." )
            : new PackageHelperException(
                PackageHelperErrors.NotAuthorized,
                "The request was not authorized." );
    }

    private void EnsureNotBusy( )
    {
        lock ( _gate )
        {
            if ( _activeOperationId is not null )
            {
                throw new PackageHelperException(
                    PackageHelperErrors.Busy,
                    "Another package operation is already running." );
            }
        }
    }

    private string Enqueue(
        string operation,
        IReadOnlyList<string> names,
        string databaseIdentity,
        string transactionDigest,
        IReadOnlyDictionary<string, string> expectedVersions )
    {
        MutableOperation created;
        lock ( _gate )
        {
            if ( _activeOperationId is not null )
            {
                throw new PackageHelperException(
                    PackageHelperErrors.Busy,
                    "Another package operation is already running." );
            }

            created = new MutableOperation
            {
                OperationId = _options.OperationIdFactory?.Invoke( ) ?? NewOperationId( ),
                Operation = operation,
                Names = names,
                DatabaseIdentity = databaseIdentity,
                TransactionDigest = transactionDigest,
                ExpectedVersions = expectedVersions,
                State = PackageOperationStates.Queued,
                Message = "The operation is waiting to start."
            };
            _operations[created.OperationId] = created;
            _activeOperationId = created.OperationId;
        }

        _pending.Writer.TryWrite( created.OperationId );
        Publish( created );
        return created.OperationId;
    }

    private static string NewOperationId( ) =>
        "package-" + Convert.ToHexStringLower( Guid.NewGuid( ).ToByteArray( ) );

    private void Publish( MutableOperation operation )
    {
        PackageHelperOperationState snapshot;
        lock ( _gate )
        {
            snapshot = operation.Snapshot( );
        }

        _options.Progress?.Invoke( snapshot.OperationId, snapshot.State, snapshot.Message );
    }

    private sealed class MutableOperation
    {
        public required string OperationId { get; init; }
        public required string Operation { get; init; }
        public required IReadOnlyList<string> Names { get; init; }
        public required string DatabaseIdentity { get; init; }
        public required string TransactionDigest { get; init; }
        public required IReadOnlyDictionary<string, string> ExpectedVersions { get; init; }
        public required string State { get; set; }
        public required string Message { get; set; }
        public List<string> VerifiedNames { get; } = [];
        public List<string> FailedNames { get; } = [];

        public PackageHelperOperationState Snapshot( ) => new( )
        {
            OperationId = OperationId,
            State = State,
            Message = Message,
            VerifiedNames = [.. VerifiedNames],
            FailedNames = [.. FailedNames]
        };
    }
}
