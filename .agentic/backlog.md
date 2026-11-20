# Backlog

Vertical slices, smallest valuable first.

## Now
- [x] Slice 1: Operations books a drawdown on a loan and sees the updated balance from ledger entries (event-sourced, ADR-0002; also accrual, repayment, point-in-time)
  - [x] Given a drawdown with an idempotency key, when it is posted twice, then only one ledger entry exists
  - [x] Given ledger entries, then the balance is always their sum (never stored separately)
  - [x] Given a drawdown awaiting sign-off, then it is not in the balance until approved
  - Test notes: one acceptance test per criterion; seeded invented data.

## Next
- [x] Sketch leg: tokens + three variants of the slice-1 screen (`sketch-ui`)
- [ ] CI green with all gates before the first feature commit (web job added; needs a first push)

- [ ] Slice 2: real login (Blocks.Auth; roles ops / finance / auditor) replaces the demo `X-Actor` header
- [ ] Slice 2: database event store (unique stream+version), shared idempotency + outbox stores
- [ ] Slice 3: rate resets and partial-period statements (PDF export for month end)

## Later
- Snapshots when a stream passes ~5k events
- Bitemporal query ("as known at") alongside "as of"
