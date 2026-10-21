namespace CommandDeck.Specs;

public static partial class Program
{
    // What hey 1.6 does when stdout is a pipe: the error envelope goes to stderr
    // and the process exits 3.
    private static ProcessResult HeyError( string json, int exitCode = 3 ) => new( )
    {
        ExitCode = exitCode,
        StandardOutput = "",
        StandardError = json
    };

    private static async Task HeyClientReadsErrorsFromStandardErrorAsync( )
    {
        HeyFake signedOut = new( )
        {
            Respond = _ => HeyError( HeySignedOutJson )
        };
        var client = signedOut.Client( );

        Equal( HeyFailureKind.SignedOut, (await client.SearchAsync( new HeySearchQuery { Text = "invoice" }, default )).Failure?.Kind );
        Equal( "Not logged in", (await client.SearchAsync( new HeySearchQuery { Text = "invoice" }, default )).Failure?.Message );
        Equal( HeyFailureKind.SignedOut, (await client.ListBoxAsync( "imbox", 10, default )).Failure?.Kind );
        Equal( HeyAvailability.SignedOut, (await client.GetStatusAsync( default )).Availability );
        Equal( HeyFailureKind.SignedOut, (await client.ReadThreadAsync( 9001, default )).Failure?.Kind );
        Equal( HeyFailureKind.SignedOut, (await client.TriageAsync( HeyTriageChanges.Seen, 601, null, default ))?.Kind );

        // Text that is not an error envelope explains nothing; the exit code does.
        HeyFake noisy = new( )
        {
            Respond = _ => new ProcessResult { ExitCode = 3, StandardOutput = "panic: oops", StandardError = "warning: something" }
        };
        var failure = (await noisy.Client( ).SearchAsync( new HeySearchQuery { Text = "invoice" }, default )).Failure;
        Equal( HeyFailureKind.Failed, failure?.Kind );
        Equal( "The HEY CLI exited with code 3.", failure?.Message );

        // The provider shows the sign-in row, not a parse failure.
        var rows = await HeySearchAsync( HeyProviderFor( signedOut ), "hey: invoice" );
        Equal( "hey:state:signed-out", rows.Single( ).Id );
    }

    private const string HeyOverflowingErrors = "HEY failed with more error output than CommandDeck reads.";

    // A failing hey that writes more errors than CommandDeck reads failed; it
    // did not answer with a thread or a listing too long to read.
    private static async Task HeyClientReportsOverflowingErrorsAsync( )
    {
        var exitCode = 137;
        HeyFake noisy = new( )
        {
            Respond = _ => new ProcessResult { ExitCode = exitCode, StandardOutput = "", StandardError = new string( 'e', 4096 ), StandardErrorLimitExceeded = true }
        };
        var client = noisy.Client( );

        foreach ( var code in new[] { 137, 0 } )
        {
            // A child can exit before the runner's overflow kill reaches it.
            exitCode = code;
            Equal( HeyOverflowingErrors, (await client.ReadThreadAsync( 9001, default )).Failure?.Message );
            Equal( HeyOverflowingErrors, (await client.SearchAsync( new HeySearchQuery { Text = "invoice" }, default )).Failure?.Message );
            Equal( HeyOverflowingErrors, (await client.ListBoxAsync( "imbox", 10, default )).Failure?.Message );
            var revision = client.ChangeGeneration;
            Equal( HeyOverflowingErrors, (await client.TriageAsync( HeyTriageChanges.Seen, 601, null, default ))?.Message );
            True( client.ChangeGeneration > revision, "an unreadable mutation invalidates listings even with a successful exit" );
        }

        // A readable error envelope still explains itself.
        HeyFake signedOut = new( )
        {
            Respond = _ => new ProcessResult { ExitCode = 3, StandardOutput = "", StandardError = HeySignedOutJson, StandardErrorLimitExceeded = true }
        };
        Equal( HeyFailureKind.SignedOut, (await signedOut.Client( ).ReadThreadAsync( 9001, default )).Failure?.Kind );

        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        // The real runner and a stand-in hey that writes 200 KB of errors.
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-hey-stderr-" ).FullName;
        try
        {
            var script = Path.Combine( root, "hey" );
            await File.WriteAllTextAsync( script, "#!/bin/sh\nhead -c 204800 /dev/zero | tr '\\0' e >&2\nexit 1\n" );
            File.SetUnixFileMode( script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );

            var thread = await new HeyClient( resolve: ( ) => script ).ReadThreadAsync( 9001, default );
            Equal( HeyFailureKind.Failed, thread.Failure?.Kind );
            Equal( HeyOverflowingErrors, thread.Failure?.Message );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
