using System.Globalization;
using System.Text.Json;

namespace CommandDeck;

/// <summary>
/// The only boundary between CommandDeck and the Herdr CLI. Every call targets
/// the default session with a fixed argv, a cleared environment, and bounded
/// output, memory, and time. Ordinary process, JSON, and Herdr failures become
/// typed outcomes; only caller cancellation propagates as an exception.
/// </summary>
public sealed partial class HerdrClient
{
    public const string Executable = "herdr";
    public const int MaximumWorkspaces = 128;
    public const int MaximumTabs = 512;
    public const int MaximumIdentifierLength = 512;
    public const int MaximumLabelLength = 512;

    public static readonly TimeSpan StatusTimeout = TimeSpan.FromMilliseconds( 250 );
    public static readonly TimeSpan ListTimeout = TimeSpan.FromMilliseconds( 250 );
    public static readonly TimeSpan FocusTimeout = TimeSpan.FromSeconds( 1 );

    public const int StatusStandardOutputBytes = 32 * 1024;
    public const int StatusStandardErrorBytes = 16 * 1024;
    public const int WorkspaceListStandardOutputBytes = 64 * 1024;
    public const int WorkspaceListStandardErrorBytes = 16 * 1024;
    public const int TabListStandardOutputBytes = 256 * 1024;
    public const int TabListStandardErrorBytes = 32 * 1024;
    public const int FocusStandardOutputBytes = 64 * 1024;
    public const int FocusStandardErrorBytes = 32 * 1024;
    public const long MaximumMemoryBytes = 128L * 1024 * 1024;

    private static readonly string[] ForwardedEnvironment = ["HOME", "XDG_CONFIG_HOME", "XDG_RUNTIME_DIR"];

    private readonly BoundedProcessRunner _run;
    private readonly Func<bool> _available;
    private readonly Func<string, string?> _environment;

    public HerdrClient(
        BoundedProcessRunner? runner = null,
        Func<bool>? available = null,
        Func<string, string?>? environment = null )
    {
        _run = runner ?? DefaultRunAsync;
        _available = available ?? (static ( ) => ProcessRunner.ExistsOnPath( Executable ));
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    public bool IsInstalled => _available( );

    public static bool IsValidIdentifier( string? value ) =>
        !string.IsNullOrEmpty( value ) &&
        value.Length <= MaximumIdentifierLength &&
        !value.Any( char.IsControl );

    // Never forwards HERDR_SESSION or HERDR_SOCKET_PATH; --session default and
    // this allow-list decide which socket the CLI resolves.
    internal IReadOnlyDictionary<string, string> BuildEnvironment( )
    {
        Dictionary<string, string> environment = new( StringComparer.Ordinal )
        {
            ["PATH"] = _environment( "PATH" ) ?? "/usr/bin:/bin",
            ["LANG"] = "C.UTF-8",
            ["LC_ALL"] = "C.UTF-8"
        };

        foreach ( var name in ForwardedEnvironment )
        {
            var value = _environment( name );
            if ( !string.IsNullOrEmpty( value ) )
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    public async ValueTask<HerdrStatus> GetStatusAsync( CancellationToken cancellationToken )
    {
        if ( !_available( ) )
        {
            return Status( HerdrAvailability.NotInstalled, "Herdr is not on the daemon PATH." );
        }

        ProcessResult result;
        try
        {
            result = await RunAsync(
                ["status", "server", "--json"],
                StatusTimeout,
                StatusStandardOutputBytes,
                StatusStandardErrorBytes,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return Status( HerdrAvailability.Failed, "Herdr status could not start." );
        }

        var failure = DescribeProcessFailure( result, "Herdr status" );
        if ( failure is not null )
        {
            return Status( HerdrAvailability.Failed, failure );
        }

        return ParseStatus( result.StandardOutput );
    }

    public async ValueTask<HerdrInventory?> ListAsync( CancellationToken cancellationToken )
    {
        if ( !_available( ) )
        {
            return null;
        }

        var workspaces = RunAsync(
            ["workspace", "list"],
            ListTimeout,
            WorkspaceListStandardOutputBytes,
            WorkspaceListStandardErrorBytes,
            cancellationToken ).AsTask( );
        var tabs = RunAsync(
            ["tab", "list"],
            ListTimeout,
            TabListStandardOutputBytes,
            TabListStandardErrorBytes,
            cancellationToken ).AsTask( );

        try
        {
            await Task.WhenAll( workspaces, tabs ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return null;
        }

        // Both halves must be fresh; a stale or missing half is never combined.
        var workspaceList = ParseWorkspaces( workspaces.Result );
        var tabList = ParseTabs( tabs.Result );
        if ( workspaceList is null || tabList is null )
        {
            return null;
        }

        return new HerdrInventory { Workspaces = workspaceList, Tabs = tabList };
    }

    private async ValueTask<HerdrFocusOutcome> DispatchFocusAsync(
        string targetKind,
        string targetId,
        CancellationToken cancellationToken )
    {
        if ( targetKind is not HerdrTargetKinds.Workspace and not HerdrTargetKinds.Tab ||
            !IsValidIdentifier( targetId ) )
        {
            return Failure( "Herdr could not switch to this result.", "The target is not a valid Herdr identifier." );
        }

        if ( !_available( ) )
        {
            return Failure( "Herdr is not on the daemon PATH." );
        }

        ProcessResult result;
        try
        {
            result = await RunAsync(
                [targetKind, "focus", targetId],
                FocusTimeout,
                FocusStandardOutputBytes,
                FocusStandardErrorBytes,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return Failure( "Herdr could not switch to this result.", "The Herdr client could not start." );
        }

        // The request may have reached the server before the acknowledgement was
        // lost, so a timeout is reported once and never retried.
        if ( result.TimedOut )
        {
            return Failure( "Herdr did not confirm the switch; it may already have happened." );
        }

        if ( result.AnyOutputLimitExceeded || result.MemoryLimitExceeded )
        {
            return Failure( "Herdr could not switch to this result.", "The Herdr client exceeded its output or memory limit." );
        }

        if ( result.ExitCode != 0 )
        {
            return Failure( DescribeFocusError( result.StandardError, targetKind ) );
        }

        return VerifyFocusAcknowledgement( result.StandardOutput, targetKind, targetId );
    }

    private async ValueTask<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        int standardOutputBytes,
        int standardErrorBytes,
        CancellationToken cancellationToken )
    {
        string[] argv = ["--session", "default", .. arguments];
        ProcessExecutionLimits limits = new( )
        {
            MaximumStandardOutputBytes = standardOutputBytes,
            MaximumStandardErrorBytes = standardErrorBytes,
            MaximumMemoryBytes = MaximumMemoryBytes,
            ClearEnvironment = true,
            EnvironmentVariables = BuildEnvironment( )
        };

        return await _run( Executable, argv, timeout, limits, cancellationToken ).ConfigureAwait( false );
    }

    private static ValueTask<ProcessResult> DefaultRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        ProcessExecutionLimits limits,
        CancellationToken cancellationToken ) =>
        ProcessRunner.RunBoundedAsync(
            fileName,
            arguments,
            workingDirectory: null,
            timeout,
            limits,
            cancellationToken );

    private static string? DescribeProcessFailure( ProcessResult result, string operation )
    {
        if ( result.TimedOut )
        {
            return $"{operation} timed out.";
        }

        if ( result.AnyOutputLimitExceeded || result.MemoryLimitExceeded )
        {
            return $"{operation} exceeded its output or memory limit.";
        }

        if ( result.ExitCode != 0 )
        {
            return $"{operation} exited with code {result.ExitCode.ToString( CultureInfo.InvariantCulture )}.";
        }

        return null;
    }

    private static HerdrStatus ParseStatus( string json )
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse( json );
        }
        catch ( JsonException )
        {
            return Status( HerdrAvailability.Failed, "Herdr status returned malformed JSON." );
        }

        using ( document )
        {
            var root = document.RootElement;
            if ( root.ValueKind != JsonValueKind.Object )
            {
                return Status( HerdrAvailability.Failed, "Herdr status returned malformed JSON." );
            }

            var running = ReadBoolean( root, "running" );
            if ( running is null )
            {
                return Status( HerdrAvailability.Failed, "Herdr status omitted the running field." );
            }

            var version = ReadString( root, "version" );
            var protocol = ReadInteger( root, "protocol" );
            var socketPath = ReadSocketPath( root );
            if ( running == false )
            {
                return Status(
                    HerdrAvailability.Stopped,
                    "Herdr is installed; its default server is not running.",
                    version,
                    protocol,
                    socketPath );
            }

            // compatible is the CLI admission field; endpoint_compatible describes
            // the client-rendering endpoint and cannot admit a mismatched CLI.
            var server = $"server {version ?? "unknown"} protocol {protocol?.ToString( CultureInfo.InvariantCulture ) ?? "unknown"}";
            return ReadBoolean( root, "compatible" ) == true
                ? Status( HerdrAvailability.Available, $"{server}; the selected client is compatible", version, protocol, socketPath )
                : Status( HerdrAvailability.Incompatible, $"{server} is not compatible with the selected client", version, protocol, socketPath );
        }
    }

    private static string? ReadSocketPath( JsonElement root )
    {
        var socketPath = ReadString( root, "socket" );

        return socketPath is not null &&
            socketPath.Length <= 4096 &&
            Path.IsPathFullyQualified( socketPath ) &&
            !socketPath.Any( char.IsControl )
                ? socketPath
                : null;
    }

    private static List<HerdrWorkspace>? ParseWorkspaces( ProcessResult result )
    {
        var items = ReadResultArray( result, "workspace_list", "workspaces" );
        if ( items is null )
        {
            return null;
        }

        using ( items.Value.Document )
        {
            List<HerdrWorkspace> workspaces = [];
            foreach ( var element in items.Value.Items.EnumerateArray( ).Take( MaximumWorkspaces ) )
            {
                var id = ReadString( element, "workspace_id" );
                if ( element.ValueKind != JsonValueKind.Object || !IsValidIdentifier( id ) )
                {
                    continue;
                }

                workspaces.Add( new HerdrWorkspace(
                    id!,
                    ReadCount( element, "number" ),
                    ReadLabel( element ),
                    ReadBoolean( element, "focused" ) == true,
                    ReadCount( element, "pane_count" ),
                    ReadCount( element, "tab_count" ),
                    ReadAgentStatus( element ) ) );
            }

            return workspaces;
        }
    }

    private static List<HerdrTab>? ParseTabs( ProcessResult result )
    {
        var items = ReadResultArray( result, "tab_list", "tabs" );
        if ( items is null )
        {
            return null;
        }

        using ( items.Value.Document )
        {
            List<HerdrTab> tabs = [];
            foreach ( var element in items.Value.Items.EnumerateArray( ).Take( MaximumTabs ) )
            {
                var id = ReadString( element, "tab_id" );
                var workspaceId = ReadString( element, "workspace_id" );
                if ( element.ValueKind != JsonValueKind.Object || !IsValidIdentifier( id ) )
                {
                    continue;
                }

                tabs.Add( new HerdrTab(
                    id!,
                    IsValidIdentifier( workspaceId ) ? workspaceId! : string.Empty,
                    ReadCount( element, "number" ),
                    ReadLabel( element ),
                    ReadBoolean( element, "focused" ) == true,
                    ReadCount( element, "pane_count" ),
                    ReadAgentStatus( element ) ) );
            }

            return tabs;
        }
    }

    // Returns the array under result.<property> when the envelope is a successful
    // response of the expected type; the caller disposes the document.
    private static (JsonDocument Document, JsonElement Items)? ReadResultArray(
        ProcessResult result,
        string expectedType,
        string property )
    {
        if ( result.TimedOut || result.AnyOutputLimitExceeded || result.MemoryLimitExceeded ||
            result.ExitCode != 0 || string.IsNullOrWhiteSpace( result.StandardOutput ) )
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse( result.StandardOutput );
        }
        catch ( JsonException )
        {
            return null;
        }

        var envelope = document.RootElement;
        if ( envelope.ValueKind != JsonValueKind.Object ||
            !envelope.TryGetProperty( "result", out var payload ) ||
            payload.ValueKind != JsonValueKind.Object ||
            ReadString( payload, "type" ) != expectedType ||
            !payload.TryGetProperty( property, out var items ) ||
            items.ValueKind != JsonValueKind.Array )
        {
            document.Dispose( );
            return null;
        }

        return (document, items);
    }

    private static HerdrFocusOutcome VerifyFocusAcknowledgement(
        string json,
        string targetKind,
        string targetId )
    {
        var expectedType = targetKind == HerdrTargetKinds.Workspace ? "workspace_info" : "tab_info";
        var idProperty = targetKind == HerdrTargetKinds.Workspace ? "workspace_id" : "tab_id";

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse( json );
        }
        catch ( JsonException )
        {
            return Failure( "Herdr could not switch to this result.", "Herdr returned a malformed acknowledgement." );
        }

        using ( document )
        {
            var envelope = document.RootElement;
            if ( envelope.ValueKind != JsonValueKind.Object ||
                !envelope.TryGetProperty( "result", out var payload ) ||
                payload.ValueKind != JsonValueKind.Object ||
                ReadString( payload, "type" ) != expectedType ||
                !payload.TryGetProperty( targetKind, out var record ) ||
                record.ValueKind != JsonValueKind.Object )
            {
                return Failure( "Herdr could not switch to this result.", "Herdr returned an unexpected acknowledgement." );
            }

            if ( ReadString( record, idProperty ) != targetId || ReadBoolean( record, "focused" ) != true )
            {
                return Failure( "Herdr could not switch to this result.", "Herdr did not confirm the requested target as focused." );
            }
        }

        return new HerdrFocusOutcome
        {
            Succeeded = true,
            Message = targetKind == HerdrTargetKinds.Workspace ? "Switched Herdr space." : "Switched Herdr tab."
        };
    }

    private static string DescribeFocusError( string standardError, string targetKind )
    {
        var code = ReadErrorCode( standardError );
        return code switch
        {
            "server_not_running" => "Herdr is no longer running.",
            "workspace_not_found" => "That Herdr space no longer exists. Search again.",
            "tab_not_found" => "That Herdr tab no longer exists. Search again.",
            "protocol_mismatch" => "The Herdr client and server are incompatible.",
            _ => targetKind == HerdrTargetKinds.Workspace
                ? "Herdr could not switch to this space."
                : "Herdr could not switch to this tab."
        };
    }

    private static string? ReadErrorCode( string standardError )
    {
        if ( string.IsNullOrWhiteSpace( standardError ) )
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse( standardError.Trim( ) );
            var root = document.RootElement;
            if ( root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty( "error", out var error ) ||
                error.ValueKind != JsonValueKind.Object )
            {
                return null;
            }

            return ReadString( error, "code" );
        }
        catch ( JsonException )
        {
            return null;
        }
    }

    private static string ReadLabel( JsonElement element )
    {
        var label = ReadString( element, "label" ) ?? string.Empty;
        var clean = new string( label.Where( static character => !char.IsControl( character ) ).ToArray( ) ).Trim( );

        return clean.Length > MaximumLabelLength ? clean[..MaximumLabelLength] : clean;
    }

    private static string ReadAgentStatus( JsonElement element )
    {
        var status = ReadString( element, "agent_status" ) ?? string.Empty;

        return status.Length <= 32 && status.All( static character => char.IsLetterOrDigit( character ) || character is '-' or '_' )
            ? status
            : string.Empty;
    }

    private static long ReadCount( JsonElement element, string property ) =>
        Math.Max( 0, ReadInteger( element, property ) ?? 0 );

    private static string? ReadString( JsonElement element, string property ) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty( property, out var value ) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString( )
            : null;

    private static bool? ReadBoolean( JsonElement element, string property ) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty( property, out var value )
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static long? ReadInteger( JsonElement element, string property ) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty( property, out var value ) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64( out var number )
            ? number
            : null;

    private static HerdrStatus Status(
        HerdrAvailability availability,
        string detail,
        string? version = null,
        long? protocol = null,
        string? socketPath = null ) => new( )
        {
            Availability = availability,
            Detail = detail,
            Version = version,
            Protocol = protocol,
            SocketPath = socketPath
        };

    private static HerdrFocusOutcome Failure( string message, string? detail = null ) => new( )
    {
        Succeeded = false,
        Message = message,
        Detail = detail
    };
}
