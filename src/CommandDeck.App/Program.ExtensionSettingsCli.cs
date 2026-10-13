using System.Globalization;
using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async ValueTask<ExtensionConnectionSetup> ReadExtensionConnectionAsync( CliArguments arguments, CancellationToken token )
    {
        var path = arguments.GetOption( "--connection-file" );
        if ( path is null )
        {
            throw new InvalidOperationException( "Select a connection setup JSON file." );
        }

        using var input = SafeRegularFile.Open( path );
        var bytes = new byte[32769];
        try
        {
            var count = await input.ReadPrefixAsync( bytes, token ).ConfigureAwait( false );
            if ( count > 32768 )
            {
                throw new InvalidDataException( "Connection setup exceeds 32 KiB." );
            }
            var result = JsonSerializer.Deserialize( bytes.AsSpan( 0, count ), CommandDeckJsonContext.Default.ExtensionConnectionSetup );
            if ( result is null )
            {
                throw new InvalidDataException( "Missing connection setup." );
            }

            return result;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory( bytes );
        }
    }
    private static long ReadExtensionSettingsRevision( CliArguments arguments ) =>
        long.TryParse( arguments.GetOption( "--expected-revision" ), NumberStyles.None, CultureInfo.InvariantCulture, out var revision )
            ? revision : throw new InvalidOperationException( "Inspect the package, then pass --expected-revision with the revision of the settings you edited." );

    private static async Task<Dictionary<string, JsonElement>> ReadExtensionSettingsFileAsync( CliArguments arguments, CancellationToken cancellationToken )
    {
        var file = arguments.GetOption( "--settings-file" );
        if ( file is null )
        {
            throw new InvalidOperationException( "Configure requires --settings-file FILE.json containing non-secret settings." );
        }

        await using var input = File.OpenRead( file );
        byte[] bytes = new byte[65537];
        var count = 0;
        while ( count < bytes.Length )
        {
            var read = await input.ReadAsync( bytes.AsMemory( count ), cancellationToken ).ConfigureAwait( false );
            if ( read == 0 )
            {
                break;
            }
            count += read;
        }
        if ( count > 65536 )
        {
            throw new InvalidDataException( "Settings input exceeds 64 KiB." );
        }
        try
        {
            using var document = JsonDocument.Parse( bytes.AsMemory( 0, count ), new JsonDocumentOptions { MaxDepth = 16 } );
            if ( document.RootElement.ValueKind != JsonValueKind.Object )
            {
                throw new InvalidDataException( "Settings must be a JSON object." );
            }
            Dictionary<string, JsonElement> values = new( StringComparer.Ordinal );
            foreach ( var field in document.RootElement.EnumerateObject( ) )
            {
                if ( values.Count == 16 || !values.TryAdd( field.Name, field.Value.Clone( ) ) )
                {
                    throw new InvalidDataException( "Settings must contain at most 16 distinct names." );
                }
            }
            return values;
        }
        catch ( JsonException )
        {
            throw new InvalidDataException( "Settings must contain valid JSON with depth at most 16." );
        }
    }
}
