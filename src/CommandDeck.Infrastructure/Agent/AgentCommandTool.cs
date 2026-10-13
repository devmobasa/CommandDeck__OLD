using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public sealed record AgentCommandPlan(
    string Executable,
    string ExecutableIdentity,
    IReadOnlyList<string> Argv,
    string WorkingDirectory,
    string EnvironmentProfile,
    IReadOnlyDictionary<string, string> Environment,
    string Digest );

public sealed record AgentCommandOutcome( int ExitCode, string Output, bool TimedOut, bool Truncated );

public sealed class AgentCommandTool( string? homeDirectory = null )
{
    public const string MinimalProfile = "minimal";
    public const string FixedPath = "/usr/bin:/bin";
    private static readonly IReadOnlyDictionary<string, string> MinimalEnvironment =
        new Dictionary<string, string>( StringComparer.Ordinal )
        {
            ["LANG"] = "C.UTF-8",
            ["LC_ALL"] = "C.UTF-8",
            ["PATH"] = FixedPath
        };
    private readonly string _home = Path.GetFullPath(
        homeDirectory ?? Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ) );

    public AgentCommandPlan Plan( IReadOnlyList<string> argv, string? workingDirectory )
    {
        AgentContractValidator.ValidateArgv( argv );
        var executable = ResolveExecutable( argv[0] );
        var cwd = ResolveWorkingDirectory( workingDirectory );
        var identity = LinuxPathIdentity.TryGet( executable, out var token ) ? token : string.Empty;
        List<string> literal = [executable, .. argv.Skip( 1 )];
        var digest = ComputeDigest( identity, literal, cwd, MinimalProfile, MinimalEnvironment );
        return new AgentCommandPlan( executable, identity, literal, cwd, MinimalProfile, MinimalEnvironment, digest );
    }

    public static string ComputeDigest(
        string identity,
        IReadOnlyList<string> argv,
        string cwd,
        string profile,
        IReadOnlyDictionary<string, string> environment )
    {
        StringBuilder builder = new( );
        builder.Append( identity ).Append( '\n' );
        foreach ( var argument in argv )
        {
            builder.Append( argument ).Append( '\0' );
        }
        builder.Append( '\n' ).Append( cwd ).Append( '\n' ).Append( profile ).Append( '\n' );
        foreach ( (var key, var value) in environment.OrderBy( static pair => pair.Key, StringComparer.Ordinal ) )
        {
            builder.Append( key ).Append( '=' ).Append( value ).Append( '\0' );
        }
        return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( builder.ToString( ) ) ) ).ToLowerInvariant( );
    }

    public static async ValueTask<AgentCommandOutcome> ExecuteAsync( AgentCommandPlan plan, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( plan );
        if ( !LinuxPathIdentity.TryGet( plan.Executable, out var current ) ||
            !string.Equals( current, plan.ExecutableIdentity, StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "The executable changed after confirmation." );
        }
        var result = await ProcessRunner.RunBoundedAsync(
            plan.Executable,
            plan.Argv.Skip( 1 ).ToArray( ),
            plan.WorkingDirectory,
            TimeSpan.FromSeconds( AgentLimits.MaximumCommandSeconds ),
            new ProcessExecutionLimits
            {
                MaximumStandardOutputBytes = AgentLimits.MaximumCommandOutputBytes,
                MaximumStandardErrorBytes = AgentLimits.MaximumCommandOutputBytes / 4,
                MaximumMemoryBytes = 4L * 1024 * 1024 * 1024,
                EnvironmentVariables = plan.Environment
            },
            cancellationToken ).ConfigureAwait( false );
        var combined = result.StandardOutput;
        if ( !string.IsNullOrEmpty( result.StandardError ) )
        {
            combined = string.IsNullOrEmpty( combined ) ? result.StandardError : combined + "\n" + result.StandardError;
        }
        var sanitized = EscapeTerminalControls( combined );
        var truncated = result.OutputLimitExceeded || result.StandardErrorLimitExceeded;
        if ( Encoding.UTF8.GetByteCount( sanitized ) > AgentLimits.MaximumCommandOutputBytes )
        {
            sanitized = TakeUtf8Prefix( sanitized, AgentLimits.MaximumCommandOutputBytes );
            truncated = true;
        }
        return new AgentCommandOutcome( result.ExitCode, sanitized, result.TimedOut, truncated );
    }

    public static string EscapeTerminalControls( string value )
    {
        StringBuilder builder = new( value.Length );
        foreach ( var character in value )
        {
            if ( character is '\n' or '\t' )
            {
                builder.Append( character );
            }
            else if ( char.IsControl( character ) || character == '\u2028' || character == '\u2029' )
            {
                builder.Append( '\uFFFD' );
            }
            else
            {
                builder.Append( character );
            }
        }
        return builder.ToString( );
    }

    private static string TakeUtf8Prefix( string value, int maximumBytes )
    {
        var length = value.Length;
        while ( length > 0 && Encoding.UTF8.GetByteCount( value.AsSpan( 0, length ) ) > maximumBytes )
        {
            length--;
            if ( length > 0 && char.IsHighSurrogate( value[length - 1] ) )
            {
                length--;
            }
        }
        return value[..length];
    }

    private static string ResolveExecutable( string command )
    {
        if ( string.IsNullOrWhiteSpace( command ) || command.Contains( '\0', StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "The command name is invalid." );
        }
        var candidate = command.Contains( '/', StringComparison.Ordinal )
            ? (Path.IsPathRooted( command ) ? command : null)
            : FixedPath.Split( ':', StringSplitOptions.RemoveEmptyEntries )
                .Select( directory => Path.Combine( directory, command ) )
                .FirstOrDefault( File.Exists );
        if ( candidate is null || !File.Exists( candidate ) )
        {
            throw new InvalidOperationException( "The command executable was not found." );
        }
        var canonical = CodexBinaryLocator.ResolveCanonical( candidate );
        if ( !IsExecutable( canonical ) )
        {
            throw new InvalidOperationException( "The command target is not executable." );
        }
        return canonical;
    }

    private static bool IsExecutable( string path )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return false;
        }
        var mode = File.GetUnixFileMode( path );
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    private string ResolveWorkingDirectory( string? workingDirectory )
    {
        var candidate = string.IsNullOrWhiteSpace( workingDirectory )
            ? _home
            : AgentContextBroker.Canonicalize( workingDirectory, _home );
        if ( !Directory.Exists( candidate ) )
        {
            throw new InvalidOperationException( "The working directory does not exist." );
        }
        return candidate;
    }
}
