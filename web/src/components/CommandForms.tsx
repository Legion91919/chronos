import { useState, type FormEvent } from 'react';

type Run = (path: string, body: unknown) => Promise<void>;

export function CommandForms({ loanId, currency, run, busy }: { loanId: string; currency: string; run: Run; busy: boolean }) {
  const [dd, setDd] = useState({ amount: '', date: '' });
  const [rp, setRp] = useState({ amount: '', date: '' });
  const [through, setThrough] = useState('');
  const base = `/api/loans/${encodeURIComponent(loanId)}`;
  const submit = (e: FormEvent, f: () => Promise<void>) => { e.preventDefault(); void f(); };
  const stamp = () => Date.now().toString(36).toUpperCase();

  return (
    <div className="forms">
      <form onSubmit={(e) => submit(e, () => run(`${base}/drawdowns`, { drawdownId: `DD-${stamp()}`, amount: Number(dd.amount), currency, requestedOn: dd.date }))}>
        <b>Request drawdown</b>
        <label>Amount ({currency})<input required inputMode="decimal" value={dd.amount} onChange={(e) => setDd({ ...dd, amount: e.target.value })} /></label>
        <label>Requested on<input required type="date" value={dd.date} onChange={(e) => setDd({ ...dd, date: e.target.value })} /></label>
        <button disabled={busy}>Request</button>
      </form>
      <form onSubmit={(e) => submit(e, () => run(`${base}/repayments`, { repaymentId: `RP-${stamp()}`, amount: Number(rp.amount), currency, receivedOn: rp.date }))}>
        <b>Record repayment</b>
        <label>Amount ({currency})<input required inputMode="decimal" value={rp.amount} onChange={(e) => setRp({ ...rp, amount: e.target.value })} /></label>
        <label>Received on<input required type="date" value={rp.date} onChange={(e) => setRp({ ...rp, date: e.target.value })} /></label>
        <button disabled={busy}>Record</button>
      </form>
      <form onSubmit={(e) => submit(e, () => run(`${base}/interest-accruals`, { through }))}>
        <b>Accrue interest (Actual/365)</b>
        <label>Through<input required type="date" value={through} onChange={(e) => setThrough(e.target.value)} /></label>
        <button disabled={busy}>Accrue</button>
      </form>
    </div>
  );
}
