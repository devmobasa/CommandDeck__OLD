# Extension search preview

The preview supports explicit scoped search and optional local global search in SDK/protocol/tool
`0.1.0-preview.6`. The [Search Notebook example](../../samples/extensions/SearchNotebook/README.md)
demonstrates saving data, finding it, and opening the selected item.

## Enable and search

Installing a package does not permit it to receive ordinary launcher queries.
Enable the package, open its management detail, choose its provider and click
**Search this extension**. The launcher opens the provider's filter with typing
ready, using the full filter ID directly. Its unique token is `ext<first ten installation-key characters>-<provider-id>:`;
typing that registered token also selects the filter, without a short-prefix length limit.
The corresponding stable filter ID is `extension-search:<installation-key>:<provider-id>`.
Typed requests and saved filters use that full ID.

For a shorter prefix, assign that filter in **Settings → Search → Filter aliases**.
Suggested manifest prefixes are hints; packages cannot claim them automatically.
Built-in, reserved and already assigned aliases are rejected. Remove a conflicting
custom alias explicitly before reassigning it. Existing source filters and command
aliases continue to work; adding providers does not execute them during discovery.

**Allow launcher queries…** asks separately before enabling local global search
for a release package. **Stop sharing launcher queries…** withdraws it. The daemon
also checks package availability and the source's search preference before admission.
Development attachments remain scoped-only. Update and rollback clear global
consent; disabled or removed packages receive no queries and start no search child.

The management protocol uses a revision-checked `global-search` action with
`key`, `globalSearchEnabled`, and `confirmed: true`. This switch is separate from
HTTP and credential grants: it never authorizes remote query transmission.

## Author contract

Declare up to four providers:

```json
"searchProviders": [
  { "id": "notes", "title": "Saved notes", "commandId": "note",
    "cacheKey": "search-note", "suggestedPrefix": "notes" }
]
```

An `IExtensionCommand` can also implement `IExtensionSearchProvider`. Its
`SearchId` matches the provider ID. `SearchAsync(SearchContext, CancellationToken)`
receives the query, result limit, `Global` purpose flag, and optional cache value.
It returns `ExtensionSearchResponse` with item IDs, titles, optional subtitles
and bounded selected-item context. The SDK prevents interactive initialization,
command events or a second search invocation within a search-purpose process.

An interactive command populates the declared cache key through existing
revision-checked `context.Data.WriteAsync` under its storage grant. The host copies
only that value into the search request; other storage and settings are excluded.
An absent cache is valid. The projection retains the existing 16 KiB storage-value
budget. Search cannot refresh it; the user opens the interactive command to refresh
or edit data. This preview uses cached data for both scoped and global search.

Only the item's declared command can open. After explicit activation it receives
`context.Inputs["searchItemId"]` and `context.Inputs["searchContext"]`. The original
query, other rows, storage handles and search process state are not transferred.
An author controls the bounded context string, so it must be treated as item input,
not host authority. Subsequent remote effects still require the command's connection grants
and confirmations. Direct host effects from search results are not exposed.

## Bounds and isolation

- Each query runs in a fresh restricted process with no callable host RPC methods.
  No network, credentials, desktop effects, storage writes, shared mutable cache,
  runtime asset URLs or remote images are exposed. Search stderr is discarded from
  the user-facing diagnostics path and never forwarded to an interactive command.
- The host admits at most two search children concurrently. Admission rotates
  across eligible providers instead of permanently selecting the first packages.
  Busy/cold/unavailable providers contribute no rows; manifest commands remain.
- Global work has a 300 ms operation deadline within the existing coordinator's
  ordinary provider budget. A single explicit provider filter permits 1.5 seconds
  of search work within a 1.8-second coordinator budget. Cancellation kills the
  systemd cgroup, including descendants, independently of SDK cooperation.
- Search processes have 128 MiB memory, 32 tasks, 50% CPU quota and a five-second
  systemd failsafe. Search uses separate package resource groups from foreground
  commands. Slots remain occupied until cleanup completes. Deadline cancellation
  kills the validated cgroup directly, so non-cooperative process cleanup cannot
  add the system manager's independent stop timeout to the query budget.
  Interactive sessions use a separate bounded graceful-stop deadline, even after
  their startup deadline expires; that cleanup policy does not apply to search.
- Queries are bounded to 2,048 characters. Responses allow 32 items and 64 KiB of
  encoded JSON, with titles limited to 256, subtitles to 512, and context to 2,048
  characters. IDs and text are validated; arbitrary scores, metadata, actions and
  source identities are rejected. Ranking and source budgets stay host-owned.
- The host retains at most 32 client search states. A displayed result's lease is
  single-use, client-bound and valid for two minutes. New queries, disappearing
  rows, disconnect, registry changes and package/grant changes retire it. A child
  exiting normally does not retire it. Stable result IDs are separate from leases.

## Verification and limits

Run the private-daemon journey with the published host, notebook archive, and
optional hostile fixture:

```bash
dotnet scripts/test-extension-search.cs artifacts/publish/linux-x64/commanddeck \
  artifacts/extensions/m6/search-notebook.cdext \
  artifacts/extensions/m6/search-hostile.cdext
```

The helper creates its own database/socket and removes them afterward. It does
not install or reload the user's desktop. It tests cached scoped/global search,
explicit consent, stale/foreign/replayed activation, disable, and optional hostile
RPC/descendant cleanup with a foreground sibling. It reports serial latency samples
and resource observations; these are local evidence, not a production benchmark.
The typing and hostile cancellation probes use scoped queries with the existing
1.5 second budget. Global latency samples retain the 300 ms provider budget;
functional lease setup permits up to five independent global queries because an
individual budget-limited query can omit a provider. No provider budget is raised.

Installed desktop acceptance remains separate. Remote search, continuous/background
refresh and independently streaming root results are not part of this preview.
Run the connected search journey above against matching host/renderer artifacts;
verify installed desktop cancellation, revocation, and cleanup separately.
