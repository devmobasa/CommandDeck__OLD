namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExclusionListingRetainsCappedPathsAsync( bool longRoot )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            foreach ( var length in new[] { 511, 512, 513 } )
            {
                var root = Directory.CreateDirectory( Path.Combine( sandbox, $"project-{length}" ) ).FullName;
                var parent = root;
                var targetLength = longRoot ? length : root.Length + 1 + length;
                while ( targetLength - parent.Length > 101 )
                {
                    parent = Directory.CreateDirectory( Path.Combine( parent, new string( 'a', 100 ) ) ).FullName;
                }

                string excluded;
                if ( longRoot )
                {
                    root = Directory.CreateDirectory( Path.Combine( parent, new string( 'b', length - parent.Length - 1 ) ) ).FullName;
                    excluded = Path.Combine( root, ".excluded" );
                }
                else
                {
                    excluded = Path.Combine( parent, "." + new string( 'b', targetLength - parent.Length - 2 ) );
                }
                await File.WriteAllTextAsync( excluded, "excluded marker" );

                var options = DeveloperIndexOptions( Path.Combine( sandbox, $"catalog-{length}.db" ), root, incremental: false );
                options.Indexing.Roots[0] = options.Indexing.Roots[0] with { IncludeHidden = false };
                await using SqliteCatalog catalog = new( options.Storage );
                await using IndexCoordinator indexer = new( catalog, catalog, options );
                await indexer.ReindexAsync( null, CancellationToken.None );

                var inspection = await indexer.InspectAsync( CancellationToken.None );
                var diagnostic = inspection.ExclusionDiagnostics.Single( );
                Equal( Math.Min( length, 512 ), (longRoot ? diagnostic.Root : diagnostic.RelativePath).Length );
                Equal( 1L, inspection.TotalExclusionDiagnosticCount );

                File.Delete( excluded );
                var afterDelete = await catalog.InspectAsync( CancellationToken.None );
                Equal( length < 512 ? 0 : 1, afterDelete.ExclusionDiagnostics.Count );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task ExclusionListingKeepsItsSampleBoundAsync( bool rootExists )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var root = Path.Combine( sandbox, "project" );
            if ( rootExists )
            {
                Directory.CreateDirectory( root );
            }

            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( sandbox, "catalog.db" ) } );
            List<IndexExclusionDiagnostic> diagnostics =
            [
                new( ) { Root = root, RelativePath = "[root]", Reason = IndexExclusionReasons.RootUnavailable }
            ];
            diagnostics.AddRange( Enumerable.Range( 0, 255 ).Select( number => new IndexExclusionDiagnostic
            {
                Root = sandbox,
                RelativePath = $"missing-{number}",
                Reason = IndexExclusionReasons.Hidden
            } ) );
            diagnostics.Add( new IndexExclusionDiagnostic
            {
                Root = sandbox,
                RelativePath = "missing-outside-sample.txt",
                Reason = IndexExclusionReasons.Hidden
            } );
            await catalog.ReplaceExclusionDiagnosticsAsync( diagnostics, CancellationToken.None );

            var inspection = await catalog.InspectAsync( CancellationToken.None );

            var retained = inspection.ExclusionDiagnostics.Single( );
            Equal( root, retained.Root );
            Equal( "[root]", retained.RelativePath );
            Equal( IndexExclusionReasons.RootUnavailable, retained.Reason );
            Equal( 2L, inspection.TotalExclusionDiagnosticCount );
            True( inspection.ExclusionDiagnosticsTruncated );
            Equal( 257L, (await catalog.AuditPrivacyAsync( CancellationToken.None )).ExclusionDiagnosticCount );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task ExclusionListingOmitsMissingPathsAsync( bool directory, string? renamed )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var root = Directory.CreateDirectory( Path.Combine( sandbox, "project" ) ).FullName;
            var excluded = Path.Combine( root, ".excluded" );
            var privateDirectory = Directory.CreateDirectory( Path.Combine( root, "private" ) ).FullName;
            await File.WriteAllTextAsync( Path.Combine( privateDirectory, "secret.txt" ), "private marker" );
            await File.WriteAllTextAsync( Path.Combine( root, ".retained.txt" ), "retained marker" );
            Directory.CreateDirectory( Path.Combine( root, ".retained-directory" ) );
            if ( directory )
            {
                Directory.CreateDirectory( excluded );
                await File.WriteAllTextAsync( Path.Combine( excluded, "child.txt" ), "excluded marker" );
            }
            else
            {
                await File.WriteAllTextAsync( excluded, "excluded marker" );
            }

            var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root, incremental: false );
            options.Indexing.Roots[0] = options.Indexing.Roots[0] with { IncludeHidden = false };
            options.Privacy.NeverIndex.Add( privateDirectory );
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.ReindexAsync( null, CancellationToken.None );
            var before = await indexer.InspectAsync( CancellationToken.None );
            Equal( 4, before.ExclusionDiagnostics.Count );
            var privacy = before.ExclusionDiagnostics.Single( value => value.RelativePath == "[redacted]" );

            if ( renamed is not null )
            {
                File.Move( excluded, Path.Combine( root, renamed ) );
            }
            else if ( directory )
            {
                Directory.Delete( excluded, recursive: true );
            }
            else
            {
                File.Delete( excluded );
            }
            Directory.Delete( privateDirectory, recursive: true );

            // Both public read paths share the filter; neither read starts a scan.
            AssertListing( await catalog.InspectAsync( CancellationToken.None ) );
            AssertListing( await indexer.InspectAsync( CancellationToken.None ) );

            // Filtering output must not erase the full-scan record.
            Equal( 4L, (await catalog.AuditPrivacyAsync( CancellationToken.None )).ExclusionDiagnosticCount );
            if ( renamed is not null )
            {
                await indexer.ReindexAsync( null, CancellationToken.None );
                var scanned = await indexer.InspectAsync( CancellationToken.None );
                Equal( renamed.StartsWith( '.' ), scanned.ExclusionDiagnostics.Any( value => value.RelativePath == renamed ) );
                True( scanned.ExclusionDiagnostics.All( value => value.RelativePath != ".excluded" && value.RelativePath != "[redacted]" ) );
            }

            void AssertListing( CatalogInspection inspection )
            {
                Equal( ".retained-directory,.retained.txt,[redacted]",
                    string.Join( ',', inspection.ExclusionDiagnostics.Select( value => value.RelativePath ).Order( StringComparer.Ordinal ) ) );
                Equal( 3L, inspection.TotalExclusionDiagnosticCount );
                True( !inspection.ExclusionDiagnosticsTruncated );
                var retainedPrivacy = inspection.ExclusionDiagnostics.Single( value => value.RelativePath == "[redacted]" );
                Equal( privacy.Root, retainedPrivacy.Root );
                Equal( privacy.Reason, retainedPrivacy.Reason );
                Equal( privacy.Rule, retainedPrivacy.Rule );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }
}
