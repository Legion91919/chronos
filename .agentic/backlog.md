# Backlog

Vertical slices, smallest valuable first.

## Now
- [ ] Slice 1: Operations books a drawdown on a loan and sees the updated balance from ledger entries
  - Given a drawdown with an idempotency key, when it is posted twice, then only one ledger entry exists
  - Given ledger entries, then the balance is always their sum (never stored separately)
  - Given a drawdown awaiting sign-off, then it is not in the balance until approved
  - Test notes: one acceptance test per criterion; seeded invented data.

## Next
- [ ] Sketch leg: tokens + three variants of the slice-1 screen (`sketch-ui`)
- [ ] CI green with all gates before the first feature commit

## Later
