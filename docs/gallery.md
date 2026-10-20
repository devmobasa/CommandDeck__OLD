# Gallery

[Demo](#demo) · [Search](#search) · [Omarchy and Hyprland](#omarchy-and-hyprland) ·
[Workflows](#workflows) · [AI](#ai)

CommandDeck's shared interface, rendered with demonstration data. Every image
is a lossless PNG rendered at 3× pixel density; select one to open it at full resolution.
AI answers are prepared samples. The captures use no personal files, clipboard
contents, accounts, or desktop windows. `dotnet scripts/capture-gallery.cs`
regenerates them; the [capture notes](screenshots/README.md) explain how.

## Demo

Home, then `gaps` finds an Omarchy toggle and the Hyprland keys behind it.
**Down** moves to `general.gaps_out` and **Ctrl+K** opens its actions. Next,
`dns` finds Omarchy's DNS submenu; **Enter** lists its entries under the menu
path, with ✓ on the provider in use. `follow` finds a snippet whose inputs
appear as pills in the search bar, filled in place, and `win:` lists open
windows. An [MP4 version](screenshots/demo.mp4) is also available.

![Animated demo of Home, a gaps search, the action menu, the Omarchy DNS submenu, snippet input pills, and the window list](screenshots/demo.gif)

## Search

### Home

Press **Ctrl+Space** with nothing typed to see bookmarks, recent and frequent
items, open windows, and Omarchy controls. Settings chooses which sections
appear. See [Home and keyboard behavior](search-and-ranking.md#home-and-keyboard-behavior).

[![Home with a bookmark, recent files and config keys, open windows, and an Omarchy control](screenshots/overview.png)](screenshots/overview.png)

### Launch apps

Press **Ctrl+Space**, type an app's name, and press **Enter** to launch it.
The `app:` filter shows only applications. This demonstration lists Firefox,
Files, Terminal, and Text Editor; no application was launched for the capture.
See [search filters](search-and-ranking.md#query-algebra-and-matching).

[![Application launcher with demonstration app entries and a Launch action](screenshots/applications.png)](screenshots/applications.png)

### Find files

Search for a document and inspect its details beside the results.
See [search filters and ranking](search-and-ranking.md).

[![File search with a document detail pane](screenshots/search.png)](screenshots/search.png)

### Calculate and convert units

Start with `=` to evaluate an expression or convert units, then copy the result.

[![Calculator converting 100 km to 62.137 miles](screenshots/calculator.png)](screenshots/calculator.png)

### Convert time zones

`time: 3pm london to tokyo` converts a time between cities; `tz: tokyo` shows a
city's current time.

[![Time zone conversion from London to Tokyo](screenshots/time.png)](screenshots/time.png)

### Find emoji

`emoji: rocket` finds an emoji by name or keyword and copies it.

[![Emoji search for rocket](screenshots/emoji.png)](screenshots/emoji.png)

### Discover prefixes

Press **?** to browse search prefixes and their runnable examples.

[![Prefix palette with search examples](screenshots/prefixes.png)](screenshots/prefixes.png)

### Learn the shortcuts

Press **F1** for every keyboard shortcut and filter.

[![Help sheet listing navigation, action, and filter shortcuts](screenshots/help.png)](screenshots/help.png)

## Omarchy and Hyprland

### Find Omarchy and Hyprland settings

`gaps` finds Omarchy's **Window Gaps** toggle, the Omarchy file behind it, and
the Hyprland keys in your configuration. A config key opens at its file and line.
See [search filters](search-and-ranking.md#query-algebra-and-matching).

[![Search for gaps with an Omarchy toggle, a file, and two Hyprland config keys](screenshots/config.png)](screenshots/config.png)

### Act on a result

**Ctrl+K** opens the selected result's actions. A config key can be edited at
its line, copied as a key, value, or assignment, revealed, bookmarked, aliased,
or hidden.

[![Action menu for a Hyprland config key](screenshots/actions.png)](screenshots/actions.png)

### Run Omarchy controls

`oma:` lists actions from Omarchy's menu, such as the theme and background
pickers and toggles.

[![Omarchy controls from the Omarchy menu](screenshots/omarchy.png)](screenshots/omarchy.png)

### Switch windows

`win:` lists open Hyprland windows with their class and workspace; **Enter**
focuses one. `ws:` finds workspaces.

[![Open Hyprland windows and a workspace](screenshots/windows.png)](screenshots/windows.png)

### Watch Herdr agents

With [Herdr](search-and-ranking.md#herdr-spaces-and-tabs) installed, `htab:`
lists its tabs with each agent's status. **Needs attention** marks a blocked agent.

[![Herdr tabs with Needs attention, Working, and Idle status](screenshots/herdr.png)](screenshots/herdr.png)

### Follow your Omarchy theme

CommandDeck uses the active Omarchy theme's colours, light or dark. Settings can
switch to CommandDeck's own palette instead.

[![Home in the Tokyo Night, Everforest, Gruvbox, and Rosé Pine themes](screenshots/themes.png)](screenshots/themes.png)

## Workflows

### Open a project

`proj: demo` finds the configured demonstration checkouts. The inspector shows
the branch and tracked changes; the action menu offers the editor, a terminal,
and Copy project path. See [project setup](launcher-workflows.md#projects).

**Inspect the checkout**

[![Project details with branch and tracked changes](screenshots/project-details.png)](screenshots/project-details.png)

**Choose an action**

[![Project action menu with editor, terminal, and copy path actions](screenshots/project.png)](screenshots/project.png)

### Browse clipboard history

`clip:` opens opt-in clipboard history. This demonstration has a pinned checklist,
a documentation link, an image preview, and a text note. Capture is paused.
See [clipboard history](launcher-workflows.md#clipboard-history).

[![Clipboard browser with pinned and recent entries and an image preview](screenshots/clipboard.png)](screenshots/clipboard.png)

### Fill a snippet template

Find a configured snippet with `snip: release`, enter its named values, and choose
**Preview expanded snippet**. The preview shows those values inserted into the
template. See [snippets and templates](launcher-workflows.md#snippets-and-templates).

**Enter the values**

[![Snippet input form with version and highlights](screenshots/snippet-form.png)](screenshots/snippet-form.png)

**Inspect the expanded text**

[![Expanded release announcement in a plain-text preview](screenshots/snippet-preview.png)](screenshots/snippet-preview.png)

### Collect files

The demonstration folder has two files selected. **Copy paths** collects their
paths; **Open all** opens the selection. See
[folder browsing and file selection](launcher-workflows.md#folder-navigation-and-file-selections).

[![Folder results with two selected files and Copy paths and Open all controls](screenshots/file-selection.png)](screenshots/file-selection.png)

## AI

Enable AI and an assistant in Settings, select a backend, and sign in. These
examples use **Answers** trust and the `ai:` prefix, which uses the assistant
selected in Settings. See [assistant setup](ai.md#embedded-assistant).

### Turn notes into a checklist

```text
ai: Make a release checklist: faster search, fewer CI runs, clearer errors.
```

Read the answer beside search results, then use **Copy** or **Open conversation**.

[![An ai: prompt and prepared release checklist beside search results](screenshots/ai-answer.png)](screenshots/ai-answer.png)

### Continue the conversation

Ask `Make it shorter for a release announcement.` in the conversation composer.
The screenshot shows an unsent draft; **Enter** or **Send** submits it.

[![Assistant transcript with Answers trust and a follow-up draft](screenshots/ai-conversation.png)](screenshots/ai-conversation.png)

### Explore U.S. history

```text
ai: How did Abraham Lincoln change America in the 1860s?
```

A prepared timeline makes a starting point for follow-up questions. Try
`Why didn't the Emancipation Proclamation free everyone immediately?`
The [history example](ai.md#example-explore-lincolns-america) links to the National
Park Service and National Archives sources used to check the sample.

[![An ai: question about Lincoln with a sample timeline of the 1860s](screenshots/ai-history.png)](screenshots/ai-history.png)

### Explain science

```text
ai: Why does the Moon have phases? Explain it like I'm 12.
```

The sample uses a ball and a lamp to explain the changing view of the Moon's
sunlit side. Ask for a home experiment or a different explanation to continue.
Its science was checked against NASA's
[Moon phases](https://science.nasa.gov/moon/moon-phases/) and
[eclipses](https://science.nasa.gov/moon/eclipses/) explanations.

[![An ai: science question with a sample explanation using a ball and a lamp](screenshots/ai-science.png)](screenshots/ai-science.png)

### Review a direct AI request

Configured direct providers have a separate confirmation step showing the exact
destination, prompt, payload digest, and provider JSON. This demonstration uses
a loopback endpoint and a fictional model identifier; **Send** was never pressed.
See [direct providers](ai.md#direct-providers).

[![AI confirmation showing a loopback destination and exact demonstration request](screenshots/ai-preflight.png)](screenshots/ai-preflight.png)

These are isolated offscreen renders of shared production components, without
daemon or model requests. They illustrate the interface; installed desktop and
provider behavior require separate verification. See the
[capture notes](screenshots/README.md) for provenance.

[Back to CommandDeck](../README.md) · [Documentation index](README.md)
