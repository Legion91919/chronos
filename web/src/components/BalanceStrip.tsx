import { formatMoney, type LoanBalance } from '../ledger';

export function BalanceStrip({ b, live }: { b: LoanBalance; live: boolean }) {
  return (
    <div className="strip" aria-label={live ? 'Current balance' : `Balance as of ${b.asOf}`}>
      <div className="tile"><div className="muted">Outstanding {b.currency}{live ? '' : ` · ${b.asOf}`}</div><div className="v">{formatMoney(b.outstanding, b.currency)}</div></div>
      <div className="tile"><div className="muted">Principal</div><div className="v">{formatMoney(b.principal, b.currency)}</div></div>
      <div className="tile"><div className="muted">Accrued interest</div><div className="v">{formatMoney(b.accruedInterest, b.currency)}</div>
        <div className="muted">{b.accruedThrough ? `through ${b.accruedThrough}` : 'none accrued yet'}</div></div>
      <div className="tile pending"><div>Awaiting approval (not in balance)</div><div className="v">{formatMoney(b.pendingDrawdowns, b.currency)}</div></div>
    </div>
  );
}
