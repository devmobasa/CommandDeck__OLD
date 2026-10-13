using System.Diagnostics;

namespace CommandDeck;

// Bounded search timing lines. Fast searches and providers stay at debug so the
// default info level only carries the stalls worth diagnosing. No line carries
// query text; see docs/16-observability.md.
internal static class SearchLogging
{
    public const long SlowThresholdMilliseconds = 100;

    public const string CompletedStatus = "completed";
    public const string CancelledStatus = "cancelled";
    public const string FailedStatus = "failed";

    public static void LogSearchCompletion( SearchRequest request, long requestId, long started,
        int resultCount, string status, string? errorType = null )
    {
        if ( !request.IsPrivacyClassified || request.ContainsPrivateQuery )
        {
            return;
        }

        LogSearchCompletion( requestId, request.Query.Length, started, resultCount, status, errorType );
    }

    public static void LogSearchCompletion(
        long requestId,
        int queryLength,
        long started,
        int resultCount,
        string status,
        string? errorType = null )
    {
        var durationMilliseconds = ( long ) Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
        var error = errorType is null ? string.Empty : $" error_type={errorType}";
        var message =
            $"event=search.complete request_id={requestId} query_chars={queryLength} " +
            $"duration_ms={durationMilliseconds} result_count={resultCount} status={status}{error}";

        if ( status == FailedStatus )
        {
            AppLog.Warning( message );
        }
        else if ( status == CompletedStatus && durationMilliseconds >= SlowThresholdMilliseconds )
        {
            AppLog.Info( message );
        }
        else
        {
            AppLog.Debug( message );
        }
    }

    public static void LogProviderDiagnostic( SearchProviderDiagnostic diagnostic )
    {
        var error = diagnostic.ErrorType is null
            ? string.Empty
            : $" error_type={diagnostic.ErrorType}";
        var message =
            $"event=provider.{diagnostic.Outcome} provider={diagnostic.ProviderId} " +
            $"duration_ms={diagnostic.DurationMilliseconds} " +
            $"result_count={diagnostic.ResultCount}{error}";

        if ( diagnostic.Outcome is SearchProviderOutcomes.TimedOut or SearchProviderOutcomes.Failed )
        {
            AppLog.Warning( message );
        }
        else if ( diagnostic.Outcome == SearchProviderOutcomes.Completed &&
            diagnostic.DurationMilliseconds >= SlowThresholdMilliseconds )
        {
            AppLog.Info( message );
        }
        else
        {
            AppLog.Debug( message );
        }
    }

    public static void LogIndexDiagnostic( IndexDiagnostic diagnostic )
    {
        if ( diagnostic.Severity == IndexDiagnosticSeverity.Warning )
        {
            AppLog.Warning( diagnostic.Message );
        }
        else
        {
            AppLog.Info( diagnostic.Message );
        }
    }
}
