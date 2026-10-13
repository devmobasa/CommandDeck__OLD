namespace CommandDeck;

public static class TimeAwareRankingPolicy
{
    public static double Score(
        string resultKind,
        long useCount,
        long lastUsedUnixMilliseconds,
        DateTimeOffset now,
        TimeAwareRankingOptions options ) =>
        options.Enabled
            ? FrecencyPolicy.Score( useCount, lastUsedUnixMilliseconds, now ) *
                Multiplier( resultKind, options )
            : 0;

    public static double Multiplier( string resultKind, TimeAwareRankingOptions options ) =>
        resultKind switch
        {
            ResultKinds.Application => options.ApplicationMultiplier,
            ResultKinds.File or ResultKinds.Configuration => options.FileMultiplier,
            ResultKinds.Workspace or ResultKinds.Window or ResultKinds.Tab => options.WorkspaceMultiplier,
            ResultKinds.Omarchy or ResultKinds.CustomCommand or ResultKinds.Quicklink =>
                options.ActionMultiplier,
            _ => 0
        };

    public static string Category( string resultKind ) => resultKind switch
    {
        ResultKinds.Application => "application",
        ResultKinds.File or ResultKinds.Configuration => "file",
        ResultKinds.Workspace or ResultKinds.Window or ResultKinds.Tab => "workspace",
        ResultKinds.Omarchy or ResultKinds.CustomCommand or ResultKinds.Quicklink => "action",
        _ => "unsupported"
    };
}
