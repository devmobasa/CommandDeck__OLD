import QtQuick
import Quickshell
import "./components" as Deck
import "./components/Protocol.js" as Protocol

// Completed sample assistant exchanges. No model request or sign-in happens;
// the client controller receives a prepared status and transcript.
ShellRoot {
  id: root
  property bool conversationOpen: false
  readonly property string fictional: "CommandDeck · fictional prompt and sample response"
  readonly property string demonstration: "CommandDeck · demonstration prompt and sample response"
  readonly property var checklist: ({
    prompt: "Make a release checklist: faster search, fewer CI runs, clearer errors.",
    answer: "Release checklist\n\n• Verify faster search with a large demo folder.\n"
      + "• Confirm one CI run per ready PR.\n• Check that errors explain how to recover.\n\n"
      + "Before publishing, record the results and note any untested desktop behavior."
  })
  readonly property var history: ({
    prompt: "How did Abraham Lincoln change America in the 1860s?",
    answer: "Lincoln's America: 1860–1865\n\n"
      + "• 1860: Lincoln was elected amid a dispute over slavery's expansion.\n"
      + "• 1861: Civil War began. Preserving the Union was his central goal.\n"
      + "• 1863: The Emancipation Proclamation declared enslaved people in areas still rebelling free, not in every state.\n"
      + "• 1865: Lincoln backed the 13th Amendment. Ratified that December, it abolished slavery except as punishment for crime.\n\n"
      + "Key shift: the war to preserve the Union also became a struggle to end slavery."
  })
  readonly property var science: ({
    prompt: "Why does the Moon have phases? Explain it like I'm 12.",
    answer: "A ball beside a lamp\n\nThe Sun lights about half the Moon, just as a lamp lights one side of a ball.\n\n"
      + "As the Moon travels around Earth, we see different amounts of its bright half:\n\n"
      + "• New Moon: the bright side mostly faces away from us.\n"
      + "• First quarter: we see a half-lit Moon.\n• Full Moon: the bright side faces us.\n\n"
      + "Earth's shadow causes a lunar eclipse, not the normal monthly phases.\n\n"
      + "Try it: move a ball around a lamp and watch the bright part change."
  })

  DemoStage {
    id: stage
    shots: [
      { name: "ai-answer.png", caption: root.fictional,
        show: function() { root.showAnswer(root.checklist) } },
      { name: "ai-conversation.png", caption: root.fictional, show: root.showConversation },
      { name: "ai-history.png", caption: root.demonstration,
        show: function() { root.showAnswer(root.history) } },
      { name: "ai-science.png", caption: root.demonstration,
        show: function() { root.showAnswer(root.science) } }
    ]

    Deck.AgentSurface {
      id: conversation
      anchors.fill: parent
      anchors.bottomMargin: 30
      theme: stage.theme
      controller: stage.deck.part_agentController
      opened: root.conversationOpen
    }
  }

  function showAnswer(example) {
    const agent = stage.deck.part_agentController
    root.conversationOpen = false
    agent.applyStatus({
      enabled: true,
      backend: {
        id: "codex.local", displayName: "Codex", kind: "codex-app-server",
        processLocality: "local", inferenceLocality: "OpenAI", authenticationState: "authenticated"
      },
      trust: { level: 0, levelName: "Answers" },
      preferences: { enabled: true, backendId: "codex.local", trustLevel: 0, webSearchEnabled: false }
    })
    agent.applySnapshot({
      entries: [
        { role: Protocol.AgentTranscriptRoles.User, text: example.prompt },
        { role: Protocol.AgentTranscriptRoles.Assistant, text: example.answer }
      ],
      summary: { state: Protocol.AgentConversationStates.Completed },
      generation: 1,
      outputGeneration: 1,
      outputActions: ["copy", "save"]
    })
    stage.setResults("ai: " + example.prompt, [
      { id: "agent:codex", kind: Protocol.ResultKinds.Assistant, sourceId: "ai",
        title: "Ask AI: " + example.prompt,
        subtitle: "Codex runs locally; inference uses your OpenAI account", icon: stage.icon("dialog-question"),
        actions: [{ id: "ask", title: "Ask", kind: Protocol.ActionKinds.AgentPrompt,
          arguments: { prompt: example.prompt, backendId: "codex.local" } }] }
    ], "Answer complete")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
    stage.deck.openAnswer("agent", example.prompt, "codex.local", "Codex")
  }

  function findNamed(item, name) {
    if (item.objectName === name) return item
    const children = item.children || []
    for (let index = 0; index < children.length; index++) {
      const found = root.findNamed(children[index], name)
      if (found) return found
    }
    return null
  }

  function showConversation() {
    root.showAnswer(root.checklist)
    root.conversationOpen = true
    Qt.callLater(function() {
      const composer = root.findNamed(conversation, "agentComposer")
      if (!composer) throw new Error("Assistant composer not found")
      composer.text = "Make it shorter for a release announcement."
    })
  }
}
