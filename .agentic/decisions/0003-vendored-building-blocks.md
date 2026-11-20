# 0003. Vendored building blocks

- Status: Accepted
- Date: 2026-11-20

## Context
Shared concerns (Money, ProblemDetails, health, idempotency, outbox, audit, test helpers) come from the
`Agentic.Blocks.*` 0.1.0 packages, which live on a private feed.

## Decision
Vendor the used `.nupkg` files (Core, Web, Idempotency, Outbox, Audit, Testing) into `eng/packages/`. The repo
`nuget.config` has two sources with package source mapping: `eng/packages` for `Agentic.Blocks.*`, nuget.org for the rest.

## Alternatives considered
- Private feed with a token in CI: one more secret and the repo no longer builds for a client on day one.
- Copy the source in: easy to fork silently; upgrades become merges.

## Consequences
- The repo builds anywhere with no private feed. Upgrading = replace the `.nupkg` and bump the version.
- Binary files in git (about 200 KB); acceptable at this size.
