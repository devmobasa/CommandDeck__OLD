using System.Text;

namespace CommandDeck;

public static class AgentContractValidator
{
    public static void ValidatePromptText( string? text )
    {
        if ( string.IsNullOrWhiteSpace( text ) )
        {
            throw new InvalidOperationException( "The agent prompt must not be empty." );
        }
        AiContractValidator.ValidateUtf8( text, AgentLimits.MaximumPromptBytes, "agent prompt", false );
    }

    public static void ValidateTrustLevel( int level )
    {
        if ( !AgentTrustLevels.IsValid( level ) )
        {
            throw new InvalidOperationException( "The trust level is outside the supported range." );
        }
    }

    public static void ValidateDuration( string? duration )
    {
        if ( duration is not (AgentTrustDurations.OneHour or AgentTrustDurations.UntilDowngrade) )
        {
            throw new InvalidOperationException( "The trust duration must be one-hour or until-downgrade." );
        }
    }

    public static void ValidatePreferences( AgentPreferences preferences )
    {
        ArgumentNullException.ThrowIfNull( preferences );
        ValidateTrustLevel( preferences.TrustLevel );
        if ( preferences.ContextPolicy is not (AgentContextPolicies.ApprovedFolders or
            AgentContextPolicies.AskEveryAccess) )
        {
            throw new InvalidOperationException( "The context policy is not supported." );
        }
        if ( preferences.ConversationTtlMinutes is < 0 or > AgentLimits.MaximumConversationTtlMinutes )
        {
            throw new InvalidOperationException( "The conversation retention is outside the supported range." );
        }
        if ( preferences.TurnTimeoutMinutes is < AgentLimits.MinimumTurnTimeoutMinutes or
            > AgentLimits.MaximumTurnTimeoutMinutes )
        {
            throw new InvalidOperationException( "The turn timeout is outside the supported range." );
        }
        if ( preferences.BackendId is not null )
        {
            AiContractValidator.ValidateIdentifier( preferences.BackendId, 64, "backendId" );
        }
        if ( preferences.ModelId is not null )
        {
            AiContractValidator.ValidateUtf8(
                preferences.ModelId, AiLimits.MaximumModelIdBytes, "modelId", false, allowLineBreaks: false );
        }
        if ( preferences.ReasoningEffort is not null )
        {
            AiContractValidator.ValidateIdentifier( preferences.ReasoningEffort, 32, "reasoningEffort" );
        }
        if ( preferences.TrustDuration is not null )
        {
            ValidateDuration( preferences.TrustDuration );
        }
        if ( preferences.LoopbackPreflightProviderId is not null )
        {
            AiContractValidator.ValidateIdentifier(
                preferences.LoopbackPreflightProviderId, 64, "loopbackPreflightProviderId" );
        }
    }

    public static void ValidateArgv( IReadOnlyList<string>? argv )
    {
        if ( argv is null || argv.Count == 0 )
        {
            throw new InvalidOperationException( "The command requires at least one argument." );
        }
        if ( argv.Count > AgentLimits.MaximumCommandArguments )
        {
            throw new InvalidOperationException( "The command has too many arguments." );
        }
        foreach ( var argument in argv )
        {
            if ( argument is null )
            {
                throw new InvalidOperationException( "Command arguments must not be null." );
            }
            if ( argument.Contains( '\0', StringComparison.Ordinal ) )
            {
                throw new InvalidOperationException( "Command arguments must not contain NUL." );
            }
            if ( Encoding.UTF8.GetByteCount( argument ) > AgentLimits.MaximumCommandArgumentBytes )
            {
                throw new InvalidOperationException( "A command argument is too long." );
            }
        }
    }

    public static bool IsValidPackageName( string? name )
    {
        if ( string.IsNullOrEmpty( name ) || name.Length > 255 || name[0] is '-' or '.' )
        {
            return false;
        }
        foreach ( var character in name )
        {
            var allowed = char.IsAsciiLetterOrDigit( character ) ||
                          character is '@' or '.' or '_' or '+' or '-';
            if ( !allowed )
            {
                return false;
            }
        }
        return true;
    }

    public static IReadOnlyList<string> NormalizePackageNames( IReadOnlyList<string>? names )
    {
        if ( names is null || names.Count == 0 )
        {
            throw new InvalidOperationException( "At least one package name is required." );
        }
        List<string> normalized = [];
        foreach ( var name in names )
        {
            if ( !IsValidPackageName( name ) )
            {
                throw new InvalidOperationException( "A package name is invalid." );
            }
            if ( !normalized.Contains( name, StringComparer.Ordinal ) )
            {
                normalized.Add( name );
            }
        }
        if ( normalized.Count > AgentLimits.MaximumPackageNames )
        {
            throw new InvalidOperationException(
                $"At most {AgentLimits.MaximumPackageNames} package names are allowed." );
        }
        normalized.Sort( StringComparer.Ordinal );
        return normalized;
    }

    public static string ValidateOpaqueId( string? value, string label )
    {
        AiContractValidator.ValidateIdentifier( value, 96, label );
        return value!;
    }
}
