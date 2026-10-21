using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunExtensionTransportRegistrationsAsync( )
    {
        await RunAsync( "extension search isolates failing search children", ExtensionSearchIsolatesFailedChildAsync );
        await RunAsync( "extension ready errors allow immediate retries", ExtensionReadyErrorAllowsImmediateRetryAsync );
        await RunAsync( "extension failed forms retry without edits", ExtensionFailedFormCanRetrySameRevisionAsync );
        await RunAsync( "extension revocation waits for admitted effects and fences later actions", ExtensionRevocationWaitsForEffectAndFencesActionsAsync );
        await RunAsync( "extension revocation cancels queued data without deadlock", ExtensionRevocationCancelsQueuedDataWithoutDeadlockAsync );
        await RunAsync( "extension test kit validates storage key syntax", ExtensionKitValidatesStorageKeySyntaxAsync );
        await RunAsync( "extension test kit requires checked checkboxes", ExtensionKitRequiresCheckedCheckboxAsync );
        Run( "extension callbacks named page back and submit remain ordinary callbacks", ExtensionNavigationNamesRemainCallbacks );
        Run( "extension manifest requirements match supported interactive features", ExtensionFeatureRequirementsMatchSupport );
        Run( "extension form field errors retain immutable snapshots and strict wire defaults", ExtensionFormErrorsRetainSnapshots );
        await RunAsync( "extension remote writes require user activation before confirmation", ExtensionRemoteWritesRequireUserActivationAsync );
        await RunAsync( "extension process disposal stops gracefully after the startup deadline", ExtensionProcessStopsAfterStartupDeadlineAsync );
        await RunAsync( "extension search disposal kills uncooperative children without graceful delay", ExtensionSearchProcessSkipsGracefulStopAsync );
        await RunAsync( "extension management and sessions share configured connections", ExtensionManagementSharesConnectionsWithSessionsAsync );
        await RunAsync( "extension credential preparation leaves the registry lease available", ExtensionConnectionPreparationDoesNotHoldRegistryLeaseAsync );
        await RunAsync( "extension browser launch leaves the registry lease available", ExtensionBrowserLaunchDoesNotHoldRegistryLeaseAsync );
        Run( "extension management typed commands require confirmation and snapshot grants", ExtensionManagementCommandsValidateConfirmationAndSnapshotInputs );
        Run( "extension address policy matches the reference and native ABI", ExtensionNativeAndAddressPolicyMatchReference );
        await RunAsync( "extension SDK receives a distinct host deadline error code", ExtensionSdkReceivesHostDeadlineCodeAsync );
        await RunAsync( "extension SDK logs bounded author exceptions without exposing renderer details", ExtensionSdkLogsBoundedAuthorErrorsAsync );
        await RunAsync( "extension SDK returns typed broker outcomes without retrying mutations", ExtensionSdkReturnsTypedBrokerOutcomesAsync );
        Run( "extension validation reports structural paths", ExtensionValidationReportsStructuralPaths );
        await RunAsync( "extension author test kit exercises typed SDK handlers and brokers", ExtensionAuthorKitExercisesRealSdkAsync );
        Run( "extension rendered views match the pre-refactor golden payloads", ExtensionRenderedViewsMatchGolden );
        await RunAsync( "extension session opens a view and admits one action through the in-process launcher", ExtensionSessionOpensAndInvokesAsync );
        await RunAsync( "extension session revocation during an action closes the session", ExtensionSessionRevocationCancelsActionAsync );
        await RunAsync( "extension session preserves the view after a child handler error", ExtensionSessionRecoversHandlerErrorAsync );
        await RunAsync( "extension peer disposal bounds uncooperative reads and starts once", ExtensionPeerDisposalBoundsUncooperativeReadsAsync );
        await RunAsync( "extension peer requires explicit start before calls", ExtensionPeerRequiresExplicitStartAsync );
        await RunAsync( "extension peer calls after disposal report cancellation", ExtensionPeerCallsAfterDisposalReportCancellationAsync );
        Run( "extension resource profiles retain configured and enforced limits", ExtensionResourceProfilesKeepLimits );
        await RunAsync( "extension OAuth callback rejects hostile headers and parameters", ExtensionOAuthCallbackRejectsHostileInputAsync );
        await RunAsync( "extension SDK rejects command mode misuse before host calls", ExtensionSdkRejectsModeMisuseLocallyAsync );
        await RunAsync( "extension data permission conflicts and quotas reach the real SDK", ExtensionDataErrorsReachSdkWithSafeCodesAsync );
        await RunAsync( "extension error codes require negotiation and preserve healthy peers", ExtensionErrorsRequireNegotiationAsync );
        Run( "extension error frames reject malformed data and preserve legacy handshake shape", ExtensionErrorFramesAreStrict );
        await RunAsync( "extension install rejects packages without a target for this host", ExtensionInstallRejectsUnrunnableTargetsAsync );
        await RunAsync( "extension rollback rejects an unrunnable retained target", ExtensionRollbackRejectsUnrunnableRetainedTargetsAsync );
        await RunAsync( "multi-target extension packages open their linux-x64 target", ExtensionMultiTargetOpensX64Async );
        await RunAsync( "extension remote-effect journals bound packages and preserve legacy no-resend history", ExtensionJournalBoundsAndLegacyAsync );
        await RunAsync( "extension management confirms journal reset and distinguishes plain from erase removal", ExtensionManagementResetAndEraseAsync );
        await RunAsync( "extension clipboard effects do not block other packages' data writes", ExtensionEffectsDoNotBlockOtherPackagesAsync );
        await RunAsync( "extension storage migrates once and erase removal survives reopening", ExtensionStorageMigrationAndEraseAsync );
        await RunAsync( "extension search discards results when storage changes during a query", ExtensionSearchRejectsChangedDataAsync );
        await RunAsync( "extension search consent cache projection and result leases", ExtensionSearchConsentProjectionAndLeasesAsync );
        await RunAsync( "extension search cancellation and revocation are independent", ExtensionSearchCancellationAndRevocationAsync );
        await RunAsync( "extension search SDK purpose and encoded response bounds", ExtensionSearchSdkPurposeAndResponseBoundsAsync );
        await RunAsync( "extension search rotates admission and updates reset query consent", ExtensionSearchAdmissionRotatesAsync );
        await RunAsync( "extension replacement search waits for cancelled child cleanup", ExtensionSearchReplacementWaitsForCleanupAsync );
        await RunAsync( "extension search stops its process before draining blocked protocol reads", ExtensionSearchStopsProcessBeforeDrainingPeerAsync );
        Run( "extension brokers reject foreign and retired invocation authority", ExtensionBrokerAuthorityRejectsForeignAndRetiredRequests );
        await RunAsync( "extension files reject links replacement and revocation", ExtensionFileConnectionsRejectReplacementLinksAndRevocationAsync );
        await RunAsync( "extension HTTP scopes secrets confirmations and durable no-retry effects", ExtensionHttpConnectionsBoundScopeSecretsAndEffectsAsync );
        await RunAsync( "extension OAuth binds state PKCE refresh and disconnect", ExtensionOAuthBindsStatePkceRefreshAndDisconnectAsync );
        await RunAsync( "extension OAuth callback serves one connection and turns away the rest", ExtensionOAuthCallbackServesOneConnectionAsync );
        await RunAsync( "development debug leases bind one artifact and revoke or expire", ExtensionDebugLeasesAreExplicitBoundedAndRevokedAsync );
        await RunAsync( "development attachment preserves production identity", ExtensionDevelopmentAttachmentCannotReplaceProductionAsync );
        Run( "extension author template and shared package builder", ExtensionAuthorTemplateAndPackage );
        await RunAsync( "extension CLI settings retain inspected revision and validate input", ExtensionCliSettingsPreserveRevisionAsync );
        Run( "extension clipboard effects require one current granted invocation", ExtensionCopyEffectsRequireCurrentInvocation );
        Run( "extension assets reject paths and bound image data", ExtensionAssetsRejectPathsAndBoundDecodedImages );
        Run( "rejected extension assets preserve navigation and permit fallback", ExtensionRejectedAssetsPreserveNavigationAndAllowRecovery );
        Run( "extension URLs retain their presented targets and require grants", ExtensionUrlsBindPresentedTargetsAndGrants );
        await RunAsync( "SDK no-view copy completes and retires its context", ExtensionSdkNoViewCopyRetiresContextAsync );
        Run( "handwritten extension manifests and views retain optional defaults", HandwrittenExtensionDefaultsSurviveDeserialization );
        Run( "extension forms validate freeze and fence submissions", ExtensionFormAdmissionValidatesAndFencesSubmissions );
        Run( "extension data rejects foreign and retired authority", ExtensionDataAuthorityRejectsForeignAndRetiredRequests );
        await RunAsync( "extension settings and storage persist with revision and installation isolation", ExtensionDataPersistsAndRejectsStaleWritesAsync );
        Run( "extension actions bind displayed payloads across refresh and reject replay", ExtensionActionsBindDisplayedPayloads );
        Run( "extension navigation retires contexts and bounds the view stack", ExtensionNavigationRetiresContextsAndBoundsStack );
        Run( "extension pages bind opaque tokens to the current revision", ExtensionPagesBindOpaqueTokensToCurrentRevision );
        Run( "extension authority rejects revocation unsolicited views and backlog overflow", ExtensionAuthorityRejectsRevocationAndUnsolicitedViews );
        await RunAsync( "C# SDK cancellation fences retained invocation contexts", ExtensionSdkCancellationFencesContextAsync );
        await RunAsync( "C# SDK executes typed callbacks and retires completed contexts", ExtensionSdkRoundTripAndRetiredContextAsync );
        await RunAsync( "C# SDK rejects views unsupported by its negotiated renderer", ExtensionSdkRejectsUnsupportedRendererAsync );
        await RunAsync( "extension activation failure recovers the prior version and permits retry", ExtensionActivationFailurePreservesCurrentAsync );
        await RunAsync( "extension artifact cleanup bounds versions and recovers orphans", ExtensionArtifactCleanupPreservesRetainedVersionsAsync );
        await RunAsync( "extension inventory bounds retain every source and reject overflow", ExtensionInventoryBoundsPreserveSourcesAsync );
        await RunAsync( "extension management uses the daemon and preserves unavailable filters", ExtensionManagementUsesDaemonAsync );
        await RunAsync( "extension archives reject traversal, links, collisions and digest mismatch", ExtensionPackagesRejectUnsafeArchivesAsync );
        await RunAsync( "extension lifecycle preserves identity and rejects stale source saves", ExtensionPackageLifecyclePreservesIdentityAsync );
        await RunAsync( "an unreadable extension registry keeps source preferences editable", UnreadableExtensionRegistryKeepsSourcePreferencesEditableAsync );
        Run( "extension typed views round trip without reflection", ExtensionViewsRoundTrip );
        Run( "extension view validation rejects hostile content and action shapes", ExtensionViewsRejectHostileContent );
        Run( "extension frames reject malformed and ambiguous input", ExtensionFramesRejectMalformedInput );
        await RunAsync( "extension frame overflow fails before a newline", ExtensionFrameOverflowFailsImmediatelyAsync );
        await RunAsync( "extension duplex handler can await a host request", ExtensionDuplexNestedRequestAsync );
        await RunAsync( "extension cancellation drains one crossed success and faults a duplicate", ExtensionCancellationCrossingAsync );
        await RunAsync( "extension completion wins before cancellation", ExtensionCompletionBeforeCancellationAsync );
        await RunAsync( "extension cancellation reaches a busy application handler", ExtensionCancellationReachesHandlerAsync );
        await RunAsync( "extension unsolicited replies and unknown methods fault", ExtensionForgedFramesFaultAsync );
        await RunAsync( "extension pending calls remain bounded", ExtensionPendingCallsAreBoundedAsync );
        await RunAsync( "extension data backlog cannot block nested replies", ExtensionBacklogAllowsRepliesAsync );
    }

    private static void ExtensionFramesRejectMalformedInput( )
    {
        string[] invalid =
        [
            "[]", "null", "{}", "{\"id\":\"h:1\",\"result\":{}}",
            "{\"jsonrpc\":\"1.0\",\"id\":\"h:1\",\"result\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":null,\"result\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"x:1\",\"result\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":{},\"result\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":{\"x\":1,\"x\":2}}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":{},\"error\":null}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"error\":{\"code\":1,\"message\":null}}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"error\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"method\":\"open\",\"result\":{}}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"open\",\"params\":[]}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":{},\"extra\":true}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":{},}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":" + new string( '[', 33 ) + "0" + new string( ']', 33 ) + "}"
        ];
        foreach ( var text in invalid )
        {
            try
            {
                ExtensionFraming.Parse( Encoding.UTF8.GetBytes( text ) );
                throw new InvalidOperationException( $"Accepted invalid frame: {text}" );
            }
            catch ( Exception exception ) when ( exception is InvalidDataException or JsonException ) { }
        }
        var badUtf8 = Encoding.UTF8.GetBytes( "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":\"x\"}" );
        badUtf8[^3] = 0xff;
        Throws<DecoderFallbackException>( ( ) => ExtensionFraming.Parse( badUtf8 ) );
        var valid = ExtensionFraming.Parse( "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"result\":null}"u8.ToArray( ) );
        Equal( "h:1", valid.Id );
    }

    private static async Task ExtensionFrameOverflowFailsImmediatelyAsync( )
    {
        using MemoryStream stream = new( new byte[ExtensionLimits.MaximumFrameBytes + 1] );
        ExtensionFraming framing = new( stream );
        await ThrowsAsync<InvalidDataException>( async ( ) => await framing.ReadAsync( CancellationToken.None ) );
        using MemoryStream truncated = new( "{}"u8.ToArray( ) );
        await ThrowsAsync<InvalidDataException>( async ( ) => await new ExtensionFraming( truncated ).ReadAsync( CancellationToken.None ) );
    }

    private static async Task ExtensionDuplexNestedRequestAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        await using ExtensionPeer host = new( pipes.Host, pipes.Host, true, ["storage.get"],
            ( frame, _ ) => ValueTask.FromResult( JsonElement.Parse( "{\"value\":\"nested\"}" ) ) );
        host.Start( );
        TaskCompletionSource<ExtensionPeer> ready = new( TaskCreationOptions.RunContinuationsAsynchronously );
        await using ExtensionPeer child = new( pipes.Child, pipes.Child, false, ["command.open"],
            async ( _, ct ) => await (await ready.Task).CallAsync( "storage.get", JsonElement.Parse( "{}" ), ct ) );
        child.Start( );
        ready.SetResult( child );
        var result = await host.CallAsync( "command.open", JsonElement.Parse( "{}" ), deadline.Token );
        Equal( "nested", result.GetProperty( "value" ).GetString( ) );
    }

    private static async Task ExtensionCancellationCrossingAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        await using ExtensionPeer host = NoRequestsPeer( pipes.Host );
        using CancellationTokenSource cancel = new( );
        var call = host.CallAsync( "command.open", null, cancel.Token ).AsTask( );
        ExtensionFraming reader = new( pipes.Child );
        var request = (await reader.ReadAsync( deadline.Token ))!;
        await cancel.CancelAsync( );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await call );
        var cancellation = (await reader.ReadAsync( deadline.Token ))!;
        Equal( "$/cancelRequest", cancellation.Method );
        var reply = new ExtensionFrame { Id = request.Id, Result = JsonElement.Parse( "{\"ok\":true}" ) };
        await pipes.Child.WriteAsync( ExtensionFraming.Serialize( reply ), deadline.Token );
        var next = host.CallAsync( "command.open", null, deadline.Token ).AsTask( );
        var nextRequest = (await reader.ReadAsync( deadline.Token ))!;
        await pipes.Child.WriteAsync( ExtensionFraming.Serialize( reply with
        {
            Id = nextRequest.Id
        } ), deadline.Token );
        True( (await next).GetProperty( "ok" ).GetBoolean( ) );
        True( host.Fault is null );
        await pipes.Child.WriteAsync( ExtensionFraming.Serialize( reply ), deadline.Token );
        await host.Completion.WaitAsync( deadline.Token );
        True( host.Fault is InvalidDataException );
    }

    private static async Task ExtensionCompletionBeforeCancellationAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        await using ExtensionPeer host = NoRequestsPeer( pipes.Host );
        using CancellationTokenSource cancel = new( );
        var call = host.CallAsync( "command.open", null, cancel.Token ).AsTask( );
        var request = (await new ExtensionFraming( pipes.Child ).ReadAsync( deadline.Token ))!;
        await pipes.Child.WriteAsync( ExtensionFraming.Serialize( new ExtensionFrame { Id = request.Id, Result = JsonElement.Parse( "null" ) } ), deadline.Token );
        Equal( JsonValueKind.Null, (await call.WaitAsync( deadline.Token )).ValueKind );
        await cancel.CancelAsync( );
        True( host.Fault is null );
    }

    private static async Task ExtensionCancellationReachesHandlerAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource cancelled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        await using ExtensionPeer host = NoRequestsPeer( pipes.Host );
        await using ExtensionPeer child = new( pipes.Child, pipes.Child, false, ["command.open"], async ( _, ct ) =>
        {
            entered.SetResult( );
            try
            {
                await Task.Delay( Timeout.Infinite, ct );
            }
            catch ( OperationCanceledException )
            {
                cancelled.SetResult( );
                throw;
            }
            return JsonElement.Parse( "{}" );
        } );
        child.Start( );
        using CancellationTokenSource cancel = new( );
        var call = host.CallAsync( "command.open", null, cancel.Token ).AsTask( );
        await entered.Task.WaitAsync( deadline.Token );
        await cancel.CancelAsync( );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await call );
        await cancelled.Task.WaitAsync( deadline.Token );
    }

    private static async Task ExtensionForgedFramesFaultAsync( )
    {
        string[] frames =
        [
            "{\"jsonrpc\":\"2.0\",\"id\":\"h:77\",\"result\":{}}\n",
            "{\"jsonrpc\":\"2.0\",\"id\":\"e:1\",\"method\":\"runShell\"}\n",
            "{\"jsonrpc\":\"2.0\",\"method\":\"$/cancelRequest\",\"params\":{\"id\":\"h:1\"}}\n"
        ];
        foreach ( var frame in frames )
        {
            await using var pipes = await ExtensionTestPipes.CreateAsync( );
            using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
            await using ExtensionPeer host = NoRequestsPeer( pipes.Host );
            await pipes.Child.WriteAsync( Encoding.UTF8.GetBytes( frame ), deadline.Token );
            await host.Completion.WaitAsync( deadline.Token );
            True( host.Fault is InvalidDataException );
        }
    }

    private static async Task ExtensionPendingCallsAreBoundedAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource cancel = new( );
        await using ExtensionPeer host = NoRequestsPeer( pipes.Host );
        var calls = Enumerable.Range( 0, ExtensionLimits.MaximumPendingCalls )
            .Select( _ => host.CallAsync( "command.open", null, cancel.Token ).AsTask( ) ).ToArray( );
        await ThrowsAsync<ExtensionRpcException>( async ( ) => await host.CallAsync( "command.open", null, cancel.Token ) );
        await cancel.CancelAsync( );
        foreach ( var call in calls )
        {
            await ThrowsAsync<OperationCanceledException>( async ( ) => await call );
        }
    }

    private static async Task ExtensionBacklogAllowsRepliesAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
        TaskCompletionSource<ExtensionPeer> ready = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource completed = new( TaskCreationOptions.RunContinuationsAsynchronously );
        await using ExtensionPeer host = new( pipes.Host, pipes.Host, true, ["views.replace"], async ( frame, ct ) =>
        {
            if ( frame.Id == "e:1" )
            {
                await (await ready.Task).CallAsync( "command.event", null, ct );
                completed.SetResult( );
            }
            return JsonElement.Parse( "{}" );
        } );
        host.Start( );
        ready.SetResult( host );
        for ( var i = 1; i <= 32; i++ )
        {
            await pipes.Child.WriteAsync( ExtensionFraming.Serialize( new ExtensionFrame { Id = $"e:{i}", Method = "views.replace" } ), deadline.Token );
        }
        ExtensionFraming reader = new( pipes.Child );
        ExtensionFrame? nested;
        do
        {
            nested = await reader.ReadAsync( deadline.Token );
        } while ( nested?.Method != "command.event" );
        await pipes.Child.WriteAsync( ExtensionFraming.Serialize( new ExtensionFrame { Id = nested.Id, Result = JsonElement.Parse( "{}" ) } ), deadline.Token );
        await completed.Task.WaitAsync( deadline.Token );
        True( host.Fault is null );
    }

    private static ExtensionPeer NoRequestsPeer( Stream stream )
    {
        ExtensionPeer peer = new( stream, stream, true, [], ( _, _ ) => throw new InvalidOperationException( "No request is expected." ) );
        peer.Start( );
        return peer;
    }

    private sealed class ExtensionTestPipes( TcpClient host, TcpClient child ) : IAsyncDisposable
    {
        public NetworkStream Host { get; } = host.GetStream( );
        public NetworkStream Child { get; } = child.GetStream( );

        public static async Task<ExtensionTestPipes> CreateAsync( )
        {
            using TcpListener listener = new( IPAddress.Loopback, 0 );
            listener.Start( );
            TcpClient host = new( );
            var accept = listener.AcceptTcpClientAsync( );
            await host.ConnectAsync( ( IPEndPoint ) listener.LocalEndpoint );
            return new ExtensionTestPipes( host, await accept );
        }

        public ValueTask DisposeAsync( )
        {
            host.Dispose( );
            child.Dispose( );
            return ValueTask.CompletedTask;
        }
    }
}
