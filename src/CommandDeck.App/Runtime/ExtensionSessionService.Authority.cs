using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck;

public sealed partial class ExtensionSessionService
{
    private async ValueTask<T> WithSessionAuthorityAsync<T>( ExtensionCommandSession session, TimeSpan timeout,
        Func<ExtensionInstallation, IReadOnlySet<string>, CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken )
    {
        var sessionLifetime = session.Lifetime;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, sessionLifetime );
        if ( timeout != Timeout.InfiniteTimeSpan )
        {
            lifetime.CancelAfter( timeout );
        }

        try
        {
            return await _registry.WithExecutionAsync( session.Installation.Key, current =>
            {
                lifetime.Token.ThrowIfCancellationRequested( );
                if ( current.Digest != session.Installation.Digest )
                {
                    throw new ExtensionApplicationException( ExtensionErrorCodes.StaleSession );
                }

                return operation( current, ExtensionPermissionPolicy.Effective( current, session.Command ), lifetime.Token );
            }, lifetime.Token ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !sessionLifetime.IsCancellationRequested )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.DeadlineExceeded );
        }
    }

    private async ValueTask ExecuteDesktopEffectAsync( ExtensionCommandSession session, ExtensionInstallation current,
        ViewAction action, CancellationToken cancellationToken )
    {
        var result = await _effects.ExecuteAsync( new SearchResult
        {
            Id = ExtensionStableIds.CommandId( current.Key, session.Command.Id ),
            Title = session.Command.Title,
            Kind = ResultKinds.ExtensionCommand,
            SourceId = ExtensionStableIds.SourceId( current.Key ),
            ProviderId = ProviderIds.Extensions
        }, new CommandAction
        {
            Id = action.Id,
            Title = action.Title,
            Kind = action is CopyTextAction ? ActionKinds.CopyText : ActionKinds.OpenUrl,
            Arguments = action is CopyTextAction copy ? new( StringComparer.Ordinal ) { ["text"] = copy.Text } :
                new( StringComparer.Ordinal ) { ["urlTemplate"] = new Uri( (( OpenUrlAction ) action).Url ).AbsoluteUri }
        }, "", cancellationToken ).ConfigureAwait( false );
        if ( !result.Succeeded )
        {
            throw new InvalidOperationException( session.Command.Mode == ExtensionCommandModes.NoView ?
                "The clipboard effect failed; it will not be retried automatically." : "The desktop action failed. Check the clipboard or URL handler." );
        }
    }
}
