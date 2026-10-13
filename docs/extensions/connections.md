# Files and service connections preview

The preview supports host-owned connection setup and the
`CommandContext.Brokers` API. It remains a
Linux x64 preview. Extensions still run without host home or network access.

## Setup and lifetime

Enable the package and its declared permissions, then open **Settings → Search →
Manage extensions → package → Connections**. Add a named file root or HTTP
connection and review its exact scope. Names belong to the installation;
another installation cannot use them. Changing a name revokes its prior access.

Connections, root descriptors, and credentials last for the daemon session.
Restart, package replacement, disable/removal, permission changes, and other
registry changes that revoke package sessions clear them. Reconfigure explicitly
afterward. Ordinary command close/reopen retains connections. Settings and draft
storage remain durable and separate. No background login or service polling runs.

The CLI provides the same operations:

```bash
commanddeck extensions connections org.commanddeck.connected-tools --json
commanddeck extensions connection-set org.commanddeck.connected-tools \
  --connection-file /absolute/notes-connection.json --yes
commanddeck extensions connection-remove org.commanddeck.connected-tools \
  --connection notes --yes
```

A file setup JSON object is:

```json
{"name":"notes","kind":"files","path":"/absolute/chosen/notes"}
```

Use the masked UI field for a session token; avoid saving tokens in setup files.
Inventory, settings, and diagnostics contain connection status, not credentials.

## File roots

`files.read` permits `Brokers.ListAsync(name, directoryReference, offset)` and
`Brokers.ReadAsync(name, fileReference)`. Listing returns opaque references and
display names, not paths for a child or renderer to open. The result body is
base64; the sample decodes UTF-8 explicitly.

The host pins the selected file/directory descriptor. Linux `openat2` resolves
descendants beneath it without symlinks or mount crossings. Reads revalidate
root location, mount/inode identity, file metadata, and replacement before and
after reading. Moving a root requires selecting it again. Hard-linked files,
devices, hidden paths, credential paths and protected system roots are denied.
The whole home directory cannot be selected. These intentionally strict rules
can reject a legitimate folder; choose a narrower root without links.

Bounds: 128 KiB per file, 32 returned entries per page, 4096 directory entries,
512 retained references per root, and 128 connections in the daemon. Reselect
the root when its reference budget is exhausted. File access adds no sandbox
mounts. [Linux resolution semantics](https://www.man7.org/linux/man-pages/man2/openat2.2.html)
describe the kernel boundary used here.

## HTTP and credentials

`http.request` permits the `Brokers.CreateHttpClient(name)` adapter or
`SendHttpAsync(name, BrokerHttpRequest)`. The adapter uses relative paths under
its synthetic base address; it never opens a socket in the child. Request
headers are limited to an optional `Idempotency-Key`; content type is supported.

Each connection fixes an origin, allowed methods and route prefix. Public
connections require HTTPS. Private connections require an explicitly selected
private IP literal, and may use HTTP for a local service. Public DNS resolving
to private/special addresses is rejected at actual connection time. Proxies,
cookies, redirects, custom Authorization/Host headers, escaped/traversing routes,
and arbitrary response headers are unsupported. TLS keeps hostname validation.

Bodies are base64 on the wire: requests at most 64 KiB, responses at most
128 KiB after decompression, response headers at most 16 KiB. Only identity or
gzip response encoding is supported. HTTP/credential work has a 15-second
deadline, within the command's foreground budget. There are no automatic retries.

Credential mechanisms are `none`, `session-bearer`, `systemd-bearer`, and `oauth`.
`secrets.use` is additionally required to send through an authenticated
connection. A systemd credential is selected by the user from the daemon's
credential directory; the child cannot request environment variables or tokens.
Tokens are attached by the host to that connection's origin. Session bearer and
OAuth tokens remain in memory; there is no claimed encrypted persistent store.
Systemd source rotation requires explicit reconfiguration.

OAuth setup fixes the issuer origin, authorization/token endpoints, public
client ID, scopes and optional revocation endpoint. Endpoints must share the
issuer origin. **Connect in browser** starts one three-minute loopback callback
listener and opens the configured authorization URL. State, S256 PKCE, callback
host/path and returned `iss` must match. Providers without the issuer response
parameter need a future reviewed adapter. Tokens refresh only for admitted
requests; concurrent callers share one refresh. Refresh failure requires an
explicit reconnect. Disconnect always removes local authority; optional remote
revocation reports acknowledgement or uncertainty separately.

This follows the [native OAuth flow](https://www.rfc-editor.org/rfc/rfc8252.html)
and [OAuth security BCP](https://www.rfc-editor.org/rfc/rfc9700.html). Real provider
acceptance requires separately authorized test accounts/data; mock testing is
not proof of provider-specific interoperability.

## Remote changes

GET is the read path. POST/PUT/PATCH/DELETE require a current user-activated
callback and a stable 16–128-character request key. The host dialog shows the
connection, method, destination, key and exact body (base64 for non-UTF-8 bytes).
Cancel starts no request. Tab/Shift+Tab stays inside the dialog; Cancel has
initial focus. The reply is bound to that session and confirmation identity.

Before sending, the host durably records an attempt keyed by installation,
connection name and request key. A repeated key, including after restart,
returns `unknown` without sending again. The journal contains hashes and attempt
markers, not bodies or tokens, and is bounded at 1024 attempts per installation
and 4096 across the daemon, including legacy markers. Entries and their containing
directories are synced before network admission. When full, new writes stop with
`quota_exceeded`. There is no automatic expiry or compaction that could revive a request.

After reviewing the service's actual state, use **Reset remote-write history** in
Connections or `commanddeck extensions effects-reset PACKAGE --yes`. This is a
confirmed reset that allows old keys to be sent again. `--include-legacy` also
clears unattributed legacy markers across packages; review that wider scope.
Reset works while the package or master extension switch is disabled. It still
requires confirmation and the current registry revision, and does not enable the package.
Ordinary package removal preserves history. Explicit erase removes that package's
current history; legacy history remains until a confirmed legacy reset.

Lost responses, interrupted started writes, blocked mutation redirects and 5xx
responses report `unknown`. Inspect the service before deliberately creating a
new request key. Cancellation and disconnection cannot undo an already-started
remote effect. Token deletion does not prove remote revocation.

## Examples and verification

[Connected Tools](../../samples/extensions/ConnectedTools/README.md) contains
selected notes and mock task examples. Build with SDK 10 outside the checkout:

```bash
dotnet scripts/build-extension-sdk.cs --output artifacts/extensions/m5/feed
dotnet scripts/build-extension-example.cs samples/extensions/ConnectedTools \
  artifacts/extensions/m5/feed artifacts/extensions/m5/connected.cdext
dotnet scripts/test-extension-brokers.cs artifacts/publish/linux-x64/commanddeck \
  artifacts/extensions/m5/connected.cdext
```

The journey uses its own daemon, files, credential and mock HTTP service. It does
not install or reload the desktop. Validate actual desktop and real-service
journeys separately.
