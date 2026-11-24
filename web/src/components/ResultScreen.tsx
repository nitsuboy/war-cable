import { WarMark } from './WarMark';
import { LanguageSwitcher } from './LanguageSwitcher';
import { useI18n } from '../i18n';
import type { RoomState } from '../game/types';

interface ResultScreenProps {
  room: RoomState;
  onRematch: () => void;
  onLeave: () => void;
}

export function ResultScreen({ room, onRematch, onLeave }: ResultScreenProps) {
  const { t } = useI18n();
  const won = room.winnerSeat === room.yourSeat;
  const canRematch = room.opponent.connected && !room.you.rematchVote;

  const reason = (() => {
    switch (room.endReason) {
      case 'wo':
        return won ? t('result.woWin') : t('result.woLose');
      case 'abandoned':
        return won ? t('result.abandonedWin') : t('result.abandonedLose');
      default:
        return won ? t('result.capturesWin') : t('result.capturesLose');
    }
  })();

  return (
    <main className={`result ${won ? 'victory' : 'defeat'}`}>
      <div className="sigil small">
        <WarMark />
      </div>
      <p className="eyebrow">{won ? t('result.wonEyebrow') : t('result.lostEyebrow')}</p>
      <h1>{won ? t('result.victory') : t('result.defeat')}</h1>
      <p className="muted">{reason}</p>

      <div className="result-score">
        <div>
          <span className="score-label">{t('table.you')}</span>
          <strong>{room.you.captures}</strong>
        </div>
        <span className="score-sep">—</span>
        <div>
          <span className="score-label">{t('table.opponent')}</span>
          <strong>{room.opponent.captures}</strong>
        </div>
      </div>

      <div className="result-actions">
        <button
          type="button"
          className="btn primary"
          onClick={onRematch}
          disabled={!canRematch}
        >
          {room.you.rematchVote
            ? t('result.rematchWaiting')
            : room.opponent.connected
              ? t('result.rematch')
              : t('result.opponentDisconnected')}
        </button>
        <button type="button" className="btn ghost" onClick={onLeave}>
          {t('result.backToLobby')}
        </button>
      </div>

      <LanguageSwitcher />
    </main>
  );
}
