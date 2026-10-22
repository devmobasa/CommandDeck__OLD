# Threat model

## Assets and trust boundaries

Sensitive assets include indexed text/paths, private clipboard/mail results,
credentials, assistant context, and the ability to launch programs or change
packages. Inputs can include hostile filenames, documents, protocol clients,
extension packages, network responses, and model-generated instructions.

| Boundary | Controls | Residual risk |
|---|---|---|
| QML/client → daemon | Bounded versioned JSONL, typed operations, current result/session identity, explicit confirmations | A process with the same user identity is already inside the local user boundary |
| Indexer → filesystem | Configuration and Settings-added root policies, identical validation and protected-path exclusions, bounded parsing/extraction, default symlink/mount restrictions | Configuration or Settings-added roots and parsers may expose content; exclusions need operator review; removing one root leaves coverage by other enabled roots intact |
| Actions → Linux/desktop | Daemon-authored action payloads, validated literal argv, bounded helpers, launch acknowledgement | A configured executable can act with the user's authority; acknowledgement is not proof of visible application state |
| Search → Omarchy menu conditions | Conditions only from Omarchy's menu files and the user's menu extension, one login bash batch per search with the query never passed in, each condition in a subshell with stdin closed, 5 s, 256 MiB and output limits, a 20 s cache, and a Settings switch | A condition runs with the user's authority and login environment; a slow or hostile one costs a bounded batch every twenty seconds while menu rows show |
| Actions → user processes | Own-UID listing from `/proc`, confirmation naming PID and process, pidfd pin, start-time and owner recheck before SIGTERM/SIGKILL | A confirmed signal ends a process the user owns and may lose its unsaved work; a process can choose a misleading name |
| Calculator → ECB exchange rates | Only after a currency expression with no fresh cache; one fixed HTTPS GET with no query content, cookies, redirects or trace headers; 5 s timeout and 64 KiB body cap; XmlReader with DTDs and resolvers disabled, accepting only dated three-letter codes with positive rates; a private atomic cache that is re-validated on load and ignored when corrupt; `search.calculator.exchangeRates=false` | The ECB learns the client's address and the time of a refresh; a compromised or spoofed feed within TLS trust can supply wrong rates, which the details show with their reference date |
| Extensions → host | Restricted child process, bounded RPC, feature negotiation, grants, displayed-view/session leases, brokered effects | Kernel/runtime assumptions and hostile resource usage need environment-specific validation |
| Host brokers → files/network | Pinned file roots/revalidation; scoped connections, bounded bodies, credential attachment in host | Remote services receive allowed data; interrupted writes may have landed |
| User → direct AI | Explicit enablement, exact request/destination disclosure, digest-bound confirmation, private-source exclusions | The selected provider receives confirmed content and applies its own policies |
| Model → assistant tools | Daemon-owned trust/grants, backend capability checks, client-bound challenges and confirmations | Prompt injection can persuade the user; approved reads still disclose content to the model |
| Assistant → user session | Owned credentials/homes, Bubblewrap below level 6, bounded process lifetimes | Level 6 intentionally removes much of this boundary; third-party CLI formats can change |
| Daemon → package helper | Separate installation, D-Bus/Polkit checks, exact transaction preview/identity | Authorized package managers and package scripts have privileged effects; interruption does not undo them |

## Privacy invariants

Private sources are excluded before noninteractive provider execution and again
at result projection. They do not become assistant context merely because a
client supplies a result ID. Credential values are resolved only at authorized
use and are not public configuration, export, or shared diagnostic fields.

Trust elevation requires the user-facing challenge path. Model text, package
manifests, tool output, and feature negotiation cannot create permissions.
Revocation and session/registry changes invalidate current effect authority;
they cannot undo effects already accepted by an external service.

## Verification boundaries

Backend specs cover rejection, bounded data, persistence, protocol and permission
contracts. Extension journeys exercise separate children and host brokers. Native
AOT smoke verifies published artifacts. QML real-event journeys verify focus and
keyboard/mouse behavior. Installed Wayland, kernel isolation, real credentials,
Polkit, upgrade, and rollback need matching live/disposable environments.

Use [the testing guide](testing.md) and preserve these regression
owners when changing a boundary. Read [security](security.md),
[AI](ai.md), and [extension authoring](extensions/README.md)
for the corresponding operator workflows.
