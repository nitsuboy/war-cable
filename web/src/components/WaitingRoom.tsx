import { useState } from 'react';
import { WarMark } from './WarMark';
import { LanguageSwitcher } from './LanguageSwitcher';
import { useI18n } from '../i18n';
import type { RoomState } from '../game/types';

interface WaitingRoomProps {
  room: RoomState;
  onLeave: () => void;
}

export function WaitingRoom({ room, onLeave }: WaitingRoomProps) {
  const { t } = useI18n();
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(room.code);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1600);
    } catch {
      setCopied(false);
    }
  };

  return (
    <main className="waiting">
      <div className="sigil small">
        <WarMark />
      </div>
      <p className="eyebrow">{t('waiting.eyebrow')}</p>
      <button
        type="button"
        className="room-code"
        onClick={copy}
        title={t('waiting.copyTitle')}
      >
        {room.code}
      </button>
      <p className="muted">{copied ? t('waiting.copied') : t('waiting.clickToCopy')}</p>
      <div className="waiting-status">
        <span className="spinner" aria-hidden="true" />
        <span>{t('waiting.waiting')}</span>
      </div>
      <p className="muted small">{t('waiting.share')}</p>
      <button type="button" className="btn ghost" onClick={onLeave}>
        {t('waiting.cancel')}
      </button>
      <LanguageSwitcher />
    </main>
  );
}
