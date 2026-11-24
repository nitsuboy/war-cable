import { DragonMark } from './DragonMark';
import { isSpecial } from '../game/rules';
import { useCountdown } from '../game/useCountdown';
import type { RoomState } from '../game/types';

interface TableProps {
  room: RoomState;
  pendingPick: number | null;
  overlayVisible: boolean;
  onPick: (handIndex: number) => void;
  onLeave: () => void;
}

function Captures({ count, target }: { count: number; target: number }) {
  return (
    <div className="captures" aria-label={`${count} of ${target} dragons captured`}>
      {Array.from({ length: target }, (_, index) => (
        <DragonMark key={index} muted={index >= count} className={index < count ? 'captured' : ''} />
      ))}
    </div>
  );
}

function Track({ position }: { position: number }) {
  return (
    <div className="track">
      <span className="track-label">Their realm</span>
      <div className="track-rail">
        <div className="track-line" />
        {[-1, 0, 1].map((slot) => (
          <div key={slot} className={`track-node${position === slot ? ' active' : ''}`}>
            <span className="track-dot" />
            {position === slot && (
              <span className="track-dragon">
                <DragonMark />
              </span>
            )}
          </div>
        ))}
      </div>
      <span className="track-label">Your realm</span>
    </div>
  );
}

export function Table({ room, pendingPick, overlayVisible, onPick, onLeave }: TableProps) {
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
    ? 'Disconnected'
    : room.paused
      ? 'Waiting'
      : room.opponent.picked
        ? 'Ready'
        : 'Choosing…';

  const opponentStatusClass = !room.opponent.connected
    ? 'offline'
    : room.opponent.picked
      ? 'ready'
      : 'thinking';

  return (
    <main className="table">
      <header className="table-top">
        <span className="chip">Room {room.code}</span>
        <span className="chip subtle">Round {room.roundNumber}</span>
        <button type="button" className="btn ghost small" onClick={onLeave}>
          Leave
        </button>
      </header>

      <section className="duelist">
        <div className="duelist-row">
          <div>
            <p className="duelist-name">Opponent</p>
            <span className={`status-pill ${opponentStatusClass}`}>{opponentStatus}</span>
          </div>
          <Captures count={room.opponent.captures} target={room.targetCaptures} />
          <span className="hand-count">{room.opponent.handCount} cards</span>
        </div>
      </section>

      <section className="arena">
        <Track position={viewTrack} />
        <div className="timer">
          <div className="timer-meta">
            <span>{room.paused ? 'Round paused' : room.phase === 'choosing' ? 'Choose a card' : '—'}</span>
            <span className={danger ? 'danger' : ''}>{room.phase === 'choosing' ? `${seconds}s` : ''}</span>
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
            <p className="duelist-name">You</p>
            <span className="status-pill you">
              {room.you.picked ? 'Locked in' : room.paused ? 'Waiting' : 'Choosing…'}
            </span>
          </div>
          <Captures count={room.you.captures} target={room.targetCaptures} />
          <span className="hand-count">{room.you.hand.length} cards</span>
        </div>
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
                aria-label={`Play card ${card}`}
              >
                <span className="card-value">{card}</span>
              </button>
            );
          })}
        </div>
        <p className="hand-hint">
          {room.you.picked
            ? 'Card locked in. Waiting for your opponent…'
            : room.paused
              ? 'Waiting for your opponent to reconnect…'
              : 'Pick one card. Both cards are revealed at the same time.'}
        </p>
      </section>
    </main>
  );
}
