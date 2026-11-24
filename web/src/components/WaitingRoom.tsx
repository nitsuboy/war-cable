import { useState } from 'react';
import { DragonMark } from './DragonMark';
import type { RoomState } from '../game/types';

interface WaitingRoomProps {
  room: RoomState;
  onLeave: () => void;
}

export function WaitingRoom({ room, onLeave }: WaitingRoomProps) {
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
        <DragonMark />
      </div>
      <p className="eyebrow">Private room</p>
      <button type="button" className="room-code" onClick={copy} title="Copy room code">
        {room.code}
      </button>
      <p className="muted">{copied ? 'Copied to clipboard' : 'Click the code to copy it'}</p>
      <div className="waiting-status">
        <span className="spinner" aria-hidden="true" />
        <span>Waiting for an opponent to join…</span>
      </div>
      <p className="muted small">
        Share the code with your rival. The match starts as soon as they enter.
      </p>
      <button type="button" className="btn ghost" onClick={onLeave}>
        Cancel room
      </button>
    </main>
  );
}
