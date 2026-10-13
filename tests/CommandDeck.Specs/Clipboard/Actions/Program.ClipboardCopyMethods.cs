namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardCopyBindsEntryOwnerAndGenerationAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-copy-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
            store.Enabled = true;
            const string content = "synthetic café\r\n<script>literal</script>";
            store.Capture( content );
            List<string> copied = [];
            ClipboardHistoryService service = new( store, copyText: ( text, _ ) =>
            {
                copied.Add( text );
                return ValueTask.FromResult( ActionExecutionResult.Success( "Copied", "must not escape" ) );
            } );
            var request = OpenClipboardCopyRequest( service );
            var result = await service.HandleAsync( "owner", request, default );
            True( result.Action!.Succeeded );
            True( result.Action.DetailMarkdown is null, "copy result contains no payload or helper output" );
            Equal( content, copied.Single( ) );
            service.RegisterOwner( "other" );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await service.HandleAsync( "other", request, default ) );

            store.Capture( content );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await service.HandleAsync( "owner", request, default ) );
            service.CloseGeneration( "owner", request.Clipboard!.FrontendInstance!, 1 );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await service.HandleAsync( "owner", request, default ) );
            Equal( 1, copied.Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardCopyCancellationRetainsBusySlotUntilCompletionAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-copy-cancel-" ).FullName;
        try
        {
            foreach ( var close in new[] { "generation", "session", "disconnect", "clear", "delete" } )
            {
                using ClipboardHistoryStore store = new( Path.Combine( root, close + ".db" ), new ClipboardHistoryOptions( ) );
                store.Enabled = true;
                store.Capture( "synthetic pending copy" );
                TaskCompletionSource<ActionExecutionResult> completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
                CancellationToken admitted = default;
                ClipboardHistoryService service = new( store, copyText: ( _, token ) =>
                {
                    admitted = token;
                    return new( completion.Task );
                } );
                var request = OpenClipboardCopyRequest( service );
                var pending = service.HandleAsync( "owner", request, default ).AsTask( );
                await ThrowsAsync<InvalidOperationException>( async ( ) => await service.HandleAsync( "owner", request, default ) );
                var value = request.Clipboard!;
                switch ( close )
                {
                    case "generation":
                        service.CloseGeneration( "owner", value.FrontendInstance!, 1 );
                        break;
                    case "session":
                        service.Close( "owner", value.SessionId! );
                        break;
                    case "disconnect":
                        service.RevokeOwner( "owner" );
                        break;
                    case "clear":
                        service.Clear( "owner", store.StoreRevision );
                        break;
                    case "delete":
                        service.Mutate( "owner", value.SessionId!, value.EntryId!, value.Incarnation!, value.EntryRevision, "delete" );
                        break;
                }

                True( admitted.IsCancellationRequested, close + " revokes pending copy" );
                True( !pending.IsCompleted, "revocation does not falsely report the child stopped" );
                completion.SetCanceled( admitted );
                var result = await pending;
                True( !result.Action!.Succeeded );
                service.RemoveOwner( "owner" );
                store.Capture( "synthetic next copy" );
                var next = OpenClipboardCopyRequest( service );
                completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
                var nextCopy = service.HandleAsync( "owner", next, default ).AsTask( );
                completion.SetResult( ActionExecutionResult.Success( ) );
                True( (await nextCopy).Action!.Succeeded, "completed cleanup releases the one copy slot" );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardCopyRejectsPayloadDamageAfterBrowserOpenAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-copy-damage-" ).FullName;
        try
        {
            foreach ( var missing in new[] { true, false } )
            {
                var path = Path.Combine( root, missing + ".db" );
                using ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) );
                store.Enabled = true;
                store.Capture( "synthetic copy" );
                var copied = false;
                ClipboardHistoryService service = new( store, copyText: ( _, _ ) =>
                {
                    copied = true;
                    return ValueTask.FromResult( ActionExecutionResult.Success( ) );
                } );
                var request = OpenClipboardCopyRequest( service );
                var payload = Directory.GetFiles( path + ".payloads" ).Single( );
                if ( missing )
                {
                    File.Delete( payload );
                }
                else
                {
                    File.WriteAllText( payload, "changed bytes" );
                }

                await ThrowsAsync<IOException>( async ( ) => await service.HandleAsync( "owner", request, default ) );
                True( !copied, "damaged payload never reaches the writer" );
                Equal( 0, store.Search( "", 10 ).Count );
                Equal( 0, Directory.GetFiles( path + ".payloads" ).Length );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static ProtocolRequest OpenClipboardCopyRequest( ClipboardHistoryService service )
    {
        service.RegisterOwner( "owner" );
        var instance = Guid.NewGuid( ).ToString( "N" );
        var session = service.Open( "owner", instance, 1 );
        var entry = service.Query( "owner", session, new( ), false, 1 ).Entries[0];
        return new( )
        {
            RequestId = 1,
            Operation = ProtocolOperations.ClipboardAction,
            Clipboard = new( )
            {
                FrontendInstance = instance,
                OpenGeneration = 1,
                SessionId = session,
                QueryGeneration = 1,
                EntryId = entry.Id,
                Incarnation = entry.Incarnation,
                EntryRevision = entry.Revision,
                Action = "copy"
            }
        };
    }
}
