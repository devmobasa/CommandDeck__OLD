using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Extensions.Views;

namespace CommandDeck.ExtensionProtocol;

public sealed record ExtensionSearchProvider( string Id, string Title, string CommandId, string CacheKey, string? SuggestedPrefix = null );
public sealed record ExtensionSearchRequest( string Query, int Limit, JsonElement? Cache = null );
public sealed record ExtensionSearchItem( string Id, string Title, string? Subtitle = null, string? Context = null );
public sealed record ExtensionSearchResponse( ExtensionSearchItem[] Items );

public static class ExtensionSearchValidation
{
    public const int MaximumItems = 32;
    public static void Validate( ExtensionSearchResponse response )
    {
        if ( response.Items is null || response.Items.Length > MaximumItems )
        {
            throw new InvalidDataException( "Too many search results." );
        }
        HashSet<string> ids = new( StringComparer.Ordinal );
        foreach ( var item in response.Items )
        {
            if ( item is null )
            {
                throw new InvalidDataException( "Missing search item." );
            }
            ViewValidation.ValidateId( item.Id );
            if ( !ids.Add( item.Id ) || item.Title is not { Length: > 0 and <= 256 } || item.Subtitle?.Length > 512 || item.Context?.Length > 2048 ||
                item.Title.Any( char.IsControl ) || item.Subtitle?.Any( char.IsControl ) == true )
            {
                throw new InvalidDataException( "Invalid search item." );
            }
        }
        if ( JsonSerializer.SerializeToUtf8Bytes( response, ExtensionSearchJsonContext.Default.ExtensionSearchResponse ).Length > 64 * 1024 )
        {
            throw new InvalidDataException( "Search results exceed the encoded response budget." );
        }
    }
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ExtensionSearchRequest ) )]
[JsonSerializable( typeof( ExtensionSearchResponse ) )]
public sealed partial class ExtensionSearchJsonContext : JsonSerializerContext;
