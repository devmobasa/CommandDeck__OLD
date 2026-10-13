# CommandDeck.ExtensionProtocol

Experimental bounded JSON-RPC contracts and duplex transport for CommandDeck
extension processes. Targets .NET 10 and supports source-generated serialization
and Native AOT without referencing daemon assemblies.

This package defines protocol frames, cancellation, handshakes, manifests, and
semantic List/Detail/Form records. Its types do not confer host permissions.
Version `0.1.0-preview.4` adds negotiated `brokers@1` request/result contracts.
It is not a stable compatibility promise.

Version `0.1.0-preview.5` added optional manifest `searchProviders`, separate
`scoped-search`/`global-search` initialization purposes, and a single bounded
`search.query` batch. Search result records cannot carry scores, host actions or
arbitrary metadata. Older manifests may omit providers; absent search caches are valid.

Preview.6 introduces typed action and field APIs and immutable sequences while
preserving existing JSON field names, defaults, and ordering. Views retain the
strict leading `kind` discriminator. Action and field converters accept the
established wire order and reject unknown kinds or unrelated payload properties.
Source-generated boundary DTOs retain omitted-field defaults under .NET 10;
the public author records use `init`. No reflection is required.

`errors.codes@1` is an optional handshake feature: both peers must confirm support
before the host emits `error.data.code`. Error codes carry fixed, safe meanings;
unknown well-formed codes are tolerated without acquiring known-code semantics.
`ExecutionEpoch` remains on the wire, reserved as `1` for fresh daemon sessions;
session IDs and invocation IDs supply the actual authority fencing.

Constructing `ExtensionPeer` starts no work. Call `Start()` after assigning the
handler's dependencies, before calling or notifying; repeated Start is harmless.
Disposal cancels requests, closes streams and bounds the wait for peer tasks.
