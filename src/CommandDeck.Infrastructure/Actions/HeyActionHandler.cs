using System.Globalization;

namespace CommandDeck;

// HEY actions. A thread opens where Settings says: HEY's web app, the way
// Omarchy's own HEY launcher opens it, or the HEY terminal app. Sign-in and
// install run in Omarchy's presented terminal so the output stays readable
// until the user dismisses it.
internal sealed partial class HeyActionHandler(
    ILauncherSearchPreferences? preferences,
    HeyClient? hey ) : ILinuxActionHandler
{
    public const string HeyWebAppLauncher = "omarchy-launch-webapp";
    public const string PresentedTerminalLauncher = "omarchy-launch-floating-terminal-with-presentation";
    public const string MiseInstaller = "omarchy-mise-install";
    public const string HeyInstallGuideUrl = "https://github.com/basecamp/hey-cli#install";
    public const string TuiLaunchOrFocus = "omarchy-launch-or-focus-tui";

    private static readonly string[] OwnedKinds =
    [
        ActionKinds.HeyOpen,
        ActionKinds.HeyOpenPage,
        ActionKinds.HeyReadThread,
        ActionKinds.HeyTriage,
        ActionKinds.HeySignIn,
        ActionKinds.HeyInstall
    ];

    public IReadOnlyList<string> Kinds => OwnedKinds;

    public async ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken ) => action.Kind switch
        {
            ActionKinds.HeyOpen => await OpenHeyThreadAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.HeyOpenPage => await OpenHeyPageAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.HeyReadThread => await ReadHeyThreadAsync( result, action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.HeyTriage => await TriageHeyThreadAsync( result, action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.HeySignIn => await SignInToHeyAsync( cancellationToken ).ConfigureAwait( false ),
            ActionKinds.HeyInstall => await InstallHeyAsync( cancellationToken ).ConfigureAwait( false ),
            _ => LinuxActionArguments.UnknownKind( action )
        };

    // The daemon runs sandboxed (a read-only root, NoNewPrivileges, UMask=0077), and
    // Omarchy's launchers start their program with `uwsm-app` in scope mode, which
    // keeps the caller's mounts and restrictions. Run from here, a browser would
    // inherit that sandbox. So each launcher runs as a new user service, outside
    // the sandbox, the way applications are launched.
    private static ValueTask<ActionExecutionResult> LaunchOutsideSandboxAsync(
        string launcher,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken ) =>
        ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = ResolveOnPath( launcher ), Arguments = arguments },
            terminal: false,
            cancellationToken );

    // The user service gets the session PATH too; an absolute path removes the doubt.
    private static string ResolveOnPath( string executable )
    {
        foreach ( var directory in (Environment.GetEnvironmentVariable( "PATH" ) ?? "").Split(
            Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ) )
        {
            var candidate = Path.Combine( directory, executable );
            if ( Path.IsPathFullyQualified( candidate ) && ProcessRunner.ExistsOnPath( candidate ) )
            {
                return candidate;
            }
        }

        return executable;
    }

    private async ValueTask<ActionExecutionResult> OpenHeyThreadAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var thread = ReadHeyThread( action );
        if ( thread is null )
        {
            return ActionExecutionResult.Failure( "This HEY thread link is not valid." );
        }

        if ( await HeyOpenTargetAsync( cancellationToken ).ConfigureAwait( false ) != HeyOpenTargets.Terminal )
        {
            return await OpenHeyWebAsync( thread.Value.AppUrl, cancellationToken ).ConfigureAwait( false );
        }

        var topicId = thread.Value.TopicId;
        return await OpenHeyTuiAsync( HeyClient.TuiTopic( topicId ),
            ( client, token ) => client.OpenInRunningTuiAsync( topicId, token ), cancellationToken ).ConfigureAwait( false );
    }

    // HEY's home, The Screener, or a new email. HEY's terminal app has no compose
    // destination, so a new email always opens HEY's web compose page.
    private async ValueTask<ActionExecutionResult> OpenHeyPageAsync( CommandAction action, CancellationToken cancellationToken )
    {
        var page = action.Arguments.GetValueOrDefault( "page" );
        if ( page is not (HeyPages.Home or HeyPages.Screener or HeyPages.Compose) )
        {
            return ActionExecutionResult.Failure( "This HEY page is not known." );
        }

        if ( page == HeyPages.Compose || await HeyOpenTargetAsync( cancellationToken ).ConfigureAwait( false ) != HeyOpenTargets.Terminal )
        {
            return await OpenHeyWebAsync( HeyPages.WebUrl( page ), cancellationToken ).ConfigureAwait( false );
        }

        return page == HeyPages.Screener
            ? await OpenHeyTuiAsync( [HeyClient.TuiScreener],
                static ( client, token ) => client.OpenScreenerInRunningTuiAsync( token ), cancellationToken ).ConfigureAwait( false )
            : await OpenHeyTuiAsync( [], null, cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<string> HeyOpenTargetAsync( CancellationToken cancellationToken ) =>
        preferences is null
            ? HeyOpenTargets.Web
            : (await preferences.GetPreferencesAsync( cancellationToken ).ConfigureAwait( false )).HeyOpenTarget;

    // A running HEY TUI takes the destination over its socket and its window is
    // raised; otherwise a new one opens on it. Omarchy's launcher finds the
    // window by the app id every HEY launcher shares and evals its command, so
    // the binary path is quoted.
    private async ValueTask<ActionExecutionResult> OpenHeyTuiAsync(
        IReadOnlyList<string> destination,
        Func<HeyClient, CancellationToken, ValueTask<bool>>? deliver,
        CancellationToken cancellationToken )
    {
        var executable = hey?.Executable;
        if ( hey is null || executable is null )
        {
            return ActionExecutionResult.Failure( "The HEY CLI is not installed." );
        }

        var delivered = deliver is not null && await deliver( hey, cancellationToken ).ConfigureAwait( false );
        List<string> tui = ["tui", "--instance", HeyClient.TuiInstance];
        if ( !delivered )
        {
            tui.AddRange( destination );
        }

        if ( ProcessRunner.ExistsOnPath( TuiLaunchOrFocus ) )
        {
            return await LaunchOutsideSandboxAsync( TuiLaunchOrFocus, ["--app-id=" + HeyClient.TuiAppId, ShellQuote( executable ), .. tui],
                cancellationToken ).ConfigureAwait( false );
        }

        // Without Omarchy's launcher there is nothing to raise a running TUI with.
        if ( delivered )
        {
            return ActionExecutionResult.Success( );
        }

        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = executable, Arguments = tui },
            terminal: true,
            cancellationToken ).ConfigureAwait( false );
    }

    // The whole thread in the output dialog, titled with its subject and shown as
    // plain text, so nothing in the mail is loaded or linked.
    private async ValueTask<ActionExecutionResult> ReadHeyThreadAsync(
        SearchResult result,
        CommandAction action,
        CancellationToken cancellationToken )
    {
        if ( !long.TryParse( action.Arguments.GetValueOrDefault( "topicId" ), NumberStyles.None,
            CultureInfo.InvariantCulture, out var topicId ) || topicId <= 0 )
        {
            return ActionExecutionResult.Failure( "This HEY thread is not valid." );
        }

        if ( hey is null )
        {
            return ActionExecutionResult.Failure( "The HEY CLI is not installed." );
        }

        var thread = await hey.ReadThreadAsync( topicId, cancellationToken ).ConfigureAwait( false );
        if ( thread.Failure is not null )
        {
            return ActionExecutionResult.Failure( thread.Failure.Message );
        }

        return ActionExecutionResult.Success( result.Title,
            thread.Trimmed ? thread.Markdown + "\n\n… The rest of this thread is in HEY." : thread.Markdown );
    }

    // The arguments come from the daemon's own cached row; they are checked
    // again so a thread can only ever open HEY's https topic page.
    internal static (long TopicId, string AppUrl)? ReadHeyThread( CommandAction action )
    {
        if ( !long.TryParse( action.Arguments.GetValueOrDefault( "topicId" ), NumberStyles.None,
            CultureInfo.InvariantCulture, out var topicId ) || topicId <= 0 )
        {
            return null;
        }

        var appUrl = action.Arguments.GetValueOrDefault( "appUrl" );
        return appUrl is not null && appUrl == HeyClient.ThreadUrl( topicId )
            ? (topicId, appUrl)
            : null;
    }

    private static async ValueTask<ActionExecutionResult> OpenHeyWebAsync(
        string url,
        CancellationToken cancellationToken )
    {
        if ( ProcessRunner.ExistsOnPath( HeyWebAppLauncher ) )
        {
            return await LaunchOutsideSandboxAsync( HeyWebAppLauncher, [url], cancellationToken ).ConfigureAwait( false );
        }

        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = "xdg-open", Arguments = [url] },
            terminal: false,
            cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<ActionExecutionResult> SignInToHeyAsync( CancellationToken cancellationToken )
    {
        var executable = hey?.Executable;
        if ( executable is null )
        {
            return ActionExecutionResult.Failure( "The HEY CLI is not installed." );
        }

        return await RunInPresentedTerminalAsync( [executable, "auth", "login"], cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask<ActionExecutionResult> InstallHeyAsync( CancellationToken cancellationToken )
    {
        if ( !ProcessRunner.ExistsOnPath( MiseInstaller ) )
        {
            return await ManagedLaunch.LaunchApplicationAsync(
                new DesktopEntryCommand { Executable = "xdg-open", Arguments = [HeyInstallGuideUrl] },
                terminal: false,
                cancellationToken ).ConfigureAwait( false );
        }

        return await RunInPresentedTerminalAsync( [MiseInstaller, "github:basecamp/hey-cli", "hey"], cancellationToken )
            .ConfigureAwait( false );
    }

    // The presentation launcher runs its argument as a bash command, so each
    // word is single-quoted; without it a plain terminal runs the argv as is.
    private static async ValueTask<ActionExecutionResult> RunInPresentedTerminalAsync(
        IReadOnlyList<string> argv,
        CancellationToken cancellationToken )
    {
        if ( ProcessRunner.ExistsOnPath( PresentedTerminalLauncher ) )
        {
            return await LaunchOutsideSandboxAsync( PresentedTerminalLauncher, [string.Join( ' ', argv.Select( ShellQuote ) )],
                cancellationToken ).ConfigureAwait( false );
        }

        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = argv[0], Arguments = [.. argv.Skip( 1 )] },
            terminal: true,
            cancellationToken ).ConfigureAwait( false );
    }

    internal static string ShellQuote( string value ) => "'" + value.Replace( "'", "'\\''", StringComparison.Ordinal ) + "'";
}
