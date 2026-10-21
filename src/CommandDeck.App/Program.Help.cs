namespace CommandDeck;

public static partial class Program
{
    private static int PrintHelp( )
    {
        Console.WriteLine( """
            CommandDeck — Omarchy-first command palette core

            Usage:
              commanddeck daemon [--config PATH] [--socket PATH]
              commanddeck bridge [--socket PATH]
              commanddeck reindex [--scope full|root|directory|file] [--root ID] [--path PATH]
              commanddeck index status|pause|resume [--root ID]
              commanddeck search QUERY [--limit N] [--json]
              commanddeck extensions help                      (local packages through the running daemon)
              commanddeck pick [--prompt TEXT] [--label-tab] [--index] < choices.txt
              commanddeck suggest QUERY [--limit 1-8] [--json]
              commanddeck ai [ask] PROMPT [--provider ID] [--model ID] [--workflow ID] [--template ID] [--attach-text TEXT] --yes [--json]
              commanddeck ai providers [--json]
              commanddeck ai models --provider ID [--json]
              commanddeck ai selection show|clear [--json]
              commanddeck ai selection set --provider ID --model ID [--json]
              commanddeck ai templates list [--json]
              commanddeck ai templates save --id ID --name NAME --user TEMPLATE [--system TEMPLATE] [--workflow ID]
              commanddeck ai templates delete --id ID --yes
              commanddeck ai settings show [--json]              (running daemon)
              commanddeck ai enable|disable [--json]             (running daemon)
              commanddeck ai providers add ID [--preset ollama|openai|xai-grok] [--name NAME] [--kind KIND]
                                             [--base-url URL] --model MODEL [--credential-kind none|environment|systemd]
                                             [--credential-name NAME] [--allow-remote] [--context N] [--disabled] [--use]
              commanddeck ai providers edit ID [same options] [--use]
              commanddeck ai providers enable|disable ID
              commanddeck ai providers remove ID --yes
              commanddeck ai providers test ID | test --draft [options]
              commanddeck status [--json]
              commanddeck catalog inspect [--offset N] [--limit N] [--json]
              commanddeck catalog audit [--json]
              commanddeck catalog integrity [--json]
              commanddeck catalog optimize [--json]
              commanddeck catalog repair [--json]
              commanddeck catalog purge --yes [--json]
              commanddeck preferences show [--json]
              commanddeck index roots [--json]
              commanddeck index add <path>
              commanddeck index remove <root-id>
              commanddeck index disable <root-id>
              commanddeck index enable <root-id>
              commanddeck index import <export-file>
              commanddeck preferences export
              commanddeck preferences reset --yes
              commanddeck ranking inspect [--limit N] [--json]
              commanddeck ranking export
              commanddeck ranking reset --yes
              commanddeck history inspect [--limit N] [--json]
              commanddeck history export
              commanddeck history delete ID --yes
              commanddeck history reset --yes
              commanddeck quicklinks [list|add|edit|remove|disable|enable|restore]  Manage quicklinks (add/edit: --title --url [--alias --icon])
              commanddeck filters list [--json]
              commanddeck filters alias set TOKEN FILTER_ID
              commanddeck filters alias remove TOKEN
              commanddeck saved-search list [--json]
              commanddeck saved-search save NAME --query QUERY [--shortcut CHORD] [--home]
              commanddeck saved-search rename ID NAME
              commanddeck saved-search run ID [--limit N] [--json]
              commanddeck saved-search delete ID --yes
              commanddeck agent status|backends|models|preferences|login|logout|ask|conversation
              commanddeck agent trust|grants|packages|probe   (see 'commanddeck agent help')
              commanddeck doctor
              commanddeck hotkey install [--chord "CTRL + SPACE"]   (Omarchy Hyprland Lua binding)
              commanddeck hotkey remove
              commanddeck config init [--config PATH] [--force]
              commanddeck version

            Search syntax:
              firefox                 universal search
              app: firefox            applications
              file: architecture      files
              cfg: gaps_out           structured configuration
              win: terminal           Hyprland windows
              workspace: 2            Hyprland workspaces
              oma: theme              Omarchy controls
              system:                 native Omarchy System Menu route
              cmd: cleanup             configured commands
              link: arch               configured and user quicklinks
              ai: explain this        explicit AI command or assistant conversation
              codex: fix my config    assistant conversation with Codex
              = (19.99 * 1.2) + 4.5  calculator

            Global options:
              --config PATH   JSON/JSONC configuration
              --verbose       diagnostic logging
            """ );
        return 0;
    }
}
