# Components (loan position screen)

Every data component has four states: **loading** (skeleton rows), **empty** (a sentence + the next action),
**error** (role=alert, correlation id, Retry), **success**.

| Component | Props | Notes |
|---|---|---|
| `BrandBar` | `asOf: string \| null`, `onAsOfChange(date \| null)` | Logo mark + "As of" date input; empty = today (live projection) |
| `LoanList` | `loans: LoanBalance[]`, `selectedId`, `onSelect(id)`, `state` | Outstanding right-aligned, "n pending" pill |
| `BalanceStrip` | `balance: LoanBalance`, `state` | Outstanding, principal, accrued interest (+ accrued-through), pending (amber, labelled "not in balance") |
| `PendingDrawdowns` | `pending: PendingDrawdown[]`, `onApprove(id)`, `state` | Approve sends `X-Actor` of the approver; four-eyes errors shown inline |
| `CommandForms` | `loanId`, `currency`, `onDone()` | Request drawdown, record repayment, accrue interest; each POST carries a fresh `Idempotency-Key` that is reused on retry |
| `StatementTable` | `lines: StatementLine[]`, `state` | One row per event, effect and running outstanding; pending rows carry a text pill |
| `StateMessage` | `kind: 'loading' \| 'empty' \| 'error'`, `message`, `onRetry?` | Shared state renderer |
