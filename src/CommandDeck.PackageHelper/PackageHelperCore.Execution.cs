namespace CommandDeck.PackageHelper;

internal sealed partial class PackageHelperCore
{
    /// <summary>
    /// Runs authorized operations one at a time until the token is cancelled.
    /// </summary>
    public async Task RunWorkerAsync( CancellationToken cancellationToken )
    {
        while ( !cancellationToken.IsCancellationRequested )
        {
            try
            {
                await RunQueuedOperationAsync( cancellationToken ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException )
            {
                return;
            }
        }
    }

    /// <summary>
    /// Waits for the next authorized operation and carries it through the
    /// running, verifying, and terminal states. The token bounds only the
    /// wait: once Pacman holds the transaction lock the operation always runs
    /// to a terminal state.
    /// </summary>
    public async Task<string?> RunQueuedOperationAsync( CancellationToken waitToken )
    {
        var operationId = await _pending.Reader
            .ReadAsync( waitToken )
            .ConfigureAwait( false );
        var operation = BeginRunning( operationId );
        if ( operation is null )
        {
            return null;
        }

        await ExecuteAsync( operation, CancellationToken.None ).ConfigureAwait( false );
        return operationId;
    }

    /// <summary>
    /// Moves a queued operation to running in the same critical section that
    /// observed it queued, so a cancellation accepted by <see cref="Cancel"/>
    /// can never be followed by execution.
    /// </summary>
    private MutableOperation? BeginRunning( string operationId )
    {
        MutableOperation? operation;
        lock ( _gate )
        {
            if ( !_operations.TryGetValue( operationId, out operation ) ||
                !string.Equals(
                    operation.State,
                    PackageOperationStates.Queued,
                    StringComparison.Ordinal ) )
            {
                return null;
            }

            operation.State = PackageOperationStates.Running;
            operation.Message = "Applying the package transaction.";
        }

        Publish( operation );
        return operation;
    }

    private async Task ExecuteAsync(
        MutableOperation operation,
        CancellationToken cancellationToken )
    {
        try
        {
            EnsureDatabaseUnlocked( );
            EnsureDatabaseUnchanged( operation.DatabaseIdentity );
            var expected = await EnsureTransactionUnchangedAsync(
                operation.Operation,
                operation.Names,
                operation.TransactionDigest,
                cancellationToken ).ConfigureAwait( false );
            if ( !VersionsEqual( operation.ExpectedVersions, expected ) )
            {
                throw new PackageHelperException(
                    PackageHelperErrors.TransactionChanged,
                    "The transaction changed while waiting to start." );
            }
            EnsureDatabaseUnlocked( );
        }
        catch ( Exception exception ) when ( exception is PackageHelperException or IOException )
        {
            Finish( operation, PackageOperationStates.Failed, "The approved package transaction changed; preview again.", operation.Names );
            return;
        }

        PackageProcessResult result;
        try
        {
            result = await _options.Runner(
                _options.PacmanExecutable,
                BuildCommitArguments( operation.Operation, operation.Names ),
                _options.CommitTimeout,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is not OperationCanceledException )
        {
            Finish( operation, PackageOperationStates.Failed, "The package manager could not run.", [] );
            return;
        }

        if ( result.TimedOut )
        {
            Finish(
                operation,
                PackageOperationStates.Failed,
                "The package transaction reached its time limit.",
                operation.Names );
            return;
        }

        if ( result.ExitCode != 0 )
        {
            Finish(
                operation,
                PackageOperationStates.Failed,
                "The package manager reported a failure.",
                operation.Names );
            return;
        }

        Transition( operation, PackageOperationStates.Verifying, "Verifying the installed packages." );
        await VerifyAsync( operation, cancellationToken ).ConfigureAwait( false );
    }

    private async Task VerifyAsync(
        MutableOperation operation,
        CancellationToken cancellationToken )
    {
        PackageProcessResult result;
        try
        {
            result = await _options.Runner(
                _options.PacmanExecutable,
                BuildVerifyArguments( operation.ExpectedVersions.Keys.Order( StringComparer.Ordinal ).ToArray( ) ),
                _options.QueryTimeout,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is not OperationCanceledException )
        {
            Finish(
                operation,
                PackageOperationStates.Failed,
                "The installed packages could not be verified.",
                operation.Names );
            return;
        }

        var installed = ParseInstalledVersions( result.StandardOutput );
        if ( !IsConclusiveQuery( result, operation.ExpectedVersions.Keys, installed ) )
        {
            Finish(
                operation,
                PackageOperationStates.Failed,
                "The installed packages could not be verified.",
                operation.Names );
            return;
        }

        var expectInstalled = string.Equals(
            operation.Operation,
            PackageOperations.Install,
            StringComparison.Ordinal );
        List<string> verified = [];
        List<string> failed = [];
        foreach ( (var name, var expectedVersion) in operation.ExpectedVersions
            .OrderBy( static pair => pair.Key, StringComparer.Ordinal ) )
        {
            var matches = expectInstalled
                ? installed.TryGetValue( name, out var actualVersion ) &&
                    string.Equals( actualVersion, expectedVersion, StringComparison.Ordinal )
                : !installed.ContainsKey( name );
            if ( matches )
            {
                verified.Add( name );
            }
            else
            {
                failed.Add( name );
            }
        }

        lock ( _gate )
        {
            operation.VerifiedNames.Clear( );
            operation.VerifiedNames.AddRange( verified );
        }

        if ( failed.Count == 0 )
        {
            Finish( operation, PackageOperationStates.Completed, "The package transaction completed.", [] );
            return;
        }

        Finish(
            operation,
            PackageOperationStates.Failed,
            "Post-verification did not match the request.",
            failed );
    }

    private static Dictionary<string, string> ParseInstalledVersions( string output )
    {
        Dictionary<string, string> versions = new( StringComparer.Ordinal );
        foreach ( var rawLine in output.Split( '\n' ) )
        {
            var line = rawLine.Trim( '\r', ' ', '\t' );
            if ( line.Length == 0 )
            {
                continue;
            }

            var space = line.IndexOf( ' ', StringComparison.Ordinal );
            if ( space > 0 && space + 1 < line.Length )
            {
                versions[line[..space]] = line[(space + 1)..].Trim( );
            }
        }

        return versions;
    }

    /// <summary>
    /// Pacman -Q prints installed packages on stdout and one "was not found"
    /// error per missing name, exiting 1 when any is missing. A timeout,
    /// truncated output, another error, or a name in neither stream leaves
    /// the result unknown, and an absent package must not pass for removed.
    /// </summary>
    private static bool IsConclusiveQuery(
        PackageProcessResult result,
        IEnumerable<string> names,
        Dictionary<string, string> installed )
    {
        if ( result.TimedOut || result.OutputLimitExceeded || result.ExitCode is not (0 or 1) )
        {
            return false;
        }

        HashSet<string> missing = new( StringComparer.Ordinal );
        foreach ( var rawLine in result.StandardError.Split( '\n' ) )
        {
            var line = rawLine.Trim( '\r', ' ', '\t' );
            if ( !line.StartsWith( "error:", StringComparison.Ordinal ) )
            {
                continue;
            }

            var name = ParseNotFoundName( line );
            if ( name is null )
            {
                return false;
            }

            missing.Add( name );
        }

        return (result.ExitCode == 1) == (missing.Count > 0) &&
            names.All( name => installed.ContainsKey( name ) != missing.Contains( name ) );
    }

    private static string? ParseNotFoundName( string line )
    {
        const string Prefix = "error: package '";
        const string Suffix = "' was not found";
        if ( !line.StartsWith( Prefix, StringComparison.Ordinal ) ||
            !line.EndsWith( Suffix, StringComparison.Ordinal ) ||
            line.Length <= Prefix.Length + Suffix.Length )
        {
            return null;
        }

        return line[Prefix.Length..^Suffix.Length];
    }

    private static bool VersionsEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual ) =>
        expected.Count == actual.Count && expected.All( pair =>
            actual.TryGetValue( pair.Key, out var value ) &&
            string.Equals( pair.Value, value, StringComparison.Ordinal ) );

    private void Transition( MutableOperation operation, string state, string message )
    {
        lock ( _gate )
        {
            operation.State = state;
            operation.Message = message;
        }

        Publish( operation );
    }

    private void Finish(
        MutableOperation operation,
        string state,
        string message,
        IReadOnlyList<string> failedNames )
    {
        lock ( _gate )
        {
            operation.State = state;
            operation.Message = message;
            operation.FailedNames.Clear( );
            operation.FailedNames.AddRange( failedNames );
            if ( string.Equals( _activeOperationId, operation.OperationId, StringComparison.Ordinal ) )
            {
                _activeOperationId = null;
            }
        }

        Publish( operation );
    }
}
