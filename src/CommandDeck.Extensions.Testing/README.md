# CommandDeck.Extensions.Testing

Experimental author test helper, targeting .NET 10 with Native AOT support.
It runs the real SDK over in-memory pipes and shares view validation, feature
requirements and error codes with the protocol. It supplies deterministic storage,
files, HTTP responses and clipboard recording. It does not emulate process isolation,
credential handling, network authorization, filesystem containment or desktop effects.
Use the daemon's integration tests for those boundaries.

```csharp
await using var host = await ExtensionTestHost.StartAsync(new MyCommand());
await host.OpenAsync();
await host.InvokeAsync("refresh");
var view = host.CurrentView;
```

Options configure `HostFeatures`, `GrantedPermissions`, `Settings`, initial `Storage`,
`Files` and connection-scoped `Http` handlers. Storage writes use revisions and throw
negotiated `revision_conflict` errors. Unsupported views fail the real SDK's shared
feature check. `SubmitAsync`, `NextPageAsync`, `BackAsync` and `CopiedText` support
common author journeys. The LocalDraft.Tests sample shows a complete test.
