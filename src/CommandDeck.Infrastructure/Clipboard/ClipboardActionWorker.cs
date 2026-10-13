using System.Text.Json;

namespace CommandDeck;

/// <summary>Runs association discovery in a bounded child. The daemon never performs resolver filesystem work itself.</summary>
public sealed class ClipboardActionWorker( string executable, IReadOnlyList<string> prefix, ClipboardResolverEnvironment environment,
    BoundedProcessRunner? runner = null, long maximumMemoryBytes = ClipboardActionWorker.DefaultMaximumMemoryBytes ) : IDisposable
{
    public const int Slots = 2;
    public const long DefaultMaximumMemoryBytes = 256L * 1024 * 1024;
    private const string KeyVariable = "COMMANDDECK_CLIPBOARD_KEY";
    private const string TargetVariable = "COMMANDDECK_CLIPBOARD_TARGET";
    private readonly BoundedProcessRunner _run = runner ?? RunAsync;
    private readonly SemaphoreSlim _slots = new( Slots, Slots );
    private int _disposed;

    public async ValueTask<ClipboardHandlerResolution> ResolveAsync( string key, string target, CancellationToken token )
    {
        ArgumentException.ThrowIfNullOrEmpty( key );
        ArgumentException.ThrowIfNullOrEmpty( target );
        ObjectDisposedException.ThrowIf( Volatile.Read( ref _disposed ) != 0, this );
        if ( !await _slots.WaitAsync( 0, token ).ConfigureAwait( false ) )
        {
            return Unavailable( ClipboardHandlerStatuses.ActionsBusy );
        }

        CancellationTokenSource? executionCancellation = null;
        Task<ProcessResult>? execution = null;
        var releaseSlot = true;
        try
        {
            Dictionary<string, string> variables = new( StringComparer.Ordinal )
            {
                [KeyVariable] = key,
                [TargetVariable] = target,
                ["HOME"] = environment.Home,
                ["PATH"] = environment.Path,
                // The child runs under a 256 MiB address-space limit; bound the runtime's reservations to fit it.
                ["DOTNET_gcServer"] = "0",
                ["DOTNET_GCHeapHardLimit"] = "0x4000000",
                ["DOTNET_GCRegionRange"] = "0x4000000",
                ["DOTNET_GCRegionSize"] = "0x100000",
                ["DOTNET_TieredPGO"] = "0",
                ["MALLOC_ARENA_MAX"] = "1"
            };
            // A framework-dependent host needs the runtime location; the published Native AOT child ignores it.
            Set( variables, "DOTNET_ROOT", Environment.GetEnvironmentVariable( "DOTNET_ROOT" ) );
            Set( variables, "XDG_CONFIG_HOME", environment.ConfigHome );
            Set( variables, "XDG_CONFIG_DIRS", environment.ConfigDirs );
            Set( variables, "XDG_DATA_HOME", environment.DataHome );
            Set( variables, "XDG_DATA_DIRS", environment.DataDirs );
            Set( variables, "XDG_CURRENT_DESKTOP", environment.CurrentDesktop );
            Set( variables, "LC_ALL", environment.Locale );

            executionCancellation = CancellationTokenSource.CreateLinkedTokenSource( token );
            execution = _run( executable, [.. prefix, "clipboard-action-worker", "--resolve"], TimeSpan.FromSeconds( 2 ),
                new ProcessExecutionLimits
                {
                    MaximumStandardOutputBytes = 256 * 1024,
                    MaximumStandardErrorBytes = 4096,
                    MaximumMemoryBytes = maximumMemoryBytes,
                    ClearEnvironment = true,
                    EnvironmentVariables = variables
                }, executionCancellation.Token ).AsTask( );
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource( token );
            deadline.CancelAfter( TimeSpan.FromSeconds( 2 ) );
            ProcessResult result;
            try
            {
                result = await execution.WaitAsync( deadline.Token ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( !token.IsCancellationRequested && deadline.IsCancellationRequested )
            {
                executionCancellation.Cancel( );
                _ = ReleaseAfterReapingAsync( execution, executionCancellation );
                releaseSlot = false;
                return Unavailable( ClipboardHandlerStatuses.CheckTimedOut );
            }
            catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
            {
                executionCancellation.Cancel( );
                _ = ReleaseAfterReapingAsync( execution, executionCancellation );
                releaseSlot = false;
                throw;
            }

            token.ThrowIfCancellationRequested( );
            if ( result.TimedOut )
            {
                return Unavailable( ClipboardHandlerStatuses.CheckTimedOut );
            }

            if ( result.ExitCode != 0 || result.AnyOutputLimitExceeded || result.MemoryLimitExceeded )
            {
                return Unavailable( ClipboardHandlerStatuses.HandlerDiscoveryUnavailable );
            }

            var resolution = JsonSerializer.Deserialize( result.StandardOutput, CommandDeckJsonContext.Default.ClipboardHandlerResolution );
            return resolution is not null && Valid( resolution ) ? resolution : Unavailable( ClipboardHandlerStatuses.HandlerDiscoveryUnavailable );
        }
        catch ( Exception exception ) when ( exception is IOException or JsonException or System.ComponentModel.Win32Exception )
        {
            return Unavailable( ClipboardHandlerStatuses.HandlerDiscoveryUnavailable );
        }
        finally
        {
            if ( releaseSlot )
            {
                executionCancellation?.Dispose( );
                _slots.Release( );
            }
        }
    }

    /// <summary>Child entry point: inputs arrive through the cleared environment; output is the bounded public resolution.</summary>
    public static int RunChild( )
    {
        var key = Environment.GetEnvironmentVariable( KeyVariable );
        var target = Environment.GetEnvironmentVariable( TargetVariable );
        if ( string.IsNullOrEmpty( key ) || string.IsNullOrEmpty( target ) ||
            !ClipboardHttpUrl.TryParseSingleHttpUrl( target, out var url, out _ ) || key != "x-scheme-handler/" + url!.Scheme )
        {
            return 2;
        }

        var resolution = new ClipboardAssociationResolver( ClipboardResolverEnvironment.Capture( ) )
            .Resolve( key, ClipboardLaunchTarget.HttpUrl, url.Literal );
        Console.Out.Write( JsonSerializer.Serialize( resolution, CommandDeckJsonContext.Default.ClipboardHandlerResolution ) );
        Console.Out.Flush( );
        return 0;
    }

    private static bool Valid( ClipboardHandlerResolution resolution ) =>
        resolution.Handlers is not null && resolution.Handlers.Count <= ClipboardAssociationResolver.MaximumHandlers &&
        resolution.Status is ClipboardHandlerStatuses.Resolved or ClipboardHandlerStatuses.NoAssociations or ClipboardHandlerStatuses.InvalidAssociations or ClipboardHandlerStatuses.HandlerDiscoveryUnavailable &&
        resolution.Revision is not null && resolution.Revision.Length <= 64 && resolution.Total >= resolution.Handlers.Count &&
        resolution.Handlers.All( static handler => handler is { DesktopId.Length: > 0 and <= 256, Label.Length: > 0 and <= 256 } &&
            handler.DesktopId.EndsWith( ".desktop", StringComparison.Ordinal ) && handler.Rank is "default" or "preferred" or "declared" &&
            !handler.Label.Any( char.IsControl ) && (handler.Reason is null || handler.Reason.Length <= 64) );

    private static ClipboardHandlerResolution Unavailable( string status ) => new( status, [], 0, false, "", null );

    private static void Set( Dictionary<string, string> variables, string name, string? value )
    {
        if ( value is not null )
        {
            variables[name] = value;
        }
    }

    private async Task ReleaseAfterReapingAsync( Task<ProcessResult> execution, CancellationTokenSource cancellation )
    {
        try
        {
            await execution.ConfigureAwait( false );
        }
        catch ( Exception )
        {
            // The caller already received cancellation or timeout; observing completion is the remaining responsibility.
        }
        finally
        {
            cancellation.Dispose( );
            _slots.Release( );
        }
    }

    public void Dispose( ) => Interlocked.Exchange( ref _disposed, 1 );

    private static ValueTask<ProcessResult> RunAsync( string file, IReadOnlyList<string> arguments, TimeSpan timeout,
        ProcessExecutionLimits limits, CancellationToken token ) => ProcessRunner.RunBoundedAsync( file, arguments, null, timeout, limits, token );
}
