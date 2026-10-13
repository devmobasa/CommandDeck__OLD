namespace CommandDeck;

public sealed class ExtensionArtifactStore( string root )
{
    public string ArtifactRoot { get; } = Path.GetFullPath( root );
    public string DirectoryFor( ExtensionInstallation installation ) => Path.Combine( ArtifactRoot, installation.Key, "versions", installation.Digest );
    public string Materialize( StagedExtension staged, string key ) => staged.Materialize( ArtifactRoot, key );
    public void Remove( string key )
    {
        var directory = Path.Combine( ArtifactRoot, key );
        if ( Directory.Exists( directory ) )
        {
            Directory.Delete( directory, recursive: true );
        }
    }

    public void Recover( ExtensionRegistryDocument document )
    {
        try
        {
            RecoverArtifactsCore( document );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or System.Security.SecurityException ) { }
    }

    private void RecoverArtifactsCore( ExtensionRegistryDocument document )
    {
        if ( !IsNonLinkDirectory( ArtifactRoot ) )
        {
            return;
        }

        foreach ( var directory in Directory.EnumerateDirectories( ArtifactRoot ) )
        {
            var name = Path.GetFileName( directory );
            if ( IsStagingDirectory( name ) )
            {
                TryDeleteManagedDirectory( directory );
                continue;
            }

            if ( !IsArtifactIdentifier( name ) )
            {
                continue;
            }

            var installation = document.Installations.FirstOrDefault( current => current.Key == name && !current.Removed );
            if ( installation is null )
            {
                TryDeleteManagedDirectory( directory );
                continue;
            }

            Cleanup( document, installation.Key );
        }
    }

    public void Cleanup( ExtensionRegistryDocument document, string key )
    {
        try
        {
            CleanupInstallationArtifactsCore( document, key );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or System.Security.SecurityException ) { }
    }

    private void CleanupInstallationArtifactsCore( ExtensionRegistryDocument document, string key )
    {
        var installation = document.Installations.FirstOrDefault( current => current.Key == key && !current.Removed );
        var packageDirectory = Path.Combine( ArtifactRoot, key );
        if ( installation is null )
        {
            TryDeleteManagedDirectory( packageDirectory );
            return;
        }

        if ( !IsNonLinkDirectory( packageDirectory ) )
        {
            return;
        }

        var versionsDirectory = Path.Combine( packageDirectory, "versions" );
        if ( !IsNonLinkDirectory( versionsDirectory ) )
        {
            return;
        }

        var retained = installation.Versions.Select( version => version.Digest ).Append( installation.Digest ).ToHashSet( StringComparer.Ordinal );
        foreach ( var directory in Directory.EnumerateDirectories( versionsDirectory ) )
        {
            var digest = Path.GetFileName( directory );
            if ( IsArtifactIdentifier( digest ) && !retained.Contains( digest ) )
            {
                TryDeleteManagedDirectory( directory );
            }
        }
    }

    private static bool IsArtifactIdentifier( string value ) =>
        value.Length == 64 && value.All( char.IsAsciiHexDigit );

    private static bool IsStagingDirectory( string value ) =>
        value.Length == 41 && value.StartsWith( ".staging-", StringComparison.Ordinal ) && value[9..].All( char.IsAsciiHexDigit );

    private static bool IsNonLinkDirectory( string path ) =>
        Directory.Exists( path ) && !File.GetAttributes( path ).HasFlag( FileAttributes.ReparsePoint );

    private static void TryDeleteManagedDirectory( string path )
    {
        try
        {
            if ( !IsNonLinkDirectory( path ) )
            {
                return;
            }

            Directory.Delete( path, recursive: true );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or System.Security.SecurityException ) { }
    }
}
