using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CommandDeck;

public sealed class AgentTrustService( AgentAuthority authority, TimeProvider? clock = null )
{
    private const int MaximumChallengesPerClient = 4;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, TrustChallenge> _challenges = new( StringComparer.Ordinal );

    public AgentTrustChallenge CreateChallenge( string clientOwnerId, int targetLevel, AgentPreferences current )
    {
        ArgumentNullException.ThrowIfNull( current );
        AgentContractValidator.ValidateTrustLevel( targetLevel );
        PruneExpired( );
        if ( _challenges.Values.Count( value => value.ClientOwnerId == clientOwnerId ) >= MaximumChallengesPerClient )
        {
            throw new InvalidOperationException( "Too many pending trust challenges; wait for one to expire." );
        }
        var id = Convert.ToHexString( RandomNumberGenerator.GetBytes( 24 ) ).ToLowerInvariant( );
        var expires = _clock.GetUtcNow( ).AddSeconds( AgentLimits.ChallengeLifetimeSeconds );
        _challenges[id] = new TrustChallenge( id, clientOwnerId, targetLevel, expires );
        var unrestricted = targetLevel == AgentTrustLevels.Unrestricted;
        return new AgentTrustChallenge
        {
            Id = id,
            TargetLevel = targetLevel,
            TargetLevelName = AgentTrustLevels.Name( targetLevel ),
            ExpiresUnixMilliseconds = expires.ToUnixTimeMilliseconds( ),
            RequiresPhrase = unrestricted,
            RequiresDuration = unrestricted,
            Prompt = BuildPrompt( current.TrustLevel, targetLevel ),
            Warnings = BuildWarnings( targetLevel )
        };
    }

    public async ValueTask<AgentPreferences> ApplyAsync(
        string clientOwnerId,
        string challengeId,
        int level,
        string? phrase,
        string? duration,
        CancellationToken cancellationToken )
    {
        if ( !_challenges.TryRemove( challengeId, out var challenge ) )
        {
            throw new InvalidOperationException( "The trust challenge is unknown or already used." );
        }
        if ( challenge.ClientOwnerId != clientOwnerId || challenge.ExpiresAt <= _clock.GetUtcNow( ) ||
            challenge.TargetLevel != level )
        {
            throw new InvalidOperationException( "The trust challenge does not match this request." );
        }
        long? lease = null;
        string? storedDuration = null;
        if ( level == AgentTrustLevels.Unrestricted )
        {
            if ( !string.Equals( phrase, AgentLimits.UnrestrictedPhrase, StringComparison.Ordinal ) )
            {
                throw new InvalidOperationException( "The unrestricted phrase did not match." );
            }
            AgentContractValidator.ValidateDuration( duration );
            storedDuration = duration;
            if ( duration == AgentTrustDurations.OneHour )
            {
                lease = _clock.GetUtcNow( ).AddMinutes( AgentLimits.TrustLeaseHourMinutes ).ToUnixTimeMilliseconds( );
            }
        }
        return (await authority.SetTrustAsync( level, storedDuration, lease, cancellationToken ).ConfigureAwait( false )).Preferences;
    }

    public static AgentTrustState Describe( AgentPreferences preferences )
    {
        ArgumentNullException.ThrowIfNull( preferences );
        return new AgentTrustState
        {
            Level = preferences.TrustLevel,
            LevelName = AgentTrustLevels.Name( preferences.TrustLevel ),
            Duration = preferences.TrustDuration,
            LeaseExpiresUnixMilliseconds = preferences.TrustLeaseExpiresUnixMilliseconds,
            DefaultLevel = AgentTrustLevels.Default,
            Explanation = DescribeLevel( preferences.TrustLevel )
        };
    }

    public void ClearClient( string clientOwnerId )
    {
        foreach ( (var key, var challenge) in _challenges )
        {
            if ( challenge.ClientOwnerId == clientOwnerId )
            {
                _challenges.TryRemove( key, out _ );
            }
        }
    }

    private void PruneExpired( )
    {
        var now = _clock.GetUtcNow( );
        foreach ( (var key, var challenge) in _challenges )
        {
            if ( challenge.ExpiresAt <= now )
            {
                _challenges.TryRemove( key, out _ );
            }
        }
    }

    private static string BuildPrompt( int current, int target ) =>
        target > current
            ? $"Raise assistant trust from {AgentTrustLevels.Name( current )} to {AgentTrustLevels.Name( target )}?"
            : $"Lower assistant trust from {AgentTrustLevels.Name( current )} to {AgentTrustLevels.Name( target )}?";

    public static string DescribeLevel( int level ) => level switch
    {
        AgentTrustLevels.Answers => "Conversation only. No tools.",
        AgentTrustLevels.Launcher => "May search the launcher and propose existing actions.",
        AgentTrustLevels.ApprovedContext => "May read files inside folders you approve.",
        AgentTrustLevels.ReadEntireHome => "May read files under your home except protected paths.",
        AgentTrustLevels.Packages => "May propose official package changes that you confirm with Polkit.",
        AgentTrustLevels.ConfirmedCommands => "May propose exact commands that you confirm before CommandDeck runs them.",
        AgentTrustLevels.Unrestricted => "Runs with your full session and no CommandDeck confirmation cards.",
        _ => string.Empty
    };

    private static List<string> BuildWarnings( int target )
    {
        List<string> warnings = [];
        if ( target >= AgentTrustLevels.ReadEntireHome )
        {
            warnings.Add( "Files under your home directory can be read without a per-folder prompt; protected credential paths stay blocked." );
        }
        if ( target >= AgentTrustLevels.Packages )
        {
            warnings.Add( "Official package installs and removals become available after a preview, a confirmation, and a Polkit prompt." );
        }
        if ( target >= AgentTrustLevels.ConfirmedCommands )
        {
            warnings.Add( "Exact commands proposed by the assistant run as you after you confirm each one; their output returns to the model." );
        }
        if ( target == AgentTrustLevels.Unrestricted )
        {
            warnings.AddRange(
            [
                "Codex can read and modify your files and contact the network.",
                "Codex can control visible applications and session services.",
                "Commands run with your authority without CommandDeck action cards.",
                "The user-manager launch removes the daemon's no-new-privileges restriction.",
                "CommandDeck never provides a sudo password, hidden terminal, or PTY; sudo may fail when it needs a prompt.",
                "pkexec and Polkit may show a graphical authorization prompt when an agent is available.",
                "Level 4 package tools remain the audited package route.",
                "Protected credential paths are no longer enforced."
            ] );
        }
        return warnings;
    }

    private sealed record TrustChallenge( string Id, string ClientOwnerId, int TargetLevel, DateTimeOffset ExpiresAt );
}
