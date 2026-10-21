using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardOpenPreparationMintsCapabilitiesWithoutEffectsAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-open-prepare-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new( ) );
            EnableRichFixture( store );
            await FeedRichAdmissionAsync( store, ["text/plain"], ["plain note"u8.ToArray( )] );
            await FeedRichAdmissionAsync( store, ["text/uri-list", "text/plain"],
                ["https://example.invalid/page"u8.ToArray( ), "https://example.invalid/page"u8.ToArray( )] );
            List<(string Key, string Target)> resolved = [];
            ClipboardHistoryService service = new( store, resolveHandlers: ( key, target, _ ) =>
            {
                resolved.Add( (key, target) );
                return ValueTask.FromResult( new ClipboardHandlerResolution( "Resolved",
                    [new( "browser.desktop", "Browser", "default", true, null ), new( "term.desktop", "Terminal", "declared", false, "UnsupportedTerminalLaunch" )],
                    2, false, new string( 'a', 64 ), "browser.desktop" ) );
            } );
            service.RegisterOwner( "one" );
            var instance = Guid.NewGuid( ).ToString( "N" );
            var opened = service.Handle( "one", new( )
            {
                RequestId = 1,
                Operation = ProtocolOperations.ClipboardOpen,
                Clipboard = new( )
                {
                    FrontendInstance = instance,
                    OpenGeneration = 1
                }
            } );
            var session = opened.SessionId!;
            var capabilities = opened.Capabilities!;
            True( capabilities.Browse && capabilities.Copy && capabilities.InspectOpenHandlers,
                "the browser advertises supported operations" );
            True( !capabilities.Open && !capabilities.Paste &&
                capabilities.EffectsUnavailableReason == ClipboardOperationCapabilities.ReleaseBarrierUnavailable,
                "the browser does not advertise unavailable effects" );
            var entries = service.Query( "one", session, new( ), false, 1 ).Entries;
            var link = entries.Single( static entry => entry.Presentation.Kind == "links" );
            var text = entries.Single( static entry => entry.Presentation.Kind == "text" );

            var response = await service.HandleAsync( "one", Prepare( instance, 1, session, link, 1 ), default );
            var actions = response.Actions!;
            Equal( "Resolved", actions.Status );
            Equal( 2, actions.Handlers.Count );
            True( actions.EffectsAvailable == capabilities.Open &&
                actions.UnavailableReason == capabilities.EffectsUnavailableReason, "effects stay gated" );
            Equal( 30, actions.ExpiresInSeconds );
            True( Guid.TryParseExact( actions.PreparationId, "N", out _ ) && Guid.TryParseExact( actions.ReleaseId, "N", out _ ), "identities are fresh GUIDs" );
            Equal( ("x-scheme-handler/https", "https://example.invalid/page"), resolved.Single( ) );

            await ThrowsAsync<InvalidOperationException>( ( ) => service.HandleAsync( "one", Prepare( instance, 1, session, text, 1 ), default ).AsTask( ) );
            Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new( )
            {
                RequestId = 9,
                Operation = ProtocolOperations.ClipboardAction,
                Clipboard = new( )
                {
                    FrontendInstance = instance,
                    OpenGeneration = 1,
                    SessionId = session,
                    EntryId = link.Id,
                    Incarnation = link.Incarnation,
                    EntryRevision = link.Revision,
                    QueryGeneration = 1,
                    Action = "open"
                }
            } ) );

            var abort = service.Handle( "one", new( )
            {
                RequestId = 3,
                Operation = ProtocolOperations.ClipboardReleaseAbort,
                Clipboard = new( )
                {
                    FrontendInstance = instance,
                    OpenGeneration = 1,
                    ReleaseId = actions.ReleaseId
                }
            } ).Abort!;
            Equal( "RevokedBeforeDispatch", abort.Outcome );
            Equal( actions.ReleaseId, abort.ReleaseId );
            Equal( "RevokedBeforeDispatch", service.Handle( "one", new( )
            {
                RequestId = 4,
                Operation = ProtocolOperations.ClipboardReleaseAbort,
                Clipboard = new( )
                {
                    FrontendInstance = instance,
                    OpenGeneration = 1
                }
            } ).Abort!.Outcome );
            await ThrowsAsync<InvalidOperationException>( ( ) => service.HandleAsync( "one", Prepare( instance, 1, session, link, 1 ), default ).AsTask( ) );
            Equal( 1, resolved.Count );
            True( service.Query( "one", session, new( ), false, 2 ).Entries.Count == 2, "browsing survives revocation" );

            var next = service.Open( "one", instance, 2 );
            var reopened = service.Query( "one", next, new( ), false, 1 ).Entries.Single( static entry => entry.Presentation.Kind == "links" );
            Equal( "Resolved", (await service.HandleAsync( "one", Prepare( instance, 2, next, reopened, 1 ), default )).Actions!.Status );
            Equal( "GenerationClosed", service.Handle( "one", new( )
            {
                RequestId = 5,
                Operation = ProtocolOperations.ClipboardReleaseAbort,
                Clipboard = new( )
                {
                    FrontendInstance = instance,
                    OpenGeneration = 1
                }
            } ).Abort!.Outcome );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }

        static ProtocolRequest Prepare( string instance, long generation, string session, ClipboardEntrySummary entry, long queryGeneration ) => new( )
        {
            RequestId = 2,
            Operation = ProtocolOperations.ClipboardActionsPrepare,
            Clipboard = new( )
            {
                FrontendInstance = instance,
                OpenGeneration = generation,
                SessionId = session,
                EntryId = entry.Id,
                Incarnation = entry.Incarnation,
                EntryRevision = entry.Revision,
                QueryGeneration = queryGeneration,
                Action = "open"
            }
        };
    }

    private static async Task ClipboardActionWorkerResolvesInBoundedChildAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-action-worker-" ).FullName;
        try
        {
            var bin = Directory.CreateDirectory( Path.Combine( root, "bin" ) ).FullName;
            var browser = Path.Combine( bin, "browser" );
            File.WriteAllText( browser, "#!/bin/sh\nexit 0\n" );
            File.SetUnixFileMode( browser, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            var data = Directory.CreateDirectory( Path.Combine( root, "data", "applications" ) ).FullName;
            File.WriteAllText( Path.Combine( data, "browser.desktop" ), "[Desktop Entry]\nType=Application\nName=Browser\nExec=browser %U\nMimeType=x-scheme-handler/https;\n" );
            var config = Directory.CreateDirectory( Path.Combine( root, "config" ) ).FullName;
            File.WriteAllText( Path.Combine( config, "mimeapps.list" ), "[Default Applications]\nx-scheme-handler/https=browser.desktop;\n" );
            ClipboardResolverEnvironment environment = new( root, config, "/nonexistent", Path.Combine( root, "data" ), "/nonexistent-data", "", bin, null );

            var host = ClipboardTransportFixtureStart( acknowledge: true );
            var prefix = host.ArgumentList.Take( host.ArgumentList.Count - 2 ).Append( "--clipboard-action-worker-fixture" ).ToArray( );
            // The framework-dependent spec host cannot start under the production 256 MiB bound; the AOT child is checked separately.
            using ClipboardActionWorker worker = new( host.FileName, prefix, environment, maximumMemoryBytes: 2L * 1024 * 1024 * 1024 );
            var resolution = await worker.ResolveAsync( "x-scheme-handler/https", "https://example.invalid/", default );
            Equal( "Resolved", resolution.Status );
            Equal( "browser.desktop", resolution.Handlers.Single( ).DesktopId );
            True( resolution.Handlers[0].Available, "child resolves the executable under the captured PATH" );

            using ClipboardActionWorker busy = new( host.FileName, prefix, environment, ( _, _, _, _, _ ) => ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "{\"status\":\"Resolved\",\"handlers\":[],\"total\":0,\"moreAvailable\":false,\"revision\":\"" + new string( 'b', 64 ) + "\"}",
                StandardError = "",
                TimedOut = true
            } ) );
            Equal( "CheckTimedOut", (await busy.ResolveAsync( "x-scheme-handler/https", "https://example.invalid/", default )).Status );

            TaskCompletionSource reaped = new( TaskCreationOptions.RunContinuationsAsynchronously );
            var started = 0;
            using ClipboardActionWorker stalled = new( host.FileName, prefix, environment, async ( _, _, _, _, cancellation ) =>
            {
                var invocation = Interlocked.Increment( ref started );
                if ( invocation > ClipboardActionWorker.Slots )
                {
                    return new ProcessResult
                    {
                        ExitCode = 0,
                        StandardOutput = "{\"status\":\"NoAssociations\",\"handlers\":[],\"total\":0,\"moreAvailable\":false,\"revision\":\"" + new string( 'c', 64 ) + "\"}",
                        StandardError = ""
                    };
                }

                try
                {
                    await Task.Delay( Timeout.Infinite, cancellation );
                }
                catch ( OperationCanceledException )
                {
                    await reaped.Task;
                    throw;
                }

                throw new InvalidOperationException( "Unreachable fixture." );
            } );
            var firstStalled = stalled.ResolveAsync( "x-scheme-handler/https", "https://one.invalid/", default ).AsTask( );
            var secondStalled = stalled.ResolveAsync( "x-scheme-handler/https", "https://two.invalid/", default ).AsTask( );
            var deadline = Stopwatch.GetTimestamp( );
            Equal( "CheckTimedOut", (await firstStalled.WaitAsync( TimeSpan.FromSeconds( 3 ) )).Status );
            Equal( "CheckTimedOut", (await secondStalled.WaitAsync( TimeSpan.FromSeconds( 3 ) )).Status );
            True( Stopwatch.GetElapsedTime( deadline ) < TimeSpan.FromSeconds( 3 ), "logical deadline does not wait for worker reaping" );
            Equal( 2, started );
            Equal( "ActionsBusy", (await stalled.ResolveAsync( "x-scheme-handler/https", "https://busy.invalid/", default )).Status );
            reaped.SetResult( );
            var released = false;
            for ( var attempt = 0; attempt < 100 && !released; attempt++ )
            {
                await Task.Delay( 10 );
                released = (await stalled.ResolveAsync( "x-scheme-handler/https", "https://released.invalid/", default )).Status != "ActionsBusy";
            }

            True( released, "worker slots remain charged only until background reaping completes" );
            using ClipboardActionWorker invalid = new( host.FileName, prefix, environment, ( _, _, _, _, _ ) => ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "{\"status\":\"Resolved\",\"handlers\":[{\"desktopId\":\"../x.desktop\",\"label\":\"bad\\u0007\",\"rank\":\"default\",\"available\":true}],\"total\":1,\"moreAvailable\":false,\"revision\":\"r\"}",
                StandardError = ""
            } ) );
            Equal( "HandlerDiscoveryUnavailable", (await invalid.ResolveAsync( "x-scheme-handler/https", "https://example.invalid/", default )).Status );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
