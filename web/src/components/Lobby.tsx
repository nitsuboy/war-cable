import { useState } from 'react';
import type { FormEvent } from 'react';
import { WarMark } from './WarMark';
import { LanguageSwitcher } from './LanguageSwitcher';
import { RULES_KEYS, useI18n } from '../i18n';

interface LobbyProps {
  queued: boolean;
  disabled: boolean;
  onCreate: () => void;
  onQuickMatch: () => void;
  onPlayBot: () => void;
  onCancelQueue: () => void;
  onJoin: (code: string) => void;
}

export function Lobby({
  queued,
  disabled,
  onCreate,
  onQuickMatch,
  onPlayBot,
  onCancelQueue,
  onJoin,
}: LobbyProps) {
  const { t } = useI18n();
  const [code, setCode] = useState('');

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = code.trim().toUpperCase();
    if (!disabled && trimmed.length === 6) {
      onJoin(trimmed);
    }
  };

  return (
    <main className="lobby">
      <div className="lang-row">
        <LanguageSwitcher />
      </div>

      <header className="lobby-hero">
        <div className="sigil">
          <WarMark />
        </div>
        <p className="eyebrow">{t('lobby.eyebrow')}</p>
        <h1>Tug of War</h1>
        <p className="tagline">{t('lobby.tagline')}</p>
      </header>

      <section className="panel lobby-actions">
        {disabled && (
          <p className="offline-note" role="status">
            {t('lobby.offlineNotice')}
          </p>
        )}

        {queued ? (
          <div className="queue-state">
            <span className="spinner" aria-hidden="true" />
            <div>
              <h2>{t('lobby.searching')}</h2>
              <p className="muted">{t('lobby.searchingHint')}</p>
            </div>
            <button type="button" className="btn ghost" onClick={onCancelQueue}>
              {t('lobby.cancel')}
            </button>
          </div>
        ) : (
          <>
            <button type="button" className="btn primary large" onClick={onQuickMatch} disabled={disabled}>
              {t('lobby.quickMatch')}
            </button>
            <button type="button" className="btn gold" onClick={onPlayBot} disabled={disabled}>
              {t('lobby.playBot')}
            </button>
            <div className="divider">
              <span>{t('lobby.orFriend')}</span>
            </div>
            <button type="button" className="btn ghost" onClick={onCreate} disabled={disabled}>
              {t('lobby.createRoom')}
            </button>
            <form className="join-form" onSubmit={submit}>
              <label htmlFor="room-code" className="sr-only">
                {t('lobby.roomCodeLabel')}
              </label>
              <input
                id="room-code"
                className="code-input"
                value={code}
                onChange={(event) =>
                  setCode(event.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 6))
                }
                placeholder={t('lobby.roomCodePlaceholder')}
                autoComplete="off"
                spellCheck={false}
                maxLength={6}
                inputMode="text"
              />
              <button type="submit" className="btn gold" disabled={disabled || code.length !== 6}>
                {t('lobby.join')}
              </button>
            </form>
          </>
        )}
      </section>

      <section className="panel rules">
        <h2>{t('lobby.rulesTitle')}</h2>
        <ul>
          {RULES_KEYS.map((key) => (
            <li key={key}>{t(key)}</li>
          ))}
        </ul>
        <p className="muted small">{t('lobby.rulesNote')}</p>
      </section>
    </main>
  );
}
