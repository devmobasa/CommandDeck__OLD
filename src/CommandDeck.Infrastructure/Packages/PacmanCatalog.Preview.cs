using System.Text;

namespace CommandDeck;

public sealed partial class PacmanCatalog
{
    internal const string AlreadyInstalledReason = "All requested packages are already installed.";
    internal const string DatabaseLockedWarning =
        "Another package operation holds the pacman database lock.";
    internal const string NoSaveWarning =
        "Commit uses pacman -Rns, so backup configuration files are not saved as .pacsave.";

    internal const string MissingInstallTargetSuffix =
        " was not found in the official repositories.";
    internal const string MissingRemoveTargetSuffix = " is not installed.";

    private const string PreviewFailedReason = "The package transaction could not be prepared.";
    private const string PreviewTimedOutReason = "The package transaction preview timed out.";
    private const string InstallPrintFormat = "%n|%v|%r|%s|%l";
    private const string RemovePrintFormat = "%n|%v";
    private const string InstalledSizeFormat = "%n\\t%m";

    /// <summary>
    /// Builds the exact transaction an install or remove would perform. Runs
    /// only unprivileged pacman --print commands; nothing is committed.
    /// </summary>
    public async ValueTask<PackageTransactionPreview> PreviewAsync(
        string operation,
        IReadOnlyList<string> names,
        string? rationale,
        CancellationToken cancellationToken )
    {
        if ( operation is not (PackageOperations.Install or PackageOperations.Remove) )
        {
            throw new InvalidOperationException(
                "The package operation must be install or remove." );
        }

        var normalized = AgentContractValidator.NormalizePackageNames( names );
        foreach ( var name in normalized )
        {
            if ( IsSelectorShaped( name ) )
            {
                throw new InvalidOperationException( "A package name is invalid." );
            }
        }

        return string.Equals( operation, PackageOperations.Install, StringComparison.Ordinal )
            ? await PreviewInstallAsync( normalized, rationale, cancellationToken )
                .ConfigureAwait( false )
            : await PreviewRemoveAsync( normalized, rationale, cancellationToken )
                .ConfigureAwait( false );
    }

    private async ValueTask<PackageTransactionPreview> PreviewInstallAsync(
        IReadOnlyList<string> names,
        string? rationale,
        CancellationToken cancellationToken )
    {
        List<string> arguments =
            ["-S", "--print", "--print-format", InstallPrintFormat, "--needed", "--"];
        arguments.AddRange( names );
        var result = await _runner( PacmanExecutable, arguments, cancellationToken )
            .ConfigureAwait( false );

        var entries =
            PacmanOutputParser.ParseInstallPlan( result.StandardOutput, names );
        var failed = HasFailed( result );
        var reason = failed
            ? DescribeFailure( result, MissingInstallTargetSuffix )
            : entries.Count == 0 ? AlreadyInstalledReason : null;

        List<string> warnings = [];
        await AppendInstalledWarningsAsync( names, entries, warnings, cancellationToken )
            .ConfigureAwait( false );
        entries = await ApplyInstalledSizesAsync( "-S", entries, cancellationToken )
            .ConfigureAwait( false );

        return BuildPreview(
            PackageOperations.Install, names, entries, warnings, reason, failed, rationale );
    }

    private async ValueTask<PackageTransactionPreview> PreviewRemoveAsync(
        IReadOnlyList<string> names,
        string? rationale,
        CancellationToken cancellationToken )
    {
        List<string> arguments = ["-Rs", "--print", "--print-format", RemovePrintFormat, "--"];
        arguments.AddRange( names );
        var result = await _runner( PacmanExecutable, arguments, cancellationToken )
            .ConfigureAwait( false );

        var entries =
            PacmanOutputParser.ParseRemovePlan( result.StandardOutput, names );
        var failed = HasFailed( result );
        var reason = failed ? DescribeFailure( result, MissingRemoveTargetSuffix ) : null;
        List<string> warnings = [NoSaveWarning];
        entries = await ApplyInstalledSizesAsync( "-Q", entries, cancellationToken )
            .ConfigureAwait( false );

        return BuildPreview(
            PackageOperations.Remove, names, entries, warnings, reason, failed, rationale );
    }

    private PackageTransactionPreview BuildPreview(
        string operation,
        IReadOnlyList<string> names,
        List<PackageTransactionEntry> entries,
        List<string> warnings,
        string? reason,
        bool failed,
        string? rationale )
    {
        var removal = string.Equals( operation, PackageOperations.Remove, StringComparison.Ordinal );
        var locked = IsDatabaseLocked( );
        if ( locked )
        {
            warnings.Add( DatabaseLockedWarning );
        }

        long download = 0;
        long installed = 0;
        foreach ( var entry in entries )
        {
            download += entry.DownloadBytes ?? 0;
            installed += entry.InstalledBytes ?? 0;
        }

        return new PackageTransactionPreview
        {
            PreviewId = NewPreviewIdentifier( ),
            Operation = operation,
            RequestedNames = [.. names],
            Entries = entries,
            DownloadBytes = removal ? 0 : download,
            InstalledDeltaBytes = removal ? -installed : installed,
            DatabaseIdentity = ComputeDatabaseIdentity( ),
            TransactionDigest = ComputeTransactionDigest( operation, entries ),
            Route = [.. BuildCommitArgv( operation, names )],
            Warnings = warnings,
            Destructive = removal,
            Commitable = !failed && !locked && entries.Count > 0,
            Reason = reason,
            ExpiresUnixMilliseconds = _clock.GetUtcNow( ).ToUnixTimeMilliseconds( ) +
                (AgentLimits.PreviewLifetimeSeconds * 1000L),
            Rationale = string.IsNullOrWhiteSpace( rationale ) ? null : Sanitize( rationale )
        };
    }

    private async ValueTask AppendInstalledWarningsAsync(
        IReadOnlyList<string> names,
        IReadOnlyList<PackageTransactionEntry> entries,
        List<string> warnings,
        CancellationToken cancellationToken )
    {
        List<string> absent = [];
        foreach ( var name in names )
        {
            if ( !ContainsEntry( entries, name ) )
            {
                absent.Add( name );
            }
        }

        if ( absent.Count == 0 )
        {
            return;
        }

        List<string> arguments = ["-Q", "--"];
        arguments.AddRange( absent );
        var result = await _runner( PacmanExecutable, arguments, cancellationToken )
            .ConfigureAwait( false );
        if ( result.TimedOut || string.IsNullOrWhiteSpace( result.StandardOutput ) )
        {
            // pacman -Q exits non-zero when any name is missing; parse what it printed.
            return;
        }

        var versions =
            PacmanOutputParser.ParseInstalledVersions( result.StandardOutput );
        foreach ( var name in absent )
        {
            if ( versions.TryGetValue( name, out var version ) )
            {
                warnings.Add( Sanitize( $"{name} is already installed at version {version}." ) );
            }
        }
    }

    private async ValueTask<List<PackageTransactionEntry>> ApplyInstalledSizesAsync(
        string mode,
        List<PackageTransactionEntry> entries,
        CancellationToken cancellationToken )
    {
        if ( entries.Count == 0 )
        {
            return entries;
        }

        List<string> arguments = [mode, InstalledSizeFormat, "--"];
        foreach ( var entry in entries )
        {
            arguments.Add( entry.Name );
        }

        var result = await _runner( ExpacExecutable, arguments, cancellationToken )
            .ConfigureAwait( false );
        if ( result.TimedOut || string.IsNullOrWhiteSpace( result.StandardOutput ) )
        {
            // Sizes are advisory; a missing or failing expac must not fail the preview.
            return entries;
        }

        var sizes =
            PacmanOutputParser.ParseExpacOutput( result.StandardOutput );
        List<PackageTransactionEntry> updated = [];
        foreach ( var entry in entries )
        {
            updated.Add( sizes.TryGetValue( entry.Name, out var size )
                ? WithInstalledBytes( entry, size.InstalledBytes )
                : entry );
        }

        return updated;
    }

    private static PackageTransactionEntry WithInstalledBytes(
        PackageTransactionEntry entry,
        long? installedBytes ) => new( )
        {
            Name = entry.Name,
            Repository = entry.Repository,
            CurrentVersion = entry.CurrentVersion,
            TargetVersion = entry.TargetVersion,
            Requested = entry.Requested,
            DownloadBytes = entry.DownloadBytes,
            InstalledBytes = installedBytes
        };

    private static string DescribeFailure( ProcessResult result, string missingTargetSuffix )
    {
        if ( result.TimedOut )
        {
            return PreviewTimedOutReason;
        }

        var missing = PacmanOutputParser.CollectMissingTargets( result.StandardError );
        if ( missing.Count > 0 )
        {
            StringBuilder builder = new( );
            foreach ( var name in missing )
            {
                if ( builder.Length > 0 )
                {
                    builder.Append( ' ' );
                }

                builder.Append( "Package " ).Append( name ).Append( missingTargetSuffix );
            }

            return Sanitize( builder.ToString( ) );
        }

        var dependencies = PacmanOutputParser.CollectDependencyLines( result.StandardError );
        return dependencies.Count > 0
            ? Sanitize( string.Join( ' ', dependencies ) )
            : PreviewFailedReason;
    }

    private static bool HasFailed( ProcessResult result ) =>
        result.ExitCode != 0 || result.TimedOut ||
        result.AnyOutputLimitExceeded || result.MemoryLimitExceeded;

    private static bool ContainsEntry( IReadOnlyList<PackageTransactionEntry> entries, string name )
    {
        foreach ( var entry in entries )
        {
            if ( string.Equals( entry.Name, name, StringComparison.Ordinal ) )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSelectorShaped( string name ) =>
        name.Length == 0 ||
        name[0] == '-' ||
        name.Contains( '/', StringComparison.Ordinal ) ||
        name.Contains( ':', StringComparison.Ordinal ) ||
        name.Contains( '=', StringComparison.Ordinal );
}
