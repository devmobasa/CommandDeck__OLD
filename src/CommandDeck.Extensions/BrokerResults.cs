using System.Collections.Immutable;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions;

public sealed record FileListing( ImmutableArray<FileEntry> Entries, int? NextOffset );
public sealed record FileEntry( string Name, string Reference, bool IsDirectory, long Size );
public sealed record FileContent( ReadOnlyMemory<byte> Bytes );
public sealed record HttpResult( int StatusCode, string? ContentType, ReadOnlyMemory<byte> Body );

/// <summary>A broker did not complete. An unknown mutation outcome must never be retried automatically.</summary>
public sealed class BrokerOutcomeException( string outcome ) : Exception( "Broker outcome: " + outcome + ". Do not automatically retry a mutation." )
{
    public string Outcome { get; } = outcome;
    public bool IsUnknown => Outcome == ExtensionBrokerOutcomes.Unknown;
    public bool IsCancelled => Outcome == ExtensionBrokerOutcomes.Cancelled;
    public bool IsRedirectDenied => Outcome == ExtensionBrokerOutcomes.RedirectDenied;
}
