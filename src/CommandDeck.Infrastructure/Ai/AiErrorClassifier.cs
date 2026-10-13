namespace CommandDeck;

public static class AiErrorClassifier
{
    public static string Classify( Exception exception )
    {
        ArgumentNullException.ThrowIfNull( exception );

        return exception switch
        {
            AiOperationException categorized => categorized.Category,
            PayloadTooLargeException => AiErrorCategories.ResponseTooLarge,
            TimeoutException or TaskCanceledException or OperationCanceledException =>
                AiErrorCategories.Timeout,
            HttpRequestException => AiErrorCategories.Network,
            System.Text.Json.JsonException or EndOfStreamException =>
                AiErrorCategories.MalformedResponse,
            InvalidDataException => AiErrorCategories.MalformedResponse,
            _ => AiErrorCategories.Unavailable
        };
    }
}
