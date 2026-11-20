import { formatMoney, type PendingDrawdown } from '../ledger';

export function PendingDrawdowns({ pending, onApprove, busy }: { pending: PendingDrawdown[]; onApprove: (id: string) => void; busy: boolean }) {
  if (pending.length === 0) return <p className="empty muted">Nothing awaiting sign-off.</p>;
  return (
    <table>
      <tbody>
        {pending.map((p) => (
          <tr key={p.drawdownId}>
            <td>{p.drawdownId} · requested {p.requestedOn} by {p.requestedBy}</td>
            <td className="num">{formatMoney(p.amount, p.currency)}</td>
            <td className="num"><button disabled={busy} onClick={() => onApprove(p.drawdownId)}>Approve</button></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
