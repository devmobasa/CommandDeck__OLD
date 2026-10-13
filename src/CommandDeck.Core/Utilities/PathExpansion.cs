namespace CommandDeck;

public static class PathExpansion
{
    public static string Expand( string path )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( path );

        var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
        var stateHome = Environment.GetEnvironmentVariable( "XDG_STATE_HOME" )
                        ?? Path.Combine( home, ".local", "state" );
        var configHome = Environment.GetEnvironmentVariable( "XDG_CONFIG_HOME" )
                         ?? Path.Combine( home, ".config" );
        var cacheHome = Environment.GetEnvironmentVariable( "XDG_CACHE_HOME" )
                        ?? Path.Combine( home, ".cache" );
        var runtimeDir = Environment.GetEnvironmentVariable( "XDG_RUNTIME_DIR" )
                         ?? Path.Combine( Path.GetTempPath( ), $"commanddeck-{Environment.UserName}" );

        var expanded = path
            .Replace( "$XDG_STATE_HOME", stateHome, StringComparison.Ordinal )
            .Replace( "${XDG_STATE_HOME}", stateHome, StringComparison.Ordinal )
            .Replace( "$XDG_CONFIG_HOME", configHome, StringComparison.Ordinal )
            .Replace( "${XDG_CONFIG_HOME}", configHome, StringComparison.Ordinal )
            .Replace( "$XDG_CACHE_HOME", cacheHome, StringComparison.Ordinal )
            .Replace( "${XDG_CACHE_HOME}", cacheHome, StringComparison.Ordinal )
            .Replace( "$XDG_RUNTIME_DIR", runtimeDir, StringComparison.Ordinal )
            .Replace( "${XDG_RUNTIME_DIR}", runtimeDir, StringComparison.Ordinal )
            .Replace( "$HOME", home, StringComparison.Ordinal )
            .Replace( "${HOME}", home, StringComparison.Ordinal );

        if ( expanded == "~" )
        {
            expanded = home;
        }
        else if ( expanded.StartsWith( "~/", StringComparison.Ordinal ) )
        {
            expanded = Path.Combine( home, expanded[2..] );
        }

        return Path.GetFullPath( expanded );
    }

    public static string GetDefaultConfigPath( )
    {
        var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
        var configHome = Environment.GetEnvironmentVariable( "XDG_CONFIG_HOME" )
                         ?? Path.Combine( home, ".config" );
        return Path.Combine( configHome, "commanddeck", "commanddeck.jsonc" );
    }

    public static string GetDefaultSocketPath( )
    {
        var runtimeDir = Environment.GetEnvironmentVariable( "XDG_RUNTIME_DIR" )
                         ?? Path.Combine( Path.GetTempPath( ), $"commanddeck-{Environment.UserName}" );
        Directory.CreateDirectory( runtimeDir );
        return Path.Combine( runtimeDir, "commanddeck.sock" );
    }
}
