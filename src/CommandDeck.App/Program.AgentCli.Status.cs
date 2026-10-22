using System.Globalization;
using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static void WriteAgentRow( string label, string value ) =>
        Console.WriteLine( $"{label + ":",-16}{Safe( value )}" );

    private static async Task<int> WriteAgentStatusAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var status = await runtime.GetAgentStatusAsync( cancellationToken )
            .ConfigureAwait( false );
        // This process never starts Codex, so it resolves the binary the way Doctor
        // does: from this CLI's PATH, which can differ from the daemon's
        // user-manager PATH.
        (var binary, var failure) = await LocateCodexAsync( runtime.Options.Agent, cancellationToken )
            .ConfigureAwait( false );
        status.BinaryPath = binary?.CanonicalPath;
        status.BinaryVersion = binary?.Version;
        status.BinaryReviewed = binary?.Reviewed;
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                status, CommandDeckJsonContext.Default.AgentStatus ) );
            return 0;
        }

        var backend = status.Backend;
        var options = runtime.Options.Agent;
        WriteAgentRow( "Enabled", status.Enabled ? "true" : "false" );
        WriteAgentRow( "Backend", $"{backend?.Id ?? "none"} ({backend?.DisplayName ?? "none"})" );
        WriteAgentRow( "Health", backend?.HealthState ?? AgentHealthStates.Disabled );
        WriteAgentRow( "Authentication", status.Login.State );
        WriteAgentRow( "Trust", $"{status.Trust.Level} ({status.Trust.LevelName})" );
        WriteAgentRow( "Trust lease", FormatAgentLease( status.Trust ) );
        WriteAgentRow( "Model", status.Preferences.ModelId ?? "backend default" );
        WriteAgentRow( "Effort", status.Preferences.ReasoningEffort ?? "backend default" );
        WriteAgentRow( "Sandbox", FormatAgentTool( status.SandboxAvailable, options.BubblewrapPath ) );
        WriteAgentRow(
            "systemd-run", FormatAgentTool( status.TransientUnitAvailable, options.SystemdRunPath ) );
        WriteAgentRow( "Binary", binary is null
            ? $"not resolved ({failure})"
            : $"{binary.CanonicalPath} {binary.Version}{(binary.Reviewed ? string.Empty : " (unreviewed App Server version)")}" );
        WriteAgentRow( "Helper", FormatAgentHelper( status.Helper ) );
        WriteAgentRow( "Conversation", FormatAgentConversation( status.Conversation ) );
        WriteAgentRow( "Pending", $"{status.PendingRequestCount}" );
        foreach ( var warning in status.Warnings )
        {
            WriteAgentRow( "Warning", warning );
        }

        return 0;
    }

    private static string FormatAgentTool( bool available, string path ) =>
        $"{(available ? "available" : "missing")} ({path})";

    private static string FormatAgentLease( AgentTrustState trust ) =>
        trust.LeaseExpiresUnixMilliseconds is long expires
            ? $"{trust.Duration ?? AgentTrustDurations.UntilDowngrade} until " +
                DateTimeOffset.FromUnixTimeMilliseconds( expires )
                    .ToString( "O", CultureInfo.InvariantCulture )
            : trust.Duration ?? "no lease";

    private static string FormatAgentHelper( AgentHelperStatus helper ) =>
        helper.Installed
            ? $"installed version={helper.Version ?? "unknown"} " +
                $"matches={helper.VersionMatches} polkit={helper.PolkitAgentLikely}"
            : helper.Message ?? "not installed";

    private static string FormatAgentConversation( AgentConversationSummary? summary ) =>
        summary is null
            ? "none"
            : $"{summary.ConversationId} state={summary.State} turns={summary.TurnCount} " +
                $"model={summary.ModelId ?? "default"}";

    private static async Task<int> WriteAgentBackendsAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var inventory = await runtime.GetAgentBackendsAsync( cancellationToken )
            .ConfigureAwait( false );
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                inventory, CommandDeckJsonContext.Default.AgentBackendInventory ) );
            return 0;
        }

        WriteAgentRow( "Enabled", inventory.Enabled ? "true" : "false" );
        WriteAgentRow( "Selected", inventory.SelectedBackendId ?? "none" );
        foreach ( var backend in inventory.Backends )
        {
            Console.WriteLine(
                $"{Safe( backend.Id )}  {Safe( backend.DisplayName )}  {Safe( backend.Kind )}  " +
                $"{Safe( backend.HealthState )}  {Safe( backend.AuthenticationState )}  " +
                $"available={backend.Available}" );
        }

        return 0;
    }

    private static async Task<int> WriteAgentModelsAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var inventory = await runtime.DiscoverAgentModelsAsync(
            arguments.GetOption( CliOptionNames.Backend ),
            arguments.HasFlag( CliOptionNames.Hidden ),
            cancellationToken ).ConfigureAwait( false );
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                inventory, CommandDeckJsonContext.Default.AgentModelInventory ) );
            return inventory.Error is null ? 0 : 1;
        }

        if ( inventory.Error is not null )
        {
            Console.Error.WriteLine(
                $"{Safe( inventory.Error.Category )}: {Safe( inventory.Error.Summary )}" );
            return 1;
        }

        WriteAgentRow( "Backend", inventory.BackendId );
        WriteAgentRow( "Default model", inventory.DefaultModelId ?? "none" );
        foreach ( var model in inventory.Models )
        {
            var efforts = string.Join(
                ",", model.ReasoningEfforts.Select( static effort => effort.Id ) );
            Console.WriteLine(
                $"{Safe( model.Id )}  {Safe( model.DisplayName )}  hidden={model.Hidden}  " +
                $"default={model.IsDefault}  efforts=[{Safe( efforts )}]" );
        }

        return 0;
    }
}
