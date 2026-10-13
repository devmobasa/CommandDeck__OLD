namespace CommandDeck;

public static class FrecencyPolicy
{
    public const double MaximumFrequencyBoost = 0.16;
    public const double FrequencyBoostPerLog2Use = 0.025;
    public const double MaximumRecencyBoost = 0.12;
    public const double RecencyDecayDays = 21;
    public const double MillisecondsPerDay = 86_400_000d;

    public static double Score( long useCount, long lastUsedUnixMilliseconds, DateTimeOffset now ) =>
        ScoreAtUnixMilliseconds(
            useCount,
            lastUsedUnixMilliseconds,
            now.ToUnixTimeMilliseconds( ) );

    public static double ScoreAtUnixMilliseconds(
        long useCount,
        long lastUsedUnixMilliseconds,
        long nowUnixMilliseconds ) =>
        Math.Min(
            MaximumFrequencyBoost,
            Math.Log2( Math.Max( 0, useCount ) + 1 ) * FrequencyBoostPerLog2Use ) +
        RecencyBoostAtUnixMilliseconds( lastUsedUnixMilliseconds, nowUnixMilliseconds );

    public static double RecencyBoost(
        long lastUsedUnixMilliseconds,
        DateTimeOffset now ) =>
        RecencyBoostAtUnixMilliseconds(
            lastUsedUnixMilliseconds,
            now.ToUnixTimeMilliseconds( ) );

    private static double RecencyBoostAtUnixMilliseconds(
        long lastUsedUnixMilliseconds,
        long nowUnixMilliseconds )
    {
        if ( lastUsedUnixMilliseconds <= 0 )
        {
            return 0;
        }

        var days = Math.Max(
            0,
            (nowUnixMilliseconds - lastUsedUnixMilliseconds) / MillisecondsPerDay );
        return MaximumRecencyBoost * Math.Exp( -days / RecencyDecayDays );
    }
}
