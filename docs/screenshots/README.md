# Screenshots

Browse the [gallery](../gallery.md) for captions and workflow guides.

## Regenerate

```bash
dotnet scripts/capture-gallery.cs            # every scene
dotnet scripts/capture-gallery.cs desktop    # one or more scenes
dotnet scripts/capture-gallery.cs --output /tmp/gallery search
```

The script needs Quickshell (`qs`) and `ffmpeg` on PATH. Scenes live in
[tests/CommandDeck.Gallery](../../tests/CommandDeck.Gallery):

| Scene | Images |
|---|---|
| `search` | `applications.png`, `search.png`, `calculator.png`, `prefixes.png` |
| `desktop` | `overview.png`, `config.png`, `windows.png`, `omarchy.png`, `herdr.png`, `actions.png`, `help.png`, `time.png`, `emoji.png` |
| `workflows` | `project-details.png`, `project.png`, `clipboard.png`, `snippet-form.png`, `snippet-preview.png`, `file-selection.png`, `ai-preflight.png` |
| `assistant` | `ai-answer.png`, `ai-conversation.png`, `ai-history.png`, `ai-science.png` |
| `themes` | `themes.png` |
| `demo` | `demo.gif`, `demo.mp4` |
| `whats-new` | `whats-new.gif`, `whats-new.mp4` |
| `settings` | `settings-display.png`, `settings-layout.png`, `settings-search.png`, `settings-folders.png`, `settings-quicklinks.png`, `settings-quicklink-edit.png` |
| `social` | `social-preview.png` |

The separate `sizing` acceptance scene writes `launcher.png`, `settings.png`,
`layout.png`, `help.png`, `actions.png`, `confirmation.png`, and `extension.png`.
It is excluded from the default gallery run. Give it a separate `--output`
directory because `help.png` and `actions.png` are also gallery filenames.
See [display sizing acceptance](../testing.md#qml).

Re-run the affected scenes after a visible UI change and review the images
before committing them.

## How they are made

Each scene copies the shared production components from `qml/common` and
renders them in an isolated offscreen Quickshell session, with private XDG
directories and no daemon, D-Bus session, Wayland display, or Hyprland
connection. The capture copy omits only the per-screen layer-shell window
factory, which the offscreen platform cannot host. `DemoStage.qml` places the
launcher in a plain window and injects results in the daemon's protocol shapes
from `DemoData.js`.

The offscreen platform has no icon theme, so the script resolves each icon name
to a file from the first installed theme that has it (Yaru, then Breeze,
Adwaita, and hicolor). An icon that is not installed renders as CommandDeck's
fallback glyph.

The images use fictional files, paths under `/home/demo`, window addresses,
Herdr tabs, and clipboard entries. No personal files, clipboard, accounts,
desktop windows, or notifications are read.

- **Search, desktop, workflows, assistant, and Settings** use CommandDeck's built-in palette.
- **themes.png** applies the colours from four Omarchy themes' `colors.toml`
  (recorded in `themes.json`) through the same token defaults as
  `qml/omarchy/ThemeTokens.qml`, with Omarchy's square corners.
- **demo.gif** and **demo.mp4** keep the Home → `gaps` → actions → DNS →
  snippet pills (typing `Sam`) → `win:` storyboard, about 22 seconds long.
  Typed queries filter demonstration results by title; the daemon does not rank them.
- **whats-new.gif** and **whats-new.mp4** show a captioned tour, about 27 seconds
  long: active DNS provider, snippet pills, `f` → `ff` and a learned Firefox Top
  Match, **↑** search recall, **Ctrl+I**, compact rows, Folders, and Quicklinks.
  Ranking and recall are prepared responses. No usage or query history is recorded.
  Both GIFs use a 96-colour palette at 1280 pixels wide; the tour stays under 1.5 MB.
- **Settings** uses the production Settings pages with fictional indexed roots
  and quicklinks. Enabled Archive has a missing-folder warning and Disable;
  disabled Downloads has no warning and offers Enable. These are prepared states;
  no paths are probed. Quicklinks pass the production frontend validator, including
  its generated reserved-alias rules.
  The list and editor are separate captures because editing replaces the link list.
  Capture assertions check folder-state consistency and that task controls stay
  visible and unclipped.
- **social-preview.png** is the 1280 × 640 image for the GitHub repository's
  social preview setting. Its miniature uses a 1920 × 1080 logical workspace,
  then scales the complete launcher into the composition; the small output
  frame does not constrain the launcher layout.

The AI prompts and responses are demonstration text. The assistant's client
controller renders a completed sample exchange; no model request or account
sign-in was performed. The follow-up shown has not been submitted. The
[history example](../ai.md#example-explore-lincolns-america) lists the sources
used to check its sample timeline. The
[science example](../ai.md#example-explain-science-with-an-analogy) links to NASA's
explanations used to check the Moon answer. Direct-provider confirmation was
rendered with a digest computed from the displayed JSON; Send was not pressed.

PNG captures use [Qt's high DPI mode](https://doc.qt.io/qt-6/highdpi.html#qt-scale-factor)
at 3× pixel density (2× for animation frames and the social preview). Most PNGs
use a 2940 × 2040 frame. Settings captures are 3840 × 2700, and the social
preview is 1280 × 640. Animation scenes name frames with their hold times;
the encoder preserves those times in GIF and MP4, with no extra final hold.

Last refreshed on 2026-10-21 from the shared QML in this checkout. The capture
run covers every documentation scene; `sizing` remains a separate acceptance fixture.

These images illustrate the interface; they do not establish installed Wayland
behavior.
