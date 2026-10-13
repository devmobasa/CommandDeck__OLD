using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public sealed record CodexBinaryInfo( string CanonicalPath, string Version, string IdentityToken, bool Reviewed = true );

internal static class CodexAppServerContract
{
    public const string ReviewedVersion = "0.152.1";
    public const string ReadOnlyPermission = ":read-only";
    public const string UnrestrictedPermission = ":danger-full-access";
    public const string ReviewedSchemaDigest = "f02b9860d085c162474162bb570e343ee3f26ef77db8642576d446ba930435c3";
    private const string ReviewedSchema =
        "0.152.1\n" +
        "initialize:experimentalApi,requestAttestation,optOutNotificationMethods\n" +
        "thread/start:ephemeral,permissions,approvalPolicy,approvalsReviewer,allowProviderModelFallback,developerInstructions,cwd,dynamicTools,model\n" +
        "permissions::read-only,:danger-full-access\n";

    public static void Validate( )
    {
        var digest = Convert.ToHexStringLower(
            SHA256.HashData( Encoding.UTF8.GetBytes( ReviewedSchema ) ) );
        if ( !string.Equals( digest, ReviewedSchemaDigest, StringComparison.Ordinal ) )
        {
            throw new AiOperationException(
                AiErrorCategories.Unsupported,
                safeSummary: "The reviewed Codex App Server schema manifest does not match this build." );
        }
    }

    /// <summary>
    /// True when the version is the one whose App Server schema this build
    /// reviewed. Other versions inside the configured range still run; the
    /// status and doctor surfaces mark them as unreviewed.
    /// </summary>
    public static bool IsReviewed( string version )
    {
        Validate( );
        return string.Equals( version, ReviewedVersion, StringComparison.Ordinal );
    }
}

public sealed class CodexLaunchRequest
{
    public required string BinaryPath { get; init; }
    public required string RuntimeHome { get; init; }
    public required string Generation { get; init; }
    public int TrustLevel { get; init; }
    public required string WorkingDirectory { get; init; }
    public string? SocketPath { get; init; }
}

public interface IAgentProcessHandle : IAsyncDisposable
{
    IJsonRpcTransport Transport { get; }
    Task Exited { get; }
    ValueTask StopAsync( CancellationToken cancellationToken );
}

public interface IAgentProcessLauncher
{
    ValueTask<IAgentProcessHandle> LaunchAsync( CodexLaunchRequest request, CancellationToken cancellationToken );
}

public static class CodexArgv
{
    // Code Mode is left enabled. The newer models (gpt-5.6-*) invoke dynamic
    // tools through Code Mode; measured live, each tool call still returns to
    // the client as an item/tool/call server request, so the daemon's mediation
    // (trust ladder, confirmation cards, context broker) applies per call.
    // CodexRuntimeHome enables the bundled code-mode host and BubblewrapCodexLauncher
    // binds it into the sandbox. Without Code Mode, gpt-5.6-* tools fail closed.
    public static IReadOnlyList<string> AppServerStdio { get; } =
        ["app-server", "--stdio", "--strict-config"];

    public static IReadOnlyList<string> AppServerSocket( string socketPath ) =>
        ["app-server", "--listen", "unix://" + socketPath, "--strict-config"];

    public static IReadOnlyList<string> ProhibitedFlags { get; } =
    [
        "--yolo",
        "--dangerously-bypass-approvals-and-sandbox",
        "danger-full-access",
        "--full-auto",
        "--analytics-default-enabled"
    ];

    public static void AssertSafe( IReadOnlyList<string> argv )
    {
        foreach ( var argument in argv )
        {
            foreach ( var prohibited in ProhibitedFlags )
            {
                if ( argument.Contains( prohibited, StringComparison.OrdinalIgnoreCase ) )
                {
                    throw new InvalidOperationException( "A prohibited Codex flag was rejected." );
                }
            }
        }
    }
}
