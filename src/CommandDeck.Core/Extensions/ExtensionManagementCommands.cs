using System.Collections.Immutable;
using System.Text.Json;

namespace CommandDeck;

public abstract record ExtensionManagementCommand( long ExpectedRevision, string? PackageKey );
public sealed record ListPackages( string? Key ) : ExtensionManagementCommand( 0, Key );
public sealed record InspectArchive( string ArchivePath, string? Key ) : ExtensionManagementCommand( 0, Key );
public sealed record InspectPackage( string Key ) : ExtensionManagementCommand( 0, Key );
public sealed record InstallPackage( string ArchivePath, string Digest, ImmutableArray<string>? Grants, bool Development, long Revision, string? Key ) : ExtensionManagementCommand( Revision, Key );
public sealed record SetPackageEnabled( string? Key, bool Enabled, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record RemovePackage( string Key, bool EraseSettings, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record RollbackPackage( string Key, string Digest, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record SetPackageGrants( string Key, ImmutableArray<string> Grants, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record ConfigurePackage( string Key, ImmutableDictionary<string, JsonElement> Settings, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record SetPackageGlobalSearch( string Key, bool Enabled, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record ListConnections( string? Key ) : ExtensionManagementCommand( 0, Key );
public sealed record SetConnection( string Key, ExtensionConnectionSetup Setup, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record RemoveConnection( string Key, string Name, bool RemoteRevoke, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record BeginConnectionLogin( string Key, string Name, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record ArmExtensionDebug( string Key, string CommandId, string Digest, int Seconds, long Revision ) : ExtensionManagementCommand( Revision, Key );
public sealed record StopExtensionDebug( string Key ) : ExtensionManagementCommand( 0, Key );
public sealed record ReadExtensionLogs( string Key, bool IncludeStderr ) : ExtensionManagementCommand( 0, Key );
public sealed record ResetExtensionEffects( string Key, bool IncludeLegacy, long Revision ) : ExtensionManagementCommand( Revision, Key );

public static class ExtensionManagementPolicy
{
    public static bool RequiresConfirmation( string action ) => action is
        ExtensionManagementActions.Install or ExtensionManagementActions.Attach or ExtensionManagementActions.Debug or
        ExtensionManagementActions.Remove or ExtensionManagementActions.Rollback or ExtensionManagementActions.Grants or
        ExtensionManagementActions.GlobalSearch or ExtensionManagementActions.EffectsReset or
        ExtensionManagementActions.ConnectionSet or ExtensionManagementActions.ConnectionRemove or ExtensionManagementActions.ConnectionLogin;
}

public sealed partial class ExtensionManagementRequest
{
    public ExtensionManagementCommand ToCommand( )
    {
        ValidateBounds( );
        // A reviewed archive is a prerequisite for asking to confirm its installation.
        InstallPackage? install = Action is ExtensionManagementActions.Install or ExtensionManagementActions.Attach ? InstallCommand( ) : null;
        if ( ExtensionManagementPolicy.RequiresConfirmation( Action ) && !Confirmed )
        {
            throw new InvalidOperationException( Action is ExtensionManagementActions.ConnectionSet or ExtensionManagementActions.ConnectionRemove or ExtensionManagementActions.ConnectionLogin ?
                "Review and confirm the connection change." : "The extension change requires confirmation." );
        }

        return Action switch
        {
            ExtensionManagementActions.List => new ListPackages( Key ),
            ExtensionManagementActions.Inspect when ArchivePath is not null => new InspectArchive( ArchivePath, Key ),
            ExtensionManagementActions.Inspect => new InspectPackage( RequireKey( ) ),
            ExtensionManagementActions.Install or ExtensionManagementActions.Attach => install!,
            ExtensionManagementActions.Enable or ExtensionManagementActions.Disable => new SetPackageEnabled( Key, Action == ExtensionManagementActions.Enable, ExpectedRevision ),
            ExtensionManagementActions.Configure or ExtensionManagementActions.GlobalSearch or ExtensionManagementActions.Remove or
                ExtensionManagementActions.Rollback or ExtensionManagementActions.Grants or ExtensionManagementActions.EffectsReset => MutationCommand( ),
            ExtensionManagementActions.Connections or ExtensionManagementActions.ConnectionSet or ExtensionManagementActions.ConnectionRemove or ExtensionManagementActions.ConnectionLogin => ConnectionCommand( ),
            ExtensionManagementActions.Debug or ExtensionManagementActions.DebugStop or ExtensionManagementActions.Logs => DevelopmentCommand( ),
            _ => throw new InvalidOperationException( "The extension management request is invalid." )
        };
    }

    private InstallPackage InstallCommand( )
    {
        if ( string.IsNullOrWhiteSpace( ArchivePath ) || Digest is not { Length: 64 } )
        {
            throw new InvalidOperationException( "Inspect the archive first, then install with its reviewed digest." );
        }

        return new InstallPackage( ArchivePath, Digest, Grants?.ToImmutableArray( ), Action == ExtensionManagementActions.Attach, ExpectedRevision, Key );
    }

    private ExtensionManagementCommand MutationCommand( )
    {
        var key = RequireKey( );
        return Action switch
        {
            ExtensionManagementActions.Configure => new ConfigurePackage( key, (Settings ?? []).ToImmutableDictionary( pair => pair.Key, pair => pair.Value.Clone( ), StringComparer.Ordinal ), ExpectedRevision ),
            ExtensionManagementActions.GlobalSearch => new SetPackageGlobalSearch( key, GlobalSearchEnabled, ExpectedRevision ),
            ExtensionManagementActions.Remove => new RemovePackage( key, EraseSettings, ExpectedRevision ),
            ExtensionManagementActions.Rollback => new RollbackPackage( key, Digest ?? "", ExpectedRevision ),
            ExtensionManagementActions.Grants => new SetPackageGrants( key, (Grants ?? []).ToImmutableArray( ), ExpectedRevision ),
            ExtensionManagementActions.EffectsReset => new ResetExtensionEffects( key, IncludeLegacy, ExpectedRevision ),
            _ => throw new InvalidOperationException( "The extension management request is invalid." )
        };
    }

    private ExtensionManagementCommand ConnectionCommand( )
    {
        if ( Action == ExtensionManagementActions.Connections )
        {
            return new ListConnections( Key );
        }
        var key = RequireKey( );
        if ( Action == ExtensionManagementActions.ConnectionSet )
        {
            var setup = Connection;
            if ( setup is null )
            {
                throw new InvalidOperationException( "Connection setup is missing." );
            }

            return new SetConnection( key, setup, ExpectedRevision );
        }
        var name = ConnectionName;
        if ( name is null )
        {
            throw new InvalidOperationException( "Select a named connection." );
        }

        return Action == ExtensionManagementActions.ConnectionRemove ? new RemoveConnection( key, name, RemoteRevoke, ExpectedRevision ) :
            new BeginConnectionLogin( key, name, ExpectedRevision );
    }

    private ExtensionManagementCommand DevelopmentCommand( ) => Action switch
    {
        ExtensionManagementActions.Debug => new ArmExtensionDebug( RequireKey( ), CommandId ?? "", Digest ?? "", DebugSeconds, ExpectedRevision ),
        ExtensionManagementActions.DebugStop => new StopExtensionDebug( RequireKey( ) ),
        ExtensionManagementActions.Logs => new ReadExtensionLogs( RequireKey( ), IncludeStderr ),
        _ => throw new InvalidOperationException( "The extension management request is invalid." )
    };

    private string RequireKey( )
    {
        var key = Key;
        if ( key is not { Length: > 0 } )
        {
            throw new InvalidOperationException( "Select an extension package." );
        }

        return key;
    }

    private void ValidateBounds( )
    {
        if ( ExpectedRevision < 0 || Key?.Length > 128 || ArchivePath?.Length > 4096 || Digest?.Length > 64 || Settings is { Count: > 16 } ||
             Grants is { Length: > 16 } || Grants?.Any( grant => grant is null || grant.Length > 64 ) == true )
        {
            throw new InvalidOperationException( "The extension management request is invalid." );
        }
        if ( CommandId?.Length > 128 || DebugSeconds is < 0 or > 300 )
        {
            throw new InvalidOperationException( "Invalid development lease." );
        }
        if ( ConnectionName?.Length > 128 )
        {
            throw new InvalidOperationException( "Review and confirm the connection change." );
        }
    }
}
