namespace CommandDeck;

public sealed partial class FileIndexSource
{

    private async ValueTask<(FileIndexPolicy Policy, string? Body)> ExtractBodyAsync(
        CanonicalPathBoundary boundary,
        IndexRootOptions rootOptions,
        string path,
        long size,
        ScanCounters counters,
        CancellationToken cancellationToken )
    {
        var policy = FileIndexPolicyResolver.Resolve( rootOptions, path );
        if ( !policy.IncludeContent )
        {
            return (policy, null);
        }

        // A directory authorized earlier can be replaced while its entries are
        // enumerated. Revalidate before reading, as well as before publishing.
        if ( !boundary.TryResolve( path, rootOptions.FollowSymbolicLinks, out var verifiedPath ) ||
            !string.Equals( path, verifiedPath, StringComparison.Ordinal ) )
        {
            return (policy, null);
        }

        var maximumBytes = _contentExtractor.GetMaximumBytes( path, policy );
        if ( maximumBytes is not null && size > maximumBytes.Value )
        {
            RecordDiagnostic(
                boundary,
                path,
                IndexExclusionReasons.SizeLimit,
                $"maximumBytes={maximumBytes.Value}" );
            return (policy, null);
        }

        try
        {
            var extraction = await _contentExtractor.ExtractAsync(
                path,
                size,
                policy,
                cancellationToken ).ConfigureAwait( false );
            if ( extraction.FailureReason is not null )
            {
                counters.Failed++;
                RecordWorkItem( path, extraction.FailureReason );
            }
            return (policy, extraction.Text);
        }
        catch ( Exception exception ) when (
            exception is UnauthorizedAccessException or IOException )
        {
            counters.Failed++;
            RecordWorkItem( path, "content-extraction-failed" );
            return (policy, null);
        }
    }

    private bool TryAuthorizeEntry(
        CanonicalPathBoundary boundary,
        IndexRootOptions rootOptions,
        string entry,
        out string canonicalEntry,
        out string reason,
        out string? rule )
    {
        if ( _storagePaths?.Contains( entry ) == true )
        {
            canonicalEntry = string.Empty;
            reason = IndexExclusionReasons.IndexStorage;
            rule = null;
            return false;
        }
        if ( !boundary.TryResolve(
                entry,
                rootOptions.FollowSymbolicLinks,
                out canonicalEntry ) )
        {
            reason = IndexExclusionReasons.PathBoundary;
            rule = null;
            return false;
        }

        var exclusion = Exclusions.Match( boundary, rootOptions, entry, canonicalEntry,
            Directory.Exists( canonicalEntry ) );
        reason = exclusion?.Reason ?? string.Empty;
        rule = exclusion?.Rule;
        return exclusion is null;
    }

    // Authorizes one enumerated entry of an authorized directory with a single
    // status call. A plain entry's canonical path is its parent's path plus its
    // name, so only its own path is checked. A followed symbolic link takes the
    // full boundary and policy check, and a linked directory is entered afresh
    // from the root at its canonical target.
    private EntryDecision AuthorizeEntry( AuthorizedDirectory directory, string entry )
    {
        if ( _storagePaths?.Contains( entry ) == true )
        {
            return EntryDecision.Excluded( new IndexExclusion( IndexExclusionReasons.IndexStorage, null ) );
        }

        if ( !LinuxMountIdentity.TryGetEntry( entry, out var status ) )
        {
            return EntryDecision.Excluded( PathBoundaryExclusion );
        }

        if ( status.SymbolicLink )
        {
            return AuthorizeLinkedEntry( directory, entry );
        }

        if ( !directory.Boundary.IsOnRootMount( status.MountId ) )
        {
            return EntryDecision.Excluded( PathBoundaryExclusion );
        }

        var exclusion = Exclusions.MatchChild( directory, entry, status.Directory, out var child );
        if ( exclusion is not null )
        {
            return EntryDecision.Excluded( exclusion );
        }

        return child is not null
            ? EntryDecision.ForDirectory( child )
            : EntryDecision.ForFile( entry, status );
    }

    private EntryDecision AuthorizeLinkedEntry( AuthorizedDirectory directory, string entry )
    {
        var boundary = directory.Boundary;
        var options = directory.Options;
        if ( !TryAuthorizeEntry( boundary, options, entry, out var canonicalEntry, out var reason, out var rule ) )
        {
            return EntryDecision.Excluded( new IndexExclusion( reason, rule ) );
        }

        if ( !LinuxMountIdentity.TryGetEntry( canonicalEntry, out var status ) )
        {
            return EntryDecision.Failed( canonicalEntry );
        }

        if ( !status.Directory )
        {
            return EntryDecision.ForFile( canonicalEntry, status );
        }

        var child = Exclusions.EnterDirectory( boundary, options, canonicalEntry, out var exclusion );
        return child is null
            ? EntryDecision.Excluded( exclusion ?? PathBoundaryExclusion )
            : EntryDecision.ForDirectory( child );
    }

    private bool IsAuthorizedPath(
        CanonicalPathBoundary boundary,
        IndexRootOptions rootOptions,
        string requestedPath,
        string canonicalPath )
    {
        var exclusion = Exclusions.Match( boundary, rootOptions, requestedPath, canonicalPath,
            Directory.Exists( canonicalPath ) );
        if ( exclusion is null )
        {
            return true;
        }
        RecordDiagnostic( boundary, requestedPath, exclusion.Reason, exclusion.Rule );
        return false;
    }

    private void ClearScopeDiagnostics( string path ) => _diagnostics.RemoveAll( diagnostic =>
        diagnostic.RelativePath != "[redacted]" && Path.IsPathFullyQualified( diagnostic.Root ) &&
        IndexExclusionPolicy.IsWithin( Path.GetFullPath( Path.Combine( diagnostic.Root, diagnostic.RelativePath ) ), path ) );

    private void RecordDiagnostic(
        CanonicalPathBoundary boundary,
        string path,
        string reason,
        string? rule )
    {
        var relativePath = reason == IndexExclusionReasons.PrivacyRule
            ? "[redacted]"
            : boundary.GetRelativePath( path );
        RecordDiagnostic( boundary.LexicalRoot, relativePath, reason, rule );
    }

    private void RecordWorkItem( string path, string reason )
    {
        if ( _workItems.Count >= MaximumDiagnostics )
        {
            return;
        }

        var item = CreateWorkItem( path, IndexWorkStates.Failed, reason );
        if ( _workItems.All( value => !string.Equals( value.Id, item.Id, StringComparison.Ordinal ) ) )
        {
            _workItems.Add( item );
        }
    }

    private IndexWorkItem CreateWorkItem( string path, string state, string reason )
    {
        var root = "[unmatched-root]";
        var relativePath = "[redacted]";
        try
        {
            var fullPath = Path.GetFullPath( path );
            var owningRoot =
                FindOwningRoot( fullPath );
            if ( owningRoot is not null )
            {
                root = Bound( owningRoot.Value.Boundary.LexicalRoot );
                relativePath = _pathPolicy.IsDenied( fullPath )
                    ? "[redacted]"
                    : Bound( owningRoot.Value.Boundary.GetRelativePath( fullPath ) );
            }
        }
        catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException )
        {
            // Invalid paths remain redacted and still receive a stable opaque ID.
        }

        return new IndexWorkItem
        {
            Id = StableId.Create( "index-work", path ),
            State = state,
            Root = root,
            RelativePath = relativePath,
            Reason = reason,
            UpdatedUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds( )
        };
    }

    private void RecordDiagnostic(
        string root,
        string relativePath,
        string reason,
        string? rule )
    {
        if ( _diagnostics.Count >= MaximumDiagnostics )
        {
            return;
        }

        _diagnostics.Add( new IndexExclusionDiagnostic
        {
            Root = Bound( root ),
            RelativePath = Bound( relativePath ),
            Reason = reason,
            Rule = rule is null ? null : Bound( rule )
        } );
    }

    private static string Bound( string value ) => value.Length <= MaximumDiagnosticPathCharacters
        ? value
        : value[..MaximumDiagnosticPathCharacters];

    private static string BuildConfigTitle( ConfigEntry config )
    {
        if ( string.IsNullOrWhiteSpace( config.Section ) ||
            config.Key.StartsWith( config.Section + ".", StringComparison.Ordinal ) )
        {
            return config.Key;
        }

        return $"{config.Section}.{config.Key}";
    }

    private static IndexedItem CreateConfigurationItem(
        string path,
        long sizeBytes,
        long modifiedUnixMilliseconds,
        ConfigEntry config,
        FileIndexPolicy policy )
    {
        var configTitle = BuildConfigTitle( config );
        var line = config.KeyLine.ToString(
            System.Globalization.CultureInfo.InvariantCulture );
        var column = config.KeyColumn.ToString(
            System.Globalization.CultureInfo.InvariantCulture );
        return new IndexedItem
        {
            StableId = StableId.Create(
                ResultKinds.Configuration,
                $"{path}:{config.KeyLine}:{config.KeyColumn}:{config.ValueLine}:" +
                $"{config.ValueColumn}:{configTitle}" ),
            Kind = ResultKinds.Configuration,
            Title = configTitle,
            Subtitle = $"{path}:{line}:{column} · {config.Value}",
            Path = path,
            Icon = "settings",
            Body = config.Value,
            ConfigKeys = $"{config.Section} {config.Key} {config.Value}",
            PayloadJson = PayloadWriter.Create(
                ("line", line),
                ("column", column),
                ("keyLine", line),
                ("keyColumn", column),
                ("valueLine", config.ValueLine.ToString(
                    System.Globalization.CultureInfo.InvariantCulture )),
                ("valueColumn", config.ValueColumn.ToString(
                    System.Globalization.CultureInfo.InvariantCulture )),
                ("key", config.Key),
                ("value", config.Value),
                ("section", config.Section),
                ("assignment", config.Assignment) ),
            SizeBytes = sizeBytes,
            ModifiedUnixMilliseconds = modifiedUnixMilliseconds,
            ProviderId = ProviderIds.Config,
            SourceId = SourceIds.Configuration,
            ExtractorVersion = ContentExtractor.ExtractorVersion,
            IncludeTitleInSearch = policy.IncludeFileName,
            IncludeMetadataInSearch = policy.IncludeMetadata,
            IndexPolicies = [.. policy.Provenance]
        };
    }

    private static readonly IndexExclusion PathBoundaryExclusion = new( IndexExclusionReasons.PathBoundary, null );

    // One entry's outcome: excluded, unreadable, a directory to enter, or a file.
    private readonly record struct EntryDecision(
        IndexExclusion? Exclusion,
        bool MetadataFailed,
        AuthorizedDirectory? Directory,
        string Path,
        LinuxEntryStatus Status )
    {
        public static EntryDecision Excluded( IndexExclusion exclusion ) =>
            new( exclusion, false, null, string.Empty, default );

        public static EntryDecision Failed( string path ) => new( null, true, null, path, default );

        public static EntryDecision ForDirectory( AuthorizedDirectory directory ) =>
            new( null, false, directory, directory.Path, default );

        public static EntryDecision ForFile( string path, LinuxEntryStatus status ) =>
            new( null, false, null, path, status );
    }

    private static string GuessIcon( string extension ) =>
        extension.ToLowerInvariant( ) switch
        {
            ".md" or ".txt" or ".rst" => "text-x-generic",
            ".pdf" => "application-pdf",
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg" => "image-x-generic",
            ".cs" or ".rs" or ".cpp" or ".c" or ".h" or ".py" or ".js" or ".ts" =>
                "text-x-script",
            ".json" or ".jsonc" or ".toml" or ".yaml" or ".yml" or ".ini" or ".conf" =>
                "settings",
            _ => "text-x-generic"
        };
}
