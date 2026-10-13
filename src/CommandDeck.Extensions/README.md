# CommandDeck.Extensions

Experimental .NET 10 author SDK for CommandDeck command processes. Register
`IExtensionCommand` instances with `ExtensionApp.RunAsync(args, commands)` and
publish typed views with `context.Views.ReplaceAsync(view, cancellationToken)`.
Use `PushAsync` for List/Detail navigation and handle `PageRequested` to replace a
List page. The host owns Back, action admission, and the eight-view stack limit.
Detail uses safe limited formatting and plain metadata. Declare the negotiated
`views.detail@1`, `views.stack@1`, and `views.pagination@1` features as needed.

The SDK uses bounded private stdio RPC and has no dependency on daemon assemblies.
Contexts belong to one active invocation. Retaining a completed context does not
permit further view updates. Clipboard and URL actions describe drafts for the
host; publishing a view does not perform those effects.

Package images use `ListItem.Asset` or `DetailView.Asset` with `assets/name.png`
and the `assets.png@1` feature. The host validates bounded PNGs and sends image data
to the renderer. `ViewAction.OpenUrl` requires `actions.open-url@1` and a current
`desktop.open-url` grant. Only HTTP(S) without embedded credentials is supported.

For a one-shot utility, declare `Mode => "no-view"` in code and `mode: "no-view"`
in the manifest, with `commands.no-view@1`. `context.Effects.CopyTextAsync` requires
`effects.copy-text@1` and a `clipboard.write` grant. One copy attempt is allowed
during the active no-view invocation; completed contexts and view commands cannot
use invocation effects. Completion terminates the child and shows host-owned status.

Native `FormView` uses a callback Submit and typed text, multiline, choice, and
checkbox fields. Handle `FormSubmitted` and echo its `FormRevision` as
`ValidatedFormRevision` when returning `FieldErrors`; delayed errors then cannot
overwrite newer edits. The daemon validates values and consumes submissions before
calling the child. Replacing a form preserves edits; new views reset them and Back
restores saved state.

`context.Data.ReadSettingsAsync` reads declared non-secret settings through
`settings@1`. `ReadAsync` requires `storage@1` and `storage.read`. `WriteAsync` and
`DeleteAsync` require `storage.write` and the revision returned by Read. Mutations
return the new revision and an empty Values dictionary, so write permission does
not disclose data. Data requests are restricted to the current session and active
invocation. Limits are 64 keys, 16 KiB per JSON value, and 64 KiB total. Settings and
data survive updates, rollback, and ordinary removal; explicit erase removes both.
Stale writes fail and are never retried automatically.

Version `0.1.0-preview.4` adds `CommandContext.Brokers`: bounded selected-file
listing/reads and a named HTTP adapter. Authenticated connections attach tokens
in the host; mutations require host confirmation and stable request keys.
Connections are daemon-session grants. Stable package compatibility remains future work.
See the repository's `samples/extensions/UuidTools` for an executable author example.
`samples/extensions/QuickTools` exercises images, URL actions, and no-view copy.
`samples/extensions/LocalDraft` demonstrates native forms, configuration, and
persistent storage, with an external build and management-UI walkthrough.
`samples/extensions/ConnectedTools` demonstrates selected notes and mock tasks.

Version `0.1.0-preview.5` added `IExtensionSearchProvider` and `SearchContext`.
Declare a provider with its command ID and cache key, then implement `SearchAsync`.
Search runs with a read-only cache projection in a separate restricted process;
it cannot use `CommandContext`, brokers, or persistent writes. A selected result
opens the declared command with `searchItemId` and `searchContext` inputs.
`samples/extensions/SearchNotebook` demonstrates the full workflow. See
[search preview](../../docs/extensions/search.md) for budgets and query consent.

## Migrating to preview.6

The SDK API changes in preview.6; the existing child wire shapes stay compatible.
Actions are `CallbackAction`, `CopyTextAction` and `OpenUrlAction`; the existing
`ViewAction` factories return those types. Form submit accepts `CallbackAction`.
Fields are typed text, multiline, choice and checkbox records. `WithDefault`
sets a typed default. Public records use `init` and view/manifest sequences use
`ImmutableArray<T>`: use collection expressions or `ToImmutableArray()`.
`FormView.FieldErrors` uses `ImmutableDictionary<string, string>`; use `SetItem`,
`Add`, `Clear`, or `ToImmutableDictionary()` and assign the resulting map to a new view.

`CommandDeck.Extensions.ExtensionCommandBase` optionally dispatches `OnActionAsync`,
`OnSubmitAsync` and `OnPageAsync`; existing `IExtensionCommand` implementations
remain supported. View commands cannot use invocation effects, and no-view
commands cannot publish views. The SDK rejects this misuse before sending RPC.
The base-class name avoids a collision with the manifest model
`CommandDeck.ExtensionProtocol.ExtensionCommand` when both namespaces are imported.

`Brokers.ListAsync(connection, directory, offset)` returns `FileListing`,
`ReadAsync(connection, reference)` returns decoded `FileContent.Bytes`, and
`SendHttpAsync` returns `HttpResult` with decoded `Body`. Non-completed outcomes
throw `BrokerOutcomeException`. An `IsUnknown` mutation outcome must never be
retried automatically. `CreateHttpClient` maps broker responses and outcomes into
HTTP responses and exceptions; RPC errors remain `ExtensionRpcException`.

Host and SDK negotiate `errors.codes@1` during initialization before adding
`error.data.code`. `ExtensionRpcException` exposes `ErrorCode`, `IsConflict`,
`IsPermissionDenied` and `IsQuotaExceeded`; legacy peers still receive their
original error shape. A command-handler error keeps a healthy view session open.
The host releases the busy action before publishing `ready`; a failed form submit
can retry unchanged values while the same form remains current. Successful submit
replay and stale/revoked views remain rejected.

Other API changes: `ExtensionRpcException` derives directly from `Exception`,
so an `IOException` catch no longer catches RPC errors. `FormValidation.ValidateSubmission`
takes `ImmutableArray<FormField>`. The HTTP adapter propagates an
`ExtensionRpcException` with `quota_exceeded` if the effect journal is full.

View validation messages identify structural paths and fixed reasons, such as
`items[36].actions[1].url: invalid URL`. The outer `InvalidDataException` keeps
existing catches working; its inner `ExtensionViewValidationException` exposes
`Path` and `Reason`. Unhandled author exceptions are written to child stderr,
bounded to 4 KiB; cancellation is not logged. Only explicit `logs --include-stderr`
discloses child diagnostics. Host-facing errors remain generic.

Remote view search and its events are reserved and carry compiler diagnostic
`CDEXT0001`. The current host does not advertise that feature.
Use [CommandDeck.Extensions.Testing](../CommandDeck.Extensions.Testing/README.md)
for in-process author tests and the LocalDraft.Tests sample as a reference.
