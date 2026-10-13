# Quick Tools

An external C# author sample for packaged images, explicit URL actions, and
commands without a persistent view. It uses the same experimental SDK feed and
.NET 10 build setup as the [authoring guide](../../../docs/extensions/authoring.md).

Copy `Program.cs` and `QuickTools.csproj` into a fresh external author directory.
Configure the SDK pin, local NuGet feed, and private package cache as described
in that guide, then publish `QuickTools.csproj` for Linux x64 with Native AOT.
From the repository, package its output with the original manifest:

```bash
dotnet scripts/pack-extension.cs \
  --manifest samples/extensions/QuickTools/commanddeck.extension.json \
  --publish-dir /tmp/commanddeck-quicktools-author/publish \
  --output artifacts/extensions/m3b-complete/quick-tools.cdext
```

The packer includes `assets/uuid.png` beside the manifest. It is a small,
procedurally generated RGBA icon without metadata or external resources.

Inspect the archive, install using its returned digest and
`--allow clipboard.write,desktop.open-url --yes`, then enable
`org.commanddeck.quick-tools` and the master extension switch.

- **UUID reference:** a List with a packaged icon; Details shows the same image.
  **Open specification** opens the RFC in the desktop browser after activation.
- **Copy new UUID:** generates and copies one UUID, stops the child process, and
  shows the host's completion status. Done or Escape returns to launcher search.

Without the relevant grant, the corresponding effect fails. No browser navigation
or clipboard write occurs during installation, discovery, or image display.
