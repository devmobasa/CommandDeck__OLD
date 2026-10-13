namespace CommandDeck;

internal sealed partial class AssistantRuntime
{

    public async ValueTask<PackageSearchResult> SearchPackagesAsync( string query, int limit, CancellationToken cancellationToken )
    {
        var result = await _packages!.SearchAsync( query, limit, cancellationToken ).ConfigureAwait( false );
        var helper = await ReadHelperStatusAsync( cancellationToken ).ConfigureAwait( false );
        return new PackageSearchResult
        {
            Query = result.Query,
            Packages = result.Packages,
            Truncated = result.Truncated,
            HelperAvailable = helper.Installed,
            Error = result.Error
        };
    }

    public async ValueTask<PackageTransactionPreview> PreviewPackagesAsync(
        string operation,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken )
    {
        var authority = await Agent.LoadSnapshotAsync( cancellationToken ).ConfigureAwait( false );
        var preferences = authority.Preferences;
        if ( !preferences.Enabled || preferences.TrustLevel < AgentTrustLevels.Packages )
        {
            throw new InvalidOperationException( "Package changes require trust level 4." );
        }
        var epoch = authority.SessionEpoch;
        var preview = await _packages!.PreviewAsync( operation, names, null, cancellationToken ).ConfigureAwait( false );
        var helper = await ReadHelperStatusAsync( cancellationToken ).ConfigureAwait( false );
        if ( !helper.Installed )
        {
            preview = new PackageTransactionPreview
            {
                PreviewId = preview.PreviewId,
                Operation = preview.Operation,
                RequestedNames = preview.RequestedNames,
                Entries = preview.Entries,
                DownloadBytes = preview.DownloadBytes,
                InstalledDeltaBytes = preview.InstalledDeltaBytes,
                DatabaseIdentity = preview.DatabaseIdentity,
                TransactionDigest = preview.TransactionDigest,
                Route = preview.Route,
                Warnings = [.. preview.Warnings, "The package helper is not installed; Commit is unavailable."],
                Destructive = preview.Destructive,
                Commitable = false,
                Reason = preview.Reason ?? "The package helper is not installed.",
                ExpiresUnixMilliseconds = preview.ExpiresUnixMilliseconds,
                Rationale = preview.Rationale
            };
        }
        if ( preview.Commitable )
        {
            _packagePreviews[preview.PreviewId] = (preview, DateTimeOffset.FromUnixTimeMilliseconds( preview.ExpiresUnixMilliseconds ), epoch);
            while ( _packagePreviews.Count > 16 )
            {
                var oldest = _packagePreviews.OrderBy( static value => value.Value.ExpiresAt ).First( ).Key;
                _packagePreviews.TryRemove( oldest, out _ );
            }
        }
        return preview;
    }

    public async ValueTask<PackageCommitResult> CommitPackagesAsync( string previewId, CancellationToken cancellationToken )
    {
        var preferences = await RefreshAgentPreferenceCacheAsync( cancellationToken ).ConfigureAwait( false );
        if ( !preferences.Enabled || preferences.TrustLevel < AgentTrustLevels.Packages )
        {
            throw new InvalidOperationException( "Package changes require trust level 4." );
        }
        if ( !_packagePreviews.TryRemove( previewId, out var entry ) ||
            entry.ExpiresAt <= DateTimeOffset.UtcNow || !_authority!.IsCurrent( entry.AuthorityEpoch ) )
        {
            throw new InvalidOperationException( "The package preview is unknown or expired; preview again." );
        }
        if ( _packages!.IsDatabaseLocked( ) || _packages.ComputeDatabaseIdentity( ) != entry.Preview.DatabaseIdentity )
        {
            throw new InvalidOperationException( "The package database changed; preview again." );
        }
        return await Agent.CommitPackageChangeAsync( entry.Preview, cancellationToken, entry.AuthorityEpoch ).ConfigureAwait( false );
    }

    public async ValueTask<PackageCommitResult> CancelPackageOperationAsync( string operationId, CancellationToken cancellationToken )
    {
        if ( _helper is null )
        {
            return new PackageCommitResult { OperationId = operationId, State = PackageOperationStates.Failed, Message = "The package helper is not installed." };
        }
        var cancelled = await _helper.CancelAsync( operationId, cancellationToken ).ConfigureAwait( false );
        return new PackageCommitResult
        {
            OperationId = operationId,
            State = cancelled ? PackageOperationStates.Cancelled : PackageOperationStates.Running,
            Message = cancelled ? "The operation was cancelled before it started." : "The operation can no longer be cancelled."
        };
    }
}
