namespace CommandDeck;

public sealed partial class PacmanCatalog
{
    internal const string UnsupportedQueryError =
        "The package query contains unsupported characters.";

    private const string SearchFailedError = "The package search could not be completed.";
    private const string SearchTimedOutError = "The package search timed out.";
    private const string SearchSizeFormat = "%n\\t%m\\t%k";
    private const int MaximumQueryCharacters = 128;
    private const int MaximumSearchLimit = 64;

    /// <summary>
    /// Searches the local sync databases. Returns an error string instead of
    /// throwing when the query itself is unusable.
    /// </summary>
    public async ValueTask<PackageSearchResult> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken )
    {
        if ( !IsSupportedQuery( query ) )
        {
            return new PackageSearchResult
            {
                Query = Sanitize( query ),
                Error = UnsupportedQueryError
            };
        }

        var bounded = Math.Clamp( limit, 1, MaximumSearchLimit );
        var result = await _runner(
            PacmanExecutable,
            ["-Ss", "--color", "never", "--", query],
            cancellationToken ).ConfigureAwait( false );

        if ( result.TimedOut )
        {
            return new PackageSearchResult { Query = query, Error = SearchTimedOutError };
        }

        if ( result.MemoryLimitExceeded )
        {
            return new PackageSearchResult { Query = query, Error = SearchFailedError };
        }

        if ( !result.AnyOutputLimitExceeded && result.ExitCode != 0 )
        {
            // pacman -Ss exits non-zero and prints nothing when nothing matches.
            return string.IsNullOrWhiteSpace( result.StandardOutput )
                ? new PackageSearchResult { Query = query }
                : new PackageSearchResult { Query = query, Error = SearchFailedError };
        }

        var records = PacmanOutputParser.ParseSearchOutput( result.StandardOutput );
        var truncated = result.AnyOutputLimitExceeded || records.Count > bounded;
        if ( records.Count > bounded )
        {
            records.RemoveRange( bounded, records.Count - bounded );
        }

        if ( records.Count > 0 )
        {
            await ApplySearchSizesAsync( records, cancellationToken ).ConfigureAwait( false );
        }

        return new PackageSearchResult
        {
            Query = query,
            Packages = records,
            Truncated = truncated,
            HelperAvailable = false
        };
    }

    private async ValueTask ApplySearchSizesAsync(
        List<PackageRecord> records,
        CancellationToken cancellationToken )
    {
        List<string> arguments = ["-S", SearchSizeFormat, "--"];
        foreach ( var record in records )
        {
            arguments.Add( record.Name );
        }

        var result = await _runner( ExpacExecutable, arguments, cancellationToken )
            .ConfigureAwait( false );
        if ( result.TimedOut || string.IsNullOrWhiteSpace( result.StandardOutput ) )
        {
            // Sizes are advisory; a missing or failing expac must not fail search.
            return;
        }

        var sizes =
            PacmanOutputParser.ParseExpacOutput( result.StandardOutput );
        for ( var index = 0; index < records.Count; index++ )
        {
            if ( sizes.TryGetValue( records[index].Name, out var size ) )
            {
                records[index] = WithSizes( records[index], size );
            }
        }
    }

    private static PackageRecord WithSizes( PackageRecord record, PackageSizes sizes ) => new( )
    {
        Name = record.Name,
        Repository = record.Repository,
        Version = record.Version,
        Description = record.Description,
        Installed = record.Installed,
        InstalledVersion = record.InstalledVersion,
        InstalledBytes = sizes.InstalledBytes,
        DownloadBytes = sizes.DownloadBytes
    };

    private static bool IsSupportedQuery( string? query )
    {
        if ( string.IsNullOrWhiteSpace( query ) ||
            query.Length > MaximumQueryCharacters ||
            query[0] == '-' )
        {
            return false;
        }

        foreach ( var character in query )
        {
            var allowed = char.IsAsciiLetterOrDigit( character ) ||
                          character is ' ' or '.' or '_' or '+' or '@' or '-';
            if ( !allowed )
            {
                return false;
            }
        }

        return true;
    }
}
