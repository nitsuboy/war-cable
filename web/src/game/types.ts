export type Seat = 'A' | 'B';
export type Phase = 'waiting' | 'choosing' | 'over';
export type Outcome = 'win' | 'lose' | 'draw';

export interface SeatView {
  seat: Seat;
  hand: string[];
  picked: boolean;
  connected: boolean;
  captures: number;
  rematchVote: boolean;
}

export interface OpponentView {
  seat: Seat;
  handCount: number;
  picked: boolean;
  connected: boolean;
  captures: number;
  rematchVote: boolean;
}

export interface RoomState {
  phase: Phase;
  code: string;
  track: number;
  targetCaptures: number;
  turnSeconds: number;
  roundNumber: number;
  deadlineMs: number;
  paused: boolean;
  yourSeat: Seat;
  you: SeatView;
  opponent: OpponentView;
  winnerSeat: Seat | null;
  endReason: string | null;
}

export interface RoundResult {
  yourCard: string | null;
  opponentCard: string | null;
  outcome: Outcome;
  captured: boolean;
  opponentCaptured: boolean;
  track: number;
  yourCaptures: number;
  opponentCaptures: number;
  reason: 'reveal' | 'timeout';
  matchWinner: 'you' | 'opponent' | null;
}

export interface MatchOver {
  winner: 'you' | 'opponent';
  reason: string;
}

export interface JoinResult {
  ok: boolean;
  error: string | null;
  roomCode: string | null;
  queued: boolean;
  reattached: boolean;
}

export interface HelloResult {
  playerId: string;
  sessionToken: string;
}

export interface Identity {
  playerId: string;
  sessionToken: string;
}
