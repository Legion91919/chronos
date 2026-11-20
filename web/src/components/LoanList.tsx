import { formatMoney, type LoanBalance } from '../ledger';

export function LoanList({ loans, selectedId, onSelect }: { loans: LoanBalance[]; selectedId: string | null; onSelect: (id: string) => void }) {
  return (
    <table aria-label="Loans">
      <thead><tr><th>Loan</th><th className="num">Outstanding</th></tr></thead>
      <tbody>
        {loans.map((l) => (
          <tr key={l.loanId} className="loan-row" aria-selected={l.loanId === selectedId} tabIndex={0}
            onClick={() => onSelect(l.loanId)} onKeyDown={(e) => e.key === 'Enter' && onSelect(l.loanId)}>
            <td><b>{l.loanId}</b><br /><span className="muted">{l.borrower}</span></td>
            <td className="num">{formatMoney(l.outstanding, l.currency)}{l.pendingCount > 0 && <><br /><span className="pill">{l.pendingCount} pending</span></>}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
