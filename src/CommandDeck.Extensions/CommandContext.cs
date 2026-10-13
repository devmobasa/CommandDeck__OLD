using System.Collections.ObjectModel;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Extensions;

public sealed class CommandContext : IDisposable
{
    private readonly CancellationToken _cancellationToken;
    private int _completed;

    internal CommandContext( ExtensionPeer peer, InitializeRequest initialization, long generation, string invocation,
        Dictionary<string, JsonElement> inputs, Dictionary<string, long> revisions, SemaphoreSlim viewWrites, string mode, CancellationToken cancellationToken )
    {
        _cancellationToken = cancellationToken;
        Generation = generation;
        InvocationId = invocation;
        Inputs = new ReadOnlyDictionary<string, JsonElement>( inputs );
        var channel = new HostChannel( this, peer, initialization, mode );
        Views = new CommandViews( channel, revisions, viewWrites );
        Effects = new CommandEffects( channel );
        Data = new CommandData( channel );
        Brokers = new CommandBrokers( channel );
    }

    public long Generation { get; }
    public string InvocationId { get; }
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; }
    public CommandViews Views { get; }
    public CommandEffects Effects { get; }
    public CommandData Data { get; }
    public CommandBrokers Brokers { get; }
    internal CancellationToken Lifetime => _cancellationToken;
    internal void EnsureActive( )
    {
        _cancellationToken.ThrowIfCancellationRequested( );
        if ( Volatile.Read( ref _completed ) != 0 )
        {
            throw new InvalidOperationException( "The command invocation has completed. Retained contexts cannot publish views or effects." );
        }
    }
    public void Dispose( ) => Interlocked.Exchange( ref _completed, 1 );
}

public sealed class CommandViews
{
    private readonly HostChannel _channel;
    private readonly Dictionary<string, long> _revisions;
    private readonly SemaphoreSlim _writes;

    internal CommandViews( HostChannel channel, Dictionary<string, long> revisions, SemaphoreSlim writes )
    {
        _channel = channel;
        _revisions = revisions;
        _writes = writes;
    }

    public ValueTask ReplaceAsync( ExtensionView view, CancellationToken cancellationToken = default ) => PublishAsync( ExtensionMethods.ViewsReplace, view, cancellationToken );
    public ValueTask PushAsync( ExtensionView view, CancellationToken cancellationToken = default ) => PublishAsync( ExtensionMethods.ViewsPush, view, cancellationToken );

    private async ValueTask PublishAsync( string method, ExtensionView view, CancellationToken cancellationToken )
    {
        _channel.RequireMode( ExtensionCommandModes.View );
        ViewValidation.Validate( view );
        var missing = ViewRequirements.FirstMissing( view, method == ExtensionMethods.ViewsPush, _channel.Features );
        if ( missing is not null )
        {
            throw new InvalidOperationException( "The host does not support required feature " + missing + "." );
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, _channel.Lifetime );
        await _writes.WaitAsync( lifetime.Token ).ConfigureAwait( false );
        try
        {
            _channel.EnsureActive( );
            var stamp = _channel.Stamp;
            var update = new ViewUpdate( stamp.SessionId, stamp.Epoch, view.Id,
                method == ExtensionMethods.ViewsPush ? 0 : _revisions.GetValueOrDefault( view.Id ), stamp.Generation, view );
            var accepted = await _channel.CallAsync( method, null, update, ExtensionViewJsonContext.Default.ViewUpdate,
                ExtensionViewJsonContext.Default.ViewAccepted, lifetime.Token ).ConfigureAwait( false );
            if ( accepted.ViewId != view.Id || accepted.Revision <= update.ExpectedRevision )
            {
                throw new InvalidDataException( "The host returned an invalid view acknowledgement." );
            }
            _revisions[view.Id] = accepted.Revision;
        }
        finally
        {
            _writes.Release( );
        }
    }
}
