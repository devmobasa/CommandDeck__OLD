using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunQuicklinksAsync( CommandDeckRuntime runtime, CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var values = arguments.PositionalsAfterCommand( );
        var action = values.Count == 0 ? "list" : values[0].ToLowerInvariant( );
        QuicklinksResult result;
        if ( action == "list" && values.Count <= 1 )
        {
            result = await runtime.GetQuicklinksAsync( cancellationToken ).ConfigureAwait( false );
        }
        else if ( action == "restore" && values.Count == 2 )
        {
            var backup = await ReadQuicklinkBackupAsync( values[1], cancellationToken ).ConfigureAwait( false );
            result = await runtime.RestoreQuicklinksAsync( backup, cancellationToken ).ConfigureAwait( false );
        }
        else if ( action == "add" && values.Count == 1 || action == "edit" && values.Count == 2 )
        {
            QuicklinkOptions link = new( )
            {
                Id = action == "edit" ? values[1] : "user-" + Guid.NewGuid( ).ToString( "N" ),
                Title = arguments.GetOption( "--title" ) ?? string.Empty,
                UrlTemplate = arguments.GetOption( "--url" ) ?? string.Empty,
                Alias = arguments.GetOption( "--alias" ),
                Icon = arguments.GetOption( "--icon" )
            };
            result = await runtime.ChangeQuicklinkAsync( "save", link, null, cancellationToken ).ConfigureAwait( false );
        }
        else if ( action is "remove" or "disable" or "enable" && values.Count == 2 )
        {
            result = await runtime.ChangeQuicklinkAsync( action, null, values[1], cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            Console.Error.WriteLine( "Use 'quicklinks', 'quicklinks add --title TITLE --url URL [--alias ALIAS] [--icon ICON]', 'quicklinks edit ID --title TITLE --url URL', 'quicklinks remove|disable|enable ID', or 'quicklinks restore BACKUP.json'." );
            return 2;
        }
        if ( arguments.HasFlag( CliOptionNames.Json ) )
        {
            Console.WriteLine( JsonSerializer.Serialize( result, CommandDeckJsonContext.Default.QuicklinksResult ) );
        }
        else
        {
            if ( result.Message is not null )
            {
                Console.WriteLine( result.Message );
            }
            foreach ( var entry in result.Quicklinks )
            {
                var link = entry.Quicklink;
                Console.WriteLine( PreferencesTextFormatter.EscapeTerminalText(
                    $"{link.Id}\t{entry.Source}\t{(entry.Enabled ? "enabled" : "disabled")}\t{link.Title}\t{link.UrlTemplate}\t{link.Alias}" ) );
            }
        }
        return 0;
    }
    private static async ValueTask<CustomizationExport> ReadQuicklinkBackupAsync( string path, CancellationToken cancellationToken )
    {
        using var input = SafeRegularFile.Open( path );
        var bytes = new byte[ProtocolLimits.MaximumCustomizationExportBytes + 1];
        var count = await input.ReadPrefixAsync( bytes, cancellationToken ).ConfigureAwait( false );
        if ( count > ProtocolLimits.MaximumCustomizationExportBytes )
        {
            throw new InvalidOperationException( "Customization backup is too large." );
        }
        var backup = JsonSerializer.Deserialize( bytes.AsSpan( 0, count ), CommandDeckJsonContext.Default.CustomizationExport );
        if ( backup is null )
        {
            throw new InvalidOperationException( "Missing customization backup." );
        }

        return backup;
    }

}
