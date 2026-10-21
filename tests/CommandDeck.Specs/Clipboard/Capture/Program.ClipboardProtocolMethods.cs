using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardProtocolRejectsMalformedAuthority( )
    {
        var instance = Guid.NewGuid( ).ToString( "N" );
        ClipboardRequest[] invalid =
        [
            new( ) { FrontendInstance = "not-an-id", OpenGeneration = 1 },
            new( ) { FrontendInstance = instance, OpenGeneration = 0 },
            new( ) { FrontendInstance = instance, OpenGeneration = 9007199254740992 },
            new( ) { FrontendInstance = instance, OpenGeneration = 1, Keywords = ["bad\0keyword"] },
            new( ) { FrontendInstance = instance, OpenGeneration = 1, Keywords = [new string( 'é', 33 )] }
        ];
        foreach ( var value in invalid )
        {
            Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new( )
            {
                RequestId = 1,
                Operation = ProtocolOperations.ClipboardOpen,
                Clipboard = value
            } ) );
        }

        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new( )
        {
            RequestId = 1,
            Operation = ProtocolOperations.Search,
            Clipboard = new( )
        } ) );
        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new( )
        {
            RequestId = 1,
            Operation = ProtocolOperations.ClipboardQuery,
            Clipboard = new( )
            {
                SessionId = instance,
                QueryGeneration = 1
            },
            Expression = new( )
            {
                IncludeFilterIds = [SourceIds.Clipboard]
            }
        } ) );
    }

    private static async Task ClipboardProtocolBindsLiveConnectionsAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-wire-" ).FullName;
        var database = Path.Combine( root, "catalog.db" );
        var config = Path.Combine( root, "config.json" );
        var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
        using CancellationTokenSource stopping = new( );
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 30 ) );
        try
        {
            using ( ClipboardHistoryStore seed = new( database + ".clipboard.db", new ClipboardHistoryOptions( ) ) )
            {
                seed.Enabled = true;
                seed.Capture( "wire secret café\r\n<script>literal</script>" );
                seed.Enabled = false;
            }

            CommandDeckOptions options = new( )
            {
                Storage = new( )
                {
                    DatabasePath = database
                },
                Indexing = new( )
                {
                    Roots = [],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Ai = new( )
                {
                    Enabled = false,
                    Providers = []
                }
            };
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, timeout.Token );
            await runtime.SetFilterAliasAsync( "cb", "clipboard", timeout.Token );
            UnixProtocolServer server = new( runtime, socket );
            var running = server.RunAsync( stopping.Token );
            try
            {
                await using var first = await ConnectWithRetryAsync( socket );
                await using var other = await ConnectWithRetryAsync( socket );
                await VerifyClipboardSearchTelemetryAsync( first, timeout.Token );
                var instance = Guid.NewGuid( ).ToString( "N" );
                var opened = await ClipboardWireRequestAsync( first, 1, ProtocolOperations.ClipboardOpen,
                    new( )
                    {
                        FrontendInstance = instance,
                        OpenGeneration = 1
                    }, timeout.Token );
                var session = opened.Clipboard!.SessionId!;
                var page = await ClipboardWireRequestAsync( first, 2, ProtocolOperations.ClipboardQuery,
                    new( )
                    {
                        SessionId = session,
                        QueryGeneration = 1
                    }, timeout.Token );
                var entry = page.Clipboard!.Page!.Entries.Single( );
                var denied = await ClipboardWireRequestAsync( other, 3, ProtocolOperations.ClipboardPreview,
                    new( )
                    {
                        SessionId = session,
                        EntryId = entry.Id,
                        Incarnation = entry.Incarnation,
                        EntryRevision = entry.Revision,
                        QueryGeneration = 1
                    },
                    timeout.Token, error: true );
                True( !denied.Error!.Contains( "wire secret" ), "owner errors contain no private text" );
                var preview = await ClipboardWireRequestAsync( first, 4, ProtocolOperations.ClipboardPreview,
                    new( )
                    {
                        SessionId = session,
                        EntryId = entry.Id,
                        Incarnation = entry.Incarnation,
                        EntryRevision = entry.Revision,
                        QueryGeneration = 1
                    }, timeout.Token );
                Equal( "wire secret café\r\n<script>literal</script>", preview.Clipboard!.Preview!.Slice.Text );
                await ClipboardWireRequestAsync( first, 5, ProtocolOperations.ClipboardAction,
                    new( )
                    {
                        FrontendInstance = instance,
                        OpenGeneration = 2,
                        SessionId = session,
                        EntryId = entry.Id,
                        Incarnation = entry.Incarnation,
                        EntryRevision = entry.Revision,
                        Action = "pin"
                    },
                    timeout.Token, error: true );
                await ClipboardWireRequestAsync( first, 6, ProtocolOperations.ClipboardAction,
                    new( )
                    {
                        FrontendInstance = instance,
                        OpenGeneration = 1,
                        SessionId = session,
                        EntryId = entry.Id,
                        Incarnation = entry.Incarnation,
                        EntryRevision = entry.Revision,
                        Action = "pin"
                    }, timeout.Token );
                await ClipboardWireRequestAsync( first, 7, ProtocolOperations.ClipboardPreview,
                    new( )
                    {
                        SessionId = session,
                        EntryId = entry.Id,
                        Incarnation = entry.Incarnation,
                        EntryRevision = entry.Revision,
                        QueryGeneration = 1
                    }, timeout.Token, error: true );
                var settings = await ClipboardWireRequestAsync( first, 8, ProtocolOperations.ClipboardSettingsGet, new( ), timeout.Token );
                Equal( ClipboardConsent.None, settings.Clipboard!.Policy!.Consent );
                await ClipboardWireRequestAsync( first, 9, ProtocolOperations.ClipboardSettingsSet,
                    new( )
                    {
                        PolicyRevision = settings.Clipboard.Policy.Revision,
                        Consent = ClipboardConsent.Text
                    }, timeout.Token, error: true );
                await ClipboardWireRequestAsync( first, 10, ProtocolOperations.ClipboardClose,
                    new( )
                    {
                        FrontendInstance = instance,
                        OpenGeneration = 1
                    }, timeout.Token );
                await ClipboardWireRequestAsync( first, 11, ProtocolOperations.ClipboardOpen,
                    new( )
                    {
                        FrontendInstance = instance,
                        OpenGeneration = 1
                    }, timeout.Token, error: true );
                await first.DisposeAsync( );
                await using var replacement = await ConnectWithRetryAsync( socket );
                await ClipboardWireRequestAsync( replacement, 12, ProtocolOperations.ClipboardQuery,
                    new( )
                    {
                        SessionId = session,
                        QueryGeneration = 2
                    }, timeout.Token, error: true );
                await ClipboardWireRequestAsync( replacement, 13, ProtocolOperations.ClipboardOpen,
                    new( )
                    {
                        FrontendInstance = Guid.NewGuid( ).ToString( "N" ),
                        OpenGeneration = 1
                    }, timeout.Token );
            }
            finally
            {
                stopping.Cancel( );
                await running;
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task<ProtocolMessage> ClipboardWireRequestAsync( UnixProtocolClient client, long id,
        string operation, ClipboardRequest clipboard, CancellationToken cancellationToken, bool error = false )
    {
        await client.SendAsync( new( )
        {
            RequestId = id,
            Operation = operation,
            Clipboard = clipboard
        }, cancellationToken );
        var response = await ReadMessageAsync( client, id, error ? ProtocolMessageTypes.Error : ProtocolMessageTypes.ClipboardResult, cancellationToken );
        if ( response is null )
        {
            throw new InvalidOperationException( "Clipboard response was not returned." );
        }

        return response;
    }
}
