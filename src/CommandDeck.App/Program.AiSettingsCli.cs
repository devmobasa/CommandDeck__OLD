using System.Net.Sockets;
using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    /// <summary>
    /// Management and draft-test commands act on the running daemon so the
    /// managed document has exactly one writer and credential references resolve
    /// in the daemon's environment, not this shell's.
    /// </summary>
    private static bool AiActionUsesDaemon( CliArguments arguments )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        if ( positionals.Count == 0 )
        {
            return false;
        }
        var action = positionals[0].ToLowerInvariant( );
        if ( action is AiCliActions.Settings or AiCliActions.Enable or AiCliActions.Disable )
        {
            return true;
        }
        return action == AiCliActions.Providers && positionals.Count >= 2 &&
            positionals[1].ToLowerInvariant( ) is AiCliActions.Add or AiCliActions.Edit or
                AiCliActions.Enable or AiCliActions.Disable or AiCliActions.Remove or AiCliActions.Test;
    }

    private static async Task<int> RunAiDaemonAsync(
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals[0].ToLowerInvariant( );
        var json = arguments.HasFlag( CliOptionNames.Json );
        try
        {
            await using var client = await UnixProtocolClient.ConnectAsync(
                arguments.GetOption( "--socket" ), cancellationToken ).ConfigureAwait( false );
            return action switch
            {
                AiCliActions.Settings => WriteAiSettings(
                    await RequestAiSettingsAsync( client, cancellationToken ).ConfigureAwait( false ), json ),
                AiCliActions.Enable or AiCliActions.Disable => await RunAiEnableAsync(
                    client, action == AiCliActions.Enable, json, cancellationToken ).ConfigureAwait( false ),
                _ => await RunAiProviderManagementAsync(
                    client, arguments, positionals, json, cancellationToken ).ConfigureAwait( false )
            };
        }
        catch ( SocketException )
        {
            Console.Error.WriteLine( "The CommandDeck daemon is not running or its socket is unavailable." );
            return 1;
        }
        catch ( AiOperationException exception )
        {
            Console.Error.WriteLine( $"{Safe( exception.Category )}: {Safe( exception.SafeSummary )}" );
            return exception.Category == AiErrorCategories.Conflict ? 3 : 1;
        }
        catch ( InvalidOperationException exception )
        {
            Console.Error.WriteLine( Safe( exception.Message ) );
            return 2;
        }
    }

    private static async Task<int> RunAiEnableAsync(
        UnixProtocolClient client,
        bool enabled,
        bool json,
        CancellationToken cancellationToken )
    {
        var current = await RequestAiSettingsAsync( client, cancellationToken ).ConfigureAwait( false );
        var updated = await RequestAiManagementAsync( client, new ProtocolRequest
        {
            RequestId = 2,
            Operation = ProtocolOperations.AiSettingsUpdate,
            Enabled = enabled,
            ExpectedRevision = current.Revision
        }, cancellationToken ).ConfigureAwait( false );
        return WriteAiSettings( updated.AiSettings!, json );
    }

    private static async Task<int> RunAiProviderManagementAsync(
        UnixProtocolClient client,
        CliArguments arguments,
        IReadOnlyList<string> positionals,
        bool json,
        CancellationToken cancellationToken )
    {
        var verb = positionals[1].ToLowerInvariant( );
        var current = await RequestAiSettingsAsync( client, cancellationToken ).ConfigureAwait( false );
        var id = positionals.Count > 2 ? positionals[2] : arguments.GetOption( CliOptionNames.Id );
        if ( verb == AiCliActions.Test && arguments.HasFlag( CliOptionNames.Draft ) )
        {
            var draft = BuildProviderDefinition( arguments, id, existing: null, preset: FindPreset( current, arguments ) );
            return WriteAiTestResult( await RequestAiTestAsync( client, current, draft, cancellationToken )
                .ConfigureAwait( false ), json );
        }
        if ( string.IsNullOrWhiteSpace( id ) )
        {
            Console.Error.WriteLine( $"ai providers {verb} requires a provider ID." );
            return 2;
        }
        var existing = current.Providers.FirstOrDefault( value =>
            string.Equals( value.Id, id, StringComparison.OrdinalIgnoreCase ) );
        switch ( verb )
        {
            case AiCliActions.Add:
                {
                    var definition = BuildProviderDefinition( arguments, id, existing: null, preset: FindPreset( current, arguments ) );
                    return WriteAiSaveResult( await RequestAiSaveAsync(
                        client, current, definition, create: true, arguments.HasFlag( CliOptionNames.Use ),
                        cancellationToken ).ConfigureAwait( false ), json );
                }
            case AiCliActions.Edit when existing is null:
            case AiCliActions.Enable when existing is null:
            case AiCliActions.Disable when existing is null:
            case AiCliActions.Test when existing is null:
                Console.Error.WriteLine( $"AI provider '{Safe( id )}' was not found." );
                return 1;
            case AiCliActions.Edit:
                {
                    var definition = BuildProviderDefinition( arguments, id, existing, preset: null );
                    return WriteAiSaveResult( await RequestAiSaveAsync(
                        client, current, definition, create: false, arguments.HasFlag( CliOptionNames.Use ),
                        cancellationToken ).ConfigureAwait( false ), json );
                }
            case AiCliActions.Enable or AiCliActions.Disable:
                {
                    var definition = CopyProvider( existing!, enabled: verb == AiCliActions.Enable );
                    return WriteAiSaveResult( await RequestAiSaveAsync(
                        client, current, definition, create: false, selectAfterSave: false,
                        cancellationToken ).ConfigureAwait( false ), json );
                }
            case AiCliActions.Remove:
                if ( !arguments.HasFlag( CliOptionNames.Yes ) )
                {
                    Console.Error.WriteLine( "ai providers remove requires --yes." );
                    return 2;
                }
                var removed = await RequestAiManagementAsync( client, new ProtocolRequest
                {
                    RequestId = 2,
                    Operation = ProtocolOperations.AiProvidersDelete,
                    ProviderId = id,
                    ExpectedRevision = current.Revision,
                    Confirmed = true
                }, cancellationToken ).ConfigureAwait( false );
                return WriteAiSettings( removed.AiSettings!, json );
            case AiCliActions.Test:
                return WriteAiTestResult( await RequestAiTestAsync( client, current, existing!, cancellationToken )
                    .ConfigureAwait( false ), json );
            default:
                Console.Error.WriteLine( "Use 'ai providers add|edit|enable|disable|remove|test'." );
                return 2;
        }
    }

    private static AiProviderPreset? FindPreset( AiProviderSettings settings, CliArguments arguments )
    {
        var presetId = arguments.GetOption( CliOptionNames.Preset );
        if ( presetId is null )
        {
            return null;
        }
        var result2 = settings.Presets.FirstOrDefault( value =>
            string.Equals( value.Id, presetId, StringComparison.OrdinalIgnoreCase ) );
        if ( result2 is null )
        {
            throw new InvalidOperationException( "Unknown preset. Use one of: " +
                string.Join( ", ", settings.Presets.Select( static value => value.Id ) ) + "." );
        }

        return result2;
    }

    private static AiProviderOptions BuildProviderDefinition(
        CliArguments arguments,
        string? id,
        AiProviderOptions? existing,
        AiProviderPreset? preset )
    {
        if ( string.IsNullOrWhiteSpace( id ) )
        {
            throw new InvalidOperationException( "A provider ID is required (--id or positional)." );
        }
        var defaults = DefinitionDefaults.From( existing, preset );
        return new AiProviderOptions
        {
            Id = id,
            DisplayName = arguments.GetOption( CliOptionNames.Name ) ?? defaults.DisplayName ?? id,
            Kind = RequireDefinitionValue(
                arguments.GetOption( CliOptionNames.Kind ) ?? defaults.Kind,
                "--kind is required (ollama or openai-chat-completions)." ),
            BaseUrl = RequireDefinitionValue(
                arguments.GetOption( CliOptionNames.BaseUrl ) ?? defaults.BaseUrl,
                "--base-url is required." ),
            Model = RequireDefinitionValue(
                arguments.GetOption( CliOptionNames.Model ) ?? defaults.Model,
                "--model is required; discovery never picks one for you." ),
            Credential = ResolveCredentialReference( arguments, defaults ),
            AllowRemoteEndpoint = arguments.HasFlag( CliOptionNames.AllowRemote ) || defaults.AllowRemoteEndpoint,
            Enabled = !arguments.HasFlag( CliOptionNames.Disabled ) && defaults.Enabled,
            ContextLimit = ResolveContextLimit( arguments, defaults.ContextLimit )
        };
    }

    // Existing definitions win over presets; a preset only seeds a new definition.
    private sealed record DefinitionDefaults(
        string? DisplayName,
        string? Kind,
        string? BaseUrl,
        string? Model,
        string CredentialKind,
        string? CredentialName,
        bool AllowRemoteEndpoint,
        bool Enabled,
        long? ContextLimit )
    {
        public static DefinitionDefaults From( AiProviderOptions? existing, AiProviderPreset? preset )
        {
            if ( existing is not null )
            {
                return new DefinitionDefaults(
                    existing.DisplayName, existing.Kind, existing.BaseUrl, existing.Model,
                    existing.Credential.Kind, existing.Credential.Name, existing.AllowRemoteEndpoint,
                    existing.Enabled, existing.ContextLimit );
            }
            if ( preset is not null )
            {
                return new DefinitionDefaults(
                    preset.DisplayName, preset.Kind, preset.BaseUrl, null,
                    preset.CredentialKind, preset.CredentialName, false, true, null );
            }
            return new DefinitionDefaults( null, null, null, null, AiCredentialKinds.None, null, false, true, null );
        }
    }

    private static string RequireDefinitionValue( string? value, string message ) =>
        string.IsNullOrWhiteSpace( value ) ? throw new InvalidOperationException( message ) : value;

    private static AiCredentialOptions ResolveCredentialReference( CliArguments arguments, DefinitionDefaults defaults )
    {
        var explicitKind = arguments.GetOption( CliOptionNames.CredentialKind );
        var kind = explicitKind ?? defaults.CredentialKind;
        // A changed kind drops the previous name unless one is given explicitly.
        var inherited = explicitKind is null ? defaults.CredentialName : null;
        var name = arguments.GetOption( CliOptionNames.CredentialName ) ?? inherited;
        return new AiCredentialOptions
        {
            Kind = kind,
            Name = kind == AiCredentialKinds.None ? null : name
        };
    }

    private static long? ResolveContextLimit( CliArguments arguments, long? inherited )
    {
        var value = arguments.GetOption( CliOptionNames.Context );
        if ( value is null )
        {
            return inherited;
        }
        return long.TryParse( value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed )
            ? parsed
            : throw new InvalidOperationException( "--context must be a positive integer." );
    }

    private static AiProviderOptions CopyProvider( AiProviderOptions source, bool enabled ) => new( )
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        Kind = source.Kind,
        BaseUrl = source.BaseUrl,
        Model = source.Model,
        Credential = new AiCredentialOptions { Kind = source.Credential.Kind, Name = source.Credential.Name },
        AllowRemoteEndpoint = source.AllowRemoteEndpoint,
        Enabled = enabled,
        ContextLimit = source.ContextLimit
    };

    private static async Task<AiProviderSettings> RequestAiSettingsAsync(
        UnixProtocolClient client,
        CancellationToken cancellationToken )
    {
        var message = await RequestAiManagementAsync( client, new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.AiSettingsGet
        }, cancellationToken ).ConfigureAwait( false );
        var result3 = message.AiSettings;
        if ( result3 is null )
        {
            throw new InvalidOperationException( "The daemon returned no AI settings." );
        }

        return result3;
    }

    private static Task<ProtocolMessage> RequestAiSaveAsync(
        UnixProtocolClient client,
        AiProviderSettings current,
        AiProviderOptions definition,
        bool create,
        bool selectAfterSave,
        CancellationToken cancellationToken ) =>
        RequestAiManagementAsync( client, new ProtocolRequest
        {
            RequestId = 2,
            Operation = ProtocolOperations.AiProvidersSave,
            AiProvider = definition,
            Create = create,
            ExpectedRevision = current.Revision,
            SelectAfterSave = selectAfterSave
        }, cancellationToken );

    private static Task<ProtocolMessage> RequestAiTestAsync(
        UnixProtocolClient client,
        AiProviderSettings current,
        AiProviderOptions definition,
        CancellationToken cancellationToken ) =>
        RequestAiManagementAsync( client, new ProtocolRequest
        {
            RequestId = 2,
            Operation = ProtocolOperations.AiProvidersTest,
            AiProvider = definition,
            DraftRevision = 1,
            ExpectedRevision = current.Revision
        }, cancellationToken );

    private static async Task<ProtocolMessage> RequestAiManagementAsync(
        UnixProtocolClient client,
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( request, cancellationToken ).ConfigureAwait( false );
        ProtocolMessage? result = null;
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var message = ProtocolJson.DeserializeMessage( line );
            if ( message is null || message.RequestId != request.RequestId )
            {
                continue;
            }
            if ( message.Type == ProtocolMessageTypes.Error )
            {
                throw ParseManagementError( message.Error );
            }
            if ( message.Type == ProtocolMessageTypes.Done )
            {
                if ( result is null )
                {
                    throw new InvalidOperationException( "The daemon acknowledged without a result." );
                }

                return result;
            }
            result = message;
        }
        // A closed socket after the request was sent is an unknown outcome: the
        // commit may have happened. Reload rather than replay.
        throw new AiOperationException(
            AiErrorCategories.Unavailable,
            safeSummary: "The daemon connection closed before acknowledging; run 'ai settings show' to see the current state." );
    }

    private static AiOperationException ParseManagementError( string? error )
    {
        var text = error ?? "The daemon rejected the request.";
        var separator = text.IndexOf( ": ", StringComparison.Ordinal );
        if ( separator > 0 && separator <= 32 && text[..separator].All( static character =>
                char.IsAsciiLetterLower( character ) || character == '-' ) )
        {
            return new AiOperationException( text[..separator], safeSummary: text[(separator + 2)..] );
        }
        return new AiOperationException( AiErrorCategories.InvalidConfiguration, safeSummary: text );
    }

    private static int WriteAiSettings( AiProviderSettings settings, bool json )
    {
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize( settings, CommandDeckJsonContext.Default.AiProviderSettings ) );
            return settings.Error is null ? 0 : 1;
        }
        Console.WriteLine( $"Direct AI: {(settings.Enabled ? "enabled" : "disabled")}" );
        Console.WriteLine( $"Source:    {Safe( settings.Source )} (revision {settings.Revision})" );
        if ( settings.LegacyIgnored && settings.Source == AiProviderSettingsSources.ManagedDocument )
        {
            Console.WriteLine( "Note:      the ai section of commanddeck.jsonc is inactive; the managed document owns providers." );
        }
        if ( settings.Error is not null )
        {
            Console.WriteLine( $"Error:     {Safe( settings.Error.Category )}: {Safe( settings.Error.Summary )}" );
        }
        if ( settings.Selection is { ProviderId: not null } selection )
        {
            Console.WriteLine( $"Selected:  {Safe( selection.ProviderId )} / {Safe( selection.ModelId ?? "" )} ({Safe( selection.State )})" );
        }
        foreach ( var provider in settings.Providers )
        {
            Console.WriteLine(
                $"{Safe( provider.Id )}  {Safe( provider.DisplayName ?? provider.Id )}  {Safe( provider.Kind )}  " +
                $"{Safe( provider.BaseUrl )}  model={Safe( provider.Model )}  " +
                $"credential={Safe( provider.Credential.Kind )}{(provider.Credential.Name is null ? "" : ":" + Safe( provider.Credential.Name ))}  " +
                $"remote={provider.AllowRemoteEndpoint}  enabled={provider.Enabled}" );
        }
        return settings.Error is null ? 0 : 1;
    }

    private static int WriteAiSaveResult( ProtocolMessage message, bool json )
    {
        var result = message.AiProviderSave;
        if ( result is null )
        {
            throw new InvalidOperationException( "The daemon returned no save result." );
        }

        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize( result, CommandDeckJsonContext.Default.AiProviderSaveResult ) );
        }
        else
        {
            Console.WriteLine( $"Saved provider '{Safe( result.ProviderId )}' at revision {result.Settings.Revision}." );
            if ( result.SelectionRequested )
            {
                Console.WriteLine( result.SelectionApplied
                    ? $"Selection: {Safe( result.Selection?.ProviderId ?? "" )} / {Safe( result.Selection?.ModelId ?? "" )}"
                    : $"Provider saved; selection was not changed ({Safe( result.SelectionError?.Category ?? "" )}: {Safe( result.SelectionError?.Summary ?? "" )})." );
            }
        }
        return result.SelectionRequested && !result.SelectionApplied ? 4 : 0;
    }

    private static int WriteAiTestResult( ProtocolMessage message, bool json )
    {
        var result = message.AiProviderTest;
        if ( result is null )
        {
            throw new InvalidOperationException( "The daemon returned no test result." );
        }

        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize( result, CommandDeckJsonContext.Default.AiProviderTestResult ) );
        }
        else
        {
            Console.WriteLine( $"Destination: {Safe( result.Destination )} ({Safe( result.Locality )}, credential {Safe( result.CredentialState )})" );
            Console.WriteLine( $"Outcome:     {Safe( result.Outcome )}" );
            if ( result.Error is not null )
            {
                Console.WriteLine( $"Error:       {Safe( result.Error.Category )}: {Safe( result.Error.Summary )}" );
            }
            foreach ( var model in result.Models )
            {
                Console.WriteLine( $"  {Safe( model.Id )}" );
            }
        }
        return result.Outcome is AiProviderTestOutcomes.Models or AiProviderTestOutcomes.Empty ? 0 : 1;
    }
}
