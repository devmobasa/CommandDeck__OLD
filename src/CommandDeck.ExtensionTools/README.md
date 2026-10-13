# CommandDeck extension author tools

Experimental Linux x64 tooling for the local CommandDeck preview SDK feed.
Run `commanddeck-ext help` for `new`, `check`, `build`, `pack`, `update`, `dev`,
`attach`, and `reload`.

`new` creates a native form example with non-secret settings and installation
storage in a new directory. `check` validates its manifest and project layout.
`build` and `pack` execute the author's MSBuild targets and publish Native AOT.
`update` sends a reviewed archive digest and explicit grants to the selected
CommandDeck host; it does not enable or run the installed extension.

Requires .NET SDK 10.0.400, Linux x64 Native AOT prerequisites, and a matching
experimental CommandDeck daemon to inspect/install/run packages. The daemon
continues to enforce the restricted release sandbox by default. `dev` creates a
separate immutable development identity; reviewed attach/reload preserve its data.
The matching host offers explicit five-minute native GDB leases and bounded logs.
Stable public SDK compatibility and managed debugger adapters remain future work.
