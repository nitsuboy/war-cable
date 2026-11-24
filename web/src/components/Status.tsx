import { useEffect } from 'react';
import type { ConnectionStatus } from '../net/useGame';

export function ErrorToast({ message, onDismiss }: { message: string; onDismiss: () => void }) {
  useEffect(() => {
    const handle = window.setTimeout(onDismiss, 6000);
    return () => window.clearTimeout(handle);
  }, [message, onDismiss]);

  return (
    <div className="toast" role="alert">
      <span>{message}</span>
      <button type="button" onClick={onDismiss} aria-label="Dismiss">
        ×
      </button>
    </div>
  );
}

export function ConnectionBanner({ status }: { status: ConnectionStatus }) {
  const text =
    status === 'reconnecting'
      ? 'Connection lost — reconnecting…'
      : status === 'offline'
        ? 'Offline. Reload the page to rejoin.'
        : 'Connecting…';

  return <div className={`connection-banner ${status}`}>{text}</div>;
}

export function BootScreen({ status, onRetry }: { status: ConnectionStatus; onRetry: () => void }) {
  return (
    <main className="boot">
      <span className="spinner large" aria-hidden="true" />
      <h1>Dragon Duel</h1>
      <p className="muted">
        {status === 'offline' ? 'Could not reach the arena.' : 'Entering the arena…'}
      </p>
      {status === 'offline' && (
        <button type="button" className="btn gold" onClick={onRetry}>
          Retry
        </button>
      )}
    </main>
  );
}
