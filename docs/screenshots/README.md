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
| `social` | `social-preview.png` |

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

- **Search, desktop, and workflows** use CommandDeck's built-in palette.
- **themes.png** applies the colours from four Omarchy themes' `colors.toml`
  (recorded in `themes.json`) through the same token defaults as
  `qml/omarchy/ThemeTokens.qml`, with Omarchy's square corners.
- **demo.gif** and **demo.mp4** are 18 seconds of frames from the same stage.
  The typed queries filter the demonstration results by title; they are not
  ranked by the daemon.
- **social-preview.png** is the 1280 × 640 image for the GitHub repository's
  social preview setting.

The AI prompts and responses are demonstration text. The assistant's client
controller renders a completed sample exchange; no model request or account
sign-in was performed. The follow-up shown has not been submitted. The
[history example](../ai.md#example-explore-lincolns-america) lists the sources
used to check its sample timeline. The
[science example](../ai.md#example-explain-science-with-an-analogy) links to NASA's
explanations used to check the Moon answer. Direct-provider confirmation was
rendered with a digest computed from the displayed JSON; Send was not pressed.

PNG captures use [Qt's high DPI mode](https://doc.qt.io/qt-6/highdpi.html#qt-scale-factor)
at 3× pixel density (2× for the animation frames and social preview). Every
screenshot uses the same 2940 × 2040 frame, so the images line up in grids.

These images illustrate the interface; they do not establish installed Wayland
behavior.
