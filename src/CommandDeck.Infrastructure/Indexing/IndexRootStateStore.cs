using System.Globalization;
using System.Text;

namespace CommandDeck;

internal sealed class IndexRootStateStore
{
    private const UnixFileMode PrivateFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly string _path;
    private readonly object _gate = new( );

    public IndexRootStateStore( string databasePath ) =>
        _path = PathExpansion.Expand( databasePath ) + ".index-roots";

    public IReadOnlyDictionary<string, PersistedIndexRootState> Read( )
    {
        lock ( _gate )
        {
            if ( !File.Exists( _path ) )
            {
                return new Dictionary<string, PersistedIndexRootState>( StringComparer.Ordinal );
            }

            try
            {
                return File.ReadLines( _path, Encoding.UTF8 )
                    .Select( Deserialize )
                    .Where( static value => value is not null )
                    .Cast<PersistedIndexRootState>( )
                    .GroupBy( static value => value.Id, StringComparer.Ordinal )
                    .ToDictionary( static group => group.Key, static group => group.Last( ),
                        StringComparer.Ordinal );
            }
            catch ( Exception exception ) when (
                exception is IOException or UnauthorizedAccessException or FormatException )
            {
                return new Dictionary<string, PersistedIndexRootState>( StringComparer.Ordinal );
            }
        }
    }

    public void Write( IEnumerable<PersistedIndexRootState> states )
    {
        lock ( _gate )
        {
            var directory = Path.GetDirectoryName( _path )!;
            Directory.CreateDirectory( directory );
            var temporary = _path + ".tmp";
            FileStreamOptions options = new( )
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.SequentialScan
            };
            if ( OperatingSystem.IsLinux( ) )
            {
                options.UnixCreateMode = PrivateFileMode;
            }

            using ( FileStream stream = new( temporary, options ) )
            {
                using ( StreamWriter writer = new( stream, Encoding.UTF8, leaveOpen: true ) )
                {
                    foreach ( var state in states.OrderBy( static value => value.Id,
                                 StringComparer.Ordinal ) )
                    {
                        writer.WriteLine( Serialize( state ) );
                    }

                    writer.Flush( );
                    stream.Flush( flushToDisk: true );
                }
            }
            File.Move( temporary, _path, overwrite: true );
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( _path, PrivateFileMode );
            }

            LinuxDirectoryDurability.FlushParent( _path );
        }
    }

    private static string Serialize( PersistedIndexRootState state ) => string.Join(
        '|',
        Encode( state.Id ),
        Encode( state.Path ),
        state.Paused ? "1" : "0",
        state.PendingEventCount.ToString( CultureInfo.InvariantCulture ),
        state.IndexedItemCount.ToString( CultureInfo.InvariantCulture ),
        state.LastSuccessUnixMilliseconds?.ToString( CultureInfo.InvariantCulture ) ?? string.Empty,
        Encode( state.LastError ) );

    private static PersistedIndexRootState? Deserialize( string line )
    {
        var fields = line.Split( '|' );
        if ( fields.Length != 7 ||
            !long.TryParse( fields[3], NumberStyles.None, CultureInfo.InvariantCulture,
                out var pending ) ||
            !long.TryParse( fields[4], NumberStyles.None, CultureInfo.InvariantCulture,
                out var indexed ) ||
            fields[5].Length > 0 && !long.TryParse(
                fields[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                out _ ) )
        {
            return null;
        }

        return new PersistedIndexRootState(
            Decode( fields[0] ) ?? string.Empty,
            Decode( fields[1] ) ?? string.Empty,
            fields[2] == "1",
            pending,
            indexed,
            fields[5].Length == 0
                ? null
                : long.Parse( fields[5], CultureInfo.InvariantCulture ),
            Decode( fields[6] ) );
    }

    private static string Encode( string? value ) => value is null
        ? string.Empty
        : Convert.ToBase64String( Encoding.UTF8.GetBytes( value ) );

    private static string? Decode( string value ) => value.Length == 0
        ? null
        : Encoding.UTF8.GetString( Convert.FromBase64String( value ) );
}

internal sealed record PersistedIndexRootState(
    string Id,
    string Path,
    bool Paused,
    long PendingEventCount,
    long IndexedItemCount,
    long? LastSuccessUnixMilliseconds,
    string? LastError );
