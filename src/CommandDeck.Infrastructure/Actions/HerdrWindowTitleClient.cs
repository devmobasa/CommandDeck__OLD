using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CommandDeck;

internal delegate ValueTask<HerdrWindowTitleResult> HerdrWindowTitleRequest(
    string socketPath,
    string? title,
    CancellationToken cancellationToken );

internal sealed class HerdrWindowTitleResult
{
    public required bool Succeeded { get; init; }
    public string? Detail { get; init; }

    public static HerdrWindowTitleResult Success( ) => new( ) { Succeeded = true };

    public static HerdrWindowTitleResult Failure( string detail ) => new( )
    {
        Succeeded = false,
        Detail = detail
    };
}

internal static class HerdrWindowTitleClient
{
    private const string RequestId = "commanddeck:window-title";
    private const int MaximumResponseBytes = 16 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds( 250 );

    public static async ValueTask<HerdrWindowTitleResult> SendAsync(
        string socketPath,
        string? title,
        CancellationToken cancellationToken )
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        deadline.CancelAfter( RequestTimeout );

        try
        {
            using Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
            await socket.ConnectAsync(
                new UnixDomainSocketEndPoint( socketPath ),
                deadline.Token ).ConfigureAwait( false );
            using NetworkStream stream = new( socket, ownsSocket: false );
            var request = BuildRequest( title );

            await stream.WriteAsync( request, deadline.Token ).ConfigureAwait( false );
            await stream.FlushAsync( deadline.Token ).ConfigureAwait( false );
            var response = await ReadLineAsync( stream, deadline.Token ).ConfigureAwait( false );

            return ParseResponse( response, title is null );
        }
        catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
        {
            return HerdrWindowTitleResult.Failure( "Herdr window identification timed out." );
        }
        catch ( Exception exception ) when (
            exception is IOException or SocketException or JsonException or ArgumentException )
        {
            return HerdrWindowTitleResult.Failure( "Herdr window identification failed." );
        }
    }

    private static byte[] BuildRequest( string? title )
    {
        using MemoryStream buffer = new( );
        using ( Utf8JsonWriter writer = new( buffer ) )
        {
            writer.WriteStartObject( );
            writer.WriteString( "id", RequestId );
            writer.WriteString(
                "method",
                title is null ? "client.window_title.clear" : "client.window_title.set" );
            writer.WriteStartObject( "params" );
            if ( title is not null )
            {
                writer.WriteString( "title", title );
            }
            writer.WriteEndObject( );
            writer.WriteEndObject( );
        }
        buffer.WriteByte( ( byte ) '\n' );

        return buffer.ToArray( );
    }

    private static async ValueTask<string> ReadLineAsync(
        Stream stream,
        CancellationToken cancellationToken )
    {
        using MemoryStream line = new( );
        byte[] chunk = new byte[1024];
        while ( line.Length <= MaximumResponseBytes )
        {
            var read = await stream.ReadAsync( chunk, cancellationToken ).ConfigureAwait( false );
            if ( read == 0 )
            {
                break;
            }

            var newline = Array.IndexOf( chunk, ( byte ) '\n', 0, read );
            var count = newline >= 0 ? newline : read;
            if ( line.Length + count > MaximumResponseBytes )
            {
                throw new IOException( "Herdr response exceeded its output limit." );
            }

            line.Write( chunk, 0, count );
            if ( newline >= 0 )
            {
                return Encoding.UTF8.GetString( line.GetBuffer( ), 0, checked(( int ) line.Length) );
            }
        }

        throw new IOException( "Herdr returned no bounded response line." );
    }

    private static HerdrWindowTitleResult ParseResponse(
        string json,
        bool clearing )
    {
        using var document = JsonDocument.Parse( json );
        var root = document.RootElement;
        if ( root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty( "id", out var id ) ||
            id.ValueKind != JsonValueKind.String ||
            id.GetString( ) != RequestId ||
            !root.TryGetProperty( "result", out var result ) ||
            result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty( "type", out var type ) ||
            type.ValueKind != JsonValueKind.String ||
            type.GetString( ) != "client_window_title" ||
            !result.TryGetProperty( "changed", out var changed ) ||
            changed.ValueKind != JsonValueKind.True ||
            !result.TryGetProperty( "reason", out var reason ) ||
            reason.ValueKind != JsonValueKind.String ||
            reason.GetString( ) != (clearing ? "cleared" : "set") )
        {
            return HerdrWindowTitleResult.Failure(
                clearing
                    ? "Herdr did not restore its terminal title."
                    : "Herdr did not identify a foreground client." );
        }

        return HerdrWindowTitleResult.Success( );
    }
}
