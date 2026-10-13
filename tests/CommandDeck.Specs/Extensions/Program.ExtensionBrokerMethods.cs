using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionBrokerAuthorityRejectsForeignAndRetiredRequests( )
    {
        var authority = new ExtensionViewAuthority( "session", 1, 3 );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        var request = new BrokerRequest( "session", 1, 1, invocation.Id, "files.list", "notes" );
        var grants = new HashSet<string> { "files.read" };
        authority.AuthorizeBroker( request, 3, grants );
        foreach ( var invalid in new[] { request with { SessionId = "foreign" }, request with { ExecutionEpoch = 2 }, request with { Generation = 2 }, request with { InvocationId = "foreign" } } )
        {
            Throws<InvalidOperationException>( ( ) => authority.AuthorizeBroker( invalid, 3, grants ) );
        }
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeBroker( request, 4, grants ) );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeBroker( request with { Operation = "http.send" }, 3, grants ) );
        authority.CompleteInvocation( invocation );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeBroker( request, 3, grants ) );
        var nextInvocation = authority.BeginInvocation( InvocationOrigin.UserAction );
        authority.Revoke( );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeBroker( request with { Generation = 2 }, 3, grants ) );
    }

    private static async Task ExtensionFileConnectionsRejectReplacementLinksAndRevocationAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var folder = Path.Combine( root, "notes" );
            Directory.CreateDirectory( folder );
            var file = Path.Combine( folder, "note.txt" );
            await File.WriteAllTextAsync( file, "hello" );
            await File.WriteAllTextAsync( Path.Combine( folder, "secrets.json" ), "protected" );
            File.CreateSymbolicLink( Path.Combine( folder, "linked.txt" ), file );
            Directory.CreateSymbolicLink( Path.Combine( root, "alias" ), folder );
            using var broker = new ExtensionConnections( Path.Combine( root, "journal" ), new ExtensionSecretResolver( ) );
            await broker.ConfigureAsync( "package", new( )
            {
                Name = "notes",
                Path = folder
            }, CancellationToken.None );
            async Task<BrokerResult> Access( BrokerRequest input, string package = "package" ) =>
                await broker.ExecuteAsync( package, input, false, ( _, _ ) => ValueTask.FromResult( false ), CancellationToken.None );
            var list = new BrokerRequest( "s", 1, 1, "i", "files.list", "notes" );
            var result = await Access( list );
            Equal( 1, result.Entries!.Length );
            var read = list with
            {
                Operation = "files.read",
                Reference = result.Entries[0].Reference
            };
            Equal( "hello", Encoding.UTF8.GetString( Convert.FromBase64String( (await Access( read )).Body! ) ) );
            await broker.ConfigureAsync( "package", new( )
            {
                Name = "single",
                Path = file
            }, CancellationToken.None );
            var single = await Access( list with
            {
                Connection = "single"
            } );
            Equal( 1, single.Entries!.Length );
            Equal( "hello", Encoding.UTF8.GetString( Convert.FromBase64String( (await Access( read with
            {
                Connection = "single",
                Reference = single.Entries[0].Reference
            } )).Body! ) ) );
            var binary = Enumerable.Repeat( ( byte ) 251, 128 * 1024 ).ToArray( );
            var binaryPath = Path.Combine( root, "binary.bin" );
            await File.WriteAllBytesAsync( binaryPath, binary );
            await broker.ConfigureAsync( "package", new( )
            {
                Name = "binary",
                Path = binaryPath
            }, CancellationToken.None );
            var binaryEntry = (await Access( list with
            {
                Connection = "binary"
            } )).Entries![0];
            var binaryResult = await Access( read with
            {
                Connection = "binary",
                Reference = binaryEntry.Reference
            } );
            var frame = ExtensionFraming.Serialize( new( )
            {
                Id = "e:binary",
                Result = JsonSerializer.SerializeToElement( binaryResult, BrokerJsonContext.Default.BrokerResult )
            } );
            True( frame.Length <= ExtensionLimits.MaximumFrameBytes );
            using var wire = new MemoryStream( frame );
            var roundTrip = (await new ExtensionFraming( wire ).ReadAsync( CancellationToken.None ))!.Result!.Value.Deserialize( BrokerJsonContext.Default.BrokerResult )!;
            True( binary.SequenceEqual( Convert.FromBase64String( roundTrip.Body! ) ) );
            var requestFrame = ExtensionFraming.Serialize( new( )
            {
                Id = "e:request",
                Method = "command.broker",
                Params = JsonSerializer.SerializeToElement(
                list with
                {
                    Operation = "http.send",
                    Http = new( "POST", "/tasks", Convert.ToBase64String( binary.AsSpan( 0, 64 * 1024 ) ) )
                }, BrokerJsonContext.Default.BrokerRequest )
            } );
            True( requestFrame.Length <= ExtensionLimits.MaximumFrameBytes );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Access( read, "other" ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Access( read with { Reference = file } ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await broker.ConfigureAsync( "package", new( ) { Name = "alias", Path = Path.Combine( root, "alias" ) }, CancellationToken.None ) );
            var nested = Path.Combine( folder, "nested" );
            Directory.CreateDirectory( nested );
            await File.WriteAllTextAsync( Path.Combine( nested, "inside.txt" ), "inside" );
            var directory = (await Access( list )).Entries!.Single( entry => entry.Directory );
            Directory.Move( nested, nested + "-moved" );
            Directory.CreateSymbolicLink( nested, nested + "-moved" );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Access( list with { Reference = directory.Reference } ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await broker.ConfigureAsync( "package", new( ) { Name = "protected", Path = Path.Combine( folder, "secrets.json" ) }, CancellationToken.None ) );
            File.Move( file, file + ".old" );
            await File.WriteAllTextAsync( file, "replacement" );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Access( read ) );
            Directory.Move( folder, folder + "-moved" );
            Directory.CreateDirectory( folder );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Access( list ) );
            broker.Revoke( "package" );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Access( list ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task ExtensionHttpConnectionsBoundScopeSecretsAndEffectsAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using var server = new BrokerTestServer( );
            using var broker = new ExtensionConnections( Path.Combine( root, "journal" ), new ExtensionSecretResolver( ) );
            var setup = new ExtensionConnectionSetup { Name = "tasks", Kind = "http", Origin = server.Origin, Route = "/tasks", Methods = ["GET", "POST"], PrivateNetwork = true, CredentialKind = "session-bearer", Credential = "host-secret" };
            await broker.ConfigureAsync( "one", setup, CancellationToken.None );
            var get = new BrokerRequest( "s", 1, 1, "i", "http.send", "tasks", Http: new( "GET", "/tasks" ) );
            async Task<BrokerResult> Send( BrokerRequest input, bool approve = true, bool secrets = true ) =>
                await broker.ExecuteAsync( "one", input, secrets, ( _, _ ) => ValueTask.FromResult( approve ), CancellationToken.None );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Send( get, secrets: false ) );
            Equal( "completed", (await Send( get )).Outcome );
            Equal( "Bearer host-secret", server.Authorization );
            var oldDirectory = Environment.GetEnvironmentVariable( "CREDENTIALS_DIRECTORY" );
            try
            {
                await File.WriteAllTextAsync( Path.Combine( root, "task-token" ), "systemd-token" );
                Environment.SetEnvironmentVariable( "CREDENTIALS_DIRECTORY", root );
                await broker.ConfigureAsync( "one", setup with
                {
                    Name = "systemd",
                    CredentialKind = "systemd-bearer",
                    Credential = "task-token"
                }, CancellationToken.None );
            }
            finally
            {
                Environment.SetEnvironmentVariable( "CREDENTIALS_DIRECTORY", oldDirectory );
            }
            Equal( "completed", (await Send( get with
            {
                Connection = "systemd"
            } )).Outcome );
            Equal( "Bearer systemd-token", server.Authorization );
            True( !JsonSerializer.Serialize( broker.List( "one" ), CommandDeckJsonContext.Default.ExtensionConnectionSummaryArray ).Contains( "host-secret", StringComparison.Ordinal ) );
            foreach ( var route in new[] { "/other", "//evil/tasks", "/tasks/../other", "/tasks/%2e%2e/other", "/tasks\\other", "/tasks/.. ", "/tasks/../. ", "/tasks/..  " } )
            {
                await ThrowsAsync<InvalidOperationException>( async ( ) => await Send( get with { Http = new( "GET", route ) } ) );
            }
            Equal( "redirect-denied", (await Send( get with
            {
                Http = new( "GET", "/tasks/redirect" )
            } )).Outcome );
            Equal( "connection-unavailable", (await Send( get with
            {
                Http = new( "GET", "/tasks/flood" )
            } )).Outcome );
            Equal( "connection-unavailable", (await Send( get with
            {
                Http = new( "GET", "/tasks/gzip" )
            } )).Outcome );
            var post = get with
            {
                Http = new( "POST", "/tasks", Convert.ToBase64String( "create"u8 ), "text/plain", "unique-mutation-001" )
            };
            Equal( "cancelled", (await Send( post, false )).Outcome );
            Equal( 0, server.Writes );
            Equal( "completed", (await Send( post )).Outcome );
            Equal( 1, server.Writes );
            Equal( "unknown", (await Send( post )).Outcome );
            Equal( 1, server.Writes );
            var dropped = post with
            {
                Http = post.Http! with
                {
                    Path = "/tasks/drop",
                    IdempotencyKey = "unique-mutation-drop"
                }
            };
            Equal( "unknown", (await Send( dropped )).Outcome );
            Equal( 2, server.Writes );
            Equal( "unknown", (await Send( dropped )).Outcome );
            Equal( 2, server.Writes );
            using var restarted = new ExtensionConnections( Path.Combine( root, "journal" ), new ExtensionSecretResolver( ) );
            await restarted.ConfigureAsync( "one", setup, CancellationToken.None );
            Equal( "unknown", (await restarted.ExecuteAsync( "one", post, true, ( _, _ ) => ValueTask.FromResult( true ), CancellationToken.None )).Outcome );
            var legacyKey = "legacy-mutation-key";
            var legacyName = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( Encoding.UTF8.GetBytes( "one\0tasks\0" + legacyKey ) ) );
            await File.WriteAllTextAsync( Path.Combine( root, "journal", legacyName ), "attempted" );
            var legacy = post with { Http = post.Http! with { IdempotencyKey = legacyKey } };
            Equal( "unknown", (await restarted.ExecuteAsync( "one", legacy, true, ( _, _ ) => ValueTask.FromResult( true ), CancellationToken.None )).Outcome );
            Equal( 2, server.Writes );
            var pending = get with
            {
                Http = new( "POST", "/tasks", IdempotencyKey: "unique-mutation-002" )
            };
            Equal( "cancelled", (await broker.ExecuteAsync( "one", pending, true, ( _, _ ) =>
            {
                broker.Revoke( "one" );
                return ValueTask.FromResult( true );
            }, CancellationToken.None )).Outcome );
            Equal( 2, server.Writes );
            await broker.ConfigureAsync( "one", setup with
            {
                Name = "public",
                Origin = "https://localhost",
                PrivateNetwork = false,
                CredentialKind = "none"
            }, CancellationToken.None );
            Equal( "connection-unavailable", (await Send( get with
            {
                Connection = "public"
            } )).Outcome );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await broker.ConfigureAsync( "one", setup with { Origin = "http://localhost" }, CancellationToken.None ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private sealed class BrokerTestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new( IPAddress.Loopback, 0 );
        private readonly CancellationTokenSource _lifetime = new( );
        private readonly Task _run;
        public string Origin { get; }
        public string? Authorization { get; private set; }
        public int Writes { get; private set; }
        public int TokenRequests { get; private set; }
        public int Revocations { get; private set; }
        public string? ExpectedChallenge { get; set; }
        public BrokerTestServer( )
        {
            _listener.Start( );
            Origin = "http://127.0.0.1:" + (( IPEndPoint ) _listener.LocalEndpoint).Port;
            _run = RunAsync( );
        }
        private async Task RunAsync( )
        {
            try
            {
                while ( !_lifetime.IsCancellationRequested )
                {
                    using var client = await _listener.AcceptTcpClientAsync( _lifetime.Token );
                    await using var stream = client.GetStream( );
                    using var reader = new StreamReader( stream, Encoding.ASCII, false, 1024, true );
                    var first = await reader.ReadLineAsync( _lifetime.Token ) ?? "";
                    string? line;
                    var length = 0;
                    while ( !string.IsNullOrEmpty( line = await reader.ReadLineAsync( _lifetime.Token ) ) )
                    {
                        if ( line.StartsWith( "Authorization: ", StringComparison.Ordinal ) )
                        {
                            Authorization = line[15..];
                        }
                        if ( line.StartsWith( "Content-Length: ", StringComparison.Ordinal ) )
                        {
                            length = int.Parse( line[16..] );
                        }
                    }
                    var body = new char[length];
                    if ( length > 0 )
                    {
                        await reader.ReadBlockAsync( body, _lifetime.Token );
                    }
                    if ( first.Contains( "/oauth/", StringComparison.Ordinal ) )
                    {
                        var responseBody = OAuthResponse( first, new string( body ) );
                        await stream.WriteAsync( Encoding.ASCII.GetBytes( "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + responseBody.Length + "\r\n\r\n" + responseBody ), _lifetime.Token );
                        continue;
                    }
                    if ( first.StartsWith( "POST", StringComparison.Ordinal ) )
                    {
                        Writes++;
                    }
                    if ( first.Contains( "/tasks/drop", StringComparison.Ordinal ) )
                    {
                        continue;
                    }
                    if ( first.Contains( "/tasks/gzip", StringComparison.Ordinal ) )
                    {
                        using var compressed = new MemoryStream( );
                        using ( var gzip = new System.IO.Compression.GZipStream( compressed, System.IO.Compression.CompressionMode.Compress, true ) )
                        {
                            gzip.Write( new byte[131073] );
                        }
                        var bytes = compressed.ToArray( );
                        await stream.WriteAsync( Encoding.ASCII.GetBytes( "HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: " + bytes.Length + "\r\n\r\n" ), _lifetime.Token );
                        await stream.WriteAsync( bytes, _lifetime.Token );
                        continue;
                    }
                    var response = first.Contains( "redirect", StringComparison.Ordinal ) ? "HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1/private\r\nContent-Length: 0\r\n\r\n" :
                        first.Contains( "flood", StringComparison.Ordinal ) ? "HTTP/1.1 200 OK\r\nContent-Length: 131073\r\n\r\n" + new string( 'x', 131073 ) : "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\n\r\nok";
                    await stream.WriteAsync( Encoding.ASCII.GetBytes( response ), _lifetime.Token );
                }
            }
            catch ( Exception exception ) when ( exception is OperationCanceledException or SocketException or IOException ) { }
        }
        private string OAuthResponse( string first, string body )
        {
            if ( first.Contains( "revoke", StringComparison.Ordinal ) )
            {
                Revocations++;
                return "{}";
            }
            TokenRequests++;
            var values = ParseOAuthQuery( body );
            if ( values.GetValueOrDefault( "grant_type" ) == "authorization_code" )
            {
                var actual = Convert.ToBase64String( System.Security.Cryptography.SHA256.HashData( Encoding.ASCII.GetBytes( values["code_verifier"] ) ) ).TrimEnd( '=' ).Replace( '+', '-' ).Replace( '/', '_' );
                Equal( ExpectedChallenge, actual );
                return "{\"access_token\":\"oauth-first\",\"refresh_token\":\"refresh-first\",\"token_type\":\"Bearer\",\"expires_in\":1}";
            }
            Equal( "refresh-first", values["refresh_token"] );
            return "{\"access_token\":\"oauth-refreshed\",\"refresh_token\":\"refresh-rotated\",\"token_type\":\"Bearer\",\"expires_in\":120}";
        }
        public async ValueTask DisposeAsync( )
        {
            _lifetime.Cancel( );
            _listener.Stop( );
            await _run;
            _lifetime.Dispose( );
        }
    }

    private static Dictionary<string, string> ParseOAuthQuery( string query ) => query.TrimStart( '?' ).Split( '&' ).Select( pair => pair.Split( '=', 2 ) )
        .ToDictionary( pair => Uri.UnescapeDataString( pair[0] ), pair => Uri.UnescapeDataString( pair[1] ) );

    private static ExtensionConnectionSetup OAuthConnectionSetup( BrokerTestServer server ) => new( )
    {
        Name = "tasks",
        Kind = "http",
        Origin = server.Origin,
        PrivateNetwork = true,
        Route = "/tasks",
        CredentialKind = "oauth",
        OAuth = new( server.Origin, server.Origin + "/oauth/authorize", server.Origin + "/oauth/token", "public-client", "tasks", server.Origin + "/oauth/revoke" )
    };

    private static async Task ExtensionOAuthBindsStatePkceRefreshAndDisconnectAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using var server = new BrokerTestServer( );
            using var broker = new ExtensionConnections( Path.Combine( root, "journal" ), new ExtensionSecretResolver( ) );
            var setup = new ExtensionConnectionSetup
            {
                Name = "tasks",
                Kind = "http",
                Origin = server.Origin,
                PrivateNetwork = true,
                Route = "/tasks",
                CredentialKind = "oauth",
                OAuth = new( server.Origin, server.Origin + "/oauth/authorize", server.Origin + "/oauth/token", "public-client", "tasks", server.Origin + "/oauth/revoke" )
            };
            await broker.ConfigureAsync( "one", setup, CancellationToken.None );
            using var browser = new HttpClient( );
            var badLogin = new Uri( await broker.BeginLoginAsync( "one", "tasks", CancellationToken.None ) );
            var bad = ParseOAuthQuery( badLogin.Query );
            try
            {
                using var ignored = await browser.GetAsync( bad["redirect_uri"] + "?code=x&state=wrong&iss=" + Uri.EscapeDataString( server.Origin ) );
            }
            catch ( HttpRequestException ) { }
            using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
            while ( !broker.List( "one" )[0].CredentialStatus.StartsWith( "Login failed", StringComparison.Ordinal ) )
            {
                await Task.Delay( 10, timeout.Token );
            }
            Equal( 0, server.TokenRequests );
            var login = new Uri( await broker.BeginLoginAsync( "one", "tasks", CancellationToken.None ) );
            var values = ParseOAuthQuery( login.Query );
            server.ExpectedChallenge = values["code_challenge"];
            Equal( "S256", values["code_challenge_method"] );
            True( values["state"] != bad["state"] );
            using var completed = await browser.GetAsync( values["redirect_uri"] + "?code=issued&state=" + values["state"] + "&iss=" + Uri.EscapeDataString( server.Origin ) );
            Equal( HttpStatusCode.OK, completed.StatusCode );
            Equal( 1, server.TokenRequests );
            await Task.Delay( 1100 );
            var request = new BrokerRequest( "s", 1, 1, "i", "http.send", "tasks", Http: new( "GET", "/tasks" ) );
            await Task.WhenAll( Enumerable.Range( 0, 3 ).Select( async _ => Equal( "completed", (await broker.ExecuteAsync( "one", request, true, ( _, _ ) => ValueTask.FromResult( false ), CancellationToken.None )).Outcome ) ) );
            Equal( 2, server.TokenRequests );
            Equal( "Bearer oauth-refreshed", server.Authorization );
            var message = await broker.DisconnectAsync( "one", "tasks", true, CancellationToken.None );
            True( message.Contains( "acknowledged", StringComparison.Ordinal ) );
            Equal( 1, server.Revocations );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await broker.ExecuteAsync( "one", request, true, ( _, _ ) => ValueTask.FromResult( false ), CancellationToken.None ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task ExtensionOAuthCallbackServesOneConnectionAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using var server = new BrokerTestServer( );
            using var broker = new ExtensionConnections( Path.Combine( root, "journal" ), new ExtensionSecretResolver( ) );
            await broker.ConfigureAsync( "one", OAuthConnectionSetup( server ), CancellationToken.None );
            var login = ParseOAuthQuery( new Uri( await broker.BeginLoginAsync( "one", "tasks", CancellationToken.None ) ).Query );
            server.ExpectedChallenge = login["code_challenge"];
            var redirect = new Uri( login["redirect_uri"] );

            // The browser's connection reaches the callback first.
            using TcpClient browser = new( );
            await browser.ConnectAsync( IPAddress.Loopback, redirect.Port );

            // Another connection, such as the browser retrying, is refused or
            // closed at once rather than parked behind the pending callback.
            True( await CallbackTurnsAwayAnotherConnectionAsync( redirect.Port ) );

            // The pending callback still completes the login.
            var stream = browser.GetStream( );
            var callback = $"GET {redirect.AbsolutePath}?code=issued&state={login["state"]}&iss={Uri.EscapeDataString( server.Origin )} HTTP/1.1\r\n" +
                $"Host: {redirect.Authority}\r\n\r\n";
            await stream.WriteAsync( Encoding.ASCII.GetBytes( callback ) );
            using StreamReader reader = new( stream, Encoding.ASCII );
            Equal( "HTTP/1.1 200 OK", await reader.ReadLineAsync( ) );
            Equal( 1, server.TokenRequests );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task<bool> CallbackTurnsAwayAnotherConnectionAsync( int port )
    {
        try
        {
            using TcpClient other = new( );
            await other.ConnectAsync( IPAddress.Loopback, port );
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
            return await other.GetStream( ).ReadAsync( new byte[1], timeout.Token ) == 0;
        }
        catch ( Exception exception ) when ( exception is SocketException or IOException )
        {
            return true;
        }
        catch ( OperationCanceledException )
        {
            return false;
        }
    }
}
