import { DragonMark } from './DragonMark';
import type { RoomState } from '../game/types';

interface ResultScreenProps {
  room: RoomState;
  onRematch: () => void;
  onLeave: () => void;
}

function reasonText(reason: string | null, won: boolean): string {
  switch (reason) {
    case 'wo':
      return won
        ? 'Your opponent could not reconnect in time.'
        : 'You were disconnected for too long.';
    case 'abandoned':
      return won ? 'Your opponent left the match.' : 'You left the match.';
    default:
      return won ? 'Four dragons now answer to your realm.' : 'Four dragons now answer to your rival.';
  }
}

export function ResultScreen({ room, onRematch, onLeave }: ResultScreenProps) {
  const won = room.winnerSeat === room.yourSeat;
  const canRematch = room.opponent.connected && !room.you.rematchVote;

  return (
    <main className={`result ${won ? 'victory' : 'defeat'}`}>
      <div className="sigil small">
        <DragonMark />
      </div>
      <p className="eyebrow">{won ? 'The dragons bow' : 'The dragons turn away'}</p>
      <h1>{won ? 'Victory' : 'Defeat'}</h1>
      <p className="muted">{reasonText(room.endReason, won)}</p>

      <div className="result-score">
        <div>
          <span className="score-label">You</span>
          <strong>{room.you.captures}</strong>
        </div>
        <span className="score-sep">—</span>
        <div>
          <span className="score-label">Opponent</span>
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
            ? 'Waiting for opponent…'
            : room.opponent.connected
              ? 'Rematch'
              : 'Opponent disconnected'}
        </button>
        <button type="button" className="btn ghost" onClick={onLeave}>
          Back to lobby
        </button>
      </div>
    </main>
  );
}
