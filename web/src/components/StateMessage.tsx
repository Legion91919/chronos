export function StateMessage({ kind, message, onRetry }: { kind: 'loading' | 'empty' | 'error'; message?: string; onRetry?: () => void }) {
  if (kind === 'loading') return <div aria-busy="true" aria-label="Loading"><div className="skel" /><div className="skel" /><div className="skel" /></div>;
  if (kind === 'error')
    return (
      <div className="alert" role="alert">
        <b>Could not load.</b> {message} {onRetry && <button className="ghost" onClick={onRetry}>Retry</button>}
      </div>
    );
  return <p className="empty muted">{message}</p>;
}
