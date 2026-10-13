namespace CommandDeck;

/// <summary>
/// Names shared by the privileged package helper and the CommandDeck daemon.
/// </summary>
public static class PackageHelperProtocol
{
    public const string BusName = "org.commanddeck.Package1";
    public const string ObjectPath = "/org/commanddeck/Package1";
    public const string InterfaceName = "org.commanddeck.Package1";
    public const string ProtocolVersion = "1";

    public const string MethodGetStatus = "GetStatus";
    public const string MethodCommit = "Commit";
    public const string MethodGetOperation = "GetOperation";
    public const string MethodCancel = "Cancel";
    public const string SignalProgress = "Progress";

    public const string ActionPackageChange = "org.commanddeck.package.change";
    public const string ActionAurBuild = "org.commanddeck.aur.build";
    public const string ActionAurInstall = "org.commanddeck.aur.install";
}

/// <summary>
/// D-Bus error names the helper replies with. Messages never contain paths.
/// </summary>
public static class PackageHelperErrors
{
    public const string Prefix = PackageHelperProtocol.InterfaceName + ".Error.";
    public const string InvalidOperation = Prefix + "InvalidOperation";
    public const string InvalidNames = Prefix + "InvalidNames";
    public const string InvalidRequest = Prefix + "InvalidRequest";
    public const string DatabaseChanged = Prefix + "DatabaseChanged";
    public const string TransactionChanged = Prefix + "TransactionChanged";
    public const string DatabaseLocked = Prefix + "DatabaseLocked";
    public const string Busy = Prefix + "Busy";
    public const string NotAuthorized = Prefix + "NotAuthorized";
    public const string NoAuthenticationAgent = Prefix + "NoAuthenticationAgent";
    public const string UnknownOperation = Prefix + "UnknownOperation";
    public const string Failed = Prefix + "Failed";
}
