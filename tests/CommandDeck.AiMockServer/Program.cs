using System.Net;
using System.Net.Sockets;
using System.Text;

if ( args.Length != 2 )
{
    Console.Error.WriteLine( "Usage: CommandDeck.AiMockServer PORT_FILE CAPTURE_DIRECTORY" );
    return 2;
}
Directory.CreateDirectory( args[1] );

int port;
using ( TcpListener reservation = new( IPAddress.Loopback, 0 ) )
{
    reservation.Start( );
    port = (( IPEndPoint ) reservation.LocalEndpoint).Port;
}

using HttpListener listener = new( );
listener.Prefixes.Add( $"http://127.0.0.1:{port}/" );
listener.Start( );
await File.WriteAllTextAsync( args[0], port.ToString( System.Globalization.CultureInfo.InvariantCulture ) );

using CancellationTokenSource shutdown = new( );
Console.CancelKeyPress += ( _, eventArgs ) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel( );
    listener.Stop( );
};

try
{
    while ( !shutdown.IsCancellationRequested )
    {
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync( ).WaitAsync( shutdown.Token );
        }
        catch ( OperationCanceledException )
        {
            break;
        }
        _ = Task.Run( ( ) => HandleAsync( context, args[1], shutdown.Token ) );
    }
}
catch ( HttpListenerException ) when ( shutdown.IsCancellationRequested )
{
}

return 0;

static async Task HandleAsync(
    HttpListenerContext context,
    string captureDirectory,
    CancellationToken cancellationToken )
{
    try
    {
        string path = context.Request.Url?.AbsolutePath ?? string.Empty;
        if ( path == "/ollama/api/tags" )
        {
            await ReplyAsync( context.Response, "application/json",
                "{\"models\":[{\"model\":\"aot-ollama\"}]}", cancellationToken );
            return;
        }
        if ( path == "/openai/models" )
        {
            if ( context.Request.Headers["Authorization"] != "Bearer aot-secret" )
            {
                context.Response.StatusCode = 401;
                context.Response.Close( );
                return;
            }
            await ReplyAsync( context.Response, "application/json",
                "{\"data\":[{\"id\":\"aot-openai\",\"owned_by\":\"commanddeck-smoke\"}]}",
                cancellationToken );
            return;
        }

        using MemoryStream requestBuffer = new( );
        await context.Request.InputStream.CopyToAsync( requestBuffer, cancellationToken );
        byte[] requestBytes = requestBuffer.ToArray( );
        string requestBody = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true ).GetString( requestBytes );
        string captureName = path.Trim( '/' ).Replace( '/', '-' ) + ".body";
        await File.WriteAllBytesAsync(
            Path.Combine( captureDirectory, captureName ),
            requestBytes,
            cancellationToken );
        if ( path == "/ollama/api/chat" )
        {
            if ( requestBody.Contains( "failure sentinel", StringComparison.Ordinal ) )
            {
                context.Response.StatusCode = 503;
                context.Response.Close( );
                return;
            }
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/x-ndjson";
            context.Response.SendChunked = true;
            if ( requestBody.Contains( "terminal output sentinel", StringComparison.Ordinal ) )
            {
                byte[] hostile = Encoding.UTF8.GetBytes(
                    "{\"model\":\"aot-ollama\",\"message\":{\"content\":" +
                    "\"visible\\u001b[31mred\\nnext\\tcell\"},\"done\":false}\n" +
                    "{\"model\":\"aot-ollama\",\"message\":{\"content\":\"\"}," +
                    "\"done\":true,\"done_reason\":\"stop\"}\n" );
                await context.Response.OutputStream.WriteAsync( hostile, cancellationToken );
                context.Response.Close( );
                return;
            }
            byte[] first = Encoding.UTF8.GetBytes(
                "{\"model\":\"aot-ollama\",\"message\":{\"content\":\"native-aot \"},\"done\":false}\n" );
            await context.Response.OutputStream.WriteAsync( first, cancellationToken );
            await context.Response.OutputStream.FlushAsync( cancellationToken );
            if ( requestBody.Contains( "cancel sentinel", StringComparison.Ordinal ) )
            {
                await Task.Delay( TimeSpan.FromMinutes( 5 ), cancellationToken );
                return;
            }
            byte[] terminal = Encoding.UTF8.GetBytes(
                "{\"model\":\"aot-ollama\",\"message\":{\"content\":\"ollama\"},\"done\":false}\n" +
                "{\"model\":\"aot-ollama\",\"message\":{\"content\":\"\"},\"done\":true," +
                "\"done_reason\":\"stop\",\"prompt_eval_count\":2,\"eval_count\":2}\n" );
            await context.Response.OutputStream.WriteAsync( terminal, cancellationToken );
            context.Response.Close( );
            return;
        }
        if ( path == "/openai/chat/completions" )
        {
            if ( context.Request.Headers["Authorization"] != "Bearer aot-secret" )
            {
                context.Response.StatusCode = 401;
                context.Response.Close( );
                return;
            }
            if ( requestBody.Contains( "structured error sentinel", StringComparison.Ordinal ) )
            {
                await ReplyAsync( context.Response, "text/event-stream",
                    "data: {\"error\":{\"message\":\"provider secret sentinel\"}}\n\n" +
                    "data: [DONE]\n\n", cancellationToken );
                return;
            }
            await ReplyAsync( context.Response, "text/event-stream",
                "data: {\"choices\":[{\"delta\":{\"content\":\"native-aot openai\"}," +
                "\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":2," +
                "\"completion_tokens\":2,\"total_tokens\":4}}\n\n" +
                "data: [DONE]\n\n", cancellationToken );
            return;
        }
        context.Response.StatusCode = 404;
        context.Response.Close( );
    }
    catch ( Exception exception ) when (
        exception is IOException or HttpListenerException or OperationCanceledException )
    {
        context.Response.Abort( );
    }
}

static async Task ReplyAsync(
    HttpListenerResponse response,
    string contentType,
    string body,
    CancellationToken cancellationToken )
{
    byte[] bytes = Encoding.UTF8.GetBytes( body );
    response.StatusCode = 200;
    response.ContentType = contentType;
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync( bytes, cancellationToken );
    response.Close( );
}
