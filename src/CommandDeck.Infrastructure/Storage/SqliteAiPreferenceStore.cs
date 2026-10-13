using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed class SqliteAiPreferenceStore(
    Func<CancellationToken, ValueTask> initialize,
    Func<CancellationToken, ValueTask<SqliteConnection>> open,
    TimeProvider timeProvider ) : IAiPreferenceStore
{
    public async ValueTask<AiSelection> GetAiSelectionAsync(
        CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT key, value FROM ai_preferences WHERE key IN ('provider_id', 'model_id');";
        string? provider = null;
        string? model = null;
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            if ( reader.GetString( 0 ) == "provider_id" )
            {
                provider = reader.GetString( 1 );
            }

            if ( reader.GetString( 0 ) == "model_id" )
            {
                model = reader.GetString( 1 );
            }
        }
        return new AiSelection
        {
            ProviderId = provider,
            ModelId = model,
            State = provider is null ? AiSelectionStates.Unknown : AiSelectionStates.Ready,
            Explanation = provider is null ? "No AI provider and model are selected." : null
        };
    }

    public async ValueTask<AiSelection> SetAiSelectionAsync(
        string? providerId,
        string? modelId,
        CancellationToken cancellationToken )
    {
        if ( providerId is null != (modelId is null) )
        {
            throw new InvalidOperationException( "AI selection requires both provider and model." );
        }
        if ( providerId is not null )
        {
            AiContractValidator.ValidateIdentifier( providerId, 64, "providerId" );
            AiContractValidator.ValidateUtf8(
                modelId, AiLimits.MaximumModelIdBytes, "modelId", false,
                allowLineBreaks: false );
        }
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: false );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        if ( providerId is null )
        {
            command.CommandText = "DELETE FROM ai_preferences WHERE key IN ('provider_id', 'model_id');";
        }
        else
        {
            command.CommandText = """
                INSERT INTO ai_preferences(key, value, updated_unix_ms)
                VALUES('provider_id', $provider, $now), ('model_id', $model, $now)
                ON CONFLICT(key) DO UPDATE SET
                    value = excluded.value, updated_unix_ms = excluded.updated_unix_ms;
                """;
            command.Parameters.AddWithValue( "$provider", providerId );
            command.Parameters.AddWithValue( "$model", modelId! );
            command.Parameters.AddWithValue( "$now", timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) );
        }
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return await GetAiSelectionAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<IReadOnlyList<AiTemplate>> GetAiTemplatesAsync(
        CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT id, name, workflow_kind, system_template, user_template,
                   parameters_json, position
            FROM ai_prompt_templates
            ORDER BY position, name COLLATE NOCASE;
            """;
        List<AiTemplate> templates = [];
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            templates.Add( new AiTemplate
            {
                Id = reader.GetString( 0 ),
                Name = reader.GetString( 1 ),
                WorkflowKind = reader.GetString( 2 ),
                SystemTemplate = reader.GetString( 3 ),
                UserTemplate = reader.GetString( 4 ),
                Parameters = JsonSerializer.Deserialize(
                    reader.GetString( 5 ), CommandDeckJsonContext.Default.ListString ) ?? [],
                Position = reader.GetInt32( 6 )
            } );
        }
        return templates;
    }

    public async ValueTask<IReadOnlyList<AiTemplate>> SaveAiTemplateAsync(
        AiTemplate promptTemplate,
        CancellationToken cancellationToken )
    {
        var template = promptTemplate;
        ValidateTemplate( template );
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: false );
        await using var count = connection.CreateCommand( );
        count.Transaction = transaction;
        count.CommandText = "SELECT COUNT(*) FROM ai_prompt_templates WHERE id <> $id;";
        count.Parameters.AddWithValue( "$id", template.Id );
        var existing = ( long ) (await count.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ) ?? 0L);
        if ( existing >= AiLimits.MaximumTemplates )
        {
            throw new InvalidOperationException( "At most 32 AI prompt templates are supported." );
        }
        await using var duplicateName = connection.CreateCommand( );
        duplicateName.Transaction = transaction;
        duplicateName.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM ai_prompt_templates
                WHERE name = $name COLLATE NOCASE AND id <> $id);
            """;
        duplicateName.Parameters.AddWithValue( "$name", template.Name.Trim( ) );
        duplicateName.Parameters.AddWithValue( "$id", template.Id );
        if ( ( long ) (await duplicateName.ExecuteScalarAsync( cancellationToken )
                .ConfigureAwait( false ) ?? 0L) != 0 )
        {
            throw new InvalidOperationException( "AI template names must be unique." );
        }
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ai_prompt_templates(
                id, name, workflow_kind, system_template, user_template,
                parameters_json, position, created_unix_ms, updated_unix_ms)
            VALUES($id, $name, $workflow, $system, $user, $parameters,
                   COALESCE(NULLIF($position, 0),
                       (SELECT COALESCE(MAX(position), 0) + 1 FROM ai_prompt_templates)),
                   $now, $now)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, workflow_kind = excluded.workflow_kind,
                system_template = excluded.system_template, user_template = excluded.user_template,
                parameters_json = excluded.parameters_json, position = excluded.position,
                updated_unix_ms = excluded.updated_unix_ms;
            """;
        command.Parameters.AddWithValue( "$id", template.Id );
        command.Parameters.AddWithValue( "$name", template.Name.Trim( ) );
        command.Parameters.AddWithValue( "$workflow", template.WorkflowKind );
        command.Parameters.AddWithValue( "$system", template.SystemTemplate );
        command.Parameters.AddWithValue( "$user", template.UserTemplate );
        command.Parameters.AddWithValue( "$parameters", JsonSerializer.Serialize(
            template.Parameters, CommandDeckJsonContext.Default.ListString ) );
        command.Parameters.AddWithValue( "$position", Math.Max( 0, template.Position ) );
        command.Parameters.AddWithValue( "$now", timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) );
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return await GetAiTemplatesAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<IReadOnlyList<AiTemplate>> DeleteAiTemplateAsync(
        string templateId,
        CancellationToken cancellationToken )
    {
        AiContractValidator.ValidateIdentifier( templateId, 64, "templateId" );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "DELETE FROM ai_prompt_templates WHERE id = $id;";
        command.Parameters.AddWithValue( "$id", templateId );
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        return await GetAiTemplatesAsync( cancellationToken ).ConfigureAwait( false );
    }

    private static void ValidateTemplate( AiTemplate template )
    {
        ArgumentNullException.ThrowIfNull( template );
        AiContractValidator.ValidateIdentifier( template.Id, 64, "templateId" );
        AiContractValidator.ValidateVisibleText( template.Name, 64, "template name" );
        AiContractValidator.ValidateIdentifier( template.WorkflowKind, 64, "workflowKind" );
        AiContractValidator.ValidateUtf8(
            template.SystemTemplate, AiLimits.MaximumSystemPromptBytes, "system template", true );
        AiContractValidator.ValidateUtf8(
            template.UserTemplate, 32 * 1024, "user template", false );
        if ( !template.UserTemplate.Contains( "{{input}}", StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "AI user templates must contain {{input}}." );
        }
        if ( template.Parameters is null || template.Parameters.Count > 16 ||
            template.Parameters.Any( static value => value is null ) )
        {
            throw new InvalidOperationException( "Template parameters are invalid." );
        }
        foreach ( var parameter in template.Parameters )
        {
            AiContractValidator.ValidateIdentifier( parameter, 64, "template parameter" );
            if ( !string.Equals( parameter, "input", StringComparison.Ordinal ) )
            {
                throw new InvalidOperationException(
                    "Only the built-in input template parameter is supported." );
            }
        }
    }
}
