using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async ValueTask<UnixProtocolClient> ConnectWithRetryAsync( string socket )
    {
        var startupDeadline = TimeSpan.FromSeconds( 5 );
        var started = Stopwatch.GetTimestamp( );
        while ( true )
        {
            try
            {
                return await UnixProtocolClient.ConnectAsync( socket, CancellationToken.None );
            }
            catch ( System.Net.Sockets.SocketException ) when ( Stopwatch.GetElapsedTime( started ) < startupDeadline )
            {
                await Task.Delay( 25 );
            }
        }
    }

    private static async Task VerifyPreviewProtocolAsync(
        UnixProtocolClient client,
        string socket,
        string editorCapture,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 4,
            Operation = ProtocolOperations.Search,
            Query = "preview-note"
        }, CancellationToken.None );
        SearchResult? fileResult = null;
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.Type == ProtocolMessageTypes.Result &&
                message.RequestId == 4 &&
                message.Result?.Kind == ResultKinds.File )
            {
                fileResult = message.Result;
            }

            if ( message?.Type == ProtocolMessageTypes.Done && message.RequestId == 4 )
            {
                break;
            }
        }

        True( fileResult is not null );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 5,
            Operation = ProtocolOperations.Preview,
            ResultId = fileResult!.Id,
            InvocationReference = fileResult.InvocationReference
        }, CancellationToken.None );
        var previewMessage = await ReadMessageAsync(
            client,
            requestId: 5,
            ProtocolMessageTypes.PreviewResult,
            cancellationToken );
        Equal( "# Preview\n\nProtocol content", previewMessage?.Preview?.Text );
        Equal( "markdown", previewMessage?.Preview?.Language );

        await VerifyPreviewEditorProtocolAsync( client, socket, fileResult.Id, fileResult.InvocationReference!, editorCapture, cancellationToken );
        await VerifyImagePreviewProtocolAsync( client, cancellationToken );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 6,
            Operation = ProtocolOperations.Preview,
            ResultId = "file:forged",
            InvocationReference = fileResult.InvocationReference
        }, CancellationToken.None );
        var forgedError = await ReadMessageAsync(
            client,
            requestId: 6,
            ProtocolMessageTypes.Error,
            cancellationToken );
        True( forgedError?.Error?.Contains( "expired", StringComparison.Ordinal ) == true );

        await client.SendRawAsync(
            """{"version":3,"requestId":601,"operation":"/forbidden/path/sentinel"}""",
            CancellationToken.None );
        var safeError = await ReadMessageAsync(
            client,
            601,
            ProtocolMessageTypes.Error,
            cancellationToken );
        Equal( "The request could not be completed safely.", safeError?.Error );
    }

    private static async Task<ProtocolMessage?> ReadMessageAsync(
        UnixProtocolClient client,
        long requestId,
        string expectedType,
        CancellationToken cancellationToken )
    {
        ProtocolMessage? expected = null;
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId == requestId && message.Type == expectedType )
            {
                expected = message;
            }

            if ( message?.RequestId == requestId &&
                message.Type == ProtocolMessageTypes.Error &&
                expectedType != ProtocolMessageTypes.Error )
            {
                throw new InvalidOperationException(
                    $"Protocol request {requestId} returned an error while awaiting " +
                    $"{expectedType}: {message.Error ?? "No error detail."}" );
            }

            if ( message?.RequestId == requestId && message.Type == ProtocolMessageTypes.Done )
            {
                return expected;
            }
        }

        return expected;
    }

    private static async Task VerifyPersonalizationProtocolAsync(
        UnixProtocolClient client,
        ILauncherPreferenceStore preferencesStore,
        string database,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 7,
            Operation = ProtocolOperations.Bootstrap
        }, CancellationToken.None );
        var bootstrap = await ReadMessageAsync(
            client,
            7,
            ProtocolMessageTypes.BootstrapResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( bootstrap );
        ArgumentNullException.ThrowIfNull( bootstrap.Bootstrap );
        Equal( LauncherSourceRegistry.Configurable.Count, bootstrap.Bootstrap.Preferences.Sources.Count );
        Equal(
            "System Menu",
            bootstrap.Bootstrap.Sources.Single( value =>
                value.Id == SourceIds.OmarchySystem ).Label );
        Equal(
            "Services",
            bootstrap.Bootstrap.Sources.Single( value =>
                value.Id == SourceIds.SystemdUnits ).Label );
        Equal(
            "Timezone",
            bootstrap.Bootstrap.Sources.Single( value =>
                value.Id == SourceIds.Timezone ).Label );
        Equal(
            "Emoji",
            bootstrap.Bootstrap.Sources.Single( value =>
                value.Id == SourceIds.Emoji ).Label );
        True( bootstrap.Bootstrap.Filters.Any( value => value.Id == "system-menu" ) );
        True( bootstrap.Bootstrap.Filters.Any( value => value.Id == "services" ) );
        True( bootstrap.Bootstrap.Filters.Any( value => value.Id == "timezone" ) );
        True( bootstrap.Bootstrap.Filters.Any( value => value.Id == "emoji" ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 8,
            Operation = ProtocolOperations.Search,
            Query = "preview-note"
        }, CancellationToken.None );
        var result = await ReadMessageAsync(
            client,
            8,
            ProtocolMessageTypes.Result,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( result );
        ArgumentNullException.ThrowIfNull( result.Result );
        True( result.Result.CanCustomize );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 9,
            Operation = ProtocolOperations.FavoriteSet,
            ResultId = result.Result.Id,
            Enabled = true
        }, CancellationToken.None );
        var favorite = await ReadMessageAsync(
            client,
            9,
            ProtocolMessageTypes.CustomizationResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( favorite );
        ArgumentNullException.ThrowIfNull( favorite.Customization );
        True( favorite.Customization.IsFavorite );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 10,
            Operation = ProtocolOperations.AliasSet,
            ResultId = result.Result.Id,
            Alias = "note"
        }, CancellationToken.None );
        var alias = await ReadMessageAsync(
            client,
            10,
            ProtocolMessageTypes.CustomizationResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( alias );
        ArgumentNullException.ThrowIfNull( alias.Customization );
        Equal( "note", alias.Customization.Aliases[0] );

        await VerifyHiddenItemsProtocolAsync( client, result.Result.Id, cancellationToken );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 108,
            Operation = ProtocolOperations.FavoriteReorder,
            StableIds = [result.Result.Id]
        }, CancellationToken.None );
        var reordered = await ReadMessageAsync(
            client,
            108,
            ProtocolMessageTypes.PreferencesResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( reordered?.Preferences );
        Equal( result.Result.Id, reordered.Preferences.Customizations[0].StableId );

        await VerifyFavoriteMoveProtocolAsync( client, result.Result.Id, cancellationToken );

        SearchResult unavailableFavorite = new( )
        {
            Id = "file:protocol-orphan",
            Kind = ResultKinds.File,
            Title = "Unavailable protocol favorite",
            ProviderId = "files",
            SourceId = SourceIds.Files,
            CanCustomize = true
        };
        await preferencesStore.SetFavoriteAsync(
            unavailableFavorite,
            true,
            CancellationToken.None );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 109,
            Operation = ProtocolOperations.FavoriteRemoveOrphan,
            CustomizationId = "customization:file:protocol-orphan"
        }, CancellationToken.None );
        var removedOrphan = await ReadMessageAsync(
            client,
            109,
            ProtocolMessageTypes.CustomizationResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( removedOrphan?.Customization );
        Equal( "file:protocol-orphan", removedOrphan.Customization.StableId );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 101,
            Operation = ProtocolOperations.FavoriteSet,
            ResultId = result.Result.Id
        }, CancellationToken.None );
        var malformedFavorite = await ReadMessageAsync(
            client,
            101,
            ProtocolMessageTypes.Error,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( malformedFavorite?.Error );
        True( malformedFavorite.Error.Contains( "requires enabled", StringComparison.Ordinal ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 11,
            Operation = ProtocolOperations.PreferencesGet
        }, CancellationToken.None );
        var preferences = await ReadMessageAsync(
            client,
            11,
            ProtocolMessageTypes.PreferencesResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( preferences );
        ArgumentNullException.ThrowIfNull( preferences.Preferences );
        Equal( 1, preferences.Preferences.Customizations.Count );
        True( preferences.Preferences.Customizations[0].IsFavorite );
        Equal( "note", preferences.Preferences.Customizations[0].Aliases[0] );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 12,
            Operation = ProtocolOperations.CustomizationsExport
        }, CancellationToken.None );
        var export = await ReadMessageAsync(
            client,
            12,
            ProtocolMessageTypes.ExportResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( export );
        ArgumentNullException.ThrowIfNull( export.Export );
        Equal( 3, export.Export.Version );
        Equal( "note", export.Export.Customizations[0].Aliases[0] );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 13,
            Operation = ProtocolOperations.FavoriteSet,
            ResultId = "file:forged",
            Enabled = true
        }, CancellationToken.None );
        var forged = await ReadMessageAsync(
            client,
            13,
            ProtocolMessageTypes.Error,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( forged );
        ArgumentNullException.ThrowIfNull( forged.Error );
        True( forged.Error.Contains( "expired", StringComparison.Ordinal ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 102,
            Operation = ProtocolOperations.CustomizationsReset
        }, CancellationToken.None );
        var rejectedReset = await ReadMessageAsync(
            client,
            102,
            ProtocolMessageTypes.Error,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( rejectedReset?.Error );
        True( rejectedReset.Error.Contains( "confirmed", StringComparison.Ordinal ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 103,
            Operation = ProtocolOperations.PreferencesGet
        }, CancellationToken.None );
        var unchanged = await ReadMessageAsync(
            client,
            103,
            ProtocolMessageTypes.PreferencesResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( unchanged?.Preferences );
        Equal( 1, unchanged.Preferences.Customizations.Count );
        Equal( "note", unchanged.Preferences.Customizations[0].Aliases[0] );

        await client.SendRawAsync(
            """{"version":3,"requestId":104,"operation":"favorite.reorder","stableIds":null}""",
            CancellationToken.None );
        var nullStableIds = await ReadMessageAsync(
            client,
            104,
            ProtocolMessageTypes.Error,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( nullStableIds?.Error );
        True( nullStableIds.Error.Contains( "stableIds", StringComparison.Ordinal ) );

        await client.SendRawAsync(
            """{"version":3,"requestId":105,"operation":"preferences.update","preferences":{"sources":null}}""",
            CancellationToken.None );
        var nullSources = await ReadMessageAsync(
            client,
            105,
            ProtocolMessageTypes.Error,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( nullSources?.Error );
        True( nullSources.Error.Contains( "preferences.sources", StringComparison.Ordinal ) );

        await client.SendRawAsync(
            """{"version":3,"requestId":106,"operation":"preferences.update","preferences":{"sources":[null]}}""",
            CancellationToken.None );
        var nullSource = await ReadMessageAsync(
            client,
            106,
            ProtocolMessageTypes.Error,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( nullSource?.Error );
        True( nullSource.Error.Contains( "preferences.sources", StringComparison.Ordinal ) );

        SearchResult oversizedFavorite = new( )
        {
            Id = "file:oversized-export",
            Kind = ResultKinds.File,
            Title = new string( '\u00e9', 140_000 ),
            ProviderId = "files",
            SourceId = SourceIds.Files,
            CanCustomize = true
        };
        await preferencesStore.SetFavoriteAsync(
            oversizedFavorite,
            true,
            CancellationToken.None );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 107,
            Operation = ProtocolOperations.CustomizationsExport
        }, CancellationToken.None );
        var oversizedExport = await ReadMessageAsync(
            client,
            107,
            ProtocolMessageTypes.Error,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( oversizedExport?.Error );
        True( oversizedExport.Error.Contains( "256 KiB", StringComparison.Ordinal ) );

        await VerifyLegacyCustomizationOverflowProtocolAsync(
            client,
            database,
            cancellationToken );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 14,
            Operation = ProtocolOperations.CustomizationsReset,
            Confirmed = true
        }, CancellationToken.None );
        var reset = await ReadMessageAsync(
            client,
            14,
            ProtocolMessageTypes.PreferencesResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( reset );
        ArgumentNullException.ThrowIfNull( reset.Preferences );
        Equal( 0, reset.Preferences.Customizations.Count );

        var fallbackPreferences = LauncherPreferences.CreateDefault( );
        fallbackPreferences = new LauncherPreferences
        {
            Sources = fallbackPreferences.Sources,
            WebFallbackQuicklinkId = "web"
        };
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 15,
            Operation = ProtocolOperations.PreferencesUpdate,
            Preferences = fallbackPreferences
        }, CancellationToken.None );
        _ = await ReadMessageAsync(
            client,
            15,
            ProtocolMessageTypes.PreferencesResult,
            cancellationToken );

        const string fallbackQuery = "definitely-no-commanddeck-match-82f44";
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 16,
            Operation = ProtocolOperations.Search,
            Query = fallbackQuery
        }, CancellationToken.None );
        var fallback = await ReadMessageAsync(
            client,
            16,
            ProtocolMessageTypes.Result,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( fallback?.Result );
        Equal( "fallback", fallback.Result.ProviderId );
        True( !fallback.Result.CanCustomize );
        Equal(
            $"https://example.test/search?q={Uri.EscapeDataString( fallbackQuery )}",
            fallback.Result.Actions[0].Arguments["urlTemplate"] );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 17,
            Operation = ProtocolOperations.Search,
            Query = fallbackQuery,
            Expression = new QueryExpression { IncludeFilterIds = ["files"] }
        }, CancellationToken.None );
        var explicitFallback = false;
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.RequestId == 17 && message.Type == ProtocolMessageTypes.Result )
            {
                explicitFallback = true;
            }

            if ( message?.RequestId == 17 && message.Type == ProtocolMessageTypes.Done )
            {
                break;
            }
        }

        True( !explicitFallback );
    }

}
