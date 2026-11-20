import { formatMoney, type StatementLine } from '../ledger';

export function StatementTable({ lines, currency }: { lines: StatementLine[]; currency: string }) {
  return (
    <table>
      <thead><tr><th>Date</th><th>Entry</th><th>By</th><th className="num">Effect</th><th className="num">Outstanding</th></tr></thead>
      <tbody>
        {lines.map((l) => (
          <tr key={l.version}>
            <td>{l.date}</td>
            <td>{l.description}{l.kind === 'DrawdownRequested' && <> <span className="pill">awaiting approval</span></>}</td>
            <td className="muted">{l.actor}</td>
            <td className="num">{formatMoney(l.balanceEffect, currency, true)}</td>
            <td className="num">{formatMoney(l.outstandingAfter, currency)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
