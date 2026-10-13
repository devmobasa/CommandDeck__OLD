namespace CommandDeck;

public static partial class Program
{
    private static int WriteAgentUsage( int exitCode )
    {
        var writer = exitCode == 0 ? Console.Out : Console.Error;
        writer.WriteLine( """
            Usage: commanddeck agent <action> [options]

              status [--json]                       Assistant enablement, backend, trust, login, helper
              backends [--json]                     Backend inventory with process and inference locality
              models [--backend ID] [--hidden]      Discover models and reasoning efforts for a backend
              preferences show|set ...              Non-escalating preferences (never the trust level)
              login [--backend ID] [--method device-code|api-key|browser]
                                                    Sign in; polls until authenticated (Claude Code asks for the pasted code)
              login complete --code CODE [--backend ID]
                                                    Finish a browser sign-in that showed a code
              logout --yes [--backend ID]           Delete the CommandDeck-owned credential of that backend
              ask "text" [--model ID] [--effort E]  Start a turn and stream it; pending cards are denied
              conversation [--json]                 Print the daemon-held conversation snapshot
              cancel | clear                        Cancel the active turn or clear the conversation
              trust show | trust set LEVEL --yes    Challenge-based trust changes (level 6 needs --phrase)
              grants list|add-folder PATH|remove ID|clear --yes
              packages search QUERY | preview install|remove NAME... | commit PREVIEW_ID --yes
              probe tools                           Print the tool list, sandbox argv, and config (S3 diagnostic)
            """ );
        return exitCode;
    }
}
