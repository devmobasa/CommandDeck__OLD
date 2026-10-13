namespace CommandDeck.Specs;

public static partial class Program
{
    private const string HeyBinary = "/opt/hey/bin/hey";

    private const string HeySignedInJson = """{"ok":true,"data":{"authenticated":true,"base_url":"https://app.hey.com"}}""";
    private const string HeySignedOutJson = """{"ok":false,"error":"Not logged in","code":"auth","hint":"Run: hey auth login"}""";

    private const string HeySearchJson = """
        {"ok":true,"summary":"3 matching threads","notice":"Search stopped after 1 pages. Continue with --page 2.",
         "data":[
          {"id":501,"topic_id":9001,"subject":"Invoice #2291\nfor September","updated_at":"2026-09-20T10:00:00Z",
           "messages":[{"id":7001,"summary":"attached is the\u0007 invoice   for the retainer","created_at":"2026-09-21T08:30:00Z",
             "app_url":"https://app.hey.com/topics/9001/entries/7001","creator":{"id":3,"name":"Jane Cooper","email_address":"jane@example.com"}}]},
          {"topic_id":9002,"subject":"","messages":[{"id":7002,"alternative_sender_name":"Billing Desk","summary":"Re: Q3",
             "app_url":"javascript:alert(1)","creator":{"id":4,"name":"","email_address":"billing@example.com"}}]},
          {"id":503,"subject":"No thread id"}
         ],
         "meta":{"page":1,"pages_fetched":1}}
        """;

    private const string HeyBoxJson = """
        {"ok":true,"data":{"id":11,"kind":"imbox","name":"Imbox","next_page":"cursor-2","postings":[
          {"id":601,"kind":"topic","box_id":11,"topic_id":9101,"name":"Lunch?","summary":"Thursday at noon?","seen":false,
           "active_at":"2026-09-22T12:00:00Z","app_url":"https://app.hey.com/topics/9101","creator":{"id":5,"name":"Alice","email_address":"alice@example.com"}},
          {"id":602,"kind":"bundle","box_id":11,"name":"Newsletters","app_url":"https://app.hey.com/contacts/77"},
          {"id":603,"kind":"topic","box_id":11,"topic_id":9103,"name":"Read me","seen":true,
           "bubble_up_schedule":{"bubble_up_at":"2026-09-23T08:00:00Z"},
           "created_at":"2026-09-19T09:00:00Z","app_url":"https://phish.example/topics/9103","creator":{"id":6,"name":"Bob"}}
        ]}}
        """;

    private static ProcessResult HeyResult( string standardOutput, int exitCode = 0 ) => new( )
    {
        ExitCode = exitCode,
        StandardOutput = standardOutput,
        StandardError = ""
    };

    private sealed class HeyFake
    {
        public List<(string File, IReadOnlyList<string> Args, TimeSpan Timeout, ProcessExecutionLimits Limits)> Calls { get; } = [];

        public Func<IReadOnlyList<string>, ProcessResult> Respond { get; init; } = static args => args[0] switch
        {
            "auth" => HeyResult( HeySignedInJson ),
            "search" => HeyResult( HeySearchJson ),
            "box" => HeyResult( HeyBoxJson ),
            _ => HeyResult( """{"ok":false,"error":"unknown command","code":"usage"}""", 1 )
        };

        public BoundedProcessRunner Runner => ( file, args, timeout, limits, _ ) =>
        {
            lock ( Calls )
            {
                Calls.Add( (file, args, timeout, limits) );
            }

            return ValueTask.FromResult( Respond( args ) );
        };

        public HeyClient Client( ) => new( Runner, static ( ) => HeyBinary, HeyEnvironment );
    }

    private static string? HeyEnvironment( string name ) => name switch
    {
        "PATH" => "/usr/bin",
        "HOME" => "/home/spec",
        "XDG_RUNTIME_DIR" => "/run/user/1000",
        "DBUS_SESSION_BUS_ADDRESS" => "unix:path=/run/user/1000/bus",
        "HEY_ACCOUNT_ID" => "12345",
        "OPENAI_API_KEY" => "secret",
        _ => null
    };

    private static void HeyExecutablePrefersNativeBinaries( )
    {
        Dictionary<string, string> environment = new( StringComparer.Ordinal )
        {
            ["PATH"] = "relative/bin:/home/spec/.local/bin:/usr/bin",
            ["HOME"] = "/home/spec"
        };
        const string wrapper = "/home/spec/.local/bin/hey";
        const string installed = "/home/spec/.local/share/mise/installs/github-basecamp-hey-cli/latest/hey";
        HashSet<string> executables = [wrapper, installed, "/usr/bin/hey", "relative/bin/hey"];
        HashSet<string> native = [installed, "/usr/bin/hey", "relative/bin/hey"];
        string? Resolve( ) => new HeyExecutable( name => environment.GetValueOrDefault( name ),
            executables.Contains, native.Contains ).Resolve( );

        // A package binary on PATH wins; a relative PATH entry is never used.
        Equal( "/usr/bin/hey", Resolve( ) );

        // Only the mise wrapper on PATH: its installed binary skips `mise use -g`.
        executables.Remove( "/usr/bin/hey" );
        Equal( installed, Resolve( ) );

        environment["MISE_DATA_DIR"] = "/data/mise";
        Equal( wrapper, Resolve( ) );
        executables.Add( "/data/mise/installs/github-basecamp-hey-cli/latest/hey" );
        native.Add( "/data/mise/installs/github-basecamp-hey-cli/latest/hey" );
        Equal( "/data/mise/installs/github-basecamp-hey-cli/latest/hey", Resolve( ) );

        executables.Clear( );
        Equal( ( string? ) null, Resolve( ) );

        // COMMANDDECK_HEY names the binary, or turns the integration off.
        executables.Add( "/opt/custom/hey" );
        executables.Add( "/usr/bin/hey" );
        native.Add( "/usr/bin/hey" );
        environment["COMMANDDECK_HEY"] = "/opt/custom/hey";
        Equal( "/opt/custom/hey", Resolve( ) );
        environment["COMMANDDECK_HEY"] = "off";
        Equal( ( string? ) null, Resolve( ) );
        environment["COMMANDDECK_HEY"] = "custom/hey";
        Equal( ( string? ) null, Resolve( ) );
        environment["COMMANDDECK_HEY"] = "/opt/missing/hey";
        Equal( ( string? ) null, Resolve( ) );
    }

    // Resolving walks PATH and reads file headers. Every plain query of three
    // characters asks whether hey is installed, and an action asks again for
    // each step, so the answer is kept for a few seconds.
    private static async Task HeyClientResolvesTheExecutableBrieflyAsync( )
    {
        var resolves = 0;
        string? Resolve( )
        {
            resolves++;
            return HeyBinary;
        }

        HeyFake fake = new( );
        HeyClient client = new( fake.Runner, Resolve, HeyEnvironment );
        HeyProvider provider = new( client, new HeyClock( HeyNow ), TimeSpan.Zero );
        foreach ( var query in new[] { "inv", "invo", "invoi", "invoice" } )
        {
            Equal( "hey:fallback", (await HeySearchAsync( provider, query )).Single( ).Id );
        }

        True( client.IsInstalled );
        Equal( HeyBinary, client.Executable );
        await client.SearchAsync( new HeySearchQuery { Text = "invoice" }, default );
        await client.TriageAsync( HeyTriageChanges.Seen, 601, null, default );
        Equal( 1, resolves );

        // Once the answer is older than its lifetime, hey is looked up again,
        // so an install shows up without a restart.
        string? installed = null;
        var lookups = 0;
        HeyClock clock = new( HeyNow );
        HeyClient installing = new( fake.Runner, ( ) =>
        {
            lookups++;
            return installed;
        }, HeyEnvironment, clock );
        True( !installing.IsInstalled );
        installed = HeyBinary;
        clock.Now += HeyClient.ExecutableLifetime - TimeSpan.FromSeconds( 1 );
        True( !installing.IsInstalled );
        Equal( 1, lookups );
        clock.Now += TimeSpan.FromSeconds( 1 );
        True( installing.IsInstalled );
        Equal( HeyBinary, installing.Executable );
        Equal( 2, lookups );
    }

    private static async Task HeyClientRunsNothingWithoutExecutableAsync( )
    {
        HeyFake fake = new( );
        HeyClient client = new( fake.Runner, static ( ) => null, HeyEnvironment );

        True( !client.IsInstalled );
        Equal( HeyAvailability.NotInstalled, (await client.GetStatusAsync( CancellationToken.None )).Availability );
        Equal( HeyFailureKind.NotInstalled, (await client.SearchAsync( new HeySearchQuery { Text = "invoice" }, CancellationToken.None )).Failure?.Kind );
        Equal( HeyFailureKind.NotInstalled, (await client.ListBoxAsync( "imbox", 20, CancellationToken.None )).Failure?.Kind );
        Equal( 0, fake.Calls.Count );
    }

    private static async Task HeyClientReadsStatusWithCleanEnvironmentAsync( )
    {
        HeyFake fake = new( );
        var status = await fake.Client( ).GetStatusAsync( CancellationToken.None );

        Equal( HeyAvailability.SignedIn, status.Availability );
        Equal( 1, fake.Calls.Count );
        Equal( HeyBinary, fake.Calls[0].File );
        Equal( "auth status --json", string.Join( ' ', fake.Calls[0].Args ) );
        Equal( HeyClient.StatusTimeout, fake.Calls[0].Timeout );

        var limits = fake.Calls[0].Limits;
        Equal( HeyClient.StatusStandardOutputBytes, limits.MaximumStandardOutputBytes );
        Equal( HeyClient.MaximumAddressSpaceBytes, limits.MaximumMemoryBytes );
        True( limits.ClearEnvironment );
        var environment = limits.EnvironmentVariables!;
        Equal( "PATH,HOME,XDG_RUNTIME_DIR,DBUS_SESSION_BUS_ADDRESS,LANG,LC_ALL,NO_COLOR,HEY_NONINTERACTIVE",
            string.Join( ',', environment.Keys.Order( new HeyEnvironmentKeyOrder( ) ) ) );
        Equal( "1", environment["HEY_NONINTERACTIVE"] );
        True( !environment.ContainsKey( "OPENAI_API_KEY" ) && !environment.ContainsKey( "HEY_ACCOUNT_ID" ) );

        Equal( HeyAvailability.SignedOut, (await StatusForAsync( HeyResult( HeySignedOutJson, 1 ) )).Availability );
        Equal( HeyAvailability.SignedOut, (await StatusForAsync( HeyResult( """{"ok":true,"data":{"authenticated":false}}""" ) )).Availability );
        Equal( HeyAvailability.Failed, (await StatusForAsync( HeyResult( "not json", 1 ) )).Availability );
        Equal( HeyAvailability.Failed, (await StatusForAsync( new ProcessResult
        {
            ExitCode = -1,
            StandardOutput = "",
            StandardError = "",
            TimedOut = true
        } )).Availability );
    }

    private static async Task<HeyStatus> StatusForAsync( ProcessResult result )
    {
        HeyFake fake = new( )
        {
            Respond = _ => result
        };
        return await fake.Client( ).GetStatusAsync( CancellationToken.None );
    }

    private static async Task HeyClientPassesSearchArgumentsSafelyAsync( )
    {
        HeyFake fake = new( );
        var client = fake.Client( );

        await client.SearchAsync( new HeySearchQuery
        {
            Text = "--all  invoice\u0000",
            From = "jane --to=x",
            Box = "imbox",
            Attachment = "pdfs"
        }, CancellationToken.None );

        Equal( 1, fake.Calls.Count );
        Equal( "search|--json|--from=jane --to=x|--in=imbox|--attachment=pdfs|--|--all invoice",
            string.Join( '|', fake.Calls[0].Args ) );
        Equal( HeyClient.ReadTimeout, fake.Calls[0].Timeout );
        Equal( HeyClient.ReadStandardOutputBytes, fake.Calls[0].Limits.MaximumStandardOutputBytes );

        // A refinement alone is a search; nothing at all runs no process.
        Equal( "search|--json|--from=alice", string.Join( '|',
            HeyClient.SearchArguments( new HeySearchQuery { From = "alice" } )! ) );
        var empty = await client.SearchAsync( new HeySearchQuery { Text = " \u0007 " }, CancellationToken.None );
        Equal( HeyFailureKind.Rejected, empty.Failure?.Kind );
        Equal( 1, fake.Calls.Count );
    }

    private static async Task HeyClientParsesSearchThreadsAsync( )
    {
        var listing = await new HeyFake( ).Client( ).SearchAsync( new HeySearchQuery { Text = "invoice" }, CancellationToken.None );

        Equal( ( HeyFailure? ) null, listing.Failure );
        True( listing.HasMore );
        Equal( 2, listing.Threads.Count );

        var first = listing.Threads[0];
        Equal( 9001L, first.TopicId );
        Equal( ( long? ) 501, first.PostingId );
        Equal( "Invoice #2291 for September", first.Subject );
        Equal( "Jane Cooper", first.Sender );
        Equal( "jane@example.com", first.SenderAddress );
        Equal( "attached is the invoice for the retainer", first.Summary );
        Equal( new DateTimeOffset( 2026, 9, 21, 8, 30, 0, TimeSpan.Zero ), first.At );
        Equal( "https://app.hey.com/topics/9001", first.AppUrl );
        Equal( ( bool? ) null, first.Seen );
        Equal( ( bool? ) null, first.BubbledUp );

        // No box item, an alternative sender, no subject, and a URL that is
        // not HEY's own https page.
        var second = listing.Threads[1];
        Equal( ( long? ) null, second.PostingId );
        Equal( "Billing Desk", second.Sender );
        Equal( "(no subject)", second.Subject );
        Equal( "https://app.hey.com/topics/9002", second.AppUrl );

        // hey's notice says where to continue when it stopped paging; any
        // other notice says nothing about more matches.
        async Task<bool> HasMoreWith( string notice )
        {
            HeyFake fake = new( )
            {
                Respond = _ => HeyResult( HeySearchJson.Replace( "Search stopped after 1 pages. Continue with --page 2.", notice, StringComparison.Ordinal ) )
            };
            return (await fake.Client( ).SearchAsync( new HeySearchQuery { Text = "invoice" }, CancellationToken.None )).HasMore;
        }

        True( !await HasMoreWith( "Signed in as jane@example.com." ), "a notice that is not about pages" );
        True( !await HasMoreWith( "" ) );
        True( await HasMoreWith( "Showing 25 results. Pagination limit reached; continue with --page using next_page." ) );
    }

    private static async Task HeyClientListsBoxesAsync( )
    {
        HeyFake fake = new( );
        var listing = await fake.Client( ).ListBoxAsync( "imbox", 500, CancellationToken.None );

        Equal( "box|view|imbox|--json|--limit=50", string.Join( '|', fake.Calls[0].Args ) );
        True( listing.HasMore );
        Equal( "9101,9103", string.Join( ',', listing.Threads.Select( static thread => thread.TopicId ) ) );
        Equal( ( bool? ) false, listing.Threads[0].Seen );
        Equal( ( bool? ) true, listing.Threads[1].Seen );
        Equal( ( bool? ) false, listing.Threads[0].BubbledUp );
        Equal( ( bool? ) true, listing.Threads[1].BubbledUp );
        Equal( ( long? ) 11, listing.Threads[0].BoxId );
        Equal( ( long? ) 601, listing.Threads[0].PostingId );
        Equal( "Lunch?", listing.Threads[0].Subject );
        Equal( "Alice", listing.Threads[0].Sender );
        Equal( new DateTimeOffset( 2026, 9, 19, 9, 0, 0, TimeSpan.Zero ), listing.Threads[1].At );
        Equal( "https://app.hey.com/topics/9103", listing.Threads[1].AppUrl );

        await ThrowsAsync<ArgumentOutOfRangeException>( async ( ) =>
            await fake.Client( ).ListBoxAsync( "imbox; rm", 10, CancellationToken.None ) );
    }

    private static async Task HeyClientReportsTypedFailuresAsync( )
    {
        async Task<HeyFailureKind?> KindFor( ProcessResult result )
        {
            HeyFake fake = new( )
            {
                Respond = _ => result
            };
            return (await fake.Client( ).SearchAsync( new HeySearchQuery { Text = "x" }, CancellationToken.None )).Failure?.Kind;
        }

        Equal( HeyFailureKind.SignedOut, await KindFor( HeyResult( HeySignedOutJson, 1 ) ) );
        Equal( HeyFailureKind.Network, await KindFor( HeyResult( """{"ok":false,"error":"dial tcp: offline","code":"network"}""", 1 ) ) );
        Equal( HeyFailureKind.RateLimited, await KindFor( HeyResult( """{"ok":false,"error":"slow down","code":"rate_limit"}""", 1 ) ) );
        Equal( HeyFailureKind.Rejected, await KindFor( HeyResult( """{"ok":false,"error":"bad --date","code":"usage"}""", 1 ) ) );
        Equal( HeyFailureKind.Failed, await KindFor( HeyResult( "", 2 ) ) );
        Equal( HeyFailureKind.Failed, await KindFor( HeyResult( """{"ok":true,"data":{"not":"a list"}}""" ) ) );
        Equal( HeyFailureKind.TimedOut, await KindFor( new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true } ) );
        Equal( HeyFailureKind.Failed, await KindFor( new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", OutputLimitExceeded = true } ) );

        // A successful envelope from a failing process is not trusted.
        Equal( HeyFailureKind.Failed, await KindFor( HeyResult( HeySearchJson, 1 ) ) );

        HeyClient broken = new( static ( _, _, _, _, _ ) => throw new IOException( "exec failed" ), static ( ) => HeyBinary, HeyEnvironment );
        Equal( HeyFailureKind.Failed, (await broken.SearchAsync( new HeySearchQuery { Text = "x" }, CancellationToken.None )).Failure?.Kind );

        // Only the caller's cancellation escapes as an exception.
        HeyClient cancelled = new( static ( _, _, _, _, token ) => throw new OperationCanceledException( token ), static ( ) => HeyBinary, HeyEnvironment );
        await ThrowsAsync<OperationCanceledException>( async ( ) =>
            await cancelled.SearchAsync( new HeySearchQuery { Text = "x" }, new CancellationToken( true ) ) );
    }

    private static void HeyClientCleansMailText( )
    {
        Equal( "a b c", HeyClient.Clean( "  a\tb​\r\nc  ", 50 ) );
        Equal( "", HeyClient.Clean( "\u0000\u0007", 50 ) );
        Equal( "ab", HeyClient.Clean( "ab😀", 3 ) );
        Equal( "https://app.hey.com/topics/5", HeyClient.ThreadUrl( 5 ) );

        // The space kept between words counts toward the bound, and the bound
        // never ends on a space.
        Equal( "abcd", HeyClient.Clean( "abcd e", 5 ) );
        Equal( "ab cd", HeyClient.Clean( "ab cd ef", 5 ) );
        Equal( "abc", HeyClient.Clean( "abc 😀", 5 ) );
        foreach ( var length in Enumerable.Range( 1, 12 ) )
        {
            True( HeyClient.Clean( "a b  c\td e😀 f", length ).Length <= length, "the bound holds at every length" );
        }
    }

    // The real runner: a stand-in `hey` proves the root working directory and
    // that only the forwarded environment reaches the process.
    private static async Task HeyClientRunsRealProcessFromRootAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-hey-client-" ).FullName;
        try
        {
            var script = Path.Combine( root, "hey" );
            await File.WriteAllTextAsync( script, """
                #!/bin/sh
                if [ "$PWD" = "/" ] && [ "$HEY_NONINTERACTIVE" = "1" ] && [ -z "$COMMANDDECK_HEY_SPEC_SECRET" ]; then
                  printf '{"ok":true,"data":{"authenticated":true}}'
                else
                  printf '{"ok":true,"data":{"authenticated":false}}'
                fi
                """ );
            File.SetUnixFileMode( script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "COMMANDDECK_HEY_SPEC_SECRET", "leaked" );

            HeyClient client = new( resolve: ( ) => script );
            var status = await client.GetStatusAsync( CancellationToken.None );

            Equal( HeyAvailability.SignedIn, status.Availability );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "COMMANDDECK_HEY_SPEC_SECRET", null );
            Directory.Delete( root, recursive: true );
        }
    }

    // Keeps the environment assertion readable: fixed order, no set semantics.
    private sealed class HeyEnvironmentKeyOrder : IComparer<string>
    {
        private static readonly string[] Order =
        [
            "PATH", "HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME",
            "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS", "MISE_DATA_DIR", "LANG", "LC_ALL", "NO_COLOR", "HEY_NONINTERACTIVE"
        ];

        public int Compare( string? left, string? right ) =>
            Rank( left ).CompareTo( Rank( right ) );

        private static int Rank( string? key )
        {
            var index = Array.IndexOf( Order, key );
            return index < 0 ? int.MaxValue : index;
        }
    }
}
