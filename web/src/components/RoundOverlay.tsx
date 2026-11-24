import type { RoundResult } from '../game/types';

export function RoundOverlay({ round }: { round: RoundResult }) {
  const timeout = round.reason === 'timeout';

  const headline = timeout
    ? round.outcome === 'win'
      ? 'Opponent timed out — the round is yours.'
      : round.outcome === 'lose'
        ? "Time's up — the round goes to your opponent."
        : 'Both players timed out — no movement.'
    : round.outcome === 'win'
      ? 'You win the round.'
      : round.outcome === 'lose'
        ? 'You lose the round.'
        : 'Draw — the dragon holds.';

  return (
    <div className={`overlay ${round.outcome}`} aria-live="polite">
      <div className="reveal">
        <div className="reveal-cards">
          <div className={`reveal-card${round.yourCard ? '' : ' empty'}`}>
            <span>{round.yourCard ?? '—'}</span>
            <small>You</small>
          </div>
          <span className="reveal-vs">vs</span>
          <div className={`reveal-card${round.opponentCard ? '' : ' empty'}`}>
            <span>{round.opponentCard ?? '—'}</span>
            <small>Opponent</small>
          </div>
        </div>
        <h2>{headline}</h2>
        {round.captured && <p className="capture-banner">Dragon captured!</p>}
        {round.opponentCaptured && <p className="capture-banner lost">Your opponent captured a dragon.</p>}
        {timeout && <p className="muted small">No cards were revealed.</p>}
      </div>
    </div>
  );
}
