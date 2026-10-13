using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal sealed record ExtensionManagementOutcome
{
    public JsonElement? Manifest { get; init; }
    public string? ArchiveDigest { get; init; }
    public Dictionary<string, JsonElement>? Settings { get; init; }
    public ExtensionDiagnostic[] Diagnostics { get; init; } = [];
    public ExtensionDebugState? Debug { get; init; }
    public ExtensionConnectionSummary[] Connections { get; init; } = [];
    public string? ConnectionMessage { get; init; }
    public int EffectAttempts { get; init; }
}

/// <summary>Coordinates typed management commands. External preparation and network effects run outside registry leases.</summary>
internal sealed class ExtensionManagementService( ExtensionPackageRegistry registry, ExtensionSessionService sessions,
    ExtensionConnections connections, ExtensionDevelopment development, IRemoteEffectJournal journal, IActionExecutor actions )
{
    public async ValueTask<ExtensionManagementOutcome> HandleAsync( ExtensionManagementCommand command, CancellationToken token )
    {
        var outcome = await DispatchAsync( command, token ).ConfigureAwait( false );
        if ( command.PackageKey is not { } key )
        {
            return outcome;
        }

        var installation = registry.Find( key );
        return outcome with { Connections = connections.List( installation.Key ), EffectAttempts = await journal.CountAsync( installation.Key, token ).ConfigureAwait( false ) };
    }

    private async ValueTask<ExtensionManagementOutcome> DispatchAsync( ExtensionManagementCommand command, CancellationToken token )
    {
        switch ( command )
        {
            case ListPackages or ListConnections:
                return new( );
            case InspectArchive inspect:
                await using ( var staged = await ExtensionArchive.StageAsync( inspect.ArchivePath, registry.ArtifactRoot, token ).ConfigureAwait( false ) )
                {
                    return new( ) { Manifest = JsonSerializer.SerializeToElement( staged.Manifest, ExtensionManifestJsonContext.Default.ExtensionManifest ), ArchiveDigest = staged.Digest };
                }
            case InspectPackage inspect:
                var installed = registry.Find( inspect.Key );
                return new( ) { Manifest = JsonSerializer.SerializeToElement( installed.Manifest, ExtensionManifestJsonContext.Default.ExtensionManifest ), Settings = installed.Settings };
            case ReadExtensionLogs logs:
                var logged = registry.Find( logs.Key );
                return new( ) { Diagnostics = development.Read( logged.Key, logs.IncludeStderr ), Debug = development.State( logged.Key ) };
            case ArmExtensionDebug debug:
                var state = await registry.WithExecutionAsync( debug.Key, installation =>
                {
                    RequireRevision( debug.ExpectedRevision, "Reload before granting a debug lease." );
                    return ValueTask.FromResult( development.Arm( installation, debug.CommandId, debug.Digest, debug.Seconds ) );
                }, token ).ConfigureAwait( false );
                return new( ) { Debug = state };
            case StopExtensionDebug stop:
                await sessions.StopDebugAsync( registry.Find( stop.Key ).Key ).ConfigureAwait( false );
                return new( );
            case SetConnection or RemoveConnection or BeginConnectionLogin:
                return new( ) { ConnectionMessage = await ManageConnectionAsync( command, token ).ConfigureAwait( false ) };
            default:
                await MutateAsync( command, token ).ConfigureAwait( false );
                return new( );
        }
    }

    private async ValueTask MutateAsync( ExtensionManagementCommand command, CancellationToken token )
    {
        switch ( command )
        {
            case InstallPackage install:
                await InstallAsync( install, token ).ConfigureAwait( false );
                break;
            case ConfigurePackage configure:
                await registry.ConfigureAsync( configure.Key, configure.Settings.ToDictionary( StringComparer.Ordinal ), configure.ExpectedRevision, token ).ConfigureAwait( false );
                break;
            case SetPackageGlobalSearch search:
                await registry.SetGlobalSearchAsync( search.Key, search.Enabled, search.ExpectedRevision, token ).ConfigureAwait( false );
                break;
            case SetPackageEnabled enabled:
                await registry.SetEnabledAsync( enabled.Key, enabled.Enabled, enabled.ExpectedRevision, token ).ConfigureAwait( false );
                break;
            case RemovePackage remove:
                await registry.RemoveAsync( remove.Key, remove.EraseSettings, remove.ExpectedRevision, token ).ConfigureAwait( false );
                if ( remove.EraseSettings )
                {
                    await journal.ClearAsync( registry.Find( remove.Key ).Key, false, token ).ConfigureAwait( false );
                }
                break;
            case ResetExtensionEffects reset:
                await registry.WithMaintenanceAsync( reset.Key, reset.ExpectedRevision,
                    installation => journal.ClearAsync( installation.Key, reset.IncludeLegacy, token ), token ).ConfigureAwait( false );
                break;
            case RollbackPackage rollback:
                await registry.RollbackAsync( rollback.Key, rollback.Digest, rollback.ExpectedRevision, token ).ConfigureAwait( false );
                break;
            case SetPackageGrants grants:
                await registry.SetGrantsAsync( grants.Key, grants.Grants.ToArray( ), grants.ExpectedRevision, token ).ConfigureAwait( false );
                break;
            default:
                throw new InvalidOperationException( "Unsupported management command." );
        }
    }

    private async ValueTask InstallAsync( InstallPackage command, CancellationToken token )
    {
        await using var staged = await ExtensionArchive.StageAsync( command.ArchivePath, registry.ArtifactRoot, token, command.Digest ).ConfigureAwait( false );
        if ( command.Development && !staged.Manifest.Id.EndsWith( ".development", StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "Development attachments require a separate .development package ID." );
        }

        await registry.InstallAsync( staged, command.Development ? ExtensionOrigins.Development : ExtensionOrigins.Local,
            command.ExpectedRevision, false, command.Grants?.ToArray( ), true, token ).ConfigureAwait( false );
    }

    private async ValueTask<string> ManageConnectionAsync( ExtensionManagementCommand command, CancellationToken token )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( token );
        deadline.CancelAfter( TimeSpan.FromSeconds( 20 ) );
        switch ( command )
        {
            case SetConnection set:
                using ( var prepared = await connections.PrepareAsync( set.Setup, deadline.Token ).ConfigureAwait( false ) )
                {
                    await registry.WithExecutionAsync( set.Key, installation =>
                    {
                        RequireRevision( set.ExpectedRevision, "Reload before changing connections." );
                        var permission = set.Setup.Kind == ExtensionConnectionKinds.Files ? ExtensionPermissionIds.FilesRead : ExtensionPermissionIds.HttpRequest;
                        if ( !installation.Grants.Contains( permission, StringComparer.Ordinal ) ||
                             set.Setup.CredentialKind != ExtensionCredentialKinds.None && !installation.Grants.Contains( ExtensionPermissionIds.SecretsUse, StringComparer.Ordinal ) )
                        {
                            throw new InvalidOperationException( "Grant the package's declared file, HTTP and credential-use permissions first." );
                        }
                        connections.Install( installation.Key, prepared, deadline.Token );
                        return ValueTask.FromResult( true );
                    }, deadline.Token ).ConfigureAwait( false );
                }
                return "Connection saved for this daemon session. Replacing or disabling the package revokes it.";
            case RemoveConnection remove:
                using ( var detached = await registry.WithExecutionAsync( remove.Key, installation =>
                {
                    RequireRevision( remove.ExpectedRevision, "Reload before changing connections." );
                    return ValueTask.FromResult( connections.Detach( installation.Key, remove.Name ) );
                }, deadline.Token ).ConfigureAwait( false ) )
                {
                    return await detached.DisconnectAsync( remove.RemoteRevoke, deadline.Token ).ConfigureAwait( false );
                }
            case BeginConnectionLogin login:
                return await OpenLoginAsync( login, deadline.Token ).ConfigureAwait( false );
            default:
                throw new InvalidOperationException( "Unsupported connection command." );
        }
    }

    private async ValueTask<string> OpenLoginAsync( BeginConnectionLogin command, CancellationToken token )
    {
        var login = await registry.WithExecutionAsync( command.Key, installation =>
        {
            RequireRevision( command.ExpectedRevision, "Reload before changing connections." );
            return ValueTask.FromResult( (Installation: installation, Pending: connections.CaptureLogin( installation.Key, command.Name )) );
        }, token ).ConfigureAwait( false );

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( token, login.Pending.Lifetime );
        var url = await login.Pending.BeginAsync( lifetime.Token ).ConfigureAwait( false );
        lifetime.Token.ThrowIfCancellationRequested( );
        var result = await actions.ExecuteAsync( new SearchResult
        {
            Id = ExtensionManagementActions.ConnectionLogin,
            Title = "Connect " + login.Installation.Manifest.Name,
            Kind = ResultKinds.ExtensionCommand,
            SourceId = ExtensionStableIds.SourceId( login.Installation.Key ),
            ProviderId = ProviderIds.Extensions
        }, new CommandAction
        {
            Id = ExtensionManagementActions.ConnectionLogin,
            Title = "Open login",
            Kind = ActionKinds.OpenUrl,
            Arguments = new( StringComparer.Ordinal ) { ["urlTemplate"] = url }
        }, "", lifetime.Token ).ConfigureAwait( false );
        return result.Succeeded ? "Complete login in your browser, then reload connection status." : "The browser did not open. Disconnect and retry login.";
    }

    private void RequireRevision( long revision, string message )
    {
        if ( registry.Document.Revision != revision )
        {
            throw new InvalidOperationException( message );
        }
    }
}
