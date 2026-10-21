using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{

    public async ValueTask<CustomizationExport> ExportAsync( CancellationToken cancellationToken )
    {
        var searches = await GetSavedSearchesAsync( cancellationToken ).ConfigureAwait( false );
        return new( )
        {
            Quicklinks = await ExportQuicklinksAsync( cancellationToken ).ConfigureAwait( false ),
            Preferences = await GetPreferencesAsync( cancellationToken ).ConfigureAwait( false ),
            Customizations = await ReadCustomizationsAsync( cancellationToken ).ConfigureAwait( false ),
            HiddenItems = (await ListHiddenAsync( null, HiddenItemLimits.MaximumHiddenItems, 0, cancellationToken )
            .ConfigureAwait( false )).Items,
            FilterAliases = [.. await GetFilterAliasesAsync( cancellationToken ).ConfigureAwait( false )],
            SavedSearches = [.. searches.Where( static search => !search.Unavailable )],
            OmittedClipboardSearches = searches.Count( static search => search.Unavailable ),
            AiSelection = await _aiPreferences.GetAiSelectionAsync( cancellationToken ).ConfigureAwait( false ),
            AiTemplates = [.. await _aiPreferences.GetAiTemplatesAsync( cancellationToken ).ConfigureAwait( false )]
        };
    }

    // The assistant's preference record and grants are not launcher data:
    // AgentAuthority resets them, and the runtime reset calls it first.
    public async ValueTask<LauncherPreferences> ResetAsync( CancellationToken cancellationToken )
    {
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );
        await SqliteCatalog.ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            DELETE FROM user_quicklinks;
            DELETE FROM disabled_configured_quicklinks;
            DELETE FROM aliases;
            DELETE FROM filter_aliases;
            DELETE FROM saved_searches;
            DELETE FROM favorites;
            DELETE FROM source_preferences;
            DELETE FROM launcher_preferences;
            DELETE FROM ai_preferences WHERE key IN ('provider_id', 'model_id');
            DELETE FROM ai_prompt_templates;
            DELETE FROM hidden_items;
            """,
            cancellationToken ).ConfigureAwait( false );
        await _publishQuicklinks( connection, transaction, cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        InvalidateHiddenItems( );
        return LauncherPreferences.CreateDefault( );
    }
}
