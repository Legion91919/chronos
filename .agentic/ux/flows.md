# User flows

## Flow 1: Book a drawdown and see the updated balance (slice 1)
1. Operations opens Chronos → `screens/loan-position.html` (`GET /api/loans`, `GET /api/loans/{id}`, `GET /api/loans/{id}/statement`, `GET /api/loans/{id}/pending`).
2. Selects a loan in **LoanList**; **BalanceStrip** shows outstanding = principal + accrued interest.
3. Requests a drawdown in **CommandForms** → `POST /api/loans/{id}/drawdowns` with `Idempotency-Key`. It appears under **PendingDrawdowns** and as a "awaiting approval" statement line; the outstanding balance does not change.
4. A second person approves → `POST /api/loans/{id}/drawdowns/{drawdownId}/approve` (`X-Actor` must differ from the requester). Outstanding rises by the amount.

## Flow 2: Month-end interest and repayments (finance)
1. Same screen → **CommandForms** "Accrue interest through" → `POST /api/loans/{id}/interest-accruals` (Actual/365).
2. Records a repayment → `POST /api/loans/{id}/repayments` (interest first, then principal).

## Flow 3: Auditor checks a past balance
1. Picks a date in **BrandBar** "As of" → `GET /api/loans/{id}/balance?asOf=YYYY-MM-DD` (replay of events dated on or before it); statement filtered with `?to=`.
2. Raw history for re-performance → `GET /api/loans/{id}/events`.
