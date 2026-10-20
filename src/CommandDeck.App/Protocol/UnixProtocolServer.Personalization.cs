namespace CommandDeck;

public sealed partial class UnixProtocolServer
{

    private async ValueTask HandlePersonalizationRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        var message = request.Operation switch
        {
            ProtocolOperations.ExtensionsManage => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.ExtensionsResult,
                Extensions = await _runtime.ManageExtensionsAsync( request.Extension ?? new( ), request.Query, request.Offset, request.Limit, cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.Bootstrap => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.BootstrapResult,
                Bootstrap = await _runtime.GetBootstrapAsync( cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.PreferencesGet => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.PreferencesResult,
                Preferences = LimitCustomizationSummaries(
                    await _runtime.GetPreferencesAsync(
                        request.IncludeShortcuts,
                        cancellationToken ).ConfigureAwait( false ) )
            },
            ProtocolOperations.PreferencesUpdate => await UpdatePreferencesAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.FavoriteSet => await SetFavoriteAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.AliasSet => await SetAliasAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.FilterAliasSet => await SetFilterAliasAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.FavoriteRemoveOrphan => await RemoveOrphanAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.FavoriteReorder => await ReorderFavoritesAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.FavoriteMove => await MoveFavoriteAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.CustomizationsExport => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.ExportResult,
                Export = await _runtime.ExportCustomizationsAsync( cancellationToken )
                    .ConfigureAwait( false )
            },
            ProtocolOperations.CustomizationsReset => await ResetCustomizationsAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.HiddenSet => await SetHiddenAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.HiddenRestore => await RestoreHiddenAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.HiddenList => await ListHiddenAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.QueryHistoryGet => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.QueryHistoryResult,
                QueryHistory = await _runtime.QueryHistory.GetAsync(
                    request.Limit,
                    cancellationToken ).ConfigureAwait( false )
            },
            ProtocolOperations.QueryHistoryDelete => await DeleteQueryHistoryAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.QueryHistoryReset => await ResetQueryHistoryAsync(
                request,
                cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.SavedSearchGet => new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.SavedSearchResult,
                SavedSearches = await _runtime.GetSavedSearchesAsync( cancellationToken )
                    .ConfigureAwait( false )
            },
            ProtocolOperations.SavedSearchSave => await SaveSavedSearchAsync(
                request, cancellationToken ).ConfigureAwait( false ),
            ProtocolOperations.SavedSearchDelete => await DeleteSavedSearchAsync(
                request, cancellationToken ).ConfigureAwait( false ),
            _ => throw UnknownOperation( request )
        };
        if ( message.Export is not null )
        {
            if ( message.Export.Customizations.Count > ProtocolLimits.MaximumCustomizationSummaries )
            {
                throw new InvalidOperationException(
                    "The customization export exceeds the socket summary limit. " +
                    "Run 'commanddeck preferences export' for a complete legacy-data export." );
            }

            _ = ProtocolJson.SerializeBoundedCustomizationExport( message.Export );
        }

        await writer.WriteAsync( message ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask<ProtocolMessage> SetFilterAliasAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        if ( string.IsNullOrWhiteSpace( request.Alias ) )
        {
            throw new InvalidOperationException( "filter-alias.set requires alias." );
        }
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.FilterAliasResult,
            FilterAliases = await _runtime.SetFilterAliasAsync(
                request.Alias,
                request.TargetFilterId,
                cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> SaveSavedSearchAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        if ( request.SavedSearch is null )
        {
            throw new InvalidOperationException( "saved-search.save requires savedSearch." );
        }
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.SavedSearchResult,
            SavedSearches = await _runtime.SaveSavedSearchAsync(
                request.SavedSearch, cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> DeleteSavedSearchAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        if ( !request.Confirmed )
        {
            throw new InvalidOperationException( "saved-search.delete requires confirmed: true." );
        }
        if ( string.IsNullOrWhiteSpace( request.SavedSearchId ) )
        {
            throw new InvalidOperationException( "saved-search.delete requires savedSearchId." );
        }
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.SavedSearchResult,
            SavedSearches = await _runtime.DeleteSavedSearchAsync(
                request.SavedSearchId, cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> DeleteQueryHistoryAsync( ProtocolRequest request, CancellationToken cancellationToken )
    {
        var input = new QueryHistoryDeleteInput( request.HistoryId, request.Confirmed );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.QueryHistoryResult,
            QueryHistory = await _runtime.QueryHistory.DeleteAndReadAsync( input, request.Limit, cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> ResetQueryHistoryAsync( ProtocolRequest request, CancellationToken cancellationToken )
    {
        var input = new QueryHistoryResetInput( request.Confirmed );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.QueryHistoryResult,
            QueryHistory = await _runtime.QueryHistory.ResetAndReadAsync( input, request.Limit, cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> UpdatePreferencesAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        if ( request.SourcePatch is not null )
        {
            await _runtime.PatchSourcePreferencesAsync( request.SourcePatch, cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            var preferences = request.Preferences;
            if ( preferences is null )
            {
                throw new InvalidOperationException( "preferences.update requires preferences." );
            }

            await _runtime.UpdatePreferencesAsync( preferences, cancellationToken ).ConfigureAwait( false );
        }
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.PreferencesResult,
            Preferences = LimitCustomizationSummaries(
                await _runtime.GetPreferencesAsync( false, cancellationToken ).ConfigureAwait( false ) )
        };
    }

    private async ValueTask<ProtocolMessage> SetFavoriteAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        var resultId = RequireValue( request.ResultId, "favorite.set requires resultId." );
        var enabled = request.Enabled;
        if ( enabled is null )
        {
            throw new InvalidOperationException( "favorite.set requires enabled." );
        }

        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.CustomizationResult,
            Customization = await _runtime.SetFavoriteAsync(
                resultId,
                enabled.Value,
                cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> SetAliasAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        var resultId = RequireValue( request.ResultId, "alias.set requires resultId." );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.CustomizationResult,
            Customization = await _runtime.SetAliasAsync(
                resultId,
                request.Alias,
                cancellationToken ).ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> RemoveOrphanAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        var id = RequireValue(
            request.CustomizationId,
            "favorite.remove-orphan requires customizationId." );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.CustomizationResult,
            Customization = await _runtime.RemoveOrphanAsync( id, cancellationToken )
                .ConfigureAwait( false )
        };
    }

    private async ValueTask<ProtocolMessage> ResetCustomizationsAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        if ( !request.Confirmed )
        {
            throw new InvalidOperationException( "customizations.reset requires confirmed: true." );
        }

        await _runtime.ResetCustomizationsAsync( cancellationToken ).ConfigureAwait( false );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.PreferencesResult,
            Preferences = LimitCustomizationSummaries(
                await _runtime.GetPreferencesAsync( false, cancellationToken ).ConfigureAwait( false ) )
        };
    }

    private async ValueTask<ProtocolMessage> MoveFavoriteAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        var resultId = RequireValue( request.ResultId, "favorite.move requires resultId." );
        var targetResultId = RequireValue( request.TargetResultId, "favorite.move requires targetResultId." );
        await _runtime.MoveFavoriteAsync( resultId, targetResultId, cancellationToken ).ConfigureAwait( false );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.PreferencesResult,
            Preferences = LimitCustomizationSummaries(
                await _runtime.GetPreferencesAsync( false, cancellationToken ).ConfigureAwait( false ) )
        };
    }

    private async ValueTask<ProtocolMessage> ReorderFavoritesAsync(
        ProtocolRequest request,
        CancellationToken cancellationToken )
    {
        await _runtime.ReorderFavoritesAsync( request.StableIds, cancellationToken )
            .ConfigureAwait( false );
        return new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.PreferencesResult,
            Preferences = LimitCustomizationSummaries(
                await _runtime.GetPreferencesAsync( false, cancellationToken ).ConfigureAwait( false ) )
        };
    }

    private static PreferencesSnapshot LimitCustomizationSummaries( PreferencesSnapshot snapshot )
    {
        var total = snapshot.TotalCustomizationCount > 0
            ? snapshot.TotalCustomizationCount
            : snapshot.Customizations.Count;
        var truncated = total > ProtocolLimits.MaximumCustomizationSummaries;
        return new PreferencesSnapshot
        {
            Preferences = snapshot.Preferences,
            Customizations = truncated
                ? snapshot.Customizations.Take( ProtocolLimits.MaximumCustomizationSummaries ).ToList( )
                : snapshot.Customizations,
            Shortcuts = snapshot.Shortcuts,
            TotalCustomizationCount = total,
            CustomizationsTruncated = truncated
        };
    }
}
