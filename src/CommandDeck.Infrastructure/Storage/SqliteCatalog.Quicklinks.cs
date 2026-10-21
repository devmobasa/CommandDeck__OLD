using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog : IQuicklinkStore
{
    private IReadOnlyList<QuicklinkOptions>? _configuredQuicklinks;

    public async ValueTask ConfigureQuicklinksAsync( IReadOnlyList<QuicklinkOptions> configured,
        CancellationToken cancellationToken )
    {
        QuicklinkValidator.ValidateUnique( configured );
        var previous = _configuredQuicklinks;
        _configuredQuicklinks = configured.ToArray( );
        _launcherPreferences.ValidateFallback = async ( connection, transaction, id, token ) =>
        {
            var snapshot = await ReadQuicklinksAsync( connection, transaction, null, token ).ConfigureAwait( false );
            if ( !snapshot.Quicklinks.Any( entry => entry.Enabled && entry.Quicklink.Id == id ) )
            {
                throw new InvalidOperationException( "The selected fallback quicklink is not configured." );
            }
        };
        try
        {
            await ChangeQuicklinkAsync( "refresh", null, null, cancellationToken ).ConfigureAwait( false );
        }
        catch
        {
            _configuredQuicklinks = previous;
            throw;
        }
    }

    public async ValueTask<QuicklinksResult> GetQuicklinksAsync( CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: true );
        return await ReadQuicklinksAsync( connection, transaction, null, cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<QuicklinksResult> ChangeQuicklinkAsync( string action, QuicklinkOptions? quicklink,
        string? id, CancellationToken cancellationToken )
    {
        if ( action == "save" )
        {
            if ( quicklink is null )
            {
                throw new InvalidOperationException( "quicklinks.save requires quicklink." );
            }
            QuicklinkValidator.Validate( quicklink );
            id = quicklink.Id;
        }
        else if ( action != "refresh" && string.IsNullOrWhiteSpace( id ) )
        {
            throw new InvalidOperationException( "A quicklink ID is required." );
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: false );
        var before = await ReadQuicklinksAsync( connection, transaction, null, cancellationToken ).ConfigureAwait( false );
        await MutateQuicklinkAsync( connection, transaction, before, action, quicklink, id, cancellationToken ).ConfigureAwait( false );
        var changed = await ReadQuicklinksAsync( connection, transaction, null, cancellationToken ).ConfigureAwait( false );
        await ValidateQuicklinkAliasesAsync( connection, transaction, changed.Quicklinks, cancellationToken ).ConfigureAwait( false );
        var resetFallback = changed.WebFallbackQuicklinkId is not null && !changed.Quicklinks.Any( entry =>
            entry.Enabled && entry.Quicklink.Id == changed.WebFallbackQuicklinkId );
        if ( resetFallback )
        {
            await using var command = connection.CreateCommand( );
            command.Transaction = transaction;
            command.CommandText = "UPDATE launcher_preferences SET value='' WHERE key='web_fallback_quicklink_id';";
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        await PublishQuicklinksAsync( connection, transaction, changed.Quicklinks, cancellationToken ).ConfigureAwait( false );
        var message = action == "refresh" ? null : "Quicklink " + (action == "save" ? "saved" : action == "remove" ? "removed" : action + "d") + ".";
        if ( resetFallback )
        {
            message = (message is null ? "" : message + " ") + "Web fallback reset to the default.";
        }
        var result = await ReadQuicklinksAsync( connection, transaction, message, cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return result;
    }

    private async ValueTask MutateQuicklinkAsync( SqliteConnection connection, SqliteTransaction transaction,
        QuicklinksResult before, string action, QuicklinkOptions? quicklink, string? id, CancellationToken cancellationToken )
    {
        var configured = (_configuredQuicklinks ?? []).FirstOrDefault( link =>
            string.Equals( link.Id, id, StringComparison.OrdinalIgnoreCase ) );
        var user = before.Quicklinks.FirstOrDefault( entry => entry.Source == "user" &&
            string.Equals( entry.Quicklink.Id, id, StringComparison.OrdinalIgnoreCase ) );

        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.Parameters.AddWithValue( "$id", id ?? string.Empty );
        switch ( action )
        {
            case "save":
                if ( configured is not null )
                {
                    throw new InvalidOperationException( $"Quicklink ID '{id}' is configured more than once." );
                }
                QuicklinkValidator.ValidateUnique( (_configuredQuicklinks ?? [])
                    .Concat( before.Quicklinks.Where( entry => entry.Source == "user" && entry != user )
                        .Select( entry => entry.Quicklink ) ).Append( quicklink! ) );
                if ( user is null && before.Quicklinks.Count( entry => entry.Source == "user" ) >= 100 )
                {
                    throw new InvalidOperationException( "At most 100 user quicklinks are supported." );
                }
                command.CommandText = """
                    INSERT INTO user_quicklinks(id, title, url_template, alias, icon, created_unix_ms)
                    VALUES($id, $title, $url, $alias, $icon, $created)
                    ON CONFLICT(id) DO UPDATE SET title=excluded.title, url_template=excluded.url_template,
                        alias=excluded.alias, icon=excluded.icon;
                    """;
                command.Parameters.AddWithValue( "$title", quicklink!.Title );
                command.Parameters.AddWithValue( "$url", quicklink.UrlTemplate );
                command.Parameters.AddWithValue( "$alias", ( object? ) quicklink.Alias ?? DBNull.Value );
                command.Parameters.AddWithValue( "$icon", ( object? ) quicklink.Icon ?? DBNull.Value );
                command.Parameters.AddWithValue( "$created", _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) );
                break;
            case "remove":
                if ( user is null )
                {
                    throw new InvalidOperationException( "Only user quicklinks can be removed." );
                }
                command.CommandText = "DELETE FROM user_quicklinks WHERE id=$id;";
                break;
            case "disable":
            case "enable":
                if ( configured is null )
                {
                    throw new InvalidOperationException( "Only configured quicklinks can be enabled or disabled." );
                }
                command.CommandText = action == "disable"
                    ? "INSERT OR IGNORE INTO disabled_configured_quicklinks(id) VALUES($id);"
                    : "DELETE FROM disabled_configured_quicklinks WHERE id=$id;";
                break;
            case "refresh":
                command.CommandText = "SELECT 1;";
                break;
            default:
                throw new InvalidOperationException( "Unknown quicklink action." );
        }
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<QuicklinksResult> ReadQuicklinksAsync( SqliteConnection connection,
        SqliteTransaction transaction, string? message, CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        var state = await SqliteLauncherPreferenceStore.ReadQuicklinkStateAsync( connection, transaction, cancellationToken ).ConfigureAwait( false );
        var disabled = state.DisabledConfiguredIds.ToHashSet( StringComparer.OrdinalIgnoreCase );
        List<QuicklinkEntry> links = (_configuredQuicklinks ?? []).Select( link => new QuicklinkEntry
        {
            Quicklink = link,
            Source = "configured",
            Enabled = !disabled.Contains( link.Id )
        } ).Concat( state.UserQuicklinks ).ToList( );
        // A new configuration cannot silently take over a stored user identity or alias.
        QuicklinkValidator.ValidateUnique( links.Select( entry => entry.Quicklink ) );

        command.CommandText = "SELECT NULLIF(value,'') FROM launcher_preferences WHERE key='web_fallback_quicklink_id';";
        var fallback = await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
        return new QuicklinksResult { Quicklinks = links, WebFallbackQuicklinkId = fallback as string, Message = message };
    }
    private async ValueTask ValidateQuicklinkAliasesAsync( SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<QuicklinkEntry> links, CancellationToken cancellationToken )
    {
        foreach ( var entry in links )
        {
            if ( entry.Quicklink.Alias is { } alias )
            {
                await EnsureAliasAvailableAsync( connection, transaction, alias, "quicklink", entry.Quicklink.Id,
                    _configuredQuicklinks ?? [], cancellationToken ).ConfigureAwait( false );
            }
        }
    }

}
