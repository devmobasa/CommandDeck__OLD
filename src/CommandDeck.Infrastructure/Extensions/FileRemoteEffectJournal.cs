using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public enum JournalAdmission { Recorded, AlreadyAttempted, PackageFull, JournalFull }

public interface IRemoteEffectJournal
{
    ValueTask<JournalAdmission> TryRecordAttemptAsync( string installationKey, string connection, string idempotencyKey, CancellationToken cancellationToken );
    ValueTask<int> CountAsync( string installationKey, CancellationToken cancellationToken );
    ValueTask ClearAsync( string installationKey, bool includeLegacy, CancellationToken cancellationToken );
}

/// <summary>Durable no-resend admission. Only a confirmed reset can revive request keys.</summary>
public sealed class FileRemoteEffectJournal : IRemoteEffectJournal, IDisposable
{
    private readonly string _root;
    private readonly int _packageLimit, _globalLimit;
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private readonly Dictionary<string, int> _counts = new( StringComparer.Ordinal );
    private int _total;
    private bool _loaded;

    public FileRemoteEffectJournal( string directory ) : this( directory, 1024, 4096 ) { }

    internal FileRemoteEffectJournal( string directory, int packageLimit, int globalLimit )
    {
        _root = Path.GetFullPath( directory );
        _packageLimit = packageLimit;
        _globalLimit = globalLimit;
    }

    public async ValueTask<JournalAdmission> TryRecordAttemptAsync( string installationKey, string connection, string idempotencyKey, CancellationToken cancellationToken )
    {
        ValidateKey( installationKey );
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new PlatformNotSupportedException( "Extension brokers require Linux." );
        }
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            Load( );
            var directory = Path.Combine( _root, installationKey );
            var entry = Path.Combine( directory, Digest( connection + "\0" + idempotencyKey ) );
            var legacy = Path.Combine( _root, Digest( installationKey + "\0" + connection + "\0" + idempotencyKey ) );
            if ( File.Exists( entry ) || File.Exists( legacy ) )
            {
                return JournalAdmission.AlreadyAttempted;
            }
            if ( _counts.GetValueOrDefault( installationKey ) >= _packageLimit )
            {
                return JournalAdmission.PackageFull;
            }
            if ( _total >= _globalLimit )
            {
                return JournalAdmission.JournalFull;
            }
            if ( !Directory.Exists( directory ) )
            {
                Directory.CreateDirectory( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
                LinuxDirectoryDurability.FlushParent( directory );
            }
            RejectLink( directory );
            await using var stream = SafeRegularFile.CreatePrivateWriteStream( entry );
            _counts[installationKey] = _counts.GetValueOrDefault( installationKey ) + 1;
            _total++;
            await stream.WriteAsync( "attempted"u8.ToArray( ), cancellationToken ).ConfigureAwait( false );
            stream.Flush( true );
            LinuxDirectoryDurability.FlushParent( entry );
            return JournalAdmission.Recorded;
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask<int> CountAsync( string installationKey, CancellationToken cancellationToken )
    {
        ValidateKey( installationKey );
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            Load( );
            return _counts.GetValueOrDefault( installationKey );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask ClearAsync( string installationKey, bool includeLegacy, CancellationToken cancellationToken )
    {
        ValidateKey( installationKey );
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            Load( );
            var directory = Path.Combine( _root, installationKey );
            if ( Directory.Exists( directory ) )
            {
                RejectLink( directory );
                Directory.Delete( directory, true );
                LinuxDirectoryDurability.FlushParent( directory );
                _total -= _counts.GetValueOrDefault( installationKey );
                _counts.Remove( installationKey );
            }
            if ( includeLegacy )
            {
                foreach ( var entry in Directory.EnumerateFiles( _root ) )
                {
                    File.Delete( entry );
                    _total--;
                }
                LinuxDirectoryDurability.FlushParent( Path.Combine( _root, "." ) );
            }
        }
        finally
        {
            _gate.Release( );
        }
    }

    private void Load( )
    {
        if ( _loaded )
        {
            return;
        }
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new PlatformNotSupportedException( "Extension brokers require Linux." );
        }
        Directory.CreateDirectory( _root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        RejectLink( _root );
        LinuxDirectoryDurability.FlushParent( _root );
        _total = Directory.EnumerateFiles( _root ).Count( );
        foreach ( var directory in Directory.EnumerateDirectories( _root ) )
        {
            RejectLink( directory );
            var count = Directory.EnumerateFiles( directory ).Count( );
            _counts[Path.GetFileName( directory )] = count;
            _total += count;
        }
        _loaded = true;
    }

    private static void RejectLink( string directory )
    {
        if ( (File.GetAttributes( directory ) & FileAttributes.ReparsePoint) != 0 )
        {
            throw new IOException( "The remote-effect journal cannot use symbolic links." );
        }
    }

    private static string Digest( string value ) => Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( value ) ) );

    private static void ValidateKey( string key )
    {
        if ( key is not { Length: > 0 and <= 128 } || key.Any( character => !char.IsAsciiLetterOrDigit( character ) && character is not '-' and not '_' ) )
        {
            throw new ArgumentException( "Invalid journal installation key.", nameof( key ) );
        }
    }

    public void Dispose( ) => _gate.Dispose( );
}
