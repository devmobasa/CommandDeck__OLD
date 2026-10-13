namespace CommandDeck;

/// <summary>
/// The CommandDeck-owned home for one CLI agent. Every CLI honours
/// <c>HOME</c>, so pointing it here keeps sign-ins, caches, and sessions in a
/// private mode-0700 tree and never reads the user's own <c>~/.claude</c>,
/// <c>~/.cursor</c>, or <c>~/.grok</c>. Turn scratch directories live under
/// the runtime directory and are wiped when the backend stops.
/// </summary>
public sealed class CliAgentHome
{
    private readonly CliAgentProfile _profile;
    private readonly string _scratchRoot;

    public CliAgentHome( CliAgentProfile profile, string stateDirectory, string runtimeDirectory )
    {
        if ( profile is null )
        {
            throw new ArgumentNullException( nameof( profile ) );
        }

        _profile = profile;
        ArgumentException.ThrowIfNullOrWhiteSpace( stateDirectory );
        ArgumentException.ThrowIfNullOrWhiteSpace( runtimeDirectory );
        Path = System.IO.Path.Combine( PathExpansion.Expand( stateDirectory ), profile.ShortName, "home" );
        _scratchRoot = System.IO.Path.Combine( PathExpansion.Expand( runtimeDirectory ), profile.ShortName );
    }

    public string Path { get; }

    public bool CredentialExists => _profile.CredentialFiles.Any( relative =>
        File.Exists( System.IO.Path.Combine( Path, relative ) ) ||
        Directory.Exists( System.IO.Path.Combine( Path, relative ) ) );

    public void Ensure( )
    {
        CreatePrivate( System.IO.Path.GetDirectoryName( Path )! );
        CreatePrivate( Path );
        CreatePrivate( System.IO.Path.Combine( Path, "xdg" ) );
    }

    public string CreateScratch( )
    {
        CreatePrivate( _scratchRoot );
        var scratch = System.IO.Path.Combine( _scratchRoot, Guid.NewGuid( ).ToString( "N" ) );
        CreatePrivate( scratch );
        return scratch;
    }

    public void DeleteCredentials( )
    {
        foreach ( var relative in _profile.CredentialFiles )
        {
            var target = System.IO.Path.Combine( Path, relative );
            try
            {
                if ( File.Exists( target ) )
                {
                    File.Delete( target );
                }
                else if ( Directory.Exists( target ) )
                {
                    Directory.Delete( target, recursive: true );
                }
            }
            catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
            {
            }
        }
    }

    public void WipeScratch( )
    {
        try
        {
            if ( Directory.Exists( _scratchRoot ) )
            {
                Directory.Delete( _scratchRoot, recursive: true );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
        }
    }

    private static void CreatePrivate( string directory )
    {
        if ( !Directory.Exists( directory ) )
        {
            Directory.CreateDirectory( directory );
        }
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }
    }
}
