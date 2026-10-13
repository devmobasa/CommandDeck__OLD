using System.Collections.Immutable;
using CommandDeck.Extensions.Views;

namespace CommandDeck;

internal abstract record ConnectionSetupModel( string Name )
{
    public abstract ExtensionConnectionSetup ToWire( );
}
internal sealed record FileConnectionModel( string Name, string Path ) : ConnectionSetupModel( Name )
{
    public override ExtensionConnectionSetup ToWire( ) => new( ) { Name = Name, Kind = ExtensionConnectionKinds.Files, Path = Path };
}
internal sealed record HttpConnectionModel( string Name, Uri Origin, string Route, ImmutableArray<string> Methods,
    bool PrivateNetwork, string CredentialKind ) : ConnectionSetupModel( Name )
{
    public override ExtensionConnectionSetup ToWire( ) => new( )
    {
        Name = Name,
        Kind = ExtensionConnectionKinds.Http,
        Origin = Origin.GetLeftPart( UriPartial.Authority ),
        Route = Route,
        Methods = Methods.ToArray( ),
        PrivateNetwork = PrivateNetwork,
        CredentialKind = CredentialKind
    };
}
internal abstract record CredentialModel;
internal sealed record NoCredentialModel : CredentialModel;
internal sealed record SessionBearerModel( string Token ) : CredentialModel;
internal sealed record SystemdBearerModel( string Name ) : CredentialModel;
internal sealed record OAuthCredentialModel( ExtensionOAuthSetup Setup ) : CredentialModel;
internal sealed record ConnectionPreparation( ConnectionSetupModel Policy, CredentialModel Credential );

internal static class ConnectionSetupConversion
{
    public static ConnectionPreparation ToModel( ExtensionConnectionSetup setup )
    {
        ViewValidation.ValidateId( setup.Name );
        if ( setup.Kind == ExtensionConnectionKinds.Files )
        {
            if ( setup.Path is null || setup.CredentialKind != ExtensionCredentialKinds.None )
            {
                throw new InvalidOperationException( "Select a file path without credentials." );
            }

            return new( new FileConnectionModel( setup.Name, setup.Path ), new NoCredentialModel( ) );
        }

        if ( setup.Kind != ExtensionConnectionKinds.Http )
        {
            throw new InvalidOperationException( "Select a file root or HTTP connection." );
        }

        var origin = ExtensionHttpTransport.Origin( setup.Origin ?? "", setup.PrivateNetwork );
        var route = ExtensionHttpTransport.PathValue( setup.Route );
        if ( route.Contains( '?' ) || setup.Methods is not { Length: > 0 and <= 5 } ||
            setup.Methods.Any( method => method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE") ) )
        {
            throw new InvalidOperationException( "Select supported methods and a route scope without a query." );
        }

        var credential = Credential( setup );
        return new( new HttpConnectionModel( setup.Name, origin, route, setup.Methods.ToImmutableArray( ), setup.PrivateNetwork, setup.CredentialKind ), credential );
    }

    private static CredentialModel Credential( ExtensionConnectionSetup setup )
    {
        if ( setup.CredentialKind == ExtensionCredentialKinds.OAuth )
        {
            var oauth = setup.OAuth;
            if ( oauth is null )
            {
                throw new InvalidOperationException( "Configure OAuth first." );
            }

            return new OAuthCredentialModel( oauth );
        }

        return setup.CredentialKind switch
        {
            ExtensionCredentialKinds.None => new NoCredentialModel( ),
            ExtensionCredentialKinds.SessionBearer => new SessionBearerModel( setup.Credential ?? "" ),
            ExtensionCredentialKinds.SystemdBearer => new SystemdBearerModel( setup.Credential ?? "" ),
            _ => throw new InvalidOperationException( "Unsupported credential mechanism." )
        };
    }
}
