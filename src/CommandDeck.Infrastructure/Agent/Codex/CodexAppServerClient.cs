using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace CommandDeck;

public sealed record CodexAccountState( bool Authenticated, string? Type, bool RequiresOpenAiAuth );

public sealed record CodexLoginStart( string LoginId, string? UserCode, string? VerificationUrl );

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly string[] OptOutNotifications =
    [
        "remoteControl/status/changed",
        "thread/started",
        "thread/name/updated",
        "turn/plan/updated",
        "turn/diff/updated",
        "item/plan/delta",
        "thread/queue/changed",
        "project/changed"
    ];

    private readonly IAgentProcessHandle _handle;
    private readonly JsonRpcConnection _connection;
    private readonly ConcurrentDictionary<string, CodexAgentSession> _sessions = new( StringComparer.Ordinal );
    private int _disposed;

    private CodexAppServerClient( IAgentProcessHandle handle, JsonRpcConnection connection )
    {
        _handle = handle;
        _connection = connection;
    }

    public event Action<string?>? AccountUpdated;

    public event Action<bool, string?, string?>? LoginCompleted;

    public bool IsFaulted => _connection.IsFaulted || _handle.Exited.IsCompleted;

    public Task Exited => _handle.Exited;

    public static async ValueTask<CodexAppServerClient> ConnectAsync(
        IAgentProcessHandle handle,
        string clientVersion,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( handle );
        CodexAppServerContract.Validate( );
        JsonRpcConnection connection = new( handle.Transport );
        CodexAppServerClient client = new( handle, connection );
        connection.NotificationHandler = client.OnNotificationAsync;
        connection.RequestHandler = client.OnRequestAsync;
        connection.Start( );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( AgentLimits.StartupDeadlineSeconds ) );
        JsonObject capabilities = new( )
        {
            ["experimentalApi"] = true,
            ["requestAttestation"] = false,
            ["optOutNotificationMethods"] = new JsonArray( OptOutNotifications.Select( static value => ( JsonNode ) value ).ToArray( ) )
        };
        JsonObject parameters = new( )
        {
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "commanddeck",
                ["title"] = "CommandDeck",
                ["version"] = clientVersion
            },
            ["capabilities"] = capabilities
        };
        try
        {
            await connection.CallAsync( "initialize", parameters, timeout.Token ).ConfigureAwait( false );
            await connection.NotifyAsync( "initialized", new JsonObject( ), timeout.Token ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
        {
            await client.DisposeAsync( ).ConfigureAwait( false );
            throw new AiOperationException(
                AiErrorCategories.Timeout,
                safeSummary: "Codex did not complete its handshake in time." );
        }
        catch ( Exception exception ) when ( exception is JsonRpcException or InvalidDataException or EndOfStreamException )
        {
            await client.DisposeAsync( ).ConfigureAwait( false );
            throw new AiOperationException(
                AiErrorCategories.Unsupported,
                safeSummary: "Codex rejected the CommandDeck handshake." );
        }
        return client;
    }

    public async ValueTask<AgentModelInventory> ListModelsAsync( bool includeHidden, CancellationToken cancellationToken )
    {
        List<AgentModelDescriptor> models = [];
        string? defaultModel = null;
        string? cursor = null;
        for ( var page = 0; page < 8; page++ )
        {
            JsonObject parameters = new( )
            {
                ["includeHidden"] = includeHidden,
                ["limit"] = 100
            };
            if ( cursor is not null )
            {
                parameters["cursor"] = cursor;
            }
            var result = await _connection.CallAsync( "model/list", parameters, cancellationToken )
                .ConfigureAwait( false );
            if ( result?["data"] is not JsonArray data )
            {
                break;
            }
            foreach ( var entry in data )
            {
                if ( entry is JsonObject model && TryReadModel( model, out var descriptor ) )
                {
                    models.Add( descriptor! );
                    if ( descriptor!.IsDefault )
                    {
                        defaultModel ??= descriptor.Id;
                    }
                }
                if ( models.Count >= AgentLimits.MaximumModels )
                {
                    break;
                }
            }
            cursor = ( string? ) result["nextCursor"];
            if ( cursor is null || models.Count >= AgentLimits.MaximumModels )
            {
                break;
            }
        }
        return new AgentModelInventory
        {
            BackendId = AgentBackendIds.Codex,
            Models = models,
            DefaultModelId = defaultModel ?? models.FirstOrDefault( static value => !value.Hidden )?.Id
        };
    }

    public async ValueTask<CodexAccountState> ReadAccountAsync( CancellationToken cancellationToken )
    {
        var result = await _connection.CallAsync(
            "account/read",
            new JsonObject { ["refreshToken"] = false },
            cancellationToken ).ConfigureAwait( false );
        var account = result?["account"];
        var type = ( string? ) account?["type"];
        var requires = ( bool? ) result?["requiresOpenaiAuth"] ?? true;
        return new CodexAccountState( account is JsonObject, type, requires );
    }

    public async ValueTask<CodexLoginStart> StartDeviceCodeLoginAsync( CancellationToken cancellationToken )
    {
        var result = await _connection.CallAsync(
            "account/login/start",
            new JsonObject { ["type"] = "chatgptDeviceCode" },
            cancellationToken ).ConfigureAwait( false );
        var loginId = ( string? ) result?["loginId"];
        if ( loginId is null )
        {
            throw new AiOperationException( AiErrorCategories.Unsupported, safeSummary: "Codex did not start a device-code login." );
        }

        return new CodexLoginStart(
            loginId,
            ( string? ) result!["userCode"],
            ( string? ) result["verificationUrl"] );
    }

    public async ValueTask StartApiKeyLoginAsync( string apiKey, CancellationToken cancellationToken )
    {
        await _connection.CallAsync(
            "account/login/start",
            new JsonObject { ["type"] = "apiKey", ["apiKey"] = apiKey },
            cancellationToken ).ConfigureAwait( false );
    }

    public ValueTask CancelLoginAsync( string loginId, CancellationToken cancellationToken ) =>
        IgnoreResult( _connection.CallAsync( "account/login/cancel", new JsonObject { ["loginId"] = loginId }, cancellationToken ) );

    public ValueTask LogoutAsync( CancellationToken cancellationToken ) =>
        IgnoreResult( _connection.CallAsync( "account/logout", new JsonObject( ), cancellationToken ) );

    public async ValueTask<CodexAgentSession> StartThreadAsync(
        AgentSessionOptions options,
        string workingDirectory,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( options );
        JsonObject parameters = new( )
        {
            ["ephemeral"] = true,
            ["permissions"] = options.TrustLevel >= AgentTrustLevels.Unrestricted
                ? CodexAppServerContract.UnrestrictedPermission
                : CodexAppServerContract.ReadOnlyPermission,
            ["approvalPolicy"] = "never",
            ["approvalsReviewer"] = "user",
            ["allowProviderModelFallback"] = false,
            ["developerInstructions"] = options.DeveloperInstructions,
            ["cwd"] = workingDirectory,
            ["dynamicTools"] = new JsonArray( options.Tools.Select( static tool => ( JsonNode ) new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse( tool.InputSchemaJson )
            } ).ToArray( ) )
        };
        if ( options.ModelId is not null )
        {
            parameters["model"] = options.ModelId;
        }
        var result = await _connection.CallAsync( "thread/start", parameters, cancellationToken )
            .ConfigureAwait( false );
        var threadId = ( string? ) result?["thread"]?["id"];
        if ( threadId is null )
        {
            throw new AiOperationException( AiErrorCategories.MalformedResponse, safeSummary: "Codex did not return a thread." );
        }

        CodexAgentSession session = new( this, threadId, ( string? ) result!["model"] ?? options.ModelId, options.ReasoningEffort );
        _sessions[threadId] = session;
        return session;
    }

    internal async ValueTask<string> StartTurnAsync(
        string threadId,
        string text,
        string? model,
        string? effort,
        CancellationToken cancellationToken )
    {
        JsonObject parameters = new( )
        {
            ["threadId"] = threadId,
            ["input"] = new JsonArray( new JsonObject { ["type"] = "text", ["text"] = text } )
        };
        if ( model is not null )
        {
            parameters["model"] = model;
        }
        if ( effort is not null )
        {
            parameters["effort"] = effort;
        }
        var result = await _connection.CallAsync( "turn/start", parameters, cancellationToken )
            .ConfigureAwait( false );
        var result2 = ( string? ) result?["turn"]?["id"];
        if ( result2 is null )
        {
            throw new AiOperationException( AiErrorCategories.MalformedResponse, safeSummary: "Codex did not return a turn." );
        }

        return result2;
    }

    internal ValueTask InterruptTurnAsync( string threadId, string turnId, CancellationToken cancellationToken ) =>
        IgnoreResult( _connection.CallAsync(
            "turn/interrupt",
            new JsonObject { ["threadId"] = threadId, ["turnId"] = turnId },
            cancellationToken ) );

    internal void RemoveSession( string threadId ) => _sessions.TryRemove( threadId, out _ );

    private static async ValueTask IgnoreResult( ValueTask<JsonNode?> call ) => _ = await call.ConfigureAwait( false );

    private ValueTask OnNotificationAsync( string method, JsonNode? parameters, CancellationToken cancellationToken )
    {
        switch ( method )
        {
            case "account/updated":
                AccountUpdated?.Invoke( ( string? ) parameters?["authMode"] );
                return ValueTask.CompletedTask;
            case "account/login/completed":
                LoginCompleted?.Invoke(
                    ( bool? ) parameters?["success"] ?? false,
                    ( string? ) parameters?["error"],
                    ( string? ) parameters?["loginId"] );
                return ValueTask.CompletedTask;
        }
        var threadId = ( string? ) parameters?["threadId"];
        if ( threadId is not null && _sessions.TryGetValue( threadId, out var session ) )
        {
            return session.EnqueueAsync( method, parameters, cancellationToken );
        }
        return ValueTask.CompletedTask;
    }

    private ValueTask<JsonNode?> OnRequestAsync( string method, JsonNode? parameters, CancellationToken cancellationToken )
    {
        if ( method is "item/tool/call" or "item/tool/requestUserInput" )
        {
            var threadId = ( string? ) parameters?["threadId"];
            if ( threadId is not null && _sessions.TryGetValue( threadId, out var session ) )
            {
                return session.HandleRequestAsync( method, parameters!, cancellationToken );
            }
        }
        throw new JsonRpcException( -32601, "CommandDeck declines this request." );
    }

    private static bool TryReadModel( JsonObject model, out AgentModelDescriptor? descriptor )
    {
        descriptor = null;
        var id = ( string? ) model["id"] ?? ( string? ) model["model"];
        if ( string.IsNullOrWhiteSpace( id ) || id.Length > AiLimits.MaximumModelIdBytes )
        {
            return false;
        }
        descriptor = new AgentModelDescriptor
        {
            Id = id,
            DisplayName = ( string? ) model["displayName"] ?? id,
            Description = ( string? ) model["description"],
            Hidden = ( bool? ) model["hidden"] ?? false,
            IsDefault = ( bool? ) model["isDefault"] ?? false,
            DefaultReasoningEffort = ( string? ) model["defaultReasoningEffort"],
            ReasoningEfforts = ReadEfforts( model["supportedReasoningEfforts"] as JsonArray )
        };
        return true;
    }

    private static List<AgentReasoningEffort> ReadEfforts( JsonArray? supported )
    {
        List<AgentReasoningEffort> efforts = [];
        if ( supported is null )
        {
            return efforts;
        }
        foreach ( var entry in supported )
        {
            var effort = ( string? ) entry?["reasoningEffort"];
            if ( !string.IsNullOrWhiteSpace( effort ) && effort.Length <= 32 )
            {
                efforts.Add( new AgentReasoningEffort
                {
                    Id = effort,
                    Description = ( string? ) entry?["description"]
                } );
            }
        }
        return efforts;
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        foreach ( var session in _sessions.Values )
        {
            session.MarkClosed( );
        }
        _sessions.Clear( );
        await _connection.DisposeAsync( ).ConfigureAwait( false );
        await _handle.DisposeAsync( ).ConfigureAwait( false );
    }
}
