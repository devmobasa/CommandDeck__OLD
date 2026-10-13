# Security policy

CommandDeck is a working preview. Security fixes land on `main`; there are no
maintained release branches.

## Report a vulnerability

Report privately through GitHub: open the repository's **Security** tab and
choose **Report a vulnerability**. If that option is unavailable, open an issue
asking for a private reporting channel. Do not include exploit details,
credentials, or private data in a public issue.

Include:

- the affected commit or version (`commanddeck version`);
- the component, such as the daemon, bridge, QML frontend, extension host,
  assistant sandbox, or package helper;
- reproduction steps and the impact you observed;
- sanitized `commanddeck doctor` output, if relevant. Review it first: paths and
  local configuration may be personal.

Leave prompts, mail, clipboard contents, API keys, and unsanitized logs out of
the report.

## Scope

[Security and privacy](docs/security.md) describes the defaults, storage, and
process boundaries. The [threat model](docs/threat-model.md) lists the trust
boundaries and residual risks, and [permissions](docs/permissions.md) lists each
capability with its default, data access, and control.

The assistant's optional unrestricted trust level grants broad user-session
access by design. Report a way to exceed the trust level you selected, not the
documented behavior of the level itself.
