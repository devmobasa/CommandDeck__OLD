# Experimental C# extensions

Extensions run as separate Linux x64 processes, publish native typed views, and
request explicitly granted host effects. The SDK targets .NET 10. Packages and
author tools use a local preview feed built from this repository; there is no
stable public SDK/feed compatibility promise yet.

- [Create, build, package, develop, and use extensions](authoring.md)
- [Install, configure, grant, update, roll back, and remove packages](packages.md)
- [Selected files, scoped HTTP, credentials, and remote confirmations](connections.md)
- [Scoped and opt-in global extension search](search.md)
- [Author SDK API](../../src/CommandDeck.Extensions/README.md)
- [In-process author test kit](../../src/CommandDeck.Extensions.Testing/README.md)
- [Wire contracts](../../src/CommandDeck.ExtensionProtocol/README.md)
- [Author tool commands](../../src/CommandDeck.ExtensionTools/README.md)

Examples are under [samples/extensions](../../samples/extensions).
New packages and the master extension switch begin disabled. Review the archive
digest and permissions before installation; enable explicitly afterward.

Compatible host/renderer artifacts, Bubblewrap, and systemd isolation prerequisites
are required. Connected mock-service tests do not establish real-provider or
installed desktop acceptance. The runtime profile is documented in the authoring
guide; unavailable isolation prerequisites fail instead of running unrestricted.
