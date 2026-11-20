import { useCallback, useEffect, useState } from 'react';
import { api, ApiError, sendCommand } from './api';
import { describeProblem, nextBookableDate, sumOfEffects, toViewState, type LoanBalance, type PendingDrawdown, type StatementLine } from './ledger';
import { BrandBar } from './components/BrandBar';
import { LoanList } from './components/LoanList';
import { BalanceStrip } from './components/BalanceStrip';
import { PendingDrawdowns } from './components/PendingDrawdowns';
import { StatementTable } from './components/StatementTable';
import { CommandForms } from './components/CommandForms';
import { StateMessage } from './components/StateMessage';

interface Detail { balance: LoanBalance; lines: StatementLine[]; pending: PendingDrawdown[] }
const errorText = (e: unknown) => (e instanceof ApiError ? describeProblem(e.problem, 'Request failed') : 'The API did not answer.');

export function App() {
  const [loans, setLoans] = useState<LoanBalance[] | null>(null);
  const [loansError, setLoansError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [asOf, setAsOf] = useState<string | null>(null);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [actor, setActor] = useState('ops-17');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);

  const loadLoans = useCallback(async () => {
    setLoansError(null);
    try {
      const list = await api.loans();
      setLoans(list);
      setSelected((s) => s ?? list[0]?.loanId ?? null);
    } catch (e) { setLoansError(errorText(e)); }
  }, []);

  const loadDetail = useCallback(async (id: string, date: string | null) => {
    setDetailError(null);
    setDetail(null);
    try {
      const [balance, lines, pending] = await Promise.all([api.balance(id, date), api.statement(id, date), date ? Promise.resolve([]) : api.pending(id)]);
      setDetail({ balance, lines, pending });
    } catch (e) { setDetailError(errorText(e)); }
  }, []);

  useEffect(() => { void loadLoans(); }, [loadLoans]);
  useEffect(() => { if (selected) void loadDetail(selected, asOf); }, [selected, asOf, loadDetail]);

  const run = async (path: string, body: unknown) => {
    setBusy(true);
    setNotice(null);
    try {
      await sendCommand(path, body, actor);
      setNotice({ ok: true, text: 'Booked. The balance below is replayed from the ledger.' });
      await loadLoans();
      if (selected) await loadDetail(selected, asOf);
    } catch (e) {
      setNotice({ ok: false, text: errorText(e) });
    } finally { setBusy(false); }
  };

  const loansView = toViewState(loans === null && !loansError, loansError, loans, (l) => l.length === 0);
  const detailView = toViewState(detail === null && !detailError, detailError, detail, () => false);
  const live = asOf === null;

  return (
    <>
      <BrandBar asOf={asOf} onAsOfChange={setAsOf} />
      <main>
        <section aria-label="Loans list">
          <h2>Loans</h2>
          {loansView.kind === 'success'
            ? <LoanList loans={loansView.data} selectedId={selected} onSelect={setSelected} />
            : <StateMessage kind={loansView.kind} message={loansView.kind === 'error' ? loansView.message : 'No loans yet. Open a facility through the API to start its ledger.'} onRetry={loadLoans} />}
          <p className="muted" style={{ padding: '0 10px' }}>
            <label>Acting as <select value={actor} onChange={(e) => setActor(e.target.value)} aria-label="Acting as">
              <option value="ops-17">ops-17 (operations)</option><option value="fin-04">fin-04 (finance approver)</option>
            </select></label>
          </p>
        </section>
        <div>
          {notice && <div className={notice.ok ? 'tile ok' : 'alert'} role={notice.ok ? 'status' : 'alert'} style={{ marginBottom: 16 }}>{notice.text}</div>}
          {detailView.kind !== 'success' ? (
            <section><StateMessage kind={detailView.kind === 'empty' ? 'loading' : detailView.kind} message={detailView.kind === 'error' ? detailView.message : undefined}
              onRetry={() => selected && loadDetail(selected, asOf)} /></section>
          ) : (
            <>
              <BalanceStrip b={detailView.data.balance} live={live} />
              {live && (
                <>
                  <section><h2>Awaiting sign-off</h2><PendingDrawdowns pending={detailView.data.pending} busy={busy}
                    onApprove={(id) => run(`/api/loans/${encodeURIComponent(detailView.data.balance.loanId)}/drawdowns/${encodeURIComponent(id)}/approve`, { approvedOn: nextBookableDate(detailView.data.balance) })} /></section>
                  <section><h2>Book</h2><CommandForms loanId={detailView.data.balance.loanId} currency={detailView.data.balance.currency} run={run} busy={busy} /></section>
                </>
              )}
              <section>
                <h2>Statement · version {detailView.data.balance.version}
                  {sumOfEffects(detailView.data.lines) === detailView.data.balance.outstanding
                    ? <span className="ok"> · ties to the ledger</span>
                    : <span className="alert" style={{ padding: 2 }}> · does not tie</span>}</h2>
                {detailView.data.lines.length === 0 ? <StateMessage kind="empty" message="No entries on or before this date." />
                  : <StatementTable lines={detailView.data.lines} currency={detailView.data.balance.currency} />}
              </section>
            </>
          )}
        </div>
      </main>
    </>
  );
}
