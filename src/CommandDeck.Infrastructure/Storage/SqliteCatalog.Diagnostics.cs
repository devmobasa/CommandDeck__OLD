using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private const int MaximumStoredExclusionDiagnostics = 512;
    private const int MaximumReturnedExclusionDiagnostics = 256;
    private const int MaximumDiagnosticTextCharacters = 512;

    public async ValueTask<PrivacyAuditResult> AuditPrivacyAsync(
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        long catalogPathCount = 0;
        long policyViolations = 0;
        await using ( var paths = connection.CreateCommand( ) )
        {
            paths.CommandText = "SELECT path FROM catalog_items WHERE path IS NOT NULL;";
            await using var reader = await paths.ExecuteReaderAsync( cancellationToken )
                .ConfigureAwait( false );
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                catalogPathCount++;
                if ( _pathPolicy.IsDenied( reader.GetString( 0 ) ) )
                {
                    policyViolations++;
                }
            }
        }

        long diagnosticCount = 0;
        long unsafeDiagnostics = 0;
        await using ( var diagnostics = connection.CreateCommand( ) )
        {
            diagnostics.CommandText = """
                SELECT root, relative_path, reason, rule
                FROM index_exclusion_diagnostics;
                """;
            await using var reader = await diagnostics.ExecuteReaderAsync(
                cancellationToken ).ConfigureAwait( false );
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                diagnosticCount++;
                var root = reader.GetString( 0 );
                var relativePath = reader.GetString( 1 );
                var reason = reader.GetString( 2 );
                var rule = reader.IsDBNull( 3 ) ? null : reader.GetString( 3 );
                if ( UnsafeDiagnosticText( root ) ||
                    UnsafeDiagnosticText( relativePath ) ||
                    UnsafeDiagnosticText( reason ) ||
                    rule is not null && UnsafeDiagnosticText( rule ) ||
                    reason == IndexExclusionReasons.PrivacyRule &&
                    relativePath != "[redacted]" )
                {
                    unsafeDiagnostics++;
                }
            }
        }

        var succeeded = policyViolations == 0 && unsafeDiagnostics == 0;
        return new PrivacyAuditResult
        {
            Succeeded = succeeded,
            CatalogPathCount = catalogPathCount,
            ExclusionDiagnosticCount = diagnosticCount,
            PolicyViolationCount = policyViolations,
            UnsafeDiagnosticCount = unsafeDiagnostics,
            Messages = succeeded
                ? ["No indexed path violates privacy policy; diagnostics contain bounded path metadata only."]
                :
                [
                    "Privacy audit found policy or diagnostic violations. " +
                    "Paths and stored values were intentionally omitted."
                ]
        };
    }

    public async ValueTask<CatalogStats> GetStatsAsync( CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT
                COUNT(*),
                SUM(CASE WHEN kind = 'file' THEN 1 ELSE 0 END),
                SUM(CASE WHEN kind = 'configuration' THEN 1 ELSE 0 END),
                SUM(CASE WHEN kind = 'application' THEN 1 ELSE 0 END),
                (SELECT COUNT(*) FROM invocation_events)
            FROM catalog_items;
            """;

        await using var reader =
            await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );

        return new CatalogStats
        {
            ItemCount = reader.IsDBNull( 0 ) ? 0 : reader.GetInt64( 0 ),
            FileCount = reader.IsDBNull( 1 ) ? 0 : reader.GetInt64( 1 ),
            ConfigCount = reader.IsDBNull( 2 ) ? 0 : reader.GetInt64( 2 ),
            ApplicationCount = reader.IsDBNull( 3 ) ? 0 : reader.GetInt64( 3 ),
            UsageEventCount = reader.IsDBNull( 4 ) ? 0 : reader.GetInt64( 4 ),
            DatabaseBytes = File.Exists( _databasePath ) ? new FileInfo( _databasePath ).Length : 0
        };
    }

    public async ValueTask ReplaceExclusionDiagnosticsAsync(
        IReadOnlyList<IndexExclusionDiagnostic> diagnostics,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( diagnostics );
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );
        await ReplaceExclusionDiagnosticsAsync(
            connection,
            transaction,
            diagnostics,
            cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask ReplaceExclusionDiagnosticsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<IndexExclusionDiagnostic> diagnostics,
        CancellationToken cancellationToken )
    {
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "DELETE FROM index_exclusion_diagnostics;",
            cancellationToken ).ConfigureAwait( false );

        await using var insert = connection.CreateCommand( );
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO index_exclusion_diagnostics(root, relative_path, reason, rule)
            VALUES($root, $relative_path, $reason, $rule);
            """;
        insert.Parameters.Add( "$root", SqliteType.Text );
        insert.Parameters.Add( "$relative_path", SqliteType.Text );
        insert.Parameters.Add( "$reason", SqliteType.Text );
        insert.Parameters.Add( "$rule", SqliteType.Text );
        await insert.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        foreach ( var diagnostic in diagnostics
            .Take( MaximumStoredExclusionDiagnostics ) )
        {
            insert.Parameters["$root"].Value = Bound( diagnostic.Root );
            insert.Parameters["$relative_path"].Value = Bound( diagnostic.RelativePath );
            insert.Parameters["$reason"].Value = Bound( diagnostic.Reason );
            insert.Parameters["$rule"].Value = diagnostic.Rule is null
                ? DBNull.Value
                : Bound( diagnostic.Rule );
            await insert.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
    }

    private static async ValueTask<(
        List<IndexExclusionDiagnostic> Diagnostics,
        long Total)> ReadExclusionDiagnosticsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT root, relative_path, reason, rule,
                   (SELECT COUNT(*) FROM index_exclusion_diagnostics)
            FROM index_exclusion_diagnostics
            ORDER BY id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue( "$limit", MaximumReturnedExclusionDiagnostics );
        List<IndexExclusionDiagnostic> diagnostics = [];
        long total = 0;
        var omitted = 0;
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            total = reader.GetInt64( 4 );
            var diagnostic = new IndexExclusionDiagnostic
            {
                Root = reader.GetString( 0 ),
                RelativePath = reader.GetString( 1 ),
                Reason = reader.GetString( 2 ),
                Rule = reader.IsDBNull( 3 ) ? null : reader.GetString( 3 )
            };

            // Root status, redacted paths and possibly truncated paths stay as reported.
            if ( diagnostic.Reason != IndexExclusionReasons.RootUnavailable &&
                diagnostic.RelativePath != "[redacted]" &&
                diagnostic.Root.Length < MaximumDiagnosticTextCharacters &&
                diagnostic.RelativePath.Length < MaximumDiagnosticTextCharacters &&
                !Path.Exists( Path.Combine( diagnostic.Root, diagnostic.RelativePath ) ) )
            {
                omitted++;
                continue;
            }

            diagnostics.Add( diagnostic );
        }

        // The total is approximate: paths outside the bounded sample remain unchecked.
        return (diagnostics, total - omitted);
    }

    private static string Bound( string value ) => value.Length <= MaximumDiagnosticTextCharacters
        ? value
        : value[..MaximumDiagnosticTextCharacters];

    private static bool UnsafeDiagnosticText( string value ) =>
        value.Length > MaximumDiagnosticTextCharacters ||
        value.Any( static character => char.IsControl( character ) );
}
