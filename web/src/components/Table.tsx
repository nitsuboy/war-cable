import { WarMark } from './WarMark';
import { LanguageSwitcher } from './LanguageSwitcher';
import { isSpecial } from '../game/rules';
import { useCountdown } from '../game/useCountdown';
import { useI18n } from '../i18n';
import type { RoomState } from '../game/types';

interface TableProps {
  room: RoomState;
  pendingPick: number | null;
  overlayVisible: boolean;
  vsBot?: boolean;
  onPick: (handIndex: number) => void;
  onLeave: () => void;
}

function Captures({ count, target }: { count: number; target: number }) {
  const { t } = useI18n();

  return (
    <div className="captures" aria-label={t('table.captures', { count, target })}>
      {Array.from({ length: target }, (_, index) => (
        <WarMark key={index} muted={index >= count} className={index < count ? 'captured' : ''} />
      ))}
    </div>
  );
}

function Track({ position }: { position: number }) {
  const { t } = useI18n();

  return (
    <div className="track">
      <span className="track-label">{t('table.trackTheir')}</span>
      <div className="track-rail">
        <div className="track-line" />
        {[-1, 0, 1].map((slot) => (
          <div key={slot} className={`track-node${position === slot ? ' active' : ''}`}>
            <span className="track-dot" />
            {position === slot && <span className="track-knot" aria-hidden="true" />}
          </div>
        ))}
      </div>
      <span className="track-label">{t('table.trackYour')}</span>
    </div>
  );
}

export function Table({
  room,
  pendingPick,
  overlayVisible,
  vsBot = false,
  onPick,
  onLeave,
}: TableProps) {
  const { t } = useI18n();
  const remaining = useCountdown(room.deadlineMs, room.paused);
  const viewTrack = room.yourSeat === 'A' ? room.track : -room.track;
  const total = room.turnSeconds * 1000;
  const ratio = total > 0 ? Math.max(0, Math.min(1, remaining / total)) : 0;
  const seconds = Math.ceil(remaining / 1000);
  const danger = room.phase === 'choosing' && !room.paused && remaining <= 5000;

  const locked =
    room.phase !== 'choosing' ||
    room.you.picked ||
    pendingPick !== null ||
    room.paused ||
    overlayVisible;

  const opponentStatus = !room.opponent.connected
    ? t('table.status.offline')
    : room.paused
      ? t('table.status.waiting')
      : room.opponent.picked
        ? t('table.status.ready')
        : t('table.status.choosing');

  const opponentStatusClass = !room.opponent.connected
    ? 'offline'
    : room.opponent.picked
      ? 'ready'
      : 'thinking';

  return (
    <main className="table">
      <header className="table-top">
        <span className="chip">{t('table.room', { code: room.code })}</span>
        <span className="chip subtle">{t('table.round', { round: room.roundNumber })}</span>
        {room.rated && <span className="chip subtle ranked">{t('table.rankedChip')}</span>}
        {vsBot && <span className="chip subtle">{t('table.botChip')}</span>}
        <button type="button" className="btn ghost small" onClick={onLeave}>
          {t('table.leave')}
        </button>
      </header>

      <section className="table-layout">
        <section className="duelist">
          <div className="duelist-row">
            <div>
              <p className="duelist-name">
                {room.opponent.bot
                  ? t('table.botChip')
                  : room.opponent.username ?? t('table.opponent')}
                {room.opponent.rating != null && (
                  <span className="rating-tag">{room.opponent.rating}</span>
                )}
              </p>
              <span className={`status-pill ${opponentStatusClass}`}>{opponentStatus}</span>
            </div>
            <Captures count={room.opponent.captures} target={room.targetCaptures} />
            <span className="hand-count">{t('table.cards', { count: room.opponent.handCount })}</span>
          </div>
        </section>

        <section className="arena">
          <Track position={viewTrack} />
          <div className="timer">
            <div className="timer-meta">
              <span>
                {room.paused
                  ? t('table.timer.paused')
                  : room.phase === 'choosing'
                    ? t('table.timer.choose')
                    : '—'}
              </span>
              <span className={danger ? 'danger' : ''}>
                {room.phase === 'choosing' ? `${seconds}s` : ''}
              </span>
            </div>
            <div className="timer-bar">
              <div
                className={`timer-fill${danger ? ' danger' : ''}`}
                style={{ width: `${ratio * 100}%` }}
              />
            </div>
          </div>
        </section>

        <section className="duelist you">
          <div className="duelist-row">
            <div>
              <p className="duelist-name">
                {room.you.username ?? t('table.you')}
                {room.you.rating != null && (
                  <span className="rating-tag">{room.you.rating}</span>
                )}
              </p>
              <span className="status-pill you">
                {room.you.picked
                  ? t('table.status.ordersGiven')
                  : room.paused
                    ? t('table.status.waiting')
                    : t('table.status.choosing')}
              </span>
            </div>
            <Captures count={room.you.captures} target={room.targetCaptures} />
            <span className="hand-count">{t('table.cards', { count: room.you.hand.length })}</span>
          </div>
        </section>
      </section>

      <section className="hand-area">
        <div className="hand">
          {room.you.hand.map((card, index) => {
            const selected = pendingPick === index;
            return (
              <button
                key={`${card}-${index}`}
                type="button"
                data-card={card}
                className={`card${isSpecial(card) ? ' special' : ''}${selected ? ' selected' : ''}`}
                onClick={() => onPick(index)}
                disabled={locked}
                aria-label={t('table.cardAria', { card })}
              >
                <span className="card-value">{card}</span>
              </button>
            );
          })}
        </div>
        <p className="hand-hint">
          {room.you.picked
            ? t('table.hint.picked')
            : room.paused
              ? t('table.hint.paused')
              : t('table.hint.choose')}
        </p>
      </section>

      <div className="lang-row table-lang">
        <LanguageSwitcher />
      </div>
    </main>
  );
}
