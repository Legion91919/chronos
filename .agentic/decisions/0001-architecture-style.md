# 0001. Architecture style

- Status: Accepted; the ledger part is superseded by [0002](0002-event-sourced-ledger-core.md)
- Date: 2026-11-19

## Context

Harbourline tracks drawdowns, interest and repayments on property loans in spreadsheets, so cash positions are slow to confirm and hard to audit. Traits: MoneyMovement, RegulatoryAudit, LongRunningWorkflows, SeveralBusinessAreas, ComplexBusinessRules, SmallTeam.

## Decision

Build a modular monolith with an append-only cash ledger, idempotency keys, an outbox and sagas for drawdown approval.

## Alternatives considered

- CRUD app with a balance column: simplest, but balances drift and cannot be audited.
- Event-sourced microservices: correct but far heavier than this volume needs.

## Consequences

- Clear boundaries that tests can enforce.
- Room to extract a part later if its load demands it.
