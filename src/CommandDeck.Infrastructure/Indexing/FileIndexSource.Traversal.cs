using System.Runtime.CompilerServices;

namespace CommandDeck;

public sealed partial class FileIndexSource
{

    public async IAsyncEnumerable<IndexedItem> ScanAsync(
        IProgress<IndexProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken )
    {
        _diagnostics.Clear( );
        _workItems.Clear( );
        ScanCounters counters = new( );

        List<(CanonicalPathBoundary Boundary, IndexRootOptions Options)> roots = [];
        foreach ( var rootOptions in _options.Roots )
        {
            var boundary = ResolveRoot( rootOptions, counters );
            if ( boundary is null )
            {
                continue;
            }

            roots.Add( (boundary, rootOptions) );
        }

        var ambiguousRoots = roots
            .GroupBy( static value => value.Boundary.CanonicalRoot, StringComparer.Ordinal )
            .Where( static group => group.Count( ) > 1 )
            .Select( static group => group.Key )
            .ToHashSet( StringComparer.Ordinal );
        roots.RemoveAll( value => ambiguousRoots.Contains( value.Boundary.CanonicalRoot ) );
        counters.Skipped += ambiguousRoots.Count;
        roots.Sort( static ( left, right ) =>
            right.Boundary.CanonicalRoot.Length.CompareTo( left.Boundary.CanonicalRoot.Length ) );
        HashSet<string> emittedStableIds = new( StringComparer.Ordinal );

        foreach ( (var boundary, var rootOptions) in roots )
        {
            var indexedBeforeRoot = counters.Indexed;
            var nestedRoots = roots
                .Where( value =>
                    value.Boundary.CanonicalRoot.Length > boundary.CanonicalRoot.Length &&
                    boundary.Contains( value.Boundary.CanonicalRoot ) )
                .Select( static value => value.Boundary.CanonicalRoot )
                .ToHashSet( StringComparer.Ordinal );

            var start = Exclusions.EnterRoot( boundary, rootOptions, out var rootExclusion );
            if ( start is null )
            {
                RecordDiagnostic( boundary, boundary.CanonicalRoot, rootExclusion!.Reason, rootExclusion.Rule );
                counters.Skipped++;
            }
            else
            {
                await foreach ( var item in ScanRootAsync(
                    start,
                    nestedRoots,
                    counters,
                    progress,
                    cancellationToken ).ConfigureAwait( false ) )
                {
                    if ( emittedStableIds.Add( item.StableId ) )
                    {
                        yield return item;
                    }
                }
            }

            progress?.Report( new IndexProgress
            {
                Stage = "root-complete",
                RootId = StableId.Create( "index-root", boundary.CanonicalRoot ),
                RootPath = boundary.CanonicalRoot,
                Discovered = counters.Discovered,
                Indexed = counters.Indexed - indexedBeforeRoot,
                Skipped = counters.Skipped,
                Failed = counters.Failed,
                CurrentPath = boundary.CanonicalRoot
            } );
        }
    }

    private CanonicalPathBoundary? ResolveRoot(
        IndexRootOptions rootOptions,
        ScanCounters counters )
    {
        if ( !CanonicalPathBoundary.TryCreate( rootOptions.Path, out var boundary ) ||
            boundary is null )
        {
            RecordDiagnostic(
                rootOptions.Path,
                "[root]",
                IndexExclusionReasons.RootUnavailable,
                null );
            counters.Skipped++;
            return null;
        }

        if ( _pathPolicy.TryGetDenial( boundary.LexicalRoot, out var lexicalRule ) ||
            _pathPolicy.TryGetDenial( boundary.CanonicalRoot, out lexicalRule ) )
        {
            RecordDiagnostic(
                rootOptions.Path,
                "[redacted]",
                IndexExclusionReasons.PrivacyRule,
                lexicalRule );
            counters.Skipped++;
            return null;
        }

        return boundary;
    }

    private (CanonicalPathBoundary Boundary, IndexRootOptions Options)? FindOwningRoot( string fullPath ) =>
        Exclusions.FindRoot( fullPath );

    // Streams every file below an authorized directory. Each directory's policy
    // is settled when it is entered, so an entry costs one status call and a
    // check of its own path; every emitted file is still re-resolved against the
    // root boundary after its content is read.
    private async IAsyncEnumerable<IndexedItem> ScanRootAsync(
        AuthorizedDirectory start,
        HashSet<string> nestedRoots,
        ScanCounters counters,
        IProgress<IndexProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken )
    {
        var boundary = start.Boundary;
        var rootOptions = start.Options;
        var root = boundary.CanonicalRoot;
        Stack<AuthorizedDirectory> directories = new( );

        // Without followed links every directory is reached once, through its
        // parent, so only link-following scans remember where they have been.
        HashSet<string>? visitedDirectories = rootOptions.FollowSymbolicLinks
            ? new( StringComparer.Ordinal )
            : null;
        directories.Push( start );

        while ( directories.Count > 0 )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var directory = directories.Pop( );
            if ( visitedDirectories is not null && !visitedDirectories.Add( directory.Path ) )
            {
                continue;
            }

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries( directory.Path );
            }
            catch ( Exception exception ) when (
                exception is UnauthorizedAccessException or IOException )
            {
                counters.Failed++;
                RecordWorkItem( directory.Path, "directory-read-failed" );
                continue;
            }

            foreach ( var entry in entries )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                counters.Discovered++;

                var decision = AuthorizeEntry( directory, entry );
                if ( decision.Exclusion is not null )
                {
                    RecordDiagnostic( boundary, entry, decision.Exclusion.Reason, decision.Exclusion.Rule );
                    counters.Skipped++;
                    continue;
                }

                if ( decision.MetadataFailed )
                {
                    counters.Failed++;
                    RecordWorkItem( decision.Path, "metadata-read-failed" );
                    continue;
                }

                if ( decision.Directory is not null )
                {
                    if ( nestedRoots.Contains( decision.Directory.Path ) )
                    {
                        counters.Skipped++;
                        continue;
                    }

                    directories.Push( decision.Directory );
                    continue;
                }

                var canonicalEntry = decision.Path;
                var size = decision.Status.Size;
                (var policy, var body) = await ExtractBodyAsync(
                    boundary,
                    rootOptions,
                    canonicalEntry,
                    size,
                    counters,
                    cancellationToken ).ConfigureAwait( false );

                if ( !boundary.TryResolve(
                        canonicalEntry,
                        rootOptions.FollowSymbolicLinks,
                        out var verifiedPath ) ||
                    !string.Equals( verifiedPath, canonicalEntry, StringComparison.Ordinal ) )
                {
                    counters.Skipped++;
                    continue;
                }

                var name = Path.GetFileName( canonicalEntry );
                var modified = decision.Status.ModifiedUnixMilliseconds;
                yield return new IndexedItem
                {
                    StableId = StableId.Create( ResultKinds.File, canonicalEntry ),
                    Kind = ResultKinds.File,
                    Title = name,
                    Subtitle = Path.GetRelativePath( root, canonicalEntry ),
                    Path = canonicalEntry,
                    Icon = GuessIcon( Path.GetExtension( name ) ),
                    Body = body,
                    SizeBytes = size,
                    ModifiedUnixMilliseconds = modified,
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files,
                    ExtractorVersion = ContentExtractor.ExtractorVersion,
                    IncludeTitleInSearch = policy.IncludeFileName,
                    IncludeMetadataInSearch = policy.IncludeMetadata,
                    IndexPolicies = [.. policy.Provenance]
                };
                counters.Indexed++;

                if ( body is not null && StructuredConfigExtractor.IsStructured( canonicalEntry ) )
                {
                    foreach ( var config in StructuredConfigExtractor.Extract(
                        canonicalEntry,
                        body ) )
                    {
                        yield return CreateConfigurationItem(
                            canonicalEntry,
                            size,
                            modified,
                            config,
                            policy );
                        counters.Indexed++;
                    }
                }

                if ( counters.Indexed % 50 == 0 )
                {
                    progress?.Report( new IndexProgress
                    {
                        Stage = "scan-files",
                        Discovered = counters.Discovered,
                        Indexed = counters.Indexed,
                        Skipped = counters.Skipped,
                        Failed = counters.Failed,
                        CurrentPath = canonicalEntry
                    } );
                }
            }
        }
    }

    private sealed class ScanCounters
    {
        public long Discovered { get; set; }
        public long Indexed { get; set; }
        public long Skipped { get; set; }
        public long Failed { get; set; }
    }
}
