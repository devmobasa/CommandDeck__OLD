namespace CommandDeck;

public interface IAiHttpTransportFactory
{
    HttpClient Create( AiProviderOptions options );
}

public sealed class AiHttpTransportFactory : IAiHttpTransportFactory
{
    public HttpClient Create( AiProviderOptions options )
    {
        SocketsHttpHandler handler = new( )
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                System.Net.DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds( 10 )
        };
        return new HttpClient( handler )
        {
            BaseAddress = NormalizeBaseAddress( options.BaseUrl ),
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static Uri NormalizeBaseAddress( string value ) => new(
        value.TrimEnd( '/' ) + "/",
        UriKind.Absolute );
}
