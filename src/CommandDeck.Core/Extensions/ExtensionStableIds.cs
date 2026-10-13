using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public static class ExtensionOrigins
{
    public const string Local = "local";
    public const string Development = "development";
}

public static class ExtensionStableIds
{
    private const string SearchItemPrefix = "extension-item:";
    public static string SourceId( string key ) => "extension:" + key;
    public static string CommandId( string key, string commandId ) => SourceId( key ) + ":" + commandId;
    public static string SearchProviderId( string key, string provider ) => "extension-search:" + key + ":" + provider;
    public static string SearchItemId( string key, string provider, string itemId ) =>
        SearchItemPrefix + key + ":" + provider + ":" + Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( itemId ) ) );
    public static bool IsSearchItem( string id ) => id.StartsWith( SearchItemPrefix, StringComparison.Ordinal );
    public static string FilterToken( string key, string? provider = null ) => "ext" + key[..10] + (provider is null ? "" : "-" + provider) + ":";
}
