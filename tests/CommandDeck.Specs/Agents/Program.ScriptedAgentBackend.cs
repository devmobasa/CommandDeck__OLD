namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class ScriptedAgentBackend : IAgentBackend
    {
        public string Id => AgentBackendIds.Codex;
        public bool Authenticated { get; set; } = true;
        public int Sessions { get; private set; }
        public bool Stopped { get; private set; }
        // Mirrors the real backend: the process is running once a session has
        // been started and stays warm when a session is disposed; only StopAsync
        // tears it down. StartSessionAsync re-arms it after a stop.
        public bool IsRunning { get; private set; }
        public int StopCount { get; private set; }
        public AgentSessionOptions? LastOptions { get; private set; }
        public Func<AgentTurnInput, IAgentTurnHost, CancellationToken, ValueTask>? Script { get; set; }

        public AgentBackendDescriptor Describe( ) => new( )
        {
            Id = Id,
            DisplayName = "Scripted",
            Kind = AgentBackendKinds.CodexAppServer,
            ProcessLocality = AiLocality.Local,
            InferenceLocality = AiLocality.Remote,
            Available = true,
            HealthState = AgentHealthStates.Ready,
            AuthenticationState = Authenticated ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
            Capabilities = new AgentBackendCapabilities { Conversation = true, Tools = true, ContractVersion = "spec" }
        };

        public ValueTask<AgentModelInventory> DiscoverModelsAsync( bool includeHidden, CancellationToken cancellationToken ) =>
            ValueTask.FromResult( new AgentModelInventory { BackendId = Id, Models = [new AgentModelDescriptor { Id = "scripted", DisplayName = "Scripted", IsDefault = true }] } );

        // Holds the next login check open so a spec can change authority while
        // a turn or prewarm is still preparing.
        public TaskCompletionSource<bool>? BlockNextLogin { get; set; }
        public TaskCompletionSource LoginEntered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask<AgentLoginState> GetLoginStateAsync( CancellationToken cancellationToken )
        {
            var block = BlockNextLogin;
            if ( block is not null )
            {
                BlockNextLogin = null;
                LoginEntered.TrySetResult( );
                await block.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
            }
            return new AgentLoginState { State = Authenticated ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated };
        }

        public ValueTask<AgentLoginState> StartLoginAsync( string method, AiCredentialOptions? credential, CancellationToken cancellationToken ) =>
            ValueTask.FromResult( new AgentLoginState { State = AgentAuthenticationStates.Pending, Method = method, UserCode = "CODE-1234", VerificationUrl = "https://auth.example.invalid/device" } );

        public ValueTask<AgentLoginState> CancelLoginAsync( CancellationToken cancellationToken ) => GetLoginStateAsync( cancellationToken );

        public ValueTask<AgentLoginState> LogoutAsync( CancellationToken cancellationToken )
        {
            Authenticated = false;
            return GetLoginStateAsync( cancellationToken );
        }

        private ScriptedSession? _lastSession;

        public TaskCompletionSource<bool>? BlockSessionStart { get; set; }
        public TaskCompletionSource SessionStartEntered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        // Mirrors the real backend's process identity: a start after a stop is a
        // new generation, admitted under the epoch of the session that started it.
        private int _generations;
        public string Generation { get; private set; } = string.Empty;
        public long? ProcessEpoch { get; private set; }
        public List<string> StoppedGenerations { get; } = [];

        public async ValueTask<IAgentSession> StartSessionAsync( AgentSessionOptions options, CancellationToken cancellationToken )
        {
            Sessions++;
            if ( !IsRunning )
            {
                Generation = $"scripted-{++_generations}";
            }
            IsRunning = true;
            ProcessEpoch = options.Admission?.Epoch;
            LastOptions = options;
            ScriptedSession session = new( this, options.ModelId, Generation );
            _lastSession = session;
            if ( BlockSessionStart is not null )
            {
                SessionStartEntered.TrySetResult( );
                await BlockSessionStart.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
            }

            return session;
        }

        // Simulates the sandboxed Codex process exiting under a live conversation.
        public void FaultActiveSession( )
        {
            if ( _lastSession is not null )
            {
                _lastSession.IsFaulted = true;
            }
        }

        // Holds the next stop open so a spec can interleave another authority
        // change while a caller is still shutting the backend down.
        public TaskCompletionSource<bool>? BlockNextStop { get; set; }
        public TaskCompletionSource StopEntered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask StopAsync( CancellationToken cancellationToken )
        {
            StopProcess( );
            await HoldIfBlockedAsync( cancellationToken ).ConfigureAwait( false );
        }

        // The pause comes first, so a session admitted at the new epoch while the
        // revocation is held survives it, as with the real backend's gate.
        public async ValueTask StopBeforeAsync( long authorityEpoch, CancellationToken cancellationToken )
        {
            // Stopped records that a stop reached the backend, as StopAsync does.
            Stopped = true;
            await HoldIfBlockedAsync( cancellationToken ).ConfigureAwait( false );
            if ( IsRunning && (ProcessEpoch is null || ProcessEpoch < authorityEpoch) )
            {
                StopProcess( );
            }
        }

        public TaskCompletionSource<bool>? BlockNextGenerationStop { get; set; }
        public TaskCompletionSource GenerationStopEntered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async ValueTask StopGenerationAsync( string processGeneration, CancellationToken cancellationToken )
        {
            var block = BlockNextGenerationStop;
            if ( block is not null )
            {
                BlockNextGenerationStop = null;
                GenerationStopEntered.TrySetResult( );
                await block.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
            }
            StoppedGenerations.Add( processGeneration );
            if ( IsRunning && Generation == processGeneration )
            {
                StopProcess( );
            }
        }

        private void StopProcess( )
        {
            Stopped = true;
            IsRunning = false;
            ProcessEpoch = null;
            StopCount++;
        }

        private async ValueTask HoldIfBlockedAsync( CancellationToken cancellationToken )
        {
            var block = BlockNextStop;
            if ( block is not null )
            {
                BlockNextStop = null;
                StopEntered.TrySetResult( );
                await block.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
            }
        }

        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync( )
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        private sealed class ScriptedSession( ScriptedAgentBackend owner, string? modelId, string generation ) : IAgentSession
        {
            public string SessionId => "scripted-session";
            public string? ModelId => modelId;
            public string? ReasoningEffort => null;
            public bool IsFaulted { get; set; }
            public string ProcessGeneration => generation;

            public ValueTask RunTurnAsync( AgentTurnInput input, IAgentTurnHost host, CancellationToken cancellationToken ) =>
                owner.Script?.Invoke( input, host, cancellationToken ) ?? host.OnTextAsync( "ok", cancellationToken );

            public ValueTask InterruptAsync( CancellationToken cancellationToken ) => ValueTask.CompletedTask;

            public ValueTask DisposeAsync( ) => ValueTask.CompletedTask;
        }
    }
}
