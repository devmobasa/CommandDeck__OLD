# Search Notebook

A .NET 10 Native AOT example of one saved note with an explicit search provider.
It uses SDK `0.1.0-preview.6`. Its search child receives only the saved projection;
opening the result starts a fresh interactive command.

Build from the checkout:

```bash
dotnet scripts/build-extension-sdk.cs --output artifacts/extensions/m6/feed
dotnet scripts/build-extension-example.cs samples/extensions/SearchNotebook \
  artifacts/extensions/m6/feed artifacts/extensions/m6/search-notebook.cdext
```

Install the archive through extension management, review storage permissions and
enable the package. Then:

1. Find **Edit searchable note**, enter a title and note, and save.
2. Open the package in management and choose **Search this extension**. Type words
   from the note. Select its result and press Enter to open the saved note.
3. Optionally assign a short prefix in Settings' filter aliases.
4. Ordinary launcher queries will not reach this provider until you confirm
   **Allow launcher queries…**. Test both the enabled and disabled states.
5. Search again, edit/save the note, disable/re-enable the package, and verify that
   stale rows cannot open. A new search creates new activation leases.

The example intentionally stores one note. Its projection is the installation's
`search-note` key; it does not index personal files, use the network or run in the
background. See the [search contract](../../../docs/extensions/search.md)
for limits, purpose isolation and the private-daemon test command.
