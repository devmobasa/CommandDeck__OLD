namespace CommandDeck.ExtensionProtocol;

public static class ExtensionErrorCodes
{
    public const string UnsupportedFeature = "unsupported_feature";
    public const string PermissionDenied = "permission_denied";
    public const string StaleSession = "stale_session";
    public const string StaleView = "stale_view";
    public const string RevisionConflict = "revision_conflict";
    public const string QuotaExceeded = "quota_exceeded";
    public const string InvalidView = "invalid_view";
    public const string InvalidRequest = "invalid_request";
    public const string Cancelled = "cancelled";
    public const string DeadlineExceeded = "deadline_exceeded";

    public static bool IsKnown( string? code ) => code is UnsupportedFeature or PermissionDenied or StaleSession or StaleView or
        RevisionConflict or QuotaExceeded or InvalidView or InvalidRequest or Cancelled or DeadlineExceeded;

    public static string Message( string code ) => code switch
    {
        UnsupportedFeature => "The requested feature is unsupported.",
        PermissionDenied => "The request requires a current permission grant.",
        StaleSession => "The session or invocation authority has expired.",
        StaleView => "The displayed view authority has expired.",
        RevisionConflict => "The stored data changed. Reload it before saving.",
        QuotaExceeded => "The request exceeds its allowed limit.",
        InvalidView => "The extension view is invalid.",
        InvalidRequest => "The extension request is invalid.",
        Cancelled => "The request was cancelled.",
        DeadlineExceeded => "The request deadline expired.",
        _ => throw new ArgumentException( "Unknown extension error code.", nameof( code ) )
    };
}

public sealed class ExtensionApplicationException( string errorCode ) : InvalidOperationException( ExtensionErrorCodes.Message( errorCode ) )
{
    public string ErrorCode { get; } = errorCode;
}
