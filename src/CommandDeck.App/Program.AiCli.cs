using System.Text;
using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunAiAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count == 0 ? AiCliActions.Ask : positionals[0].ToLowerInvariant( );
        return action switch
        {
            AiCliActions.Providers => WriteProviders( runtime.GetAiProviders( ), arguments ),
            AiCliActions.Models => await WriteModelsAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AiCliActions.Selection => await RunAiSelectionAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AiCliActions.Templates => await RunAiTemplatesAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AiCliActions.Ask => await RunAiPromptAsync( runtime, arguments, true, cancellationToken )
                .ConfigureAwait( false ),
            _ => await RunAiPromptAsync( runtime, arguments, false, cancellationToken )
                .ConfigureAwait( false )
        };
    }

    private static async Task<int> RunAiPromptAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        bool skipAction,
        CancellationToken cancellationToken )
    {
        var selection = await runtime.GetAiSelectionAsync( cancellationToken )
            .ConfigureAwait( false );
        var provider = ResolveProviderId( runtime, arguments, selection );
        var prompt = ResolvePrompt( arguments, skipAction );
        if ( string.IsNullOrWhiteSpace( prompt ) )
        {
            Console.Error.WriteLine( "Provide a prompt with --prompt or positional text." );
            return 2;
        }

        var providerOptions = runtime.EffectiveAiOptions.Providers.FirstOrDefault( value =>
            value.Enabled && string.Equals( value.Id, provider, StringComparison.OrdinalIgnoreCase ) );
        if ( providerOptions is null )
        {
            Console.Error.WriteLine( "The selected AI provider is unavailable." );
            return 2;
        }
        var draft = CreateDraft( arguments, selection, providerOptions, provider, prompt );
        var clientOwnerId = $"cli-{Guid.NewGuid( ):N}";
        var preflight = await runtime.CreateAiPreflightAsync(
            clientOwnerId, draft, cancellationToken ).ConfigureAwait( false );
        WritePreflight( preflight, arguments.HasFlag( CliOptionNames.Yes ) );
        if ( !arguments.HasFlag( CliOptionNames.Yes ) )
        {
            return 2;
        }

        StringBuilder output = new( );
        AiDone? terminal = null;
        await runtime.StreamAiAsync(
            clientOwnerId,
            preflight.Id,
            preflight.PayloadDigest,
            preflight.Generation,
            chunk =>
            {
                output.Append( chunk.Text );
                if ( !arguments.HasFlag( CliOptionNames.Json ) )
                {
                    Console.Write( Safe( chunk.Text ) );
                }

                return ValueTask.CompletedTask;
            },
            done =>
            {
                terminal = done;
                return ValueTask.CompletedTask;
            },
            cancellationToken ).ConfigureAwait( false );
        if ( terminal?.Outcome != AiGenerationOutcomes.Completed )
        {
            Console.Error.WriteLine( terminal?.Error?.Summary ?? "AI generation failed." );
            return 1;
        }
        WriteAiOutput( arguments, provider, preflight.ModelId, output.ToString( ) );
        return 0;
    }

    private static string ResolveProviderId(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        AiSelection selection ) =>
        arguments.GetOption( CliOptionNames.Provider ) ?? selection.ProviderId ??
        runtime.EffectiveAiOptions.Providers.FirstOrDefault( static value => value.Enabled )?.Id ??
        string.Empty;

    private static string ResolvePrompt( CliArguments arguments, bool skipAction ) =>
        arguments.GetOption( CliOptionNames.Prompt ) ??
        string.Join( ' ', arguments.PositionalsAfterCommand( ).Skip( skipAction ? 1 : 0 ) );

    private static AiDraft CreateDraft(
        CliArguments arguments,
        AiSelection selection,
        AiProviderOptions providerOptions,
        string provider,
        string prompt )
    {
        var selectedModel = string.Equals(
            selection.ProviderId, provider, StringComparison.OrdinalIgnoreCase )
            ? selection.ModelId
            : null;
        List<AiAttachmentDraft> attachments = [];
        if ( arguments.GetOption( CliOptionNames.AttachText ) is string attachmentText )
        {
            attachments.Add( new AiAttachmentDraft
            {
                Kind = AiAttachmentKinds.Text,
                Text = attachmentText,
                DisplayName = "CLI text attachment"
            } );
        }
        return new AiDraft
        {
            ProviderId = provider,
            ModelId = arguments.GetOption( CliOptionNames.Model ) ?? selectedModel ?? providerOptions.Model,
            UserText = prompt,
            WorkflowId = arguments.GetOption( CliOptionNames.Workflow ),
            TemplateId = arguments.GetOption( CliOptionNames.Template ),
            Attachments = attachments
        };
    }

    private static int WriteProviders( AiProviderInventory inventory, CliArguments arguments )
    {
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                inventory, CommandDeckJsonContext.Default.AiProviderInventory ) );
            return 0;
        }
        foreach ( var provider in inventory.Providers )
        {
            Console.WriteLine(
                $"{Safe( provider.Id )}  {Safe( provider.DisplayName )}  {provider.Locality}  " +
                $"{provider.HealthState}  {Safe( provider.Destination )}" );
        }
        return 0;
    }

    private static async Task<int> WriteModelsAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var provider = arguments.GetOption( CliOptionNames.Provider );
        if ( string.IsNullOrWhiteSpace( provider ) )
        {
            Console.Error.WriteLine( "ai models requires --provider ID." );
            return 2;
        }
        var inventory = await runtime.DiscoverAiModelsAsync(
            provider, cancellationToken ).ConfigureAwait( false );
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                inventory, CommandDeckJsonContext.Default.AiModelInventory ) );
        }
        else if ( inventory.Error is not null )
        {
            Console.Error.WriteLine( $"{inventory.Error.Category}: {Safe( inventory.Error.Summary )}" );
        }
        else
        {
            foreach ( var model in inventory.Models )
            {
                Console.WriteLine( $"{Safe( model.Id )}  {Safe( model.Owner ?? "unknown owner" )}" );
            }
        }
        return inventory.Error is null ? 0 : 1;
    }

    private static async Task<int> RunAiSelectionAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant( ) : "show";
        AiSelection selection;
        if ( action == "set" )
        {
            var provider = arguments.GetOption( CliOptionNames.Provider );
            var model = arguments.GetOption( CliOptionNames.Model );
            if ( string.IsNullOrWhiteSpace( provider ) || string.IsNullOrWhiteSpace( model ) )
            {
                Console.Error.WriteLine( "ai selection set requires --provider ID --model ID." );
                return 2;
            }
            selection = await runtime.SetAiSelectionAsync( provider, model, cancellationToken )
                .ConfigureAwait( false );
        }
        else if ( action == "clear" )
        {
            selection = await runtime.SetAiSelectionAsync( null, null, cancellationToken )
                .ConfigureAwait( false );
        }
        else if ( action == "show" )
        {
            selection = await runtime.GetAiSelectionAsync( cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            Console.Error.WriteLine( "Use 'ai selection show', 'set', or 'clear'." );
            return 2;
        }
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                selection, CommandDeckJsonContext.Default.AiSelection ) );
        }
        else
        {
            Console.WriteLine(
                $"Provider: {Safe( selection.ProviderId ?? "none" )}\n" +
                $"Model:    {Safe( selection.ModelId ?? "none" )}\n" +
                $"State:    {selection.State}\n" +
                $"Detail:   {Safe( selection.Explanation ?? "none" )}" );
        }
        return 0;
    }

    private static async Task<int> RunAiTemplatesAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant( ) : AiCliActions.List;
        IReadOnlyList<AiTemplate> templates;
        if ( action == AiCliActions.Save )
        {
            var id = arguments.GetOption( CliOptionNames.Id );
            var name = arguments.GetOption( CliOptionNames.Name );
            var user = arguments.GetOption( CliOptionNames.User );
            if ( string.IsNullOrWhiteSpace( id ) || string.IsNullOrWhiteSpace( name ) ||
                string.IsNullOrWhiteSpace( user ) )
            {
                Console.Error.WriteLine( "ai templates save requires --id, --name, and --user." );
                return 2;
            }
            templates = await runtime.SaveAiTemplateAsync( new AiTemplate
            {
                Id = id,
                Name = name,
                WorkflowKind = arguments.GetOption( CliOptionNames.Workflow ) ?? "custom",
                SystemTemplate = arguments.GetOption( CliOptionNames.System ) ?? string.Empty,
                UserTemplate = user
            }, cancellationToken ).ConfigureAwait( false );
        }
        else if ( action == AiCliActions.Delete )
        {
            var id = arguments.GetOption( CliOptionNames.Id );
            if ( string.IsNullOrWhiteSpace( id ) || !arguments.HasFlag( CliOptionNames.Yes ) )
            {
                Console.Error.WriteLine( "ai templates delete requires --id ID --yes." );
                return 2;
            }
            templates = await runtime.DeleteAiTemplateAsync( id, cancellationToken )
                .ConfigureAwait( false );
        }
        else if ( action == AiCliActions.List )
        {
            templates = await runtime.GetAiTemplatesAsync( cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            Console.Error.WriteLine( "Use 'ai templates list', 'save', or 'delete'." );
            return 2;
        }
        AiTemplateResult result = new( )
        {
            Templates = [.. templates]
        };
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                result, CommandDeckJsonContext.Default.AiTemplateResult ) );
        }
        else
        {
            foreach ( var template in templates )
            {
                Console.WriteLine( $"{Safe( template.Id )}  {Safe( template.Name )}  {template.WorkflowKind}" );
            }
        }
        return 0;
    }

    private static string Safe( string value ) =>
        PreferencesTextFormatter.EscapeTerminalText( value );

    private static void WritePreflight( AiPreflight preflight, bool confirmed )
    {
        Console.Error.WriteLine( $"Provider:    {Safe( preflight.ProviderId )}" );
        Console.Error.WriteLine( $"Destination: {Safe( preflight.Destination )} ({Safe( preflight.Locality )})" );
        Console.Error.WriteLine( $"Model:       {Safe( preflight.ModelId )}" );
        Console.Error.WriteLine( $"Credential:  {Safe( preflight.CredentialSource )}" );
        Console.Error.WriteLine( $"Payload:     {preflight.PayloadBytes} UTF-8 bytes" );
        Console.Error.WriteLine( $"Digest:      {preflight.PayloadDigest}" );
        Console.Error.WriteLine( $"System:      {Safe( preflight.SystemMessage )}" );
        Console.Error.WriteLine( $"User:        {Safe( preflight.UserMessage )}" );
        Console.Error.WriteLine(
            $"Provider request: {Safe( preflight.ProviderRequestPreview )}" );
        foreach ( var attachment in preflight.Attachments )
        {
            Console.Error.WriteLine(
                $"Attachment: {Safe( attachment.DisplayName )} ({attachment.Utf8Bytes} UTF-8 bytes)" );
        }
        Console.Error.WriteLine( confirmed
            ? "The exact payload above is confirmed; transmitting now."
            : "Review the prompt above and rerun with --yes to create, display, and transmit a fresh preflight." );
    }

    private static void WriteAiOutput(
        CliArguments arguments,
        string provider,
        string model,
        string output )
    {
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                new AiResponse { ProviderId = provider, Model = model, Text = output },
                CommandDeckJsonContext.Default.AiResponse ) );
        }
        else
        {
            Console.WriteLine( );
        }
    }
}
