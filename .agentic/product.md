# Product brief

> Harbourline Lending is a fictional company. All names, documents and numbers in this repo are invented.

## Problem
Harbourline tracks drawdowns, interest and repayments on property loans in spreadsheets, so cash positions are slow to confirm and hard to audit.

## Users
Loan operations (daily); finance (month end); auditors (yearly).

## Outcomes
Cash position confirmed same day; interest recalculation matches to the cent.

## Constraints
Money as decimal; no balance is ever overwritten; every request safe to retry.

## Out of scope
Anything not needed for the outcomes above in the first three slices.

## Traits (for the architecture advisor)
- MoneyMovement: "drawdowns, interest accrual, repayments, fees"
- RegulatoryAudit: "auditors re-perform interest calculations"
- LongRunningWorkflows: "drawdown requests wait days for sign-off and conditions"
- SeveralBusinessAreas: "loans, cash, statements"
- ComplexBusinessRules: "day-count conventions, rate resets, partial repayments"
- SmallTeam: "two engineers"
