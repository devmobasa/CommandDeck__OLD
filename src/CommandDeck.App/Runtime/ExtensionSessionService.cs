using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

/// <summary>Owns foreground extension sessions; registry leases order launch and host effects with revocation.</summary>
public sealed partial class ExtensionSessionService : IAsyncDisposable
{
    internal static string[] Features => ExtensionHostFeatures.Interactive.ToArray( );

    private readonly ExtensionPackageRegistry _registry;
    private readonly IExtensionLauncher _supervisor;
    private readonly IActionExecutor _effects;
    private readonly object _gate = new( );
    private readonly Dictionary<string, ExtensionCommandSession> _sessions = new( StringComparer.Ordinal );
    private bool _disposed;
    private readonly ExtensionDevelopment _development;
    private readonly ExtensionConnections _connections;

    internal ExtensionSessionService( ExtensionPackageRegistry registry, IExtensionLauncher supervisor, IActionExecutor effects,
        ExtensionDevelopment development, ExtensionConnections connections )
    {
        _registry = registry;
        _supervisor = supervisor;
        _effects = effects;
        _development = development;
        _connections = connections;
        registry.RegisterRevocation( ExtensionRevocationStage.CommandSessions, ( key, _ ) => ClosePackageAsync( key ) );
    }

    public async ValueTask OpenAsync( string owner, string stableId, string[] rendererFeatures,
        Func<ExtensionSessionState, CancellationToken, ValueTask> publish, CancellationToken cancellationToken,
        ExtensionSearchService.SearchActivation? search = null )
    {
        var contribution = _registry.Snapshot.Commands.FirstOrDefault( command => command.StableId == stableId && command.Available );
        if ( contribution is null )
        {
            throw new InvalidOperationException( "The extension command is unavailable. Search again." );
        }

        var session = await _registry.WithExecutionAsync( contribution.InstallationKey, installation =>
        {
            if ( search is not null && (search.PackageKey != installation.Key || search.Digest != installation.Digest || search.GrantRevision != installation.GrantRevision || search.CommandId != stableId) )
            {
                throw new InvalidOperationException( "The search result authority changed. Search again." );
            }
            var command = installation.Manifest.Commands.Single( command => command.Id == contribution.CommandId );
            var features = Features.Intersect( rendererFeatures, StringComparer.Ordinal ).Append( ExtensionFeatures.ErrorCodes ).Distinct( StringComparer.Ordinal ).ToArray( );
            if ( command.Mode == ExtensionCommandModes.NoView && !features.Contains( ExtensionFeatures.NoViewCommands, StringComparer.Ordinal ) ||
                 !features.Contains( ExtensionFeatures.Commands, StringComparer.Ordinal ) ||
                 installation.Manifest.RequiredFeatures.Concat( command.RequiredFeatures ).Except( features, StringComparer.Ordinal ).Any( ) )
            {
                throw new InvalidOperationException( "This renderer does not support the extension command's required features." );
            }
            _ = ExtensionSettingsResolver.Resolve( installation );
            var target = ExtensionRuntimeSupport.Require( installation.Manifest );
            lock ( _gate )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                ObjectDisposedException.ThrowIf( _disposed, this );
                if ( _sessions.ContainsKey( owner ) )
                {
                    throw new InvalidOperationException( "Close the current extension before opening another." );
                }
                if ( _sessions.Count >= 4 || _sessions.Values.Count( value => value.Installation.Key == installation.Key ) >= 2 )
                {
                    throw new InvalidOperationException( "The active extension session limit was reached." );
                }
                var created = new ExtensionCommandSession( installation, command, features, new SessionHost( this, installation.Key, publish ),
                    _development.Take( installation, command.Id, rendererFeatures.Contains( ExtensionFeatures.DevelopmentDebug, StringComparer.Ordinal ) ), search?.Inputs );
                _sessions.Add( owner, created );
                created.Run( _supervisor, _registry.ArtifactDirectory( installation ), target.Entrypoint, ( ) => Release( owner, created ) );
                return ValueTask.FromResult( created );
            }
        }, cancellationToken ).ConfigureAwait( false );
        try
        {
            await session.Opened.WaitAsync( cancellationToken ).ConfigureAwait( false );
        }
        catch
        {
            await session.CloseAsync( ).ConfigureAwait( false );
            throw;
        }
    }

    // Called synchronously from the socket read path before any asynchronous dispatch.
    public void Present( string owner, string sessionId, ExtensionSessionInput input ) =>
        Find( owner, sessionId ).Authority.Present( input.Sequence, input.Revision );

    public ValueTask ActAsync( string owner, string sessionId, ExtensionSessionInput input, CancellationToken cancellationToken )
    {
        var session = Find( owner, sessionId );
        if ( input.ConfirmationId is not null )
        {
            session.DecideRemote( input );
            return ValueTask.CompletedTask;
        }
        var admission = session.Admit( input );
        return DispatchAsync( session, admission, cancellationToken );
    }

    private async ValueTask DispatchAsync( ExtensionCommandSession session, ExtensionActionAdmission admission, CancellationToken cancellationToken )
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, session.Lifetime );
        if ( !session.Debugging || admission.Binding is EffectBinding )
        {
            operation.CancelAfter( TimeSpan.FromSeconds( 30 ) );
        }

        string? error = null;
        try
        {
            // Never hold this lease while awaiting the child. A copy is a bounded host effect.
            var action = await WithSessionAuthorityAsync( session, Timeout.InfiniteTimeSpan, async ( current, grants, token ) =>
            {
                var accepted = session.Authority.BeginAction( admission, current.GrantRevision, grants );
                if ( accepted is EffectBinding effect )
                {
                    await ExecuteDesktopEffectAsync( session, current, effect.Action, token ).ConfigureAwait( false );
                }
                return accepted;
            }, operation.Token ).ConfigureAwait( false );
            if ( action is CallbackBinding or PageBinding or SubmitBinding )
            {
                await session.CallbackAsync( admission, operation.Token ).ConfigureAwait( false );
            }
            else if ( action is BackBinding )
            {
                await session.BackAsync( operation.Token ).ConfigureAwait( false );
            }
        }
        catch ( OperationCanceledException )
        {
            await session.CloseAsync( session.Lifetime.IsCancellationRequested ? null :
                "The extension operation stopped or timed out. Reopen to continue; an effect may already have completed." ).ConfigureAwait( false );
            throw;
        }
        catch ( ExtensionRpcException )
        {
            if ( session.HasTransportFault )
            {
                await session.CloseAsync( "The extension stopped responding. Reopen to continue; an effect may already have completed." ).ConfigureAwait( false );
                throw;
            }

            session.Authority.RejectSubmission( admission );
            error = "The extension couldn't complete that action.";
        }
        catch ( IOException )
        {
            await session.CloseAsync( "The extension stopped responding. Reopen to continue; an effect may already have completed." ).ConfigureAwait( false );
            throw;
        }
        finally
        {
            session.FinishAction( );
        }

        await session.PublishStatusAsync( ExtensionSessionStatuses.Ready, error, operation.Token ).ConfigureAwait( false );
    }

    public ValueTask CloseAsync( string owner, string? sessionId = null )
    {
        ExtensionCommandSession? session;
        lock ( _gate )
        {
            _sessions.TryGetValue( owner, out session );
            if ( sessionId is not null && session?.Id != sessionId )
            {
                throw new InvalidOperationException( "The extension session does not belong to this connection." );
            }
        }
        return session?.CloseAsync( ) ?? ValueTask.CompletedTask;
    }

    private async ValueTask CopyAsync( ExtensionCommandSession session, CopyTextRequest request, CancellationToken cancellationToken ) =>
        await WithSessionAuthorityAsync( session, TimeSpan.FromSeconds( 5 ), async ( current, grants, token ) =>
        {
            session.AdmitCopyEffect( request, current.GrantRevision, grants );
            await ExecuteDesktopEffectAsync( session, current, CommandDeck.Extensions.Views.ViewAction.CopyText( "copy", "Copy", request.Text ), token ).ConfigureAwait( false );
            return true;
        }, cancellationToken ).ConfigureAwait( false );

    private ValueTask<CommandDataResult> DataAsync( ExtensionCommandSession session, CommandDataRequest request, CancellationToken cancellationToken ) =>
        WithSessionAuthorityAsync( session, TimeSpan.FromSeconds( 5 ), ( current, grants, token ) =>
        {
            session.AuthorizeData( request, current.GrantRevision, grants );
            return _registry.AccessDataAsync( current, request, token );
        }, cancellationToken );

    private async ValueTask ClosePackageAsync( string? key )
    {
        _development.Revoke( key );
        _connections.Revoke( key );
        ExtensionCommandSession[] sessions;
        lock ( _gate )
        {
            sessions = _sessions.Values.Where( session => key is null || session.Installation.Key == key ).ToArray( );
        }
        foreach ( var session in sessions )
        {
            session.Revoke( );
        }
        await Task.WhenAll( sessions.Select( session => session.CloseAsync( ).AsTask( ) ) ).ConfigureAwait( false );
    }

    internal async ValueTask StopDebugAsync( string key )
    {
        _development.Revoke( key );
        ExtensionCommandSession[] sessions;
        lock ( _gate )
        {
            sessions = _sessions.Values.Where( session => session.Installation.Key == key && session.Debugging ).ToArray( );
        }
        await Task.WhenAll( sessions.Select( session => session.CloseAsync( ).AsTask( ) ) ).ConfigureAwait( false );
    }

    private ExtensionCommandSession Find( string owner, string id )
    {
        lock ( _gate )
        {
            if ( !_sessions.TryGetValue( owner, out var session ) || session.Id != id )
            {
                throw new InvalidOperationException( "The extension session does not belong to this connection." );
            }
            return session;
        }
    }

    private void Release( string owner, ExtensionCommandSession session )
    {
        lock ( _gate )
        {
            if ( _sessions.GetValueOrDefault( owner ) == session )
            {
                _sessions.Remove( owner );
            }
        }
    }

    public async ValueTask DisposeAsync( )
    {
        lock ( _gate )
        {
            _disposed = true;
        }
        await ClosePackageAsync( null ).ConfigureAwait( false );
    }

    private async ValueTask<BrokerResult> BrokerAsync( ExtensionCommandSession session, BrokerRequest request, CancellationToken token )
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource( token, session.Lifetime );
        operation.CancelAfter( TimeSpan.FromSeconds( 30 ) );
        var secrets = await WithSessionAuthorityAsync( session, TimeSpan.FromSeconds( 30 ), ( current, grants, _ ) =>
        {
            session.AuthorizeBroker( request, current.GrantRevision, grants );
            return ValueTask.FromResult( grants.Contains( ExtensionPermissionIds.SecretsUse ) );
        }, operation.Token ).ConfigureAwait( false );
        return await _connections.ExecuteAsync( session.Installation.Key, request, secrets, session.ConfirmRemoteAsync, operation.Token ).ConfigureAwait( false );
    }
}
