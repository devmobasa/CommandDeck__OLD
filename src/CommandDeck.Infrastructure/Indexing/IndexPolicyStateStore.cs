using System.Text;

namespace CommandDeck;

// Persist only policy inputs, not source-file contents. This detects ignore and
// project-marker changes made while the watcher was stopped, including additions.
internal sealed class IndexPolicyStateStore( string databasePath )
{
    private const int MaximumInputs = 100_000;
    private const int MaximumStateBytes = 32 * 1024 * 1024;
    private readonly string _path = PathExpansion.Expand( databasePath ) + ".index-policy";
    private readonly object _writeLock = new( );
    private IndexExclusionPolicy? _savedPolicy;
    private long _savedRevision;

    public bool IsCurrent( IndexExclusionPolicy policy )
    {
        try
        {
            if ( !File.Exists( _path ) || new FileInfo( _path ).Length > MaximumStateBytes )
            {
                return false;
            }
            using StreamReader reader = new( _path, Encoding.UTF8 );
            if ( reader.ReadLine( ) != policy.ConfigurationFingerprint )
            {
                return false;
            }
            List<PolicyInputStamp> inputs = [];
            while ( reader.ReadLine( ) is { } line )
            {
                var fields = line.Split( '|' );
                if ( fields.Length < 3 || fields[0] is not ("d" or "f") || inputs.Count >= MaximumInputs )
                {
                    return false;
                }
                var path = Decode( fields[1] );
                if ( !Path.IsPathFullyQualified( path ) )
                {
                    return false;
                }
                inputs.Add( new PolicyInputStamp( path, fields[0] == "d", fields[2] )
                {
                    Scopes = fields.Skip( 3 ).Select( Decode ).ToArray( )
                } );
            }
            if ( !policy.RestoreInputsIfCurrent( inputs ) )
            {
                return false;
            }
            _savedPolicy = policy;
            _savedRevision = policy.InputRevision;
            return true;
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or FormatException )
        {
            return false;
        }
    }

    public void Write( IndexExclusionPolicy policy )
    {
        lock ( _writeLock )
        {
            if ( ReferenceEquals( _savedPolicy, policy ) && _savedRevision == policy.InputRevision )
            {
                return;
            }
            var inputs = policy.SnapshotInputs( out var revision );
            WriteSnapshot( policy.ConfigurationFingerprint, inputs );
            // Record only the captured revision after durable publication. Inputs
            // observed during the write remain dirty for the next event batch.
            _savedPolicy = policy;
            _savedRevision = revision;
        }
    }

    private void WriteSnapshot( string configurationFingerprint, PolicyInputStamp[] inputs )
    {
        var temporary = _path + ".tmp";
        Directory.CreateDirectory( Path.GetDirectoryName( _path )! );
        FileStreamOptions options = new( )
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.SequentialScan
        };
        if ( OperatingSystem.IsLinux( ) )
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using ( FileStream stream = new( temporary, options ) )
        {
            using ( StreamWriter writer = new( stream, Encoding.UTF8, leaveOpen: true ) )
            {
                // An oversized snapshot deliberately stays stale: startup reconciles
                // again instead of claiming that unrecorded ignore inputs are current.
                writer.WriteLine( inputs.Length > MaximumInputs ? "incomplete" : configurationFingerprint );
                foreach ( var input in inputs.Take( MaximumInputs ) )
                {
                    writer.WriteLine( string.Join( '|', new[]
                    {
                    input.Directory ? "d" : "f", Encode( input.Path ), input.Fingerprint
                }.Concat( input.Scopes.Select( Encode ) ) ) );
                }
                writer.Flush( );
                stream.Flush( flushToDisk: true );
            }
        }
        File.Move( temporary, _path, overwrite: true );
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( _path, UnixFileMode.UserRead | UnixFileMode.UserWrite );
        }
        LinuxDirectoryDurability.FlushParent( _path );
    }

    private static string Encode( string value ) => Convert.ToBase64String( Encoding.UTF8.GetBytes( value ) );
    private static string Decode( string value ) => Encoding.UTF8.GetString( Convert.FromBase64String( value ) );
}
