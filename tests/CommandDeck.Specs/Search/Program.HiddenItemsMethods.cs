namespace CommandDeck.Specs;

public static partial class Program
{
    // Hiding is a display preference keyed by canonical identity: the row leaves
    // every catalog path, bookmark and alias survive underneath, and restore by
    // id brings it back without touching the item.
    private static async Task HiddenItemsLeaveEveryCatalogPathAndRestoreAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-hidden-items-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: true ), null, CancellationToken.None );
            var file = (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None ))
                .Single( value => value.Kind == ResultKinds.File );
            await catalog.SetAliasAsync( file, "nt", CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync( file.Id, "open", null, CancellationToken.None );
            var preferences = await catalog.GetPreferencesAsync( CancellationToken.None );

            var hidden = await catalog.SetHiddenAsync( file, true, CancellationToken.None );
            True( hidden.IsHidden && hidden.IsFavorite && hidden.Aliases.SequenceEqual( ["nt"] ) );
            Equal( "Hidden notes.txt", hidden.Message );
            True( (await catalog.GetHiddenStableIdsAsync( CancellationToken.None )).Contains( "file:notes" ) );

            var typed = await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None );
            True( typed.All( static value => value.Id != "file:notes" ), "typed search excludes the hidden file" );
            var fuzzy = await catalog.SearchAsync( QueryParser.Parse( "nots" ), 10, CancellationToken.None );
            True( fuzzy.All( static value => value.Id != "file:notes" ), "fuzzy search excludes the hidden file" );
            True( await catalog.FindAliasAsync( "nt", CancellationToken.None ) is null, "alias does not bypass hiding" );
            var home = await catalog.GetHomeResultsAsync( preferences, 20, CancellationToken.None );
            True( home.All( static value => value.Id != "file:notes" ), "home excludes the hidden bookmark" );

            var listed = await catalog.ListHiddenAsync( null, 64, 0, CancellationToken.None );
            Equal( 1, listed.Total );
            True( listed.Items[0].Id == "hidden:file:notes" && listed.Items[0].IsAvailable
                && listed.Items[0].SourceId == SourceIds.Files && listed.Items[0].Title == "notes.txt" );
            Equal( 0, (await catalog.ListHiddenAsync( "zzz", 64, 0, CancellationToken.None )).Total );
            Equal( 1, (await catalog.ListHiddenAsync( "NOTE", 64, 0, CancellationToken.None )).Total );

            var restored = await catalog.RestoreHiddenAsync( "hidden:file:notes", CancellationToken.None );
            True( !restored.IsHidden && restored.IsFavorite && restored.Aliases.SequenceEqual( ["nt"] ) );
            Equal( "Restored notes.txt", restored.Message );
            True( (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None ))
                .Any( static value => value.Id == "file:notes" ) );
            Equal( "file:notes", (await catalog.FindAliasAsync( "nt", CancellationToken.None ))?.Id );
            Equal( 0, (await catalog.GetHiddenStableIdsAsync( CancellationToken.None )).Count );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.RestoreHiddenAsync( "customization:file:notes", CancellationToken.None ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    // Hidden rows survive reindexing that removes their item, can be restored
    // while unavailable, ride along in the export, and go with a confirmed reset.
    private static async Task HiddenItemsSurviveReindexExportAndResetAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-hidden-lifecycle-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: true ), null, CancellationToken.None );
            var file = (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None ))
                .Single( value => value.Kind == ResultKinds.File );
            await catalog.SetHiddenAsync( file, true, CancellationToken.None );

            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: false ), null, CancellationToken.None );
            var listed = await catalog.ListHiddenAsync( null, 64, 0, CancellationToken.None );
            True( listed.Items.Single( ).StableId == "file:notes" && !listed.Items[0].IsAvailable );
            var export = await catalog.ExportAsync( CancellationToken.None );
            Equal( 3, export.Version );
            Equal( "file:notes", export.HiddenItems.Single( ).StableId );

            var restored = await catalog.RestoreHiddenAsync( listed.Items[0].Id, CancellationToken.None );
            True( !restored.IsHidden );
            Equal( 0, (await catalog.ListHiddenAsync( null, 64, 0, CancellationToken.None )).Total );

            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: true ), null, CancellationToken.None );
            file = (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None ))
                .Single( value => value.Kind == ResultKinds.File );
            await catalog.SetHiddenAsync( file, true, CancellationToken.None );
            await catalog.ResetAsync( CancellationToken.None );
            Equal( 0, (await catalog.ListHiddenAsync( null, 64, 0, CancellationToken.None )).Total );
            True( (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None ))
                .Any( static value => value.Id == "file:notes" ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    // The 513th hide is refused with a clear message instead of silently
    // dropping an older entry, and non-customizable results are refused.
    private static async Task HiddenItemsAreBoundedAndRequireDurableIdentityAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-hidden-bounds-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: true ), null, CancellationToken.None );
            await using ( var connection = await catalog.OpenAsync( CancellationToken.None ) )
            {
                await using var fill = connection.CreateCommand( );
                fill.CommandText = """
                    WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 512)
                    INSERT INTO hidden_items(stable_id, title_snapshot, source_id, hidden_unix_ms)
                    SELECT 'file:filler-' || n, 'Filler ' || n, 'files', n FROM seq;
                    """;
                await fill.ExecuteNonQueryAsync( );
            }

            var file = (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None ))
                .Single( value => value.Kind == ResultKinds.File );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.SetHiddenAsync( file, true, CancellationToken.None ) );
            var page = await catalog.ListHiddenAsync( null, 64, 0, CancellationToken.None );
            True( page.Items.Count == 64 && page.Total == 512 && page.Truncated );
            var last = await catalog.ListHiddenAsync( null, 64, 448, CancellationToken.None );
            True( last.Items.Count == 64 && !last.Truncated );

            SearchResult transient = new( file )
            {
                CanCustomize = false
            };
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await catalog.SetHiddenAsync( transient, true, CancellationToken.None ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    // Providers that never touch SQL (extension commands, launch variants) are
    // filtered by the coordinator against the same canonical id.
    private static async Task CoordinatorDropsHiddenCandidatesFromLiveProvidersAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-hidden-coordinator-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: true ), null, CancellationToken.None );
            var file = (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, CancellationToken.None ))
                .Single( value => value.Kind == ResultKinds.File );
            SearchResult variant = new( file )
            {
                Id = "file:notes#variant",
                Metadata = new Dictionary<string, string>( file.Metadata, StringComparer.Ordinal )
                {
                    ["catalogResultId"] = file.Id
                }
            };
            StaticSearchProvider provider = new( [variant] );
            SearchCoordinator coordinator = new( [provider], new SearchOptions( ), catalog, catalog, catalog.UsageHistory );
            SearchRequest request = new( )
            {
                Query = "notes",
                Limit = 20
            };

            True( (await coordinator.SearchAsync( request, CancellationToken.None ))
                .Any( static value => value.Id == "file:notes#variant" ) );
            await catalog.SetHiddenAsync( file, true, CancellationToken.None );
            True( (await coordinator.SearchAsync( request, CancellationToken.None ))
                .All( static value => value.Id != "file:notes#variant" ),
                "a launch variant of a hidden item is dropped before ranking" );
            await catalog.RestoreHiddenAsync( "hidden:" + file.Id, CancellationToken.None );
            True( (await coordinator.SearchAsync( request, CancellationToken.None ))
                .Any( static value => value.Id == "file:notes#variant" ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task VerifyHiddenItemsProtocolAsync(
        UnixProtocolClient client,
        string resultId,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 130,
            Operation = ProtocolOperations.HiddenSet,
            ResultId = resultId
        }, CancellationToken.None );
        var malformed = await ReadMessageAsync( client, 130, ProtocolMessageTypes.Error, cancellationToken );
        True( malformed?.Error?.Contains( "requires enabled", StringComparison.Ordinal ) == true );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 131,
            Operation = ProtocolOperations.HiddenSet,
            ResultId = resultId,
            Enabled = true
        }, CancellationToken.None );
        var hidden = await ReadMessageAsync( client, 131, ProtocolMessageTypes.CustomizationResult, cancellationToken );
        True( hidden?.Customization?.IsHidden == true );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 132,
            Operation = ProtocolOperations.Search,
            Query = "preview-note"
        }, CancellationToken.None );
        True( await ReadMessageAsync( client, 132, ProtocolMessageTypes.Result, cancellationToken ) is null,
            "a hidden item does not come back from an exact search" );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 133,
            Operation = ProtocolOperations.HiddenList,
            Limit = 64
        }, CancellationToken.None );
        var listed = await ReadMessageAsync( client, 133, ProtocolMessageTypes.HiddenItemsResult, cancellationToken );
        ArgumentNullException.ThrowIfNull( listed?.HiddenItems );
        Equal( 1, listed.HiddenItems.Total );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 134,
            Operation = ProtocolOperations.HiddenRestore,
            CustomizationId = listed.HiddenItems.Items[0].Id
        }, CancellationToken.None );
        var restored = await ReadMessageAsync( client, 134, ProtocolMessageTypes.CustomizationResult, cancellationToken );
        True( restored?.Customization?.IsHidden == false );
    }
}
