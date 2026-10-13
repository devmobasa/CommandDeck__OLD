namespace CommandDeck;

public static class ExtensionConnectionKinds
{
    public const string Files = "files";
    public const string Http = "http";
}

public static class ExtensionCredentialKinds
{
    public const string None = "none";
    public const string SessionBearer = "session-bearer";
    public const string SystemdBearer = "systemd-bearer";
    public const string OAuth = "oauth";
}

/// <summary>Host setup input. Credentials are accepted here only, never returned in inventory.</summary>
public sealed record ExtensionConnectionSetup
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = ExtensionConnectionKinds.Files;
    public string? Path { get; init; }
    public string? Origin { get; init; }
    public string Route { get; init; } = "/";
    public string[] Methods { get; init; } = ["GET"];
    public bool PrivateNetwork { get; init; }
    public string CredentialKind { get; init; } = ExtensionCredentialKinds.None;
    public string? Credential { get; init; }
    public ExtensionOAuthSetup? OAuth { get; init; }
}

public sealed record ExtensionOAuthSetup( string Issuer, string AuthorizationEndpoint, string TokenEndpoint,
    string ClientId, string Scope, string? RevocationEndpoint = null );
public sealed record ExtensionConnectionSummary( string Name, string Kind, string Scope, string CredentialStatus );
public sealed record ExtensionRemoteConfirmation( string Id, string Connection, string Method, string Destination,
    string Body, string IdempotencyKey );
