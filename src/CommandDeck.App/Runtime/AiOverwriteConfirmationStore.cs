using System.Security.Cryptography;

namespace CommandDeck;

internal sealed class AiOverwriteConfirmationStore( TimeProvider clock )
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes( 2 );
    private const int MaximumPerClient = 16;
    private const int MaximumGlobally = 256;
    private readonly Dictionary<string, Confirmation> _entries = new( StringComparer.Ordinal );
    private readonly object _gate = new( );
    private long _sequence;

    public string Create(
        string clientOwnerId,
        long generation,
        string path,
        string outputDigest,
        string targetIdentity )
    {
        var token = Convert.ToHexString( RandomNumberGenerator.GetBytes( 32 ) ).ToLowerInvariant( );
        lock ( _gate )
        {
            RemoveExpired( );
            while ( _entries.Count( pair => string.Equals(
                       pair.Value.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) ) >=
                   MaximumPerClient )
            {
                RemoveOldest( pair => string.Equals(
                    pair.Value.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) );
            }
            while ( _entries.Count >= MaximumGlobally )
            {
                RemoveOldest( static _ => true );
            }
            _entries[token] = new Confirmation(
                clientOwnerId,
                generation,
                path,
                outputDigest,
                targetIdentity,
                clock.GetUtcNow( ) + Lifetime,
                Interlocked.Increment( ref _sequence ) );
        }
        return token;
    }

    public bool Consume(
        string? token,
        string clientOwnerId,
        long generation,
        string path,
        string outputDigest,
        string targetIdentity )
    {
        if ( string.IsNullOrWhiteSpace( token ) )
        {
            return false;
        }
        lock ( _gate )
        {
            RemoveExpired( );
            if ( !_entries.Remove( token, out var pending ) )
            {
                return false;
            }
            return string.Equals(
                    pending.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) &&
                pending.Generation == generation &&
                string.Equals( pending.Path, path, StringComparison.Ordinal ) &&
                string.Equals( pending.OutputDigest, outputDigest, StringComparison.Ordinal ) &&
                string.Equals( pending.TargetIdentity, targetIdentity, StringComparison.Ordinal );
        }
    }

    public void ClearClient( string clientOwnerId )
    {
        lock ( _gate )
        {
            var tokens = _entries
                .Where( pair => string.Equals(
                    pair.Value.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) )
                .Select( static pair => pair.Key )
                .ToArray( );
            foreach ( var token in tokens )
            {
                _entries.Remove( token );
            }
        }
    }

    public void Clear( )
    {
        lock ( _gate )
        {
            _entries.Clear( );
        }
    }

    private void RemoveExpired( )
    {
        var now = clock.GetUtcNow( );
        var expired = _entries
            .Where( pair => pair.Value.ExpiresAt < now )
            .Select( static pair => pair.Key )
            .ToArray( );
        foreach ( var token in expired )
        {
            _entries.Remove( token );
        }
    }

    private void RemoveOldest( Func<KeyValuePair<string, Confirmation>, bool> predicate )
    {
        var oldest = _entries
            .Where( predicate )
            .OrderBy( static pair => pair.Value.Sequence )
            .Select( static pair => pair.Key )
            .FirstOrDefault( );
        if ( oldest is not null )
        {
            _entries.Remove( oldest );
        }
    }

    private sealed record Confirmation(
        string ClientOwnerId,
        long Generation,
        string Path,
        string OutputDigest,
        string TargetIdentity,
        DateTimeOffset ExpiresAt,
        long Sequence );
}
