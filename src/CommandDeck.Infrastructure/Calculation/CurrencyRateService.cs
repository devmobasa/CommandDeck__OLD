using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Xml;

namespace CommandDeck;

public sealed class CurrencyRateService : IAsyncDisposable
{
    private const int MaximumBytes = 64 * 1024;
    private const UnixFileMode FileModePrivate = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly object _gate = new( );
    private readonly string _cachePath;
    private readonly bool _fetchEnabled;
    private readonly TimeProvider _time;
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly CancellationTokenSource _shutdown = new( );
    private CurrencyRateSnapshot? _snapshot;
    private DateTimeOffset? _fetched;
    private DateTimeOffset? _failed;
    private Task _fetch = Task.CompletedTask;
    private bool _loaded;
    private bool _disposed;
    private Task? _dispose;

    public CurrencyRateService( string cacheFilePath, bool fetchEnabled, TimeProvider? timeProvider = null,
        HttpMessageHandler? handler = null, TimeSpan? requestTimeout = null )
    {
        _cachePath = cacheFilePath;
        _fetchEnabled = fetchEnabled;
        _time = timeProvider ?? TimeProvider.System;
        _timeout = requestTimeout ?? TimeSpan.FromSeconds( 5 );
        _client = new( handler ?? CreateHandler( ) ) { Timeout = Timeout.InfiniteTimeSpan };
    }

    // No redirects, cookies or trace headers: the fetch sends only the documented request.
    internal static SocketsHttpHandler CreateHandler( ) => new( )
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ActivityHeadersPropagator = null,
        ConnectTimeout = TimeSpan.FromSeconds( 5 )
    };

    public CurrencyRateSnapshot? GetRates( )
    {
        lock ( _gate )
        {
            if ( !_loaded )
            {
                _loaded = true;
                LoadCache( );
            }

            var now = _time.GetUtcNow( );
            if ( !_disposed && _fetchEnabled && _fetch.IsCompleted &&
                (_snapshot is null || _fetched is null || now - _fetched >= TimeSpan.FromHours( 6 )) &&
                (_failed is null || now - _failed >= TimeSpan.FromMinutes( 30 )) )
            {
                _fetch = Task.Run( FetchAsync );
            }

            return _snapshot;
        }
    }

    internal Task WhenIdleAsync( )
    {
        lock ( _gate )
        {
            return _fetch;
        }
    }

    private async Task FetchAsync( )
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource( _shutdown.Token );

            timeout.CancelAfter( _timeout );

            using HttpRequestMessage request = new( HttpMethod.Get, "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml" );

            request.Headers.UserAgent.ParseAdd( "CommandDeck" );

            using var response = await _client.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, timeout.Token );
            response.EnsureSuccessStatusCode( );
            if ( response.Content.Headers.ContentLength > MaximumBytes )
            {
                throw new FormatException( );
            }

            await using var stream = await response.Content.ReadAsStreamAsync( timeout.Token );
            using MemoryStream body = new( );
            var buffer = new byte[4096];

            while ( true )
            {
                var read = await stream.ReadAsync( buffer.AsMemory( 0, Math.Min( buffer.Length, MaximumBytes + 1 - ( int ) body.Length ) ), timeout.Token );
                if ( read == 0 )
                {
                    break;
                }

                body.Write( buffer, 0, read );
                if ( body.Length > MaximumBytes )
                {
                    throw new FormatException( );
                }
            }

            body.Position = 0;
            var snapshot = ParseXml( body );
            var fetched = _time.GetUtcNow( );

            lock ( _gate )
            {
                _snapshot = snapshot;
                _fetched = fetched;
                _failed = null;
            }

            WriteCache( snapshot, fetched );
        }
        catch ( Exception )
        {
            lock ( _gate )
            {
                _failed = _time.GetUtcNow( );
            }
        }
    }

    private static CurrencyRateSnapshot ParseXml( Stream stream )
    {
        using var reader = XmlReader.Create( stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumBytes,
            MaxCharactersFromEntities = 0
        } );
        DateOnly? date = null;
        Dictionary<string, decimal> rates = new( StringComparer.Ordinal );

        while ( reader.Read( ) )
        {
            if ( reader.NodeType != XmlNodeType.Element || reader.LocalName != "Cube" )
            {
                continue;
            }

            var time = reader.GetAttribute( "time" );
            if ( time is not null )
            {
                if ( date is not null || !DateOnly.TryParseExact( time, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed ) )
                {
                    throw new FormatException( );
                }

                date = parsed;
            }

            var code = reader.GetAttribute( "currency" );
            var rate = reader.GetAttribute( "rate" );
            if ( code is not null || rate is not null )
            {
                AddRate( rates, code, rate );
            }
        }

        if ( date is null || rates.Count == 0 )
        {
            throw new FormatException( );
        }

        return new( date.Value, rates.ToFrozenDictionary( StringComparer.Ordinal ) );
    }

    private static void AddRate( Dictionary<string, decimal> rates, string? code, string? text )
    {
        if ( code is null || code.Length != 3 || code.Any( c => c is < 'A' or > 'Z' ) ||
            !decimal.TryParse( text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate ) ||
            rate <= 0 || !rates.TryAdd( code, rate ) )
        {
            throw new FormatException( );
        }
    }

    private void LoadCache( )
    {
        try
        {
            using FileStream stream = new( _cachePath, FileMode.Open, FileAccess.Read, FileShare.Read );
            if ( stream.Length > MaximumBytes )
            {
                return;
            }

            using StreamReader reader = new( stream, Encoding.UTF8 );
            var dateText = reader.ReadLine( );
            var fetchedText = reader.ReadLine( );
            if ( !DateOnly.TryParseExact( dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date ) ||
                !DateTimeOffset.TryParseExact( fetchedText, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fetched ) ||
                fetched.Offset != TimeSpan.Zero )
            {
                return;
            }

            Dictionary<string, decimal> rates = new( StringComparer.Ordinal );

            while ( reader.ReadLine( ) is { } line )
            {
                var fields = line.Split( ' ' );
                if ( fields.Length != 2 )
                {
                    return;
                }

                AddRate( rates, fields[0], fields[1] );
            }

            if ( rates.Count > 0 )
            {
                _snapshot = new( date, rates.ToFrozenDictionary( StringComparer.Ordinal ) );
                _fetched = fetched;
            }
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException ) { }
    }

    private void WriteCache( CurrencyRateSnapshot snapshot, DateTimeOffset fetched )
    {
        var directory = Path.GetDirectoryName( Path.GetFullPath( _cachePath ) )!;
        if ( !Directory.Exists( directory ) )
        {
            if ( OperatingSystem.IsLinux( ) )
            {
                Directory.CreateDirectory( directory, FileModePrivate | UnixFileMode.UserExecute );
            }
            else
            {
                Directory.CreateDirectory( directory );
            }
        }

        var temporary = _cachePath + "." + Guid.NewGuid( ).ToString( "N" ) + ".tmp";

        try
        {
            FileStreamOptions options = new( ) { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if ( OperatingSystem.IsLinux( ) )
            {
                options.UnixCreateMode = FileModePrivate;
            }

            using ( FileStream stream = new( temporary, options ) )
            {
                using StreamWriter writer = new( stream, new UTF8Encoding( false ), leaveOpen: true );

                writer.WriteLine( snapshot.Date.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture ) );
                writer.WriteLine( fetched.ToUniversalTime( ).ToString( "O", CultureInfo.InvariantCulture ) );

                foreach ( var (code, rate) in snapshot.PerEuro.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
                {
                    writer.WriteLine( code + " " + rate.ToString( CultureInfo.InvariantCulture ) );
                }

                writer.Flush( );
                stream.Flush( flushToDisk: true );
            }

            File.Move( temporary, _cachePath, overwrite: true );
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( _cachePath, FileModePrivate );
            }

            LinuxDirectoryDurability.FlushParent( _cachePath );
        }
        finally
        {
            File.Delete( temporary );
        }
    }

    public ValueTask DisposeAsync( )
    {
        lock ( _gate )
        {
            if ( _dispose is null )
            {
                _disposed = true;
                _dispose = DisposeCoreAsync( _fetch );
            }

            return new( _dispose );
        }
    }

    private async Task DisposeCoreAsync( Task fetch )
    {
        await _shutdown.CancelAsync( );
        await fetch;

        _client.Dispose( );
        _shutdown.Dispose( );
    }
}
