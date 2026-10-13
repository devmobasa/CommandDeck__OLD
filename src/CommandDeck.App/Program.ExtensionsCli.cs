using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunExtensionsCliAsync( CliArguments arguments, CancellationToken cancellationToken )
    {
        var values = arguments.PositionalsAfterCommand( );
        var action = values.Count > 0 ? values[0] : ExtensionManagementActions.List;
        if ( action == "help" )
        {
            Console.WriteLine( "extensions list|inspect|install|attach|logs|debug|debug-stop|enable|disable|remove|rollback|grants|configure [package-id] [--archive path] [--digest sha256] [--allow capability,...] [--yes] [--json]" );
            Console.WriteLine( "Debug: development packages only; --command ID --digest SHA256 --seconds 1..300 --yes. Open that command, then connect GDB to the returned socket. debug-stop revokes the lease. logs --include-stderr explicitly includes untrusted child text that may contain sensitive data." );
            Console.WriteLine( "Configure: --settings-file FILE.json --expected-revision REVISION. Inspect first; the file replaces non-secret settings. An empty object restores declared defaults." );
            Console.WriteLine( "Connections: connections|connection-set|connection-remove|connection-login PACKAGE. Set uses --connection-file FILE.json --yes; remove/login use --connection NAME --yes. Remove optionally uses --remote-revoke. Credentials and roots last only for this daemon session." );
            Console.WriteLine( "effects-reset PACKAGE --yes [--include-legacy] clears remote-write history and permits previously used request keys to be sent again. --include-legacy also clears unattributed legacy history for all packages." );
            Console.WriteLine( "Inspect an archive, then install its reviewed digest with --yes. New packages are disabled. Enable the package and the master switch separately. Omit package-id only for the master enable or disable switch." );
            return 0;
        }
        var key = values.Count > 1 ? values[1] : null;
        var settings = action == ExtensionManagementActions.Configure ? await ReadExtensionSettingsFileAsync( arguments, cancellationToken ).ConfigureAwait( false ) : null;
        var connection = action == ExtensionManagementActions.ConnectionSet ? await ReadExtensionConnectionAsync( arguments, cancellationToken ).ConfigureAwait( false ) : null;
        long? settingsRevision = action == ExtensionManagementActions.Configure ? ReadExtensionSettingsRevision( arguments ) : null;
        if ( ExtensionManagementPolicy.RequiresConfirmation( action ) && !arguments.HasFlag( "--yes" ) )
        {
            throw new InvalidOperationException( "Review the extension and repeat with --yes to confirm this change." );
        }
        await using var client = await UnixProtocolClient.ConnectAsync( arguments.GetOption( "--socket" ), cancellationToken ).ConfigureAwait( false );
        var current = await RequestExtensionInventoryAsync( client, new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.ExtensionsManage,
            Extension = new( ),
            Limit = 32
        }, cancellationToken ).ConfigureAwait( false );
        var archive = arguments.GetOption( "--archive" );
        var result = await RequestExtensionInventoryAsync( client, new ProtocolRequest
        {
            RequestId = 2,
            Operation = ProtocolOperations.ExtensionsManage,
            Query = arguments.GetOption( "--query" ) ?? "",
            Offset = int.Parse( arguments.GetOption( "--offset" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ),
            Limit = 32,
            Extension = new( )
            {
                Action = action,
                Key = key,
                ArchivePath = archive is null ? null : Path.GetFullPath( archive ),
                Digest = arguments.GetOption( "--digest" ),
                ExpectedRevision = settingsRevision ?? current.Revision,
                Settings = settings,
                Connection = connection,
                ConnectionName = arguments.GetOption( "--connection" ),
                RemoteRevoke = arguments.HasFlag( "--remote-revoke" ),
                IncludeStderr = arguments.HasFlag( "--include-stderr" ),
                IncludeLegacy = arguments.HasFlag( "--include-legacy" ),
                CommandId = arguments.GetOption( "--command" ),
                DebugSeconds = int.Parse( arguments.GetOption( "--seconds" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ),
                Confirmed = arguments.HasFlag( "--yes" ),
                EraseSettings = arguments.HasFlag( "--erase-settings" ),
                Grants = arguments.GetOption( "--allow" )?.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
            }
        }, cancellationToken ).ConfigureAwait( false );
        WriteExtensionInventory( result, arguments.HasFlag( "--json" ) );
        return 0;
    }

    private static void WriteExtensionInventory( ExtensionInventory result, bool json )
    {
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize( result, CommandDeckJsonContext.Default.ExtensionInventory ) );
        }
        else
        {
            Console.WriteLine( $"Extensions {(result.Enabled ? "enabled" : "disabled")}; registry revision {result.Revision}; {result.Total} retained packages." );
            if ( result.Error is not null )
            {
                Console.WriteLine( result.Error );
            }
            foreach ( var package in result.Packages )
            {
                Console.WriteLine( $"{package.Id} {package.Version} {(package.Removed ? "removed" : package.Enabled ? "enabled" : "disabled")} {package.Digest}" );
            }
            if ( result.Manifest is { } manifest )
            {
                Console.WriteLine( manifest.GetRawText( ) );
            }
            if ( result.ArchiveDigest is not null )
            {
                Console.WriteLine( "Reviewed archive digest: " + result.ArchiveDigest );
            }
            foreach ( var connection in result.Connections )
            {
                Console.WriteLine( $"{connection.Name} · {connection.Kind} · {connection.Scope} · {connection.CredentialStatus}" );
            }
            if ( result.ConnectionMessage is not null )
            {
                Console.WriteLine( result.ConnectionMessage );
            }
            if ( result.Debug is { } debug )
            {
                Console.WriteLine( $"Debug lease: {debug.CommandId}, expires {debug.ExpiresAt:O}, socket {debug.SocketPath}, consumed {debug.Consumed}" );
            }
            foreach ( var diagnostic in result.Diagnostics )
            {
                Console.WriteLine( $"{diagnostic.At:O} {diagnostic.CommandId} {diagnostic.SessionId} {diagnostic.Digest} {diagnostic.Status}" );
                if ( diagnostic.Stderr is not null )
                {
                    Console.WriteLine( "Untrusted child stderr:\n" + diagnostic.Stderr );
                }
            }
        }
    }

    private static async Task<ExtensionInventory> RequestExtensionInventoryAsync(
        UnixProtocolClient client, ProtocolRequest request, CancellationToken cancellationToken )
    {
        ProtocolRequestValidator.Validate( request, deferFilterResolution: true );
        await client.SendAsync( request, cancellationToken ).ConfigureAwait( false );
        ExtensionInventory? result = null;
        string? error = null;
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var message = ProtocolJson.DeserializeMessage( line );
            if ( message is null )
            {
                continue;
            }
            if ( message.RequestId != request.RequestId )
            {
                continue;
            }
            if ( message.Type == ProtocolMessageTypes.Error )
            {
                error = message.Error;
            }
            if ( message.Extensions is not null )
            {
                result = message.Extensions;
            }
            if ( message.Type == ProtocolMessageTypes.Done )
            {
                break;
            }
        }
        if ( error is not null )
        {
            throw new InvalidOperationException( error );
        }
        if ( result is null )
        {
            throw new InvalidOperationException( "The daemon did not return an extension inventory." );
        }

        return result;
    }
}
