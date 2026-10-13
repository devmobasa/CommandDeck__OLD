# Connected Tools

Two Native AOT examples for the [connections guide](../../../docs/extensions/connections.md):

- **Search selected notes** lists/searches file names in the chosen `notes` root
  and opens UTF-8 text up to the native Detail view's 64 KiB limit through opaque file references. Filtering is local to the
  current page. Refresh recovers from changed files or revoked roots.
- **Connected tasks** lists `/tasks`, opens a native title form, and creates a
  task only after the host confirms the exact POST. Pending request keys survive
  reopen in installation storage; repeated attempts do not duplicate a write.

Build using the external example command in the guide. Inspect/install the
archive with its reviewed digest and grant `files.read`, `http.request`,
`secrets.use`, `storage.read`, and `storage.write`; enable the package and master
switch. In Connections, add `notes` for a chosen folder. Open the notes command,
type a file name, open a note, Back, then refresh.

The private connected journey supplies `tasks` automatically. The service expects
GET `/tasks` to return a JSON array of objects with `title`; POST `/tasks` accepts
`{"title":"..."}` and returns a 2xx status. For manual testing, configure only a
mock service you control, with `/tasks` scope and GET/POST methods. Creating real
service data is a separately authorized acceptance task.

Test both Cancel and Send once, then close/reopen and disconnect. An unknown
outcome means inspect the service before choosing **New task**. There is no
automatic retry. Credentials and root grants stay in the host and expire when
the daemon restarts or the package is revoked; drafts/request keys persist.
