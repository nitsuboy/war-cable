import { useEffect } from 'react';
import type { ConnectionStatus } from '../net/useGame';
import { useI18n } from '../i18n';

export function ErrorToast({ message, onDismiss }: { message: string; onDismiss: () => void }) {
  const { t } = useI18n();

  useEffect(() => {
    const handle = window.setTimeout(onDismiss, 6000);
    return () => window.clearTimeout(handle);
  }, [message, onDismiss]);

  return (
    <div className="toast" role="alert">
      <span>{message}</span>
      <button type="button" onClick={onDismiss} aria-label={t('toast.dismiss')}>
        ×
      </button>
    </div>
  );
}

export function ConnectionBanner({
  status,
  retrying = false,
}: {
  status: ConnectionStatus;
  retrying?: boolean;
}) {
  const { t } = useI18n();

  const text =
    status === 'reconnecting'
      ? t('conn.reconnecting')
      : status === 'offline'
        ? t(retrying ? 'conn.offlineRetrying' : 'conn.offline')
        : t('conn.connecting');

  return <div className={`connection-banner ${status}`}>{text}</div>;
}

export function BootScreen({ status, onRetry }: { status: ConnectionStatus; onRetry: () => void }) {
  const { t } = useI18n();

  return (
    <main className="boot">
      <span className="spinner large" aria-hidden="true" />
      <h1>Tug of War</h1>
      <p className="muted">{status === 'offline' ? t('boot.offline') : t('boot.connecting')}</p>
      {status === 'offline' && (
        <button type="button" className="btn gold" onClick={onRetry}>
          {t('boot.retry')}
        </button>
      )}
    </main>
  );
}
