# 0002. Event-sourced ledger core

- Status: Accepted (supersedes the "append-only cash ledger" part of 0001; modular monolith, idempotency keys and outbox stand)
- Date: 2026-11-20

## Context
ADR-0001 chose a modular monolith with an append-only ledger of entries. Two needs push further:
- **Auditors re-perform interest** and ask "what was the position on 31 March, and why?". An entry table answers
  the first half; it does not keep the decisions (requested by whom, approved by whom, which day-count, which rate).
- **Drawdowns wait days for sign-off**, so the state of a request matters as much as the money.

`arch-advisor compare` rates "Event-sourced core with CQRS" a **good alternative** (complexity 4 vs 3) for these
traits: "Money or regulated records where full history and replay matter". This repo deliberately takes that variant.

## Decision
The loan is an **event-sourced aggregate**. Its stream (`loan-{id}`) of `LoanOpened`, `DrawdownRequested`,
`DrawdownApproved`, `InterestAccrued`, `RepaymentRecorded` is the only source of truth.
- **Write side** (`Chronos.Ledger`): load the stream, rebuild the `Loan`, decide, append with the version it decided
  on (**optimistic concurrency**). A conflict reloads and decides again (up to 10 tries, jittered back-off).
- **Idempotent commands**: the HTTP `Idempotency-Key` (Agentic.Blocks.Idempotency) replays the response; the key is
  also stored as `commandId` on every event, so a retry after the replay cache expires is still a no-op.
- **Read side** (`Chronos.Statements`): balance and statement **projections** with a checkpoint, caught up before each
  query and rebuildable from position 0. **Point-in-time** answers replay a stream prefix (business dates in a stream
  never go backwards, so "dated on or before D" is a prefix).
- **Event store**: `IEventStore` with an in-memory and a JSON-lines file implementation (flush before visible, torn
  last line repaired). A database adapter with a unique `(stream, version)` key is the production path.
- **Event versioning**: type name + schema version per event; old versions are **upcast on read**, never rewritten
  (`InterestAccrued` v1 → v2 added `dayCount`).

## Alternatives considered
- Keep 0001's entry table: simpler, but history of decisions and replay would need a second audit model.
- Modular monolith + CQRS read models over CRUD tables: fast reads, but the write model still overwrites state.
- Event-sourced microservices: correct, far heavier than two engineers can run.

## Consequences
- + Full history and point-in-time replay for auditors; new reports are projections over old events.
- + Concurrency is explicit (stream version) and testable; idempotency is enforced twice.
- − Event schemas are a public contract forever: every change needs a new version and an upcaster with a test.
- − Projections are extra moving parts: checkpoints, rebuilds, and (with a separate read store) eventual consistency.
- − Corrections are compensating events, not edits; the team must learn to think in facts.
- Revisit if streams grow past ~10k events per loan (add snapshots) or reads need another store (subscription + checkpoint table).
