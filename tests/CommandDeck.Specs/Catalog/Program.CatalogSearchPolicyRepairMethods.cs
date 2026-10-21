using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IntegrityAcceptsFieldsExcludedFromSearchAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-policy-integrity-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( PolicyItems( ), null, CancellationToken.None );

            // Excluded fields are blank in the search projection by design; the
            // check reported them as drift, so maintenance failed every run.
            var healthy = await catalog.CheckIntegrityAsync( CancellationToken.None );
            True( healthy.Succeeded && !healthy.RebuildRequired, string.Join( ' ', healthy.Messages ) );

            await ExecuteCatalogSqlAsync(
                database,
                "UPDATE catalog_fts SET title = 'renamed.txt' WHERE stable_id = 'file:policy-public';" );
            var drifted = await catalog.CheckIntegrityAsync( CancellationToken.None );
            var driftMessages = string.Join( ' ', drifted.Messages );
            True( !drifted.Succeeded &&
                driftMessages.Contains( "missing=0, orphan=0, mismatched=1", StringComparison.Ordinal ),
                driftMessages );

            await catalog.RepairAsync( CancellationToken.None );
            True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
            Equal( "file:policy-public", (await SearchAsync( catalog, "publictitletoken" )).Single( ).Id );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task RepairKeepsExcludedFieldsOutOfSearchAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-policy-repair-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( PolicyItems( ), null, CancellationToken.None );
            await ExecuteCatalogSqlAsync(
                database,
                "DELETE FROM catalog_fts WHERE stable_id = 'file:policy-private';" );

            // Repair rebuilt the search row from catalog columns, which put the
            // excluded path and subtitle into full-text search.
            await catalog.RepairAsync( CancellationToken.None );
            Equal( 0, (await SearchAsync( catalog, "privatepathtoken" )).Count );
            Equal( 0, (await SearchAsync( catalog, "privatesubtitletoken" )).Count );
            Equal( "||", await ReadSearchFieldsAsync( database, "file:policy-private" ) );
            Equal( "file:policy-public", (await SearchAsync( catalog, "publicpathtoken" )).Single( ).Id );
            True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task CatalogMigrationRestoresSearchInclusionFromPolicyAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-policy-migration-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using ( SqliteCatalog current = new( new StorageOptions { DatabasePath = database } ) )
            {
                await current.ReplaceAllAsync( PolicyItems( ), null, CancellationToken.None );
            }

            // A version 20 catalog has the policy provenance but no stored inclusion.
            SqliteConnection.ClearAllPools( );
            await ExecuteCatalogSqlAsync( database, """
                ALTER TABLE catalog_items DROP COLUMN title_in_search;
                ALTER TABLE catalog_items DROP COLUMN metadata_in_search;
                DELETE FROM schema_migrations WHERE version >= 21;
                DELETE FROM catalog_fts WHERE stable_id = 'file:policy-private';
                """ );

            await using SqliteCatalog migrated = new( new StorageOptions { DatabasePath = database } );
            var drifted = await migrated.CheckIntegrityAsync( CancellationToken.None );
            True( string.Join( ' ', drifted.Messages ).Contains(
                    "missing=1, orphan=0, mismatched=0",
                    StringComparison.Ordinal ),
                string.Join( ' ', drifted.Messages ) );

            await migrated.RepairAsync( CancellationToken.None );
            Equal( "||", await ReadSearchFieldsAsync( database, "file:policy-private" ) );
            Equal( 0, (await SearchAsync( migrated, "privatepathtoken" )).Count );
            Equal( "file:policy-public", (await SearchAsync( migrated, "publicpathtoken" )).Single( ).Id );
            True( (await migrated.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task FuzzySearchKeepsExcludedNamesOutOfMatchesAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-policy-fuzzy-{Guid.NewGuid( ):N}.db" );
        try
        {
            // Equal field weights let a path or subtitle alone pass the fuzzy
            // threshold, as a user's ranking configuration can.
            SearchOptions search = new( )
            {
                Ranking = new RankingOptions { TitleWeight = 7, PathWeight = 7, MetadataWeight = 7 }
            };
            await using SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database },
                searchOptions: search );
            await catalog.ReplaceAllAsync( FuzzyPolicyItems( ), null, CancellationToken.None );
            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog ), new ApplicationSearchProvider( catalog )],
                search );

            // The fuzzy fallback matched catalog titles and paths directly, so a
            // name excluded from search was still found by exact and typo queries.
            foreach ( var text in new[] { "quarterlyledger", "quartrlyledger", "zetavault" } )
            {
                var stored = await SearchAsync( catalog, text );
                var ranked = await coordinator.SearchAsync(
                    new SearchRequest { Query = text, Limit = 20 },
                    CancellationToken.None );
                var ids = string.Join( ", ", stored.Concat( ranked ).Select( static result => result.Id ) );
                True( !stored.Concat( ranked ).Any( static result => result.Id is
                        "file:fuzzy-excluded-name" or "file:fuzzy-excluded-path" ),
                    $"'{text}' must not match an excluded field: {ids}" );
                True( stored.Any( static result => result.Id is
                        "file:fuzzy-included-name" or "file:fuzzy-included-path" ),
                    $"'{text}' still matches the included sibling: {ids}" );
            }
        }
        finally
        {
            DeleteDatabase( database );
        }

        static async IAsyncEnumerable<IndexedItem> FuzzyPolicyItems( )
        {
            // The path contains the name, so hiding a name means excluding both.
            IndexFieldPolicy[] excludedName =
            [
                new( ) { Field = RankingFieldNames.Title, Included = false, Reason = "file-type-override", Rule = "*.txt" },
                new( ) { Field = RankingFieldNames.Path, Included = false, Reason = "file-type-override", Rule = "*.txt" },
                new( ) { Field = RankingFieldNames.Metadata, Included = false, Reason = "file-type-override", Rule = "*.txt" }
            ];
            IndexFieldPolicy[] excludedMetadata =
            [
                new( ) { Field = RankingFieldNames.Path, Included = false, Reason = "file-type-override", Rule = "*.log" },
                new( ) { Field = RankingFieldNames.Metadata, Included = false, Reason = "file-type-override", Rule = "*.log" }
            ];
            yield return FuzzyPolicyItem( "fuzzy-excluded-name", "quarterlyledger.txt", "/spec/fuzzy/a/quarterlyledger.txt",
                includeTitle: false, includeMetadata: false, [.. excludedName] );
            yield return FuzzyPolicyItem( "fuzzy-included-name", "quarterlyledger-public.md", "/spec/fuzzy/b/quarterlyledger-public.md",
                includeTitle: true, includeMetadata: true, [] );
            yield return FuzzyPolicyItem( "fuzzy-excluded-path", "zeta.log", "/spec/zetavault/zeta.log",
                includeTitle: true, includeMetadata: false, [.. excludedMetadata] );
            yield return FuzzyPolicyItem( "fuzzy-included-path", "zeta.md", "/spec/zetavault-public/zeta.md",
                includeTitle: true, includeMetadata: true, [] );
            await Task.CompletedTask;
        }

        static IndexedItem FuzzyPolicyItem(
            string id,
            string title,
            string path,
            bool includeTitle,
            bool includeMetadata,
            List<IndexFieldPolicy> policies ) => new( )
            {
                StableId = $"file:{id}",
                Kind = ResultKinds.File,
                Title = title,
                Subtitle = path,
                Path = path,
                Body = "fuzzypolicybody",
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files,
                IncludeTitleInSearch = includeTitle,
                IncludeMetadataInSearch = includeMetadata,
                IndexPolicies = policies
            };
    }

    private static async IAsyncEnumerable<IndexedItem> PolicyItems( )
    {
        yield return new IndexedItem
        {
            StableId = "file:policy-private",
            Kind = ResultKinds.File,
            Title = "privatenametoken.txt",
            Subtitle = "privatesubtitletoken",
            Path = "/spec/privatepathtoken/privatenametoken.txt",
            Body = "privatebodytoken",
            ProviderId = ProviderIds.Files,
            SourceId = SourceIds.Files,
            IncludeTitleInSearch = false,
            IncludeMetadataInSearch = false,
            IndexPolicies =
            [
                new IndexFieldPolicy { Field = RankingFieldNames.Title, Included = false, Reason = "file-type-override", Rule = "*.txt" },
                new IndexFieldPolicy { Field = RankingFieldNames.Path, Included = false, Reason = "file-type-override", Rule = "*.txt" },
                new IndexFieldPolicy { Field = RankingFieldNames.Metadata, Included = false, Reason = "file-type-override", Rule = "*.txt" }
            ]
        };
        yield return new IndexedItem
        {
            StableId = "file:policy-public",
            Kind = ResultKinds.File,
            Title = "publictitletoken.md",
            Subtitle = "publicsubtitletoken",
            Path = "/spec/publicpathtoken/publictitletoken.md",
            Body = "publicbodytoken",
            ProviderId = ProviderIds.Files,
            SourceId = SourceIds.Files
        };
        await Task.CompletedTask;
    }

    private static async Task ExecuteCatalogSqlAsync( string database, string sql )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var command = connection.CreateCommand( );
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync( );
    }

    private static async Task<string> ReadSearchFieldsAsync( string database, string stableId )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT title || '|' || subtitle || '|' || path
            FROM catalog_fts WHERE stable_id = $stable_id;
            """;
        command.Parameters.AddWithValue( "$stable_id", stableId );

        return ( string ) (await command.ExecuteScalarAsync( ))!;
    }
}
