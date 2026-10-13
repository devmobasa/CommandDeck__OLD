using System.Text;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionManagementSharesConnectionsWithSessionsAsync( )
    {
        await using var harness = await ExtensionSessionHarness.CreateAsync( new ReadConfiguredCommand( ), [ExtensionPermissionIds.FilesRead] );
        var notes = Path.Combine( harness.Root, "notes" );
        Directory.CreateDirectory( notes );
        File.WriteAllText( Path.Combine( notes, "one.txt" ), "Configured through management" );
        await harness.Management.HandleAsync( new SetConnection( harness.Installation.Key,
            new( ) { Name = "notes", Path = notes }, harness.Registry.Document.Revision ), CancellationToken.None );
        await harness.OpenAsync( );
        Equal( "Configured through management", harness.LatestView.Content!.Value.GetProperty( "body" ).GetString( ) );
    }

    private sealed class ReadConfiguredCommand : IExtensionCommand
    {
        public string Id => "read";
        public async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            var listing = await context.Brokers.ListAsync( "notes", cancellationToken: cancellationToken );
            var file = await context.Brokers.ReadAsync( "notes", listing.Entries.Single( ).Reference, cancellationToken );
            await context.Views.ReplaceAsync( new DetailView { Id = "notes", Title = "Notes", Body = Encoding.UTF8.GetString( file.Bytes.Span ) }, cancellationToken );
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    }

    private sealed class DelayedExtensionSecrets : IExtensionSecretResolver
    {
        public TaskCompletionSource Entered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public async ValueTask<AiCredentialLease?> ResolveAsync( string name, CancellationToken cancellationToken )
        {
            Entered.TrySetResult( );
            await Release.Task.WaitAsync( cancellationToken );
            return new( Encoding.UTF8.GetBytes( "synthetic-test-credential" ) );
        }
    }

    private static async Task ExtensionConnectionPreparationDoesNotHoldRegistryLeaseAsync( )
    {
        DelayedExtensionSecrets secrets = new( );
        await using var harness = await ExtensionSessionHarness.CreateAsync( new SessionViewCommand( ),
            [ExtensionPermissionIds.HttpRequest, ExtensionPermissionIds.SecretsUse], secrets: secrets );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        var revision = harness.Registry.Document.Revision;
        var preparing = harness.Management.HandleAsync( new SetConnection( harness.Installation.Key, new( )
        {
            Name = "service",
            Kind = ExtensionConnectionKinds.Http,
            Origin = "https://example.com",
            CredentialKind = ExtensionCredentialKinds.SystemdBearer,
            Credential = "test-secret"
        }, revision ), deadline.Token ).AsTask( );
        await secrets.Entered.Task.WaitAsync( deadline.Token );
        try
        {
            await WriteWhilePendingAsync( harness, preparing );
            Equal( revision, harness.Registry.Document.Revision );
        }
        finally { secrets.Release.TrySetResult( ); }
        var result = await preparing;
        Equal( "service", result.Connections.Single( ).Name );
    }

    private static async Task ExtensionBrowserLaunchDoesNotHoldRegistryLeaseAsync( )
    {
        await using var harness = await ExtensionSessionHarness.CreateAsync( new SessionViewCommand( ),
            [ExtensionPermissionIds.HttpRequest, ExtensionPermissionIds.SecretsUse] );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        var setup = new ExtensionConnectionSetup
        {
            Name = "oauth",
            Kind = ExtensionConnectionKinds.Http,
            Origin = "http://127.0.0.1:9",
            PrivateNetwork = true,
            CredentialKind = ExtensionCredentialKinds.OAuth,
            OAuth = new( "http://127.0.0.1:9", "http://127.0.0.1:9/auth", "http://127.0.0.1:9/token", "test", "read" )
        };
        await harness.Management.HandleAsync( new SetConnection( harness.Installation.Key, setup, harness.Registry.Document.Revision ), deadline.Token );
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        harness.Effects.BeforeExecute = async token => { entered.TrySetResult( ); await release.Task.WaitAsync( token ); };
        var browser = harness.Management.HandleAsync( new BeginConnectionLogin( harness.Installation.Key, "oauth", harness.Registry.Document.Revision ), deadline.Token ).AsTask( );
        await entered.Task.WaitAsync( deadline.Token );
        try
        { await WriteWhilePendingAsync( harness, browser ); }
        finally { release.TrySetResult( ); }
        await browser;
        Equal( ActionKinds.OpenUrl, harness.Effects.Actions.Single( ).Kind );
    }

    private static async Task WriteWhilePendingAsync( ExtensionSessionHarness harness, Task external )
    {
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 1 ) );
        var result = await harness.Registry.WithExecutionAsync( harness.Installation.Key, current => harness.Registry.AccessDataAsync( current,
            new( "s", 1, 1, "i", ExtensionDataOperations.Write, "draft", 0, JsonElement.Parse( "\"Independent\"" ) ), deadline.Token ), deadline.Token );
        Equal( 1L, result.Revision );
        True( !external.IsCompleted );
    }

    private static void ExtensionManagementCommandsValidateConfirmationAndSnapshotInputs( )
    {
        try
        {
            new ExtensionManagementRequest { Action = ExtensionManagementActions.Install, ArchivePath = "package.cdext" }.ToCommand( );
            throw new InvalidOperationException( "An uninspected archive was accepted." );
        }
        catch ( InvalidOperationException exception )
        {
            True( exception.Message.StartsWith( "Inspect the archive first", StringComparison.Ordinal ) );
        }

        string[] protectedActions = [ExtensionManagementActions.Install, ExtensionManagementActions.Attach, ExtensionManagementActions.Debug,
            ExtensionManagementActions.Remove, ExtensionManagementActions.Rollback, ExtensionManagementActions.Grants,
            ExtensionManagementActions.GlobalSearch, ExtensionManagementActions.EffectsReset, ExtensionManagementActions.ConnectionSet,
            ExtensionManagementActions.ConnectionRemove, ExtensionManagementActions.ConnectionLogin];
        foreach ( var action in protectedActions )
        {
            True( ExtensionManagementPolicy.RequiresConfirmation( action ) );
            Throws<InvalidOperationException>( ( ) => new ExtensionManagementRequest { Action = action, Key = "package" }.ToCommand( ) );
        }
        Throws<InvalidOperationException>( ( ) => new ExtensionManagementRequest { Action = "unknown" }.ToCommand( ) );
        Throws<InvalidOperationException>( ( ) => new ExtensionManagementRequest { Action = ExtensionManagementActions.Remove, Confirmed = true }.ToCommand( ) );
        Throws<InvalidOperationException>( ( ) => new ExtensionManagementRequest { ExpectedRevision = -1 }.ToCommand( ) );
        string[] grants = [ExtensionPermissionIds.StorageRead];
        var command = ( SetPackageGrants ) new ExtensionManagementRequest
        {
            Action = ExtensionManagementActions.Grants,
            Key = "package",
            Confirmed = true,
            Grants = grants,
            ExpectedRevision = 12
        }.ToCommand( );
        grants[0] = "mutated";
        Equal( ExtensionPermissionIds.StorageRead, command.Grants.Single( ) );
        Equal( 12L, command.ExpectedRevision );
    }
}
