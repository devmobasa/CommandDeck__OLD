using System.Collections.Immutable;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class InProcessExtensionLauncher( Func<string, IReadOnlyList<IExtensionCommand>> commands, TextWriter? diagnostics = null ) : IExtensionLauncher
    {
        public async ValueTask<IExtensionProcess> StartAsync( ExtensionLaunchRequest request, CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var pipes = await ExtensionTestPipes.CreateAsync( );
            return new InProcessExtensionProcess( pipes, commands( request.InstallationKey ), diagnostics ?? TextWriter.Null );
        }
    }

    private sealed class InProcessExtensionProcess : IExtensionProcess
    {
        private readonly ExtensionTestPipes _pipes;
        private readonly CancellationTokenSource _lifetime = new( );
        private readonly Task _run;

        public InProcessExtensionProcess( ExtensionTestPipes pipes, IReadOnlyList<IExtensionCommand> commands, TextWriter diagnostics )
        {
            _pipes = pipes;
            _run = ExtensionApp.RunAsync( pipes.Child, pipes.Child, commands, diagnostics, _lifetime.Token );
        }

        public Stream Input => _pipes.Host;
        public Stream Output => _pipes.Host;
        public string ReadDiagnostics( ) => "";

        public async ValueTask DisposeAsync( )
        {
            await _lifetime.CancelAsync( );
            await _pipes.DisposeAsync( );
            try
            {
                await _run.WaitAsync( TimeSpan.FromSeconds( 2 ) );
            }
            catch ( OperationCanceledException ) when ( _lifetime.IsCancellationRequested ) { }
            catch ( IOException ) when ( _lifetime.IsCancellationRequested ) { }
            finally
            {
                _lifetime.Dispose( );
            }
        }
    }

    private sealed class RecordingExtensionActionExecutor : IActionExecutor
    {
        public List<CommandAction> Actions { get; } = [];
        public Func<CancellationToken, Task>? BeforeExecute { get; set; }

        public async ValueTask<ActionExecutionResult> ExecuteAsync(
            SearchResult result, CommandAction action, string query, CancellationToken cancellationToken )
        {
            if ( BeforeExecute is not null )
            {
                await BeforeExecute( cancellationToken );
            }
            Actions.Add( action );
            return new ActionExecutionResult { Succeeded = true };
        }
    }

    private sealed class ExtensionSessionHarness : IAsyncDisposable
    {
        private readonly string _root;
        private readonly ExtensionConnections _connections;
        private readonly ExtensionDevelopment _development;
        private readonly object _gate = new( );
        private readonly List<ExtensionSessionState> _states = [];
        private long _sequence;

        private ExtensionSessionHarness( string root, SqliteCatalog catalog, ExtensionPackageRegistry registry,
            ExtensionSessionService sessions, ExtensionInstallation installation, RecordingExtensionActionExecutor effects,
            ExtensionConnections connections, ExtensionDevelopment development )
        {
            _root = root;
            Catalog = catalog;
            Registry = registry;
            Sessions = sessions;
            Installation = installation;
            Effects = effects;
            _connections = connections;
            _development = development;
            Management = new( registry, sessions, connections, development, connections.Journal, effects );
        }

        public ExtensionManagementService Management { get; }
        public SqliteCatalog Catalog { get; }
        public ExtensionPackageRegistry Registry { get; }
        public ExtensionSessionService Sessions { get; }
        public ExtensionInstallation Installation { get; }
        public RecordingExtensionActionExecutor Effects { get; }
        public string Root => _root;
        public ExtensionSessionState Latest => States.Last( );
        public ExtensionSessionState LatestView => States.Last( state => state.Content is not null );
        public Func<ExtensionSessionState, CancellationToken, ValueTask>? OnPublish { get; set; }
        public ExtensionSessionState[] States
        {
            get
            {
                lock ( _gate )
                {
                    return _states.ToArray( );
                }
            }
        }

        public static async Task<ExtensionSessionHarness> CreateAsync( IExtensionCommand command, string[]? grants = null, ExtensionManifest? manifest = null, TextWriter? diagnostics = null, IExtensionSecretResolver? secrets = null )
        {
            var root = CreateCliSpecRoot( );
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            var registry = await ExtensionPackageRegistry.OpenAsync( catalog, Path.Combine( root, "artifacts" ), CancellationToken.None );
            catalog.Contributions = registry;
            var package = manifest ?? PackageFixture( ) with
            {
                Commands = [new( ) { Id = command.Id, Title = "Session command", Mode = command.Mode, Capabilities = (grants ?? []).ToImmutableArray( ) }],
                Capabilities = (grants ?? []).ToDictionary( grant => grant, _ => new CapabilityRequest( "Session test" ), StringComparer.Ordinal )
            };
            await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( root, package ), registry.ArtifactRoot, CancellationToken.None );
            var installed = await registry.InstallAsync( staged, "local", registry.Document.Revision, true, grants ?? [], true, CancellationToken.None );
            await registry.SetEnabledAsync( null, true, registry.Document.Revision, CancellationToken.None );
            RecordingExtensionActionExecutor effects = new( );
            ExtensionConnections connections = new( Path.Combine( registry.ArtifactRoot, "effect-journal" ), secrets ?? new ExtensionSecretResolver( ) );
            ExtensionDevelopment development = new( );
            ExtensionSessionService sessions = new( registry, new InProcessExtensionLauncher( _ => [command], diagnostics ), effects, development, connections );
            return new( root, catalog, registry, sessions, registry.Find( installed.Key ), effects, connections, development );
        }

        public ValueTask OpenAsync( string[]? features = null, CancellationToken cancellationToken = default ) => Sessions.OpenAsync(
            "owner", ExtensionStableIds.CommandId( Installation.Key, Installation.Manifest.Commands[0].Id ),
            features ?? ExtensionSessionService.Features, PublishAsync, cancellationToken );

        private ValueTask PublishAsync( ExtensionSessionState state, CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            lock ( _gate )
            {
                _states.Add( state );
            }
            return OnPublish?.Invoke( state, cancellationToken ) ?? ValueTask.CompletedTask;
        }

        public ExtensionSessionState PresentLatest( )
        {
            var view = LatestView;
            Sessions.Present( "owner", view.SessionId, new( ) { Sequence = ++_sequence, Revision = view.Revision } );
            return view;
        }

        public ValueTask ActAsync( string title, CancellationToken cancellationToken = default )
        {
            var view = PresentLatest( );
            var action = view.Content!.Value.GetProperty( "actions" ).EnumerateArray( ).Single( action => action.GetProperty( "title" ).GetString( ) == title );
            return Sessions.ActAsync( "owner", view.SessionId, new( )
            {
                Sequence = ++_sequence,
                Revision = view.Revision,
                Handle = action.GetProperty( "handle" ).GetString( )
            }, cancellationToken );
        }

        public ValueTask SubmitAsync( long formRevision, Dictionary<string, JsonElement> values, CancellationToken cancellationToken = default )
        {
            var view = PresentLatest( );
            return Sessions.ActAsync( "owner", view.SessionId, new( )
            {
                Sequence = ++_sequence,
                Revision = view.Revision,
                Handle = view.Content!.Value.GetProperty( "submit" ).GetProperty( "handle" ).GetString( ),
                FormRevision = formRevision,
                Values = values
            }, cancellationToken );
        }

        public ValueTask DecideAsync( ExtensionSessionState state, bool confirmed, CancellationToken cancellationToken ) =>
            Sessions.ActAsync( "owner", state.SessionId, new( )
            {
                Sequence = ++_sequence,
                Revision = state.Revision,
                ConfirmationId = state.Confirmation!.Id,
                Confirmed = confirmed
            }, cancellationToken );

        public async ValueTask DisposeAsync( )
        {
            await Sessions.DisposeAsync( );
            _connections.Dispose( );
            _development.Dispose( );
            Registry.Dispose( );
            await Catalog.DisposeAsync( );
            Directory.Delete( _root, true );
        }
    }

    private sealed class SessionViewCommand : IExtensionCommand
    {
        public string Id => "session";
        public Func<CommandContext, CommandEvent, CancellationToken, ValueTask>? Handler { get; init; }
        public ImmutableArray<ViewAction> Actions { get; init; } = [ViewAction.Callback( "update", "Update" )];
        public List<CommandEvent> Events { get; } = [];

        public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken ) => PublishAsync( context, "Initial", cancellationToken );

        public async ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken )
        {
            Events.Add( commandEvent );
            if ( Handler is not null )
            {
                await Handler( context, commandEvent, cancellationToken );
                return;
            }
            await PublishAsync( context, "Updated", cancellationToken );
        }

        private ValueTask PublishAsync( CommandContext context, string title, CancellationToken token ) =>
            context.Views.ReplaceAsync( new DetailView
            {
                Id = "session",
                Title = title,
                Body = title,
                Actions = Actions
            }, token );
    }
}
