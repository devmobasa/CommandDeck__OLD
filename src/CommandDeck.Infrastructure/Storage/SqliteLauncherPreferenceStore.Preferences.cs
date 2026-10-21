using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    public async ValueTask<LauncherPreferences> GetPreferencesAsync(
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        var defaults = LauncherPreferences.CreateDefault( );
        var registry = ContributionSnapshot;
        var sources = defaults.Sources.ToDictionary(
            static value => value.SourceId,
            StringComparer.Ordinal );
        foreach ( var source in registry.Sources )
        {
            sources.TryAdd( source.Id, new SourcePreference
            {
                SourceId = source.Id,
                EnabledInSearch = true,
                EnabledOnHome = false,
                Position = sources.Count + 1
            } );
        }

        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using ( var command = connection.CreateCommand( ) )
        {
            command.CommandText = """
                SELECT source_id, enabled_in_search, enabled_on_home, position
                FROM source_preferences;
                """;
            await using var reader =
                await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                var sourceId = reader.GetString( 0 );
                if ( !sources.ContainsKey( sourceId ) )
                {
                    continue;
                }

                sources[sourceId] = new SourcePreference
                {
                    SourceId = sourceId,
                    EnabledInSearch = reader.GetInt64( 1 ) != 0,
                    EnabledOnHome = reader.GetInt64( 2 ) != 0,
                    Position = reader.GetInt32( 3 )
                };
            }
        }

        var hideAfterAction = true;
        var clipboardShortcut = defaults.ClipboardHistoryShortcut;
        var workspacePreviewMode = defaults.WorkspacePreviewMode;
        var reduceMotion = false;
        var searchHomeOnly = false;
        long preferenceRevision = 0;
        string? fallback = null;
        var preferredEditor = defaults.PreferredEditor;
        var heyOpenTarget = defaults.HeyOpenTarget;
        var graphicalEditor = defaults.GraphicalEditor;
        var terminalEditor = defaults.TerminalEditor;
        Dictionary<string, string> choices = new( StringComparer.Ordinal );
        await using ( var command = connection.CreateCommand( ) )
        {
            command.CommandText = "SELECT key, value FROM launcher_preferences;";
            await using var reader =
                await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                var key = reader.GetString( 0 );
                var value = reader.GetString( 1 );
                if ( key == "hide_after_action" )
                {
                    hideAfterAction = value != "false";
                }
                else if ( key == "clipboard_history_shortcut" )
                {
                    clipboardShortcut = ClipboardShortcut.Parse( value );
                }
                else if ( key == "reduce_motion" )
                {
                    reduceMotion = value == "true";
                }
                else if ( key == "workspace_preview_mode" )
                {
                    workspacePreviewMode = WorkspacePreviewModes.Normalize( value );
                }
                else if ( key == "search_home_only" )
                {
                    searchHomeOnly = value == "true";
                }
                else if ( key == "web_fallback_quicklink_id" )
                {
                    fallback = string.IsNullOrWhiteSpace( value ) ? null : value;
                }
                else if ( key == "preferred_editor" )
                {
                    preferredEditor = value;
                }
                else if ( key == "hey_open_target" )
                {
                    heyOpenTarget = HeyOpenTargets.Normalize( value );
                }
                else if ( key == "graphical_editor" )
                {
                    graphicalEditor = System.Text.Json.JsonSerializer.Deserialize(
                        value, CommandDeckJsonContext.Default.EditorAdapter ) ?? graphicalEditor;
                }
                else if ( key == "terminal_editor" )
                {
                    terminalEditor = System.Text.Json.JsonSerializer.Deserialize(
                        value, CommandDeckJsonContext.Default.EditorAdapter ) ?? terminalEditor;
                }
                else if ( key is RememberSearchesKey or LearnFromUseKey or OmarchyMenuStateKey or TopMatchesKey )
                {
                    choices[key] = value;
                }
                else if ( key == "source_preferences_revision" )
                {
                    preferenceRevision = long.Parse( value, System.Globalization.CultureInfo.InvariantCulture );
                }
            }
        }

        return new LauncherPreferences
        {
            RegistryRevision = registry.Revision,
            PreferenceRevision = preferenceRevision,
            Sources = sources.Values.OrderBy( static value => value.Position ).ThenBy( value => value.SourceId, StringComparer.Ordinal )
                .Select( ( source, index ) => new SourcePreference { SourceId = source.SourceId, EnabledInSearch = source.EnabledInSearch, EnabledOnHome = source.EnabledOnHome, Position = index + 1 } ).ToList( ),
            HideAfterAction = hideAfterAction,
            ClipboardHistoryShortcut = clipboardShortcut,
            WorkspacePreviewMode = workspacePreviewMode,
            ReduceMotion = reduceMotion,
            SearchHomeOnly = searchHomeOnly,
            WebFallbackQuicklinkId = fallback,
            PreferredEditor = preferredEditor,
            HeyOpenTarget = heyOpenTarget,
            GraphicalEditor = EditorAdapterValidator.Validate( graphicalEditor, terminal: false ),
            TerminalEditor = EditorAdapterValidator.Validate( terminalEditor, terminal: true ),
            RememberSearches = ReadChoice( choices.GetValueOrDefault( RememberSearchesKey ) ),
            LearnFromUse = ReadChoice( choices.GetValueOrDefault( LearnFromUseKey ) ),
            OmarchyMenuState = ReadChoice( choices.GetValueOrDefault( OmarchyMenuStateKey ) ),
            TopMatches = ReadChoice( choices.GetValueOrDefault( TopMatchesKey ) )
        };
    }

    private const string RememberSearchesKey = "remember_searches";
    private const string LearnFromUseKey = "learn_from_use";
    private const string OmarchyMenuStateKey = "omarchy_menu_state";
    private const string TopMatchesKey = "top_matches";

    // A choice made in Settings; a missing, empty or unknown value follows the configuration.
    private static bool? ReadChoice( string? value ) => value switch
    {
        "true" => true,
        "false" => false,
        _ => null
    };

    private static string WriteChoice( bool? value ) => value switch
    {
        true => "true",
        false => "false",
        null => string.Empty
    };

    public async ValueTask<PreferencesSnapshot> GetSnapshotAsync(
        bool includeShortcuts,
        CancellationToken cancellationToken )
    {
        var preferences = await GetPreferencesAsync( cancellationToken )
            .ConfigureAwait( false );
        var customizations = await ReadCustomizationsAsync( cancellationToken )
            .ConfigureAwait( false );
        var shortcuts = includeShortcuts
            ? await HyprlandShortcutInspector.InspectAsync( cancellationToken ).ConfigureAwait( false )
            : [];
        return new PreferencesSnapshot
        {
            Preferences = preferences,
            Customizations = customizations,
            Shortcuts = [.. shortcuts],
            TotalCustomizationCount = customizations.Count
        };
    }

    public async ValueTask<LauncherPreferences> ReplacePreferencesAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken )
    {
        ValidatePreferences( preferences );
        var now = _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( );
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );

        var clipboardShortcut = ClipboardShortcut.Parse( preferences.ClipboardHistoryShortcut );
        await EnsureClipboardShortcutAvailableAsync( connection, transaction, clipboardShortcut, cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync( connection, transaction, "clipboard_history_shortcut", clipboardShortcut, now,
            cancellationToken ).ConfigureAwait( false );

        await EnsureRegistryRevisionAsync( connection, transaction, preferences.RegistryRevision, cancellationToken ).ConfigureAwait( false );
        if ( preferences.RegistryRevision > 0 )
        {
            await using var revision = connection.CreateCommand( );
            revision.Transaction = transaction;
            revision.CommandText = "SELECT value FROM launcher_preferences WHERE key = 'source_preferences_revision';";
            var stored = ( string? ) await revision.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ) ?? "0";
            if ( stored != preferences.PreferenceRevision.ToString( System.Globalization.CultureInfo.InvariantCulture ) )
            {
                throw new InvalidOperationException( "Source preferences changed. Refresh Settings and try again." );
            }
        }
        foreach ( var source in preferences.Sources )
        {
            await using var insert = connection.CreateCommand( );
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO source_preferences(
                    source_id, enabled_in_search, enabled_on_home, position, updated_unix_ms)
                VALUES($source, $search, $home, $position, $updated)
                ON CONFLICT(source_id) DO UPDATE SET enabled_in_search = excluded.enabled_in_search,
                    enabled_on_home = excluded.enabled_on_home, position = excluded.position, updated_unix_ms = excluded.updated_unix_ms;
                """;
            insert.Parameters.AddWithValue( "$source", source.SourceId );
            insert.Parameters.AddWithValue( "$search", source.EnabledInSearch ? 1 : 0 );
            insert.Parameters.AddWithValue( "$home", source.EnabledOnHome ? 1 : 0 );
            insert.Parameters.AddWithValue( "$position", source.Position );
            insert.Parameters.AddWithValue( "$updated", now );
            await insert.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "search_home_only",
            preferences.SearchHomeOnly ? "true" : "false",
            now,
            cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "hide_after_action",
            preferences.HideAfterAction ? "true" : "false",
            now,
            cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "reduce_motion",
            preferences.ReduceMotion ? "true" : "false",
            now,
            cancellationToken ).ConfigureAwait( false );
        foreach ( var (key, value) in new[]
        {
            (RememberSearchesKey, preferences.RememberSearches),
            (LearnFromUseKey, preferences.LearnFromUse),
            (OmarchyMenuStateKey, preferences.OmarchyMenuState),
            (TopMatchesKey, preferences.TopMatches)
        } )
        {
            await UpsertLauncherPreferenceAsync( connection, transaction, key, WriteChoice( value ), now, cancellationToken )
                .ConfigureAwait( false );
        }
        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "web_fallback_quicklink_id",
            preferences.WebFallbackQuicklinkId ?? string.Empty,
            now,
            cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "preferred_editor",
            preferences.PreferredEditor,
            now,
            cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "hey_open_target",
            preferences.HeyOpenTarget,
            now,
            cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "graphical_editor",
            System.Text.Json.JsonSerializer.Serialize(
                preferences.GraphicalEditor, CommandDeckJsonContext.Default.EditorAdapter ),
            now,
            cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync(
            connection,
            transaction,
            "terminal_editor",
            System.Text.Json.JsonSerializer.Serialize(
                preferences.TerminalEditor, CommandDeckJsonContext.Default.EditorAdapter ),
            now,
            cancellationToken ).ConfigureAwait( false );
        await IncrementSourcePreferenceRevisionAsync( connection, transaction, cancellationToken ).ConfigureAwait( false );
        await UpsertLauncherPreferenceAsync(
            connection, transaction, "workspace_preview_mode",
            WorkspacePreviewModes.Normalize( preferences.WorkspacePreviewMode ), now,
            cancellationToken ).ConfigureAwait( false );

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return await GetPreferencesAsync( cancellationToken ).ConfigureAwait( false );
    }

    private void ValidatePreferences( LauncherPreferences preferences )
    {
        ArgumentNullException.ThrowIfNull( preferences );
        var expected = ContributionSnapshot.Sources
            .Select( static value => value.Id )
            .Order( StringComparer.Ordinal )
            .ToArray( );
        var actual = preferences.Sources
            .Select( static value => value.SourceId )
            .Order( StringComparer.Ordinal )
            .ToArray( );
        var known = ContributionSnapshot.Sources.Select( source => source.Id ).ToHashSet( StringComparer.Ordinal );
        if ( expected.Except( actual, StringComparer.Ordinal ).Any( ) || actual.Distinct( StringComparer.Ordinal ).Count( ) != actual.Length || actual.Any( id => !known.Contains( id ) ) )
        {
            throw new InvalidOperationException( "Source preferences must contain each known source exactly once." );
        }

        if ( preferences.Sources.Select( static value => value.Position ).Distinct( ).Count( ) != actual.Length ||
            preferences.Sources.Any( value => value.Position < 1 || value.Position > actual.Length ) )
        {
            throw new InvalidOperationException( "Source preference positions must be a complete unique permutation." );
        }
        if ( preferences.PreferredEditor is not EditorKinds.Graphical and not EditorKinds.Terminal )
        {
            throw new InvalidOperationException(
                "Preferred editor must be graphical or terminal." );
        }
        if ( !HeyOpenTargets.IsKnown( preferences.HeyOpenTarget ) )
        {
            throw new InvalidOperationException( "HEY threads open in the web app or the terminal app." );
        }

        _ = EditorAdapterValidator.Validate( preferences.GraphicalEditor, terminal: false );
        _ = EditorAdapterValidator.Validate( preferences.TerminalEditor, terminal: true );
        EnsureHomeAvailable( preferences.Sources );
    }

    // A source whose rows never appear on Home (HEY mail) cannot be switched on there.
    private void EnsureHomeAvailable( IEnumerable<SourcePreference> sources )
    {
        var unavailable = ContributionSnapshot.Sources
            .Where( static source => !source.HomeAvailable )
            .Select( static source => source.Id )
            .ToHashSet( StringComparer.Ordinal );
        var blocked = sources.FirstOrDefault( source => source.EnabledOnHome && unavailable.Contains( source.SourceId ) );
        if ( blocked is not null )
        {
            throw new InvalidOperationException( $"{ContributionSnapshot.Label( blocked.SourceId )} never appears on Home." );
        }
    }

    private static async ValueTask UpsertLauncherPreferenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        long now,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO launcher_preferences(key, value, updated_unix_ms)
            VALUES($key, $value, $now)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_unix_ms = excluded.updated_unix_ms;
            """;
        command.Parameters.AddWithValue( "$key", key );
        command.Parameters.AddWithValue( "$value", value );
        command.Parameters.AddWithValue( "$now", now );
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }
}
