// Pure helpers, unit-tested in ledger.test.ts.

export interface LoanBalance {
  loanId: string; borrower: string; currency: string; annualRate: number; facilityLimit: number;
  principal: number; accruedInterest: number; outstanding: number; pendingDrawdowns: number; pendingCount: number;
  available: number; accruedThrough: string | null; asOf: string; version: number;
}
export interface StatementLine {
  version: number; date: string; kind: string; reference: string; description: string;
  amount: number; balanceEffect: number; principalAfter: number; interestAfter: number; outstandingAfter: number; actor: string;
}
export interface PendingDrawdown { drawdownId: string; amount: number; currency: string; requestedOn: string; requestedBy: string; }
export interface Problem { title?: string; detail?: string; code?: string; correlationId?: string; status?: number; }

export type ViewState<T> =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'empty' }
  | { kind: 'success'; data: T };

/** Money with tabular-friendly grouping and exactly the currency's minor units; negatives use a real minus sign. */
export function formatMoney(amount: number, currency = 'GBP', signed = false): string {
  const digits = new Intl.NumberFormat('en-GB', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2;
  const text = new Intl.NumberFormat('en-GB', { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(Math.abs(amount));
  if (amount < 0) return `−${text}`;
  return signed && amount > 0 ? `+${text}` : text;
}

/** The balance is the sum of the ledger's effects; the UI checks the API's projection against it. */
export function sumOfEffects(lines: StatementLine[]): number {
  // Work in minor units so 0.1 + 0.2 style float errors cannot creep in.
  return lines.reduce((cents, l) => cents + Math.round(l.balanceEffect * 100), 0) / 100;
}

export function toViewState<T>(loading: boolean, error: string | null, data: T | null, isEmpty: (d: T) => boolean): ViewState<T> {
  if (error) return { kind: 'error', message: error };
  if (loading || data === null) return { kind: 'loading' };
  return isEmpty(data) ? { kind: 'empty' } : { kind: 'success', data };
}

export function describeProblem(p: Problem | null, fallback: string): string {
  if (!p) return fallback;
  const ref = p.correlationId ? ` (ref ${p.correlationId.slice(0, 8)})` : '';
  return `${p.detail ?? p.title ?? fallback}${ref}`;
}

/** Earliest date money can move: business dates never go backwards and must fall after the last accrued day. */
export function nextBookableDate(b: Pick<LoanBalance, 'asOf' | 'accruedThrough'>): string {
  if (!b.accruedThrough || b.accruedThrough < b.asOf) return b.asOf;
  const d = new Date(`${b.accruedThrough}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}
