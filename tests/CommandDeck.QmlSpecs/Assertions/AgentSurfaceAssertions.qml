import QtQuick
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(){
    host.part_agentSpecController.applyStatus({
      enabled: false,
      backend: {
        id: "codex.local", displayName: "Codex", kind: "codex-app-server",
        processLocality: "local", inferenceLocality: "OpenAI",
        authenticationState: "authenticated"
      },
      trust: { level: 2, levelName: "Approved context" },
      preferences: {
        enabled: false, backendId: "codex.local", modelId: "gpt-5-codex",
        reasoningEffort: "medium", trustLevel: 2,
        contextPolicy: "approved-folders", webSearchEnabled: false
      }
    })
    host.expect(host.part_agentSpecSurface.headerText.indexOf("Codex") >= 0
        && host.part_agentSpecSurface.headerText.indexOf("runs locally") >= 0
        && host.part_agentSpecSurface.headerText.indexOf("inference: OpenAI") >= 0
        && host.part_agentSpecSurface.headerText.indexOf("model: gpt-5-codex") >= 0
        && host.part_agentSpecSurface.headerText.indexOf("web search: off") >= 0,
      "the conversation header discloses locality, model, and web search state")
    host.expect(host.part_agentSpecSurface.reasoningEffort === "medium"
        && host.part_agentSpecSurface.reasoningEffortLabel === "Medium"
        && host.part_agentSpecSurface.headerText.indexOf("effort") < 0,
      "the reasoning effort reads as its own labeled chip, not the elided header")
    host.expect(host.part_agentSpecSurface.enableCardVisible
        && !host.part_agentSpecSurface.trustAlarming
        && host.part_agentSpecSurface.trustBadgeText.indexOf("Approved context") >= 0,
      "a disabled assistant shows the inline enable card and a calm trust badge")

    host.part_agentSpecController.applyStatus({
      enabled: true,
      backend: {
        id: "codex.local", displayName: "Codex", processLocality: "local",
        inferenceLocality: "OpenAI", authenticationState: "unauthenticated"
      },
      trust: {
        level: 6, levelName: "Unrestricted",
        leaseExpiresUnixMilliseconds: Date.now() + 3600000
      },
      preferences: { enabled: true, backendId: "codex.local", trustLevel: 6 }
    })
    host.expect(host.part_agentSpecSurface.loginCardVisible
        && host.part_agentSpecSurface.trustAlarming
        && host.part_agentSpecSurface.leaseText.indexOf("lease") >= 0,
      "unauthenticated backends offer login while level 6 stays visibly red")

    host.part_agentSpecController.pending = {
      id: "pending-9", kind: "context",
      prompt: "Read the project folder?",
      context: { operation: "read", path: "/home/user/project" },
      decisions: ["allow", "deny"]
    }
    host.part_agentSpecController.state = "waiting"
    host.expect(host.part_agentSpecSurface.pendingCardVisible
        && host.part_agentSpecSurface.statusText === "Waiting for your answer"
        && host.part_agentSpecSurface.allowsDecision("allow")
        && !host.part_agentSpecSurface.allowsDecision("allow-folder"),
      "the pending card offers only the decisions the daemon allows")

    host.part_agentSpecSurface.pendingHidden = true
    host.expect(!host.part_agentSpecSurface.pendingCardVisible
        && host.part_agentSpecSurface.pendingParked
        && host.part_agentSpecController.pending !== null,
      "hiding a pending card parks it without sending any decision")
    host.part_agentSpecSurface.pendingHidden = false

    host.part_agentSpecController.pending = {
      id: "pending-cmd", kind: "command",
      prompt: "Run this command?",
      action: {
        id: "cmd-1", kind: "command", title: "Rebuild the project",
        risk: "high", executable: "/usr/bin/make",
        argv: ["-C", "/home/user/project", "clean all"],
        workingDirectory: "/home/user/project",
        environmentProfile: "minimal", destructive: true, canRemember: false
      },
      decisions: ["allow", "deny", "cancel-turn"]
    }
    host.part_agentSpecController.state = "waiting"
    host.expect(host.part_agentSpecSurface.pendingCommandText.indexOf("/usr/bin/make") >= 0
        && host.part_agentSpecSurface.pendingCommandText.indexOf("\nclean all") >= 0
        && host.part_agentSpecSurface.pendingCommandText.indexOf(
          "cwd: /home/user/project") >= 0
        && host.part_agentSpecSurface.pendingCommandText.indexOf(
          "environment: minimal") >= 0,
      "a command card shows the resolved executable and every literal argument")
    host.expect(host.part_agentSpecSurface.pendingDestructive
        && !host.part_agentSpecSurface.allowsDecision("allow-always"),
      "an unrememberable destructive command offers no always-allow shortcut")

    host.part_agentSpecController.pending = null
    host.part_agentSpecController.state = "running"
    host.part_agentSpecController.activeText = ""
    host.expect(host.part_agentSpecSurface.statusText === "Thinking"
        && host.part_agentSpecSurface.runningHintText === "Closing leaves the turn running",
      "a running turn says it keeps running after the view closes")
    host.part_agentSpecController.activeText = "answer"
    host.expect(host.part_agentSpecSurface.statusText === "Answering",
      "the status line switches to answering once text arrives")
    host.part_agentSpecController.pending = null
    host.part_agentSpecController.state = "idle"
    host.part_agentSpecController.activeText = ""
  }
}
