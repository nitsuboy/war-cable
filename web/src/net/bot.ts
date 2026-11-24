import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type { HelloResult, JoinResult, RoomState } from '../game/types';
import { CARD_SYMBOLS, resolveLocal } from '../game/rules';

const HUB_URL = import.meta.env.VITE_HUB_URL ?? '/hub';

/**
 * Client-side opponent. It opens a second hub connection with its own
 * throwaway identity, joins the room and plays like a regular player.
 * The server stays authoritative: the bot only submits picks and rematches.
 */
export class BotPlayer {
  private connection: HubConnection | null = null;
  private identity: HelloResult | null = null;
  private pickTimer: number | null = null;
  private rematchTimer: number | null = null;
  private disposed = false;

  async join(code: string): Promise<boolean> {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { withCredentials: false })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();

    connection.on('RoomState', (room: RoomState) => this.handleRoomState(room));
    connection.onreconnected(async () => {
      try {
        if (this.identity) {
          await connection.invoke('Hello', this.identity.playerId, this.identity.sessionToken);
        }
      } catch {
        // The room is gone; nothing left to recover.
      }
    });
    connection.onclose(() => {
      this.clearTimers();
    });

    try {
      await connection.start();
      this.identity = await connection.invoke<HelloResult>('Hello', null, null);
      const joined = await connection.invoke<JoinResult>('JoinRoom', code);
      if (!joined?.ok) {
        await connection.stop();
        return false;
      }
    } catch {
      await connection.stop().catch(() => undefined);
      return false;
    }

    if (this.disposed) {
      await connection.stop().catch(() => undefined);
      return false;
    }

    this.connection = connection;
    return true;
  }

  dispose(): void {
    this.disposed = true;
    this.clearTimers();

    const connection = this.connection;
    this.connection = null;
    if (!connection) {
      return;
    }

    void (async () => {
      try {
        if (connection.state === HubConnectionState.Connected) {
          await connection.invoke('Leave');
        }
      } catch {
        // Already gone; stopping the connection is enough.
      }
      await connection.stop().catch(() => undefined);
    })();
  }

  private clearTimers(): void {
    if (this.pickTimer !== null) {
      window.clearTimeout(this.pickTimer);
      this.pickTimer = null;
    }
    if (this.rematchTimer !== null) {
      window.clearTimeout(this.rematchTimer);
      this.rematchTimer = null;
    }
  }

  private handleRoomState(room: RoomState): void {
    if (this.disposed) {
      return;
    }

    if (room.phase === 'choosing' && !room.you.picked && this.pickTimer === null) {
      this.pickTimer = window.setTimeout(() => {
        this.pickTimer = null;
        void this.submitPick(room);
      }, this.pickDelay(room));
    }

    if (room.phase === 'over' && !room.you.rematchVote && this.rematchTimer === null) {
      this.rematchTimer = window.setTimeout(() => {
        this.rematchTimer = null;
        void this.submitRematch();
      }, 900);
    }
  }

  private pickDelay(room: RoomState): number {
    const budget = Math.min(1400, Math.max(200, room.turnSeconds * 1000 * 0.45));
    return Math.round(budget * (0.6 + Math.random() * 0.6));
  }

  private async submitPick(room: RoomState): Promise<void> {
    const connection = this.connection;
    if (!connection || connection.state !== HubConnectionState.Connected) {
      return;
    }

    try {
      await connection.invoke('SubmitPick', chooseCardIndex(room));
    } catch {
      // The round already moved on; the server will time the bot out.
    }
  }

  private async submitRematch(): Promise<void> {
    const connection = this.connection;
    if (!connection || connection.state !== HubConnectionState.Connected) {
      return;
    }

    try {
      await connection.invoke('Rematch');
    } catch {
      // Not over anymore; nothing to do.
    }
  }
}

/**
 * Scores each card by how many hidden cards it beats minus how many beat it,
 * weighting wins when the knot is on the bot's line. A small jitter keeps the
 * bot from replaying the same hand order every round.
 */
function chooseCardIndex(room: RoomState): number {
  const capturing = (room.yourSeat === 'A' ? room.track : -room.track) === 1;

  const scored = room.you.hand.map((card, index) => {
    let wins = 0;
    let losses = 0;
    for (const other of CARD_SYMBOLS) {
      const result = resolveLocal(card, other);
      if (result === 'win') {
        wins += 1;
      } else if (result === 'lose') {
        losses += 1;
      }
    }

    const score = (capturing ? wins * 1.35 : wins) - losses + Math.random() * 0.8;
    return { index, score };
  });

  scored.sort((a, b) => b.score - a.score);
  return scored[0]?.index ?? 0;
}
