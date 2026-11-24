import type { RoundResult } from '../game/types';
import { useI18n } from '../i18n';

export function RoundOverlay({ round }: { round: RoundResult }) {
  const { t } = useI18n();
  const timeout = round.reason === 'timeout';

  const headline = timeout
    ? round.outcome === 'win'
      ? t('round.timeoutWin')
      : round.outcome === 'lose'
        ? t('round.timeoutLose')
        : t('round.timeoutDraw')
    : round.outcome === 'win'
      ? t('round.win')
      : round.outcome === 'lose'
        ? t('round.lose')
        : t('round.draw');

  return (
    <div className={`overlay ${round.outcome}`} aria-live="polite">
      <div className="reveal">
        <div className="reveal-cards">
          <div className={`reveal-card${round.yourCard ? '' : ' empty'}`}>
            <span>{round.yourCard ?? '—'}</span>
            <small>{t('table.you')}</small>
          </div>
          <span className="reveal-vs">vs</span>
          <div className={`reveal-card${round.opponentCard ? '' : ' empty'}`}>
            <span>{round.opponentCard ?? '—'}</span>
            <small>{t('table.opponent')}</small>
          </div>
        </div>
        <h2>{headline}</h2>
        {round.captured && <p className="capture-banner">{t('round.captured')}</p>}
        {round.opponentCaptured && (
          <p className="capture-banner lost">{t('round.opponentCaptured')}</p>
        )}
        {timeout && <p className="muted small">{t('round.noCards')}</p>}
      </div>
    </div>
  );
}
