import { useState } from 'react';
import type { FormEvent } from 'react';
import { DragonMark } from './DragonMark';
import { RULES_SUMMARY } from '../game/rules';

interface LobbyProps {
  queued: boolean;
  disabled: boolean;
  onCreate: () => void;
  onQuickMatch: () => void;
  onCancelQueue: () => void;
  onJoin: (code: string) => void;
}

export function Lobby({ queued, disabled, onCreate, onQuickMatch, onCancelQueue, onJoin }: LobbyProps) {
  const [code, setCode] = useState('');

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = code.trim().toUpperCase();
    if (trimmed.length === 6) {
      onJoin(trimmed);
    }
  };

  return (
    <main className="lobby">
      <header className="lobby-hero">
        <div className="sigil">
          <DragonMark />
        </div>
        <p className="eyebrow">A duel of dragons and nerve</p>
        <h1>Dragon Duel</h1>
        <p className="tagline">
          Nine cards. Three steps of track. Read your rival, push the dragon into
          your realm and capture four of them.
        </p>
      </header>

      <section className="panel lobby-actions">
        {queued ? (
          <div className="queue-state">
            <span className="spinner" aria-hidden="true" />
            <div>
              <h2>Searching for an opponent…</h2>
              <p className="muted">You will be paired with the next player in the queue.</p>
            </div>
            <button type="button" className="btn ghost" onClick={onCancelQueue}>
              Cancel
            </button>
          </div>
        ) : (
          <>
            <button type="button" className="btn primary large" onClick={onQuickMatch} disabled={disabled}>
              Quick match
            </button>
            <div className="divider">
              <span>or play with a friend</span>
            </div>
            <button type="button" className="btn ghost" onClick={onCreate} disabled={disabled}>
              Create a private room
            </button>
            <form className="join-form" onSubmit={submit}>
              <label htmlFor="room-code" className="sr-only">
                Room code
              </label>
              <input
                id="room-code"
                className="code-input"
                value={code}
                onChange={(event) =>
                  setCode(event.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 6))
                }
                placeholder="ROOM CODE"
                autoComplete="off"
                spellCheck={false}
                maxLength={6}
                inputMode="text"
              />
              <button type="submit" className="btn gold" disabled={disabled || code.length !== 6}>
                Join
              </button>
            </form>
          </>
        )}
      </section>

      <section className="panel rules">
        <h2>How to play</h2>
        <ul>
          {RULES_SUMMARY.map((rule) => (
            <li key={rule}>{rule}</li>
          ))}
        </ul>
        <p className="muted small">
          Every turn lasts 20 seconds. If time runs out, the round goes to your
          opponent. Disconnect for more than 30 seconds and the match is lost.
        </p>
      </section>
    </main>
  );
}
