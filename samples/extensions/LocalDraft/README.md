# Local Draft

This example uses native text, multiline, choice, and checkbox fields. The host
owns the draft and non-secret default setting; the child has no filesystem or
network access. Storage is scoped to this installation.

Follow the [external author build](../../../docs/extensions/authoring.md#build-an-existing-example-outside-the-checkout)
with `LocalDraft` in place of `UuidTools`, a freshly built SDK feed, SDK 10.0.400,
and a fresh private NuGet cache. Then package it from the repository:

```bash
dotnet scripts/pack-extension.cs \
  --manifest samples/extensions/LocalDraft/commanddeck.extension.json \
  --publish-dir /tmp/commanddeck-local-draft-author/publish \
  --output artifacts/extensions/m4/local-draft.cdext
```

In CommandDeck, open Settings → Search → Manage extensions. Choose **Install /
update archive**, enter the archive's absolute path, and inspect it. Review and
select `storage.read` and `storage.write`, install, then enable Local Draft and
the master extension switch. Select Local Draft → Configure to change the
default draft title.

Close Settings and search **Edit local draft**. The title is focused immediately.
Tab moves through the draft text, category, completed checkbox, Save, and Cancel;
Shift+Tab reverses it. Enter inserts a newline in the multiline field. Space
activates a focused button or checkbox. Escape returns to the launcher.

Save a draft and reopen it. The saved draft overrides the configured default;
changing the default does not overwrite existing work. An empty title is rejected
locally and by the daemon. `Untitled` demonstrates an extension-supplied inline
error. Concurrent saves use a storage revision: an outdated editor cannot
overwrite a newer draft and asks you to close/reopen. No write is retried.

Disable, configure, permission changes, rollback, and removal close active
sessions. Removal retains settings and drafts by default. The removal confirmation
offers an explicit permanent erase option. Updating/reinstalling the same local
package retains its data; rollback does not roll back stored draft content.

The connected test uses a temporary daemon, registry, and real restricted child:

```bash
dotnet scripts/test-extension-draft.cs \
  artifacts/publish/linux-x64/commanddeck \
  artifacts/extensions/m4/local-draft.cdext
```

It checks permissions, configuration, validation, save/replay, daemon restart,
two concurrent editors, and disable/removal. Native mouse/keyboard checks run in
`scripts/test-qml-components.sh`. Neither establishes installed desktop acceptance.
