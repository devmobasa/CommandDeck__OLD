using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions;

/// <summary>Optional command base with typed event handlers. IExtensionCommand remains supported.</summary>
public abstract class ExtensionCommandBase : IExtensionCommand
{
    public abstract string Id { get; }
    public virtual string Mode => ExtensionCommandModes.View;
    public abstract ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken );
    protected virtual ValueTask OnActionAsync( CommandContext context, ActionInvoked action, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    protected virtual ValueTask OnSubmitAsync( CommandContext context, FormSubmitted submission, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    protected virtual ValueTask OnPageAsync( CommandContext context, PageRequested page, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => commandEvent switch
    {
        ActionInvoked action => OnActionAsync( context, action, cancellationToken ),
        FormSubmitted submission => OnSubmitAsync( context, submission, cancellationToken ),
        PageRequested page => OnPageAsync( context, page, cancellationToken ),
        _ => ValueTask.CompletedTask
    };
}
