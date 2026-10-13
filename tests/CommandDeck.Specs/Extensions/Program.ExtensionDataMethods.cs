using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionFormAdmissionValidatesAndFencesSubmissions( )
    {
        ExtensionViewAuthority authority = new( "session", 1, 1 );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        var form = new FormView { Id = "draft", Title = "Draft", Fields = [FormField.Text( "title", "Title", true, 8 )], Submit = ViewAction.Callback( "save", "Save" ) };
        var first = authority.Replace( new ViewUpdate( "session", 1, form.Id, 0, 1, form ) );
        authority.Present( 1, first.Revision );
        var handle = first.Content.GetProperty( "submit" ).GetProperty( "handle" ).GetString( )!;
        Throws<InvalidDataException>( ( ) => authority.AdmitAction( 2, 1, handle, null, 1, new( ) { ["title"] = JsonElement.Parse( "\"\"" ) } ) );
        Throws<InvalidDataException>( ( ) => authority.AdmitAction( 3, 1, handle, null, 1, new( ) { ["unexpected"] = JsonElement.Parse( "true" ) } ) );
        Dictionary<string, JsonElement> values = new( )
        {
            ["title"] = JsonElement.Parse( "\"First\"" )
        };
        var accepted = authority.AdmitAction( 4, 1, handle, null, 1, values );
        values["title"] = JsonElement.Parse( "\"Changed\"" );
        Equal( "First", accepted.Submission!.Values["title"].GetString( ) );
        True( authority.BeginAction( accepted, 1, new HashSet<string>( ) ) is SubmitBinding );
        Throws<InvalidOperationException>( ( ) => authority.AdmitAction( 5, 1, handle, null, 1, values ) );
        var next = authority.Replace( new ViewUpdate( "session", 1, form.Id, 1, 1, form ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitAction( 6, 1, handle, null, 2, values ) );
        authority.Present( 7, next.Revision );
        var fresh = next.Content.GetProperty( "submit" ).GetProperty( "handle" ).GetString( )!;
        Equal( "Changed", authority.AdmitAction( 8, 2, fresh, null, 1, values ).Submission!.Values["title"].GetString( ) );
    }

    private static void ExtensionDataAuthorityRejectsForeignAndRetiredRequests( )
    {
        ExtensionViewAuthority authority = new( "session", 1, 3 );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        HashSet<string> grants = ["storage.read", "storage.write"];
        var request = new CommandDataRequest( "session", 1, 1, invocation.Id, "read" );
        authority.AuthorizeData( request, 3, grants );
        foreach ( var invalid in new[] { request with { SessionId = "sibling" }, request with { ExecutionEpoch = 2 }, request with { Generation = 2 }, request with { InvocationId = "foreign" } } )
        {
            Throws<InvalidOperationException>( ( ) => authority.AuthorizeData( invalid, 3, grants ) );
        }
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeData( request, 4, grants ) );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeData( request, 3, new HashSet<string>( ) ) );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeData( request with { Operation = "write" }, 3, new HashSet<string> { "storage.read" } ) );
        authority.CompleteInvocation( invocation );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeData( request, 3, grants ) );
        var nextInvocation = authority.BeginInvocation( InvocationOrigin.UserAction );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeData( request, 3, grants ) );
        authority.Revoke( );
        Throws<InvalidOperationException>( ( ) => authority.AuthorizeData( request with { Generation = 2 }, 3, grants ) );
    }

    private static async Task ExtensionDataPersistsAndRejectsStaleWritesAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-data-spec-" ).FullName;
        try
        {
            var storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) };
            var artifacts = Path.Combine( root, "artifacts" );
            string key;
            await using ( SqliteCatalog catalog = new( storage ) )
            {
                using ( var registry = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None ) )
                {
                    catalog.Contributions = registry;
                    var manifest = PackageFixture( ) with
                    {
                        Preferences = [new( ) { Id = "prefix", Title = "Default title", Type = "text", Required = true, MaximumLength = 20, Default = JsonElement.Parse( "\"Draft\"" ) }]
                    };
                    await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( root, manifest ), artifacts, CancellationToken.None );
                    var installed = await registry.InstallAsync( staged, "local", 0, true, [], false, CancellationToken.None );
                    key = installed.Key;
                    await registry.SetEnabledAsync( null, true, registry.Document.Revision, CancellationToken.None );
                    var initialGrant = installed.GrantRevision;
                    await registry.ConfigureAsync( key, new( )
                    {
                        ["prefix"] = JsonElement.Parse( "\"Personal\"" )
                    }, registry.Document.Revision, CancellationToken.None );
                    True( registry.Find( key ).GrantRevision > initialGrant );
                    await ThrowsAsync<InvalidDataException>( async ( ) => await registry.ConfigureAsync( key, new( ) { ["prefix"] = JsonElement.Parse( "false" ) }, registry.Document.Revision, CancellationToken.None ) );
                    await ThrowsAsync<InvalidOperationException>( async ( ) => await registry.ConfigureAsync( key, [], 0, CancellationToken.None ) );
                    var request = new CommandDataRequest( "s", 1, 1, "i", "write", "draft", 0, JsonElement.Parse( "\"Saved text\"" ) );
                    var registryRevision = registry.Document.Revision;
                    var snapshot = registry.Snapshot;
                    var sourcePreferences = await catalog.GetPreferencesAsync( CancellationToken.None );
                    var write = await registry.WithExecutionAsync( key, current => registry.AccessDataAsync( current, request, CancellationToken.None ), CancellationToken.None );
                    Equal( 1L, write.Revision );
                    Equal( 0, write.Values.Count ); // Write permission does not disclose other values.
                    Equal( registryRevision, registry.Document.Revision );
                    True( ReferenceEquals( snapshot, registry.Snapshot ) );
                    await catalog.PatchSourcePreferencesAsync( new( )
                    {
                        RegistryRevision = registryRevision,
                        PreferenceRevision = sourcePreferences.PreferenceRevision,
                        Changes = [new( ) { SourceId = ExtensionStableIds.SourceId( key ), EnabledInSearch = false }]
                    }, CancellationToken.None );
                    await ThrowsAsync<InvalidOperationException>( async ( ) => await registry.WithExecutionAsync( key, current => registry.AccessDataAsync( current, request, CancellationToken.None ), CancellationToken.None ) );
                    var huge = JsonSerializer.SerializeToElement( new string( 'x', 16384 ) );
                    await ThrowsAsync<ExtensionApplicationException>( async ( ) => await registry.WithExecutionAsync( key, current => registry.AccessDataAsync( current, request with { ExpectedRevision = 1, Value = huge }, CancellationToken.None ), CancellationToken.None ) );
                    await registry.SetEnabledAsync( key, false, registryRevision, CancellationToken.None );
                    await registry.SetEnabledAsync( key, true, registry.Document.Revision, CancellationToken.None );
                    await using var sibling = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( ) with
                    {
                        Id = "org.example.sibling"
                    } ), artifacts, CancellationToken.None );
                    var other = await registry.InstallAsync( sibling, "local", registry.Document.Revision, true, [], false, CancellationToken.None );
                    var empty = await registry.WithExecutionAsync( other.Key, current => registry.AccessDataAsync( current, request with { Operation = "read" }, CancellationToken.None ), CancellationToken.None );
                    Equal( 0, empty.Values.Count );
                }
            }
            await using ( SqliteCatalog catalog = new( storage ) )
            {
                using ( var registry = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None ) )
                {
                    catalog.Contributions = registry;
                    Equal( "Personal", ExtensionSettingsResolver.Resolve( registry.Find( key ) )["prefix"].GetString( ) );
                    Equal( "Saved text", registry.DataStore.Read( key ).Values["draft"].GetString( ) );
                    await registry.RemoveAsync( key, false, registry.Document.Revision, CancellationToken.None );
                    Equal( "Saved text", registry.DataStore.Read( key ).Values["draft"].GetString( ) );
                    await registry.RemoveAsync( key, true, registry.Document.Revision, CancellationToken.None );
                    Equal( 0, registry.DataStore.Read( key ).Values.Count );
                    Equal( 0, registry.Find( key ).Settings.Count );
                }
            }
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
