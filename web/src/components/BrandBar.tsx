export function BrandBar({ asOf, onAsOfChange }: { asOf: string | null; onAsOfChange: (d: string | null) => void }) {
  return (
    <header className="top">
      <span className="mark">
        <svg width="26" height="26" viewBox="0 0 26 26" aria-hidden="true">
          <circle cx="13" cy="13" r="11" fill="none" stroke="var(--c-accent)" strokeWidth="2.5" />
          <path d="M13 6v7l5 3" fill="none" stroke="var(--c-accent)" strokeWidth="2.5" strokeLinecap="round" />
          <path d="M3.5 8.5A11 11 0 0 1 8 3.6" stroke="var(--c-pending)" strokeWidth="2.5" fill="none" strokeLinecap="round" />
        </svg>
        Chronos <span className="muted" style={{ fontWeight: 400 }}>loan positions</span>
      </span>
      <label className="muted">
        As of{' '}
        <input type="date" aria-label="Balance as of date" value={asOf ?? ''} onChange={(e) => onAsOfChange(e.target.value || null)} />{' '}
        <button className="ghost" onClick={() => onAsOfChange(null)} disabled={asOf === null}>Live</button>
      </label>
    </header>
  );
}
