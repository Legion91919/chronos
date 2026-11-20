import type { LoanBalance, PendingDrawdown, Problem, StatementLine } from './ledger';

export class ApiError extends Error {
  constructor(public problem: Problem) { super(problem.detail ?? problem.title ?? 'Request failed'); }
}

async function get<T>(path: string): Promise<T> {
  const r = await fetch(path, { headers: { Accept: 'application/json' } });
  if (!r.ok) throw new ApiError(await r.json().catch(() => ({ title: `HTTP ${r.status}` })));
  return r.json() as Promise<T>;
}

export type Sender = (path: string, body: unknown, actor: string, key: string) => Promise<Response>;

const defaultSender: Sender = (path, body, actor, key) =>
  fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Actor': actor },
    body: JSON.stringify(body),
  });

/**
 * Sends a command. The idempotency key is created once and reused for every retry, so a network blip
 * or a timeout can never book the same drawdown twice. Retries only on network errors and 5xx/409-in-progress.
 */
export async function sendCommand(path: string, body: unknown, actor: string, opts: { key?: string; attempts?: number; send?: Sender } = {}) {
  const key = opts.key ?? crypto.randomUUID();
  const send = opts.send ?? defaultSender;
  const attempts = opts.attempts ?? 3;
  let last: Problem = { title: 'Network error' };
  for (let i = 0; i < attempts; i++) {
    try {
      const r = await send(path, body, actor, key);
      const json = await r.json().catch(() => ({}));
      if (r.ok) return json;
      last = json as Problem;
      const retryable = r.status >= 500 || (r.status === 409 && last.code === 'idempotency.in_progress');
      if (!retryable) break;
    } catch {
      last = { title: 'Network error' };
    }
  }
  throw new ApiError(last);
}

export const api = {
  loans: () => get<LoanBalance[]>('/api/loans'),
  balance: (id: string, asOf: string | null) => get<LoanBalance>(`/api/loans/${encodeURIComponent(id)}/balance${asOf ? `?asOf=${asOf}` : ''}`),
  statement: (id: string, to: string | null) => get<StatementLine[]>(`/api/loans/${encodeURIComponent(id)}/statement${to ? `?to=${to}` : ''}`),
  pending: (id: string) => get<PendingDrawdown[]>(`/api/loans/${encodeURIComponent(id)}/pending`),
};
