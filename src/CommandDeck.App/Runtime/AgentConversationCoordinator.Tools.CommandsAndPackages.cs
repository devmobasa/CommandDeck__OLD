using System.Text.Json.Nodes;

namespace CommandDeck;

public sealed partial class AgentConversationCoordinator
{

    private async ValueTask<AgentToolResult> CommandToolAsync(
        Conversation conversation,
        JsonObject arguments,
        CancellationToken cancellationToken )
    {
        List<string> argv = [];
        if ( arguments["argv"] is JsonArray array )
        {
            foreach ( var node in array )
            {
                argv.Add( ( string? ) node ?? string.Empty );
            }
        }
        var plan = _commands.Plan( argv, ReadOptional( arguments, "cwd" ) );
        var remembered = await IsCommandRememberedAsync( plan, cancellationToken ).ConfigureAwait( false );
        // Skip the per-command card for a remembered command, or — only when the
        // option is on — for a direct read-only tool (shells and writers never
        // qualify). Otherwise every command is confirmed, as before.
        var autoApproved = _autoApproveReadOnlyCommands
                           && AgentLauncherTools.ClassifyCommandRisk( plan ) == AgentActionRisks.Immediate;
        if ( !remembered && !autoApproved )
        {
            AgentActionCard card = new( )
            {
                Id = NewId( ),
                Kind = AgentActionDraftKinds.Command,
                Title = "Run command: " + Path.GetFileName( plan.Executable ),
                Risk = AgentActionRisks.Confirm,
                Executable = plan.Executable,
                Argv = [.. plan.Argv],
                WorkingDirectory = plan.WorkingDirectory,
                EnvironmentProfile = plan.EnvironmentProfile,
                Environment = new Dictionary<string, string>( plan.Environment, StringComparer.Ordinal ),
                CanRemember = true,
                Details =
                [
                    new DetailField("Executable", plan.Executable, Monospace: true),
                    new DetailField("Working directory", plan.WorkingDirectory, Monospace: true),
                    new DetailField("Environment", string.Join(' ', plan.Environment.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value}")), Monospace: true),
                    new DetailField("Timeout", $"{AgentLimits.MaximumCommandSeconds} seconds"),
                    new DetailField("Output", "Bounded output returns to the model")
                ],
                Rationale = ReadOptional( arguments, "rationale" )
            };
            var decision = await ParkAsync( conversation, new AgentPendingRequest
            {
                Id = card.Id,
                Kind = AgentPendingRequestKinds.Command,
                TurnId = conversation.ActiveTurnId,
                Prompt = card.Title,
                Action = card,
                Decisions = [AgentRequestDecisions.Allow, AgentRequestDecisions.AllowAlways, AgentRequestDecisions.Deny, AgentRequestDecisions.CancelTurn]
            }, cancellationToken ).ConfigureAwait( false );
            if ( decision.Decision is not (AgentRequestDecisions.Allow or AgentRequestDecisions.AllowAlways) )
            {
                return AgentToolResult.Denied( "The user declined the command." );
            }
            if ( decision.Decision == AgentRequestDecisions.AllowAlways )
            {
                await RememberCommandAsync( plan, cancellationToken ).ConfigureAwait( false );
            }
        }
        var outcome = await AgentCommandTool.ExecuteAsync( plan, cancellationToken ).ConfigureAwait( false );
        JsonObject payload = new( )
        {
            ["exitCode"] = outcome.ExitCode,
            ["timedOut"] = outcome.TimedOut,
            ["truncated"] = outcome.Truncated,
            ["output"] = outcome.Output
        };
        return AgentToolResult.Ok( payload.ToJsonString( ) );
    }

    private async ValueTask<bool> IsCommandRememberedAsync( AgentCommandPlan plan, CancellationToken cancellationToken )
    {
        var grants = await _authority.GetGrantsAsync( cancellationToken ).ConfigureAwait( false );
        return grants.Any( grant => grant.Kind == AgentGrantKinds.Command && grant.State == AgentGrantStates.Active &&
            string.Equals( grant.PayloadDigest, plan.Digest, StringComparison.Ordinal ) );
    }

    private async ValueTask RememberCommandAsync( AgentCommandPlan plan, CancellationToken cancellationToken )
    {
        await _authority.AddGrantAsync( new AgentGrant
        {
            Id = NewId( ),
            Kind = AgentGrantKinds.Command,
            Scope = string.Join( ' ', plan.Argv ),
            Identity = plan.ExecutableIdentity,
            PayloadDigest = plan.Digest,
            State = AgentGrantStates.Active,
            Label = Path.GetFileName( plan.Executable )
        }, cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<AgentToolResult> PackageSearchToolAsync( JsonObject arguments, CancellationToken cancellationToken )
    {
        var result = await _packages!.SearchAsync( ReadString( arguments, "query" ), 12, cancellationToken )
            .ConfigureAwait( false );
        if ( result.Error is not null )
        {
            return AgentToolResult.Denied( result.Error );
        }
        JsonArray packages = new( );
        foreach ( var record in result.Packages )
        {
            JsonNode packageNode = new JsonObject
            {
                ["name"] = record.Name,
                ["repository"] = record.Repository,
                ["version"] = record.Version,
                ["description"] = record.Description,
                ["installed"] = record.Installed
            };
            packages.Add( packageNode );
        }
        return AgentToolResult.Ok( new JsonObject { ["packages"] = packages, ["truncated"] = result.Truncated }.ToJsonString( ) );
    }

    private async ValueTask<AgentToolResult> PackageChangeToolAsync(
        Conversation conversation,
        JsonObject arguments,
        CancellationToken cancellationToken )
    {
        var operation = ReadString( arguments, "operation" );
        List<string> names = [];
        if ( arguments["packages"] is JsonArray array )
        {
            foreach ( var node in array )
            {
                names.Add( ( string? ) node ?? string.Empty );
            }
        }
        var preview = await _packages!.PreviewAsync( operation, names, ReadOptional( arguments, "rationale" ), cancellationToken )
            .ConfigureAwait( false );
        if ( !preview.Commitable )
        {
            return AgentToolResult.Denied( preview.Reason ?? "The package change cannot be previewed." );
        }
        var helper = _helper is null
            ? new AgentHelperStatus { Installed = false, Message = "The package helper is not installed." }
            : await _helper.GetStatusAsync( cancellationToken ).ConfigureAwait( false );
        if ( !helper.Installed )
        {
            return AgentToolResult.Denied( "The privileged package helper is not installed, so package changes are unavailable." );
        }
        var card = BuildPackageCard( preview );
        var decision = await ParkAsync( conversation, new AgentPendingRequest
        {
            Id = card.Id,
            Kind = AgentPendingRequestKinds.Action,
            TurnId = conversation.ActiveTurnId,
            Prompt = card.Title,
            Detail = string.Join( " ", preview.Warnings ),
            Action = card,
            Decisions = [AgentRequestDecisions.Allow, AgentRequestDecisions.Deny, AgentRequestDecisions.CancelTurn]
        }, cancellationToken ).ConfigureAwait( false );
        if ( decision.Decision != AgentRequestDecisions.Allow )
        {
            return AgentToolResult.Denied( "The user declined the package change." );
        }
        var commit = await CommitPackageChangeAsync( preview, cancellationToken, conversation.AuthorityEpoch ).ConfigureAwait( false );
        JsonObject payload = new( )
        {
            ["operationId"] = commit.OperationId,
            ["state"] = commit.State,
            ["message"] = commit.Message
        };
        return commit.State == PackageOperationStates.Completed
            ? AgentToolResult.Ok( payload.ToJsonString( ) )
            : AgentToolResult.Denied( payload.ToJsonString( ) );
    }

    private static AgentActionCard BuildPackageCard( PackageTransactionPreview preview )
    {
        List<DetailField> details = [];
        foreach ( var entry in preview.Entries.Take( 24 ) )
        {
            var version = entry.TargetVersion ?? entry.CurrentVersion ?? string.Empty;
            details.Add( new DetailField( entry.Requested ? entry.Name : entry.Name + " (dependency)", version, Monospace: true ) );
        }
        details.Add( new DetailField( "Download", $"{preview.DownloadBytes / 1024} KiB" ) );
        details.Add( new DetailField( "Installed size change", $"{preview.InstalledDeltaBytes / 1024} KiB" ) );
        return new AgentActionCard
        {
            Id = preview.PreviewId,
            Kind = AgentActionDraftKinds.PackageChange,
            Title = (preview.Operation == PackageOperations.Remove ? "Remove packages: " : "Install packages: ") +
                string.Join( ", ", preview.RequestedNames ),
            Risk = AgentActionRisks.Confirm,
            Argv = [.. preview.Route],
            Destructive = preview.Destructive,
            CanRemember = false,
            Details = details,
            Rationale = preview.Rationale
        };
    }

    public async ValueTask<PackageCommitResult> CommitPackageChangeAsync(
        PackageTransactionPreview preview,
        CancellationToken cancellationToken,
        long? expectedEpoch = null )
    {
        ArgumentNullException.ThrowIfNull( preview );
        // Inside a turn, enforce the lease without waiting for that same turn
        // to stop. Both UI and tool commits re-read persisted authority here.
        var preferences = await EnforceLeaseAsync( cancellationToken ).ConfigureAwait( false );
        if ( !preferences.Enabled || preferences.TrustLevel < AgentTrustLevels.Packages )
        {
            throw new InvalidOperationException( "Package changes require trust level 4." );
        }
        if ( expectedEpoch is { } epoch && !_authority.IsCurrent( epoch ) )
        {
            throw new InvalidOperationException( "Assistant permissions changed; preview the package change again." );
        }
        if ( _helper is null )
        {
            return new PackageCommitResult { OperationId = preview.PreviewId, State = PackageOperationStates.Failed, Message = "The package helper is not installed." };
        }
        var title = preview.Operation + " " + string.Join( ", ", preview.RequestedNames );
        string operationId;
        try
        {
            operationId = await _helper.CommitAsync( preview.Operation, preview.RequestedNames, preview.DatabaseIdentity, preview.TransactionDigest, cancellationToken )
                .ConfigureAwait( false );
        }
        catch ( InvalidOperationException exception )
        {
            return new PackageCommitResult { OperationId = preview.PreviewId, State = PackageOperationStates.Failed, Message = Sanitize( exception.Message ) };
        }
        RecordOperation( operationId, PackageOperationStates.Running, title, "Authorizing and running pacman." );
        var result = await _helper.GetOperationAsync( operationId, cancellationToken ).ConfigureAwait( false );
        var polls = 0;
        while ( result.State is PackageOperationStates.Queued or PackageOperationStates.Authorizing or PackageOperationStates.Running or PackageOperationStates.Verifying && polls++ < 1800 )
        {
            await Task.Delay( 1000, cancellationToken ).ConfigureAwait( false );
            result = await _helper.GetOperationAsync( operationId, cancellationToken ).ConfigureAwait( false );
            RecordOperation( operationId, result.State, title, result.Message );
        }
        RecordOperation( operationId, result.State, title, result.Message );
        return result;
    }
}
