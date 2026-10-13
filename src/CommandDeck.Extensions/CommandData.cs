using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions;

/// <summary>Host-owned settings and bounded data for this installation. Retained contexts cannot access data.</summary>
public sealed class CommandData
{
    private readonly HostChannel _channel;
    internal CommandData( HostChannel channel ) => _channel = channel;

    public ValueTask<CommandDataResult> ReadSettingsAsync( CancellationToken cancellationToken = default ) => SendAsync( ExtensionDataOperations.Settings, null, 0, null, cancellationToken );
    public ValueTask<CommandDataResult> ReadAsync( CancellationToken cancellationToken = default ) => SendAsync( ExtensionDataOperations.Read, null, 0, null, cancellationToken );
    public ValueTask<CommandDataResult> WriteAsync( string key, JsonElement value, long expectedRevision, CancellationToken cancellationToken = default ) => SendAsync( ExtensionDataOperations.Write, key, expectedRevision, value, cancellationToken );
    public ValueTask<CommandDataResult> DeleteAsync( string key, long expectedRevision, CancellationToken cancellationToken = default ) => SendAsync( ExtensionDataOperations.Delete, key, expectedRevision, null, cancellationToken );

    private ValueTask<CommandDataResult> SendAsync( string operation, string? key, long revision, JsonElement? value,
        CancellationToken cancellationToken )
    {
        var stamp = _channel.Stamp;
        var request = new CommandDataRequest( stamp.SessionId, stamp.Epoch, stamp.Generation, stamp.InvocationId, operation, key, revision, value );
        return _channel.CallAsync( ExtensionMethods.CommandData,
            operation == ExtensionDataOperations.Settings ? ExtensionFeatures.Settings : ExtensionFeatures.Storage,
            request, ExtensionViewJsonContext.Default.CommandDataRequest, ExtensionViewJsonContext.Default.CommandDataResult, cancellationToken );
    }
}
