using System.Text.Json;

namespace CommandDeck;

public sealed class HerdrWindowActivator
{
    private const int ResolutionAttempts = 5;

    private static readonly TimeSpan HyprctlTimeout = TimeSpan.FromMilliseconds( 150 );
    private static readonly TimeSpan ResolutionDelay = TimeSpan.FromMilliseconds( 20 );
    private static readonly SemaphoreSlim ActivationGate = new( 1, 1 );

    private readonly HerdrClient _herdr;
    private readonly HerdrWindowTitleRequest _titleRequest;
    private readonly ExternalProcessRunner _run;
    private readonly Func<bool> _hyprlandAvailable;
    private readonly Func<string> _marker;

    public HerdrWindowActivator( HerdrClient herdr )
        : this(
            herdr,
            HerdrWindowTitleClient.SendAsync,
            DefaultRunAsync,
            DefaultHyprlandAvailable,
            static ( ) => $"commanddeck-herdr-{Guid.NewGuid( ):N}" )
    {
    }

    internal HerdrWindowActivator(
        HerdrClient herdr,
        HerdrWindowTitleRequest titleRequest,
        ExternalProcessRunner runner,
        Func<bool> hyprlandAvailable,
        Func<string> marker )
    {
        _herdr = herdr;
        _titleRequest = titleRequest;
        _run = runner;
        _hyprlandAvailable = hyprlandAvailable;
        _marker = marker;
    }

    public async ValueTask<ActionExecutionResult> ActivateAsync(
        string targetKind,
        CancellationToken cancellationToken )
    {
        if ( !_hyprlandAvailable( ) )
        {
            return ActionExecutionResult.Success( );
        }

        await ActivationGate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await ActivateCoreAsync( targetKind, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            ActivationGate.Release( );
        }
    }

    private async ValueTask<ActionExecutionResult> ActivateCoreAsync(
        string targetKind,
        CancellationToken cancellationToken )
    {
        var status = await _herdr.GetStatusAsync( cancellationToken ).ConfigureAwait( false );
        if ( status.Availability != HerdrAvailability.Available || status.SocketPath is null )
        {
            return Failure( targetKind, "Herdr did not provide its default-session socket." );
        }

        var marker = _marker( );
        ActionExecutionResult activation;
        HerdrWindowTitleResult restored;
        try
        {
            // The title may have changed even when its acknowledgement is lost.
            // Include marking in the independently bounded cleanup scope.
            var marked = await _titleRequest(
                status.SocketPath,
                marker,
                cancellationToken ).ConfigureAwait( false );
            if ( !marked.Succeeded )
            {
                return Failure( targetKind, marked.Detail ?? "Herdr did not identify a foreground client." );
            }

            activation = await FocusMarkedWindowAsync(
                targetKind,
                marker,
                cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            restored = await _titleRequest(
                status.SocketPath,
                title: null,
                CancellationToken.None ).ConfigureAwait( false );
        }

        return restored.Succeeded
            ? activation
            : Failure( targetKind, restored.Detail ?? "Herdr did not restore its terminal title." );
    }

    private async ValueTask<ActionExecutionResult> FocusMarkedWindowAsync(
        string targetKind,
        string marker,
        CancellationToken cancellationToken )
    {
        for ( var attempt = 0; attempt < ResolutionAttempts; attempt++ )
        {
            ProcessResult clients;
            try
            {
                clients = await _run(
                    HyprlandDispatch.Executable,
                    ["-j", "clients"],
                    HyprctlTimeout,
                    cancellationToken ).ConfigureAwait( false );
            }
            catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
            {
                return Failure( targetKind, "Hyprland client inspection failed." );
            }

            if ( clients.ExitCode != 0 || clients.TimedOut )
            {
                return Failure( targetKind, "Hyprland client inspection failed." );
            }

            var address = FindMarkedAddress( clients.StandardOutput, marker );
            if ( address is not null )
            {
                return await DispatchFocusAsync(
                    targetKind,
                    address,
                    cancellationToken ).ConfigureAwait( false );
            }

            if ( attempt + 1 < ResolutionAttempts )
            {
                await Task.Delay( ResolutionDelay, cancellationToken ).ConfigureAwait( false );
            }
        }

        return Failure( targetKind, "Hyprland did not report the foreground Herdr window." );
    }

    private async ValueTask<ActionExecutionResult> DispatchFocusAsync(
        string targetKind,
        string address,
        CancellationToken cancellationToken )
    {
        ProcessResult result;
        try
        {
            result = await _run(
                HyprlandDispatch.Executable,
                [HyprlandDispatch.Command,
                    $"hl.dsp.focus({{ window = {HyprlandDispatch.QuoteLuaString( $"address:{address}" )} }})"],
                HyprctlTimeout,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return Failure( targetKind, "Hyprland window focus failed." );
        }

        // Lua dispatch warnings can arrive with exit code zero. Only the normal
        // acknowledgement confirms that the dispatcher accepted the focus.
        return HyprlandDispatch.WasAccepted( result )
            ? ActionExecutionResult.Success( )
            : Failure( targetKind, "Hyprland window focus failed." );
    }

    private static string? FindMarkedAddress( string json, string marker )
    {
        try
        {
            using var document = JsonDocument.Parse( json );
            if ( document.RootElement.ValueKind != JsonValueKind.Array )
            {
                return null;
            }

            string? match = null;
            foreach ( var client in document.RootElement.EnumerateArray( ) )
            {
                if ( client.ValueKind != JsonValueKind.Object ||
                    !client.TryGetProperty( "title", out var title ) ||
                    title.ValueKind != JsonValueKind.String ||
                    title.GetString( ) != marker ||
                    !client.TryGetProperty( "address", out var addressElement ) ||
                    addressElement.ValueKind != JsonValueKind.String )
                {
                    continue;
                }

                var address = addressElement.GetString( );
                if ( !IsValidAddress( address ) || match is not null )
                {
                    return null;
                }

                match = address;
            }

            return match;
        }
        catch ( JsonException )
        {
            return null;
        }
    }

    private static bool IsValidAddress( string? address )
    {
        if ( address is not { Length: >= 3 and <= 34 } ||
            !address.StartsWith( "0x", StringComparison.Ordinal ) )
        {
            return false;
        }

        foreach ( var character in address.AsSpan( 2 ) )
        {
            if ( !char.IsAsciiHexDigit( character ) )
            {
                return false;
            }
        }

        return true;
    }

    private static bool DefaultHyprlandAvailable( ) =>
        OperatingSystem.IsLinux( ) &&
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable( "HYPRLAND_INSTANCE_SIGNATURE" ) ) &&
        ProcessRunner.ExistsOnPath( HyprlandDispatch.Executable );

    private static ValueTask<ProcessResult> DefaultRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken ) =>
        ProcessRunner.RunAsync(
            fileName,
            arguments,
            standardInput: null,
            workingDirectory: null,
            timeout,
            cancellationToken );

    private static ActionExecutionResult Failure( string targetKind, string detail ) =>
        ActionExecutionResult.Failure(
            targetKind == HerdrTargetKinds.Workspace
                ? "Switched Herdr space, but could not focus its window."
                : "Switched Herdr tab, but could not focus its window.",
            detail );
}
