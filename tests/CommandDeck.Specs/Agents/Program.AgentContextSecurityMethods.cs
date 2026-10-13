using System.Text;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AgentContextProtectsConfiguredCredentialDirectoriesAsync( )
    {
        var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
        var fixture = Path.Combine( home, $"commanddeck-credential-boundary-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( fixture );
        var originalStateHome = Environment.GetEnvironmentVariable( "XDG_STATE_HOME" );
        try
        {
            Environment.SetEnvironmentVariable( "XDG_STATE_HOME", Path.Combine( fixture, "xdg-state" ) );
            var explicitState = Path.Combine( fixture, "custom-state" );
            Directory.CreateDirectory( explicitState );
            var alias = Path.Combine( fixture, "state-link" );
            Directory.CreateSymbolicLink( alias, explicitState );

            foreach ( var configuredState in new[] { "$XDG_STATE_HOME/commanddeck", explicitState, alias } )
            {
                AgentOptions options = new( )
                {
                    StateDirectory = configuredState,
                    RuntimeDirectory = Path.Combine( fixture, "runtime" )
                };
                var state = PathExpansion.Expand( configuredState );
                var credential = new CodexCredentialStore( configuredState );
                credential.Write( Encoding.UTF8.GetBytes( "{\"OPENAI_API_KEY\":\"synthetic-boundary-sentinel\"}" ) );
                var resolvedState = Directory.ResolveLinkTarget( state, returnFinalTarget: true )?.FullName ?? state;
                var targets = new List<string> { Path.Combine( resolvedState, "codex", "auth.json" ) };
                foreach ( var profile in CliAgentProfile.All )
                {
                    var ownedHome = new CliAgentHome( profile, configuredState, options.RuntimeDirectory );
                    ownedHome.Ensure( );
                    var probe = Path.Combine( resolvedState, profile.ShortName, "home", "account.txt" );
                    await File.WriteAllTextAsync( probe, "synthetic-boundary-sentinel" );
                    targets.Add( probe );
                }
                Directory.CreateDirectory( options.RuntimeDirectory );
                var runtimeCredential = Path.Combine( options.RuntimeDirectory, "auth.json" );
                await File.WriteAllTextAsync( runtimeCredential, "synthetic-boundary-sentinel" );
                targets.Add( runtimeCredential );

                ScriptedAgentBackend backend = new( );
                backend.Script = async ( input, host, token ) =>
                {
                    var result = await host.OnToolCallAsync( new AgentToolCall
                    {
                        CallId = "credential-probe",
                        Name = AgentToolNames.ReadFile,
                        ArgumentsJson = JsonSerializer.Serialize( new { path = input.Text } )
                    }, token );
                    await host.OnTextAsync( result.Success ? "allowed" : "denied", token );
                };
                using CancellationTokenSource serverCancellation = new( );
                (var runtime, var _, var root, var _) = await StartAgentRuntimeAsync(
                    backend, serverCancellation.Token, agentOptions: options );
                try
                {
                    await using ( runtime )
                    {
                        await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                        var challenge = await runtime.CreateAgentTrustChallengeAsync( "owner", AgentTrustLevels.ReadEntireHome, default );
                        await runtime.ApplyAgentTrustAsync( "owner", challenge.Id, AgentTrustLevels.ReadEntireHome, null, null, default );

                        foreach ( var target in targets )
                        {
                            await runtime.StartAgentTurnAsync( target, null, null, null, "owner", default );
                            True( await WaitUntilAsync( ( ) => Task.FromResult(
                                runtime.GetAgentConversation( "owner" ).Pending is not null ||
                                runtime.GetAgentConversation( "owner" ).Summary?.State == AgentConversationStates.Completed ),
                                TimeSpan.FromSeconds( 5 ) ) );
                            var pending = runtime.GetAgentConversation( "owner" ).Pending;
                            True( pending is not null, "configured credentials must require a protected-path decision" );
                            True( pending!.Context!.ProtectedTarget && pending.Context.ProposedFolder is null );
                            True( !pending.Decisions.Contains( AgentRequestDecisions.AllowFolder ) );
                            var decision = target == targets[0] ? AgentRequestDecisions.Allow : AgentRequestDecisions.Deny;
                            await runtime.ResolveAgentPendingAsync( pending.Id, decision, null, "owner", default );
                            True( await WaitUntilAsync( ( ) => Task.FromResult(
                                runtime.GetAgentConversation( ).Summary?.State == AgentConversationStates.Completed ), TimeSpan.FromSeconds( 5 ) ) );
                            Equal( decision == AgentRequestDecisions.Allow ? "allowed" : "denied", runtime.GetAgentConversation( ).Entries[^1].Text );
                        }

                        var neighbor = resolvedState + "-notes";
                        Directory.CreateDirectory( neighbor );
                        var note = Path.Combine( neighbor, "note.txt" );
                        await File.WriteAllTextAsync( note, "ordinary context" );
                        await runtime.StartAgentTurnAsync( note, null, null, null, "owner", default );
                        True( await WaitUntilAsync( ( ) => Task.FromResult(
                            runtime.GetAgentConversation( ).Summary?.State == AgentConversationStates.Completed ), TimeSpan.FromSeconds( 5 ) ) );
                        Equal( "allowed", runtime.GetAgentConversation( ).Entries[^1].Text );
                    }
                }
                finally
                {
                    serverCancellation.Cancel( );
                    Directory.Delete( root, recursive: true );
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable( "XDG_STATE_HOME", originalStateHome );
            Directory.Delete( fixture, recursive: true );
        }
    }

    private static async Task AgentContextHidesConfiguredCredentialsAsync( )
    {
        var home = Path.Combine( Path.GetTempPath( ), $"commanddeck-context-exclusions-{Guid.NewGuid( ):N}" );
        var actual = Path.Combine( home, "actual" );
        var alias = Path.Combine( home, "alias" );
        Directory.CreateDirectory( actual );
        Directory.CreateSymbolicLink( alias, actual );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( home, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            AgentOptions options = new( )
            {
                StateDirectory = Path.Combine( alias, "state" ) + "/",
                RuntimeDirectory = Path.Combine( home, "runtime" )
            };
            AgentContextBroker broker = new( new AgentAuthority( catalog.AgentPreferences ), home, options );
            var credentials = new CodexCredentialStore( options.StateDirectory );
            credentials.Write( Encoding.UTF8.GetBytes( "{\"OPENAI_API_KEY\":\"synthetic-secret-match\"}" ) );
            var protectedState = Path.Combine( actual, "state" );
            var note = Path.Combine( actual, "ordinary.txt" );
            await File.WriteAllTextAsync( note, "ordinary match" );
            var fileAlias = Path.Combine( actual, "credential-link.txt" );
            File.CreateSymbolicLink( fileAlias, Path.Combine( protectedState, "codex", "auth.json" ) );

            await broker.GrantFolderAsync( actual, default );
            AgentPreferences preferences = new( )
            {
                TrustLevel = AgentTrustLevels.ApprovedContext,
                ContextPolicy = AgentContextPolicies.ApprovedFolders
            };
            foreach ( var target in new[] { Path.Combine( protectedState, "codex", "auth.json" ), fileAlias } )
            {
                var authorization = await broker.AuthorizeAsync( target, AgentContextOperations.ReadFile, preferences, default );
                True( !authorization.Allowed && authorization.RequiresDecision && authorization.Protected,
                    "a parent grant cannot override configured credential protection" );
            }
            True( !broker.ListDirectory( actual, 32 ).Any( entry => entry.Name == "state" ) );
            var matches = broker.SearchText( actual, "match", 32, 4096, default );
            Equal( 1, matches.Count );
            Equal( "ordinary match", matches[0].Text );
            True( (await broker.AuthorizeAsync( note, AgentContextOperations.ReadFile, preferences, default )).Allowed );
            await ThrowsAsync<InvalidOperationException>( ( ) => broker.GrantFolderAsync( protectedState, default ).AsTask( ) );
        }
        finally
        {
            Directory.Delete( home, recursive: true );
        }
    }

    private static async Task AgentTextSearchesShareTheContextBudgetAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-context-budget-{Guid.NewGuid( ):N}" );
        var searchDirectory = Path.Combine( root, "notes" );
        Directory.CreateDirectory( searchDirectory );
        var file = Path.Combine( root, "large.txt" );
        await File.WriteAllTextAsync( file, new string( 'x', AgentLimits.MaximumFileBytes ) );
        var searchLine = new string( '界', 240 );
        await File.WriteAllTextAsync( Path.Combine( searchDirectory, "matches.txt" ),
            string.Join( '\n', Enumerable.Repeat( searchLine, AgentLimits.MaximumSearchMatches ) ) );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences
            {
                Enabled = true,
                TrustLevel = AgentTrustLevels.ReadEntireHome
            }, default );
            ScriptedAgentBackend backend = new( );
            List<AgentToolResult> results = [];
            backend.Script = async ( input, host, token ) =>
            {
                async ValueTask<AgentToolResult> Call( string name, string path ) => await host.OnToolCallAsync( new AgentToolCall
                {
                    CallId = Guid.NewGuid( ).ToString( "N" ),
                    Name = name,
                    ArgumentsJson = JsonSerializer.Serialize( new { path, query = "界" } )
                }, token );

                if ( input.Text == "read then search" )
                {
                    for ( var index = 0; index < AgentLimits.MaximumContextBytesPerTurn / AgentLimits.MaximumFileBytes; index++ )
                    {
                        results.Add( await Call( AgentToolNames.ReadFile, file ) );
                    }
                    results.Add( await Call( AgentToolNames.SearchText, searchDirectory ) );
                }
                else if ( input.Text == "search then read" )
                {
                    results.Add( await Call( AgentToolNames.SearchText, searchDirectory ) );
                    for ( var index = 0; index < AgentLimits.MaximumContextBytesPerTurn / AgentLimits.MaximumFileBytes; index++ )
                    {
                        results.Add( await Call( AgentToolNames.ReadFile, file ) );
                    }
                }
                else if ( input.Text == "parallel searches" )
                {
                    results.AddRange( await Task.WhenAll( Enumerable.Range( 0, 16 ).Select(
                        _ => Task.Run( async ( ) => await Call( AgentToolNames.SearchText, searchDirectory ), token ) ) ) );
                }
                else
                {
                    for ( var index = 0; index < 16; index++ )
                    {
                        results.Add( await Call( AgentToolNames.SearchText, searchDirectory ) );
                    }
                }
                await host.OnTextAsync( "finished", token );
            };
            await using var coordinator = CreateAgentCoordinator( catalog, backend, root, TimeProvider.System );
            foreach ( var turn in new[] { "read then search", "search then read", "repeat searches", "parallel searches" } )
            {
                results.Clear( );
                await coordinator.StartTurnAsync( turn, null, null, null, default );
                True( await WaitUntilAsync( ( ) => Task.FromResult(
                    coordinator.Snapshot( ).Summary?.State == AgentConversationStates.Completed ), TimeSpan.FromSeconds( 5 ) ) );
                True( results.Any( result => result.Success ), "each new turn starts with a fresh context budget" );
                True( results.Any( result => !result.Success ), "file reads and searches must share the per-turn byte limit" );
                True( results.Where( result => !result.Success ).All( result =>
                    result.ContentText.Contains( "context budget", StringComparison.OrdinalIgnoreCase ) ) );
                if ( turn is "repeat searches" or "parallel searches" )
                {
                    var returnedBytes = results.Where( result => result.Success ).Sum( result => Encoding.UTF8.GetByteCount( result.ContentText ) );
                    True( returnedBytes <= AgentLimits.MaximumContextBytesPerTurn );
                }
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
