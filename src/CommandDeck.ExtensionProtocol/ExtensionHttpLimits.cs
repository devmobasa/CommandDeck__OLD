namespace CommandDeck.ExtensionProtocol;

public static class ExtensionHttpLimits
{
    public const int MaximumRequestBytes = 65536;
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public static int Base64Length( int bytes ) => checked((bytes + 2) / 3 * 4);
}
