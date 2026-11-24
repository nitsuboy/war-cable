import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type {
  HelloResult,
  JoinResult,
  MatchOver,
  RoomState,
  RoundResult,
} from '../game/types';
import { loadIdentity, saveIdentity } from './identity';
import type { TranslationKey } from '../i18n';

const HUB_URL = import.meta.env.VITE_HUB_URL ?? '/hub';

const RETRY_DELAYS = [0, 1000, 2000, 3000, 5000, 8000, 13000, 21000];

const ERROR_KEYS: Record<string, TranslationKey> = {
  invalid_identity: 'errors.invalidIdentity',
  hello_required: 'errors.helloRequired',
  rate_limited: 'errors.rateLimited',
  already_in_room: 'errors.alreadyInRoom',
  room_not_found: 'errors.roomNotFound',
  room_full: 'errors.roomFull',
  match_started: 'errors.matchStarted',
  not_in_room: 'errors.notInRoom',
  not_choosing: 'errors.notChoosing',
  invalid_pick: 'errors.invalidPick',
  not_over: 'errors.notOver',
};

function describeError(error: unknown): TranslationKey {
  const raw = error instanceof Error ? error.message : String(error);
  const marker = raw.lastIndexOf('HubException:');
  const code = (marker >= 0 ? raw.slice(marker + 'HubException:'.length) : raw).trim();
  return ERROR_KEYS[code] ?? 'errors.unknown';
}

function describeCode(code: string | null): TranslationKey {
  if (!code) {
    return 'errors.unknown';
  }
  return ERROR_KEYS[code] ?? 'errors.unknown';
}

function deriveOver(room: RoomState): MatchOver | null {
  if (room.phase !== 'over' || !room.winnerSeat) {
    return null;
  }
  return {
    winner: room.winnerSeat === room.yourSeat ? 'you' : 'opponent',
    reason: room.endReason ?? 'captures',
  };
}

export type ConnectionStatus = 'connecting' | 'online' | 'reconnecting' | 'offline';

export interface GameSnapshot {
  status: ConnectionStatus;
  ready: boolean;
  room: RoomState | null;
  round: RoundResult | null;
  over: MatchOver | null;
  error: TranslationKey | null;
  queued: boolean;
}

const INITIAL: GameSnapshot = {
  status: 'connecting',
  ready: false,
  room: null,
  round: null,
  over: null,
  error: null,
  queued: false,
};

export function useGame() {
  const [state, setState] = useState<GameSnapshot>(INITIAL);
  const connectionRef = useRef<HubConnection | null>(null);
  const roundTimerRef = useRef<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    let retryHandle: number | undefined;

    const hello = async (connection: HubConnection) => {
      const stored = loadIdentity();
      const result = await connection.invoke<HelloResult>(
        'Hello',
        stored?.playerId ?? null,
        stored?.sessionToken ?? null,
      );
      saveIdentity(result);
    };

    const handleRoomState = (room: RoomState) => {
      setState((previous) => ({
        ...previous,
        room,
        queued: false,
        ready: true,
        error: null,
        over: room.phase === 'over' ? previous.over ?? deriveOver(room) : null,
      }));
    };

    const handleRound = (round: RoundResult) => {
      setState((previous) => ({ ...previous, round }));
      if (roundTimerRef.current !== null) {
        window.clearTimeout(roundTimerRef.current);
      }
      roundTimerRef.current = window.setTimeout(() => {
        setState((previous) => ({ ...previous, round: null }));
      }, 2400);
    };

    const handleOver = (over: MatchOver) => {
      setState((previous) => ({ ...previous, over }));
    };

    const start = async () => {
      setState((previous) => ({ ...previous, status: 'connecting' }));
      const connection = new HubConnectionBuilder()
        .withUrl(HUB_URL, { withCredentials: false })
        .withAutomaticReconnect(RETRY_DELAYS)
        .configureLogging(import.meta.env.DEV ? LogLevel.Warning : LogLevel.Error)
        .build();

      connection.on('RoomState', handleRoomState);
      connection.on('RoundResult', handleRound);
      connection.on('MatchOver', handleOver);

      connection.onreconnecting(() => {
        setState((previous) => ({ ...previous, status: 'reconnecting' }));
      });

      connection.onreconnected(async () => {
        try {
          await hello(connection);
          setState((previous) => ({ ...previous, status: 'online' }));
        } catch {
          setState((previous) => ({ ...previous, status: 'offline' }));
        }
      });

      connection.onclose(() => {
        setState((previous) => ({ ...previous, status: 'offline' }));
      });

      try {
        await connection.start();
        if (cancelled) {
          await connection.stop();
          return;
        }

        await hello(connection);
        connectionRef.current = connection;
        setState((previous) => ({ ...previous, status: 'online', ready: true }));
      } catch {
        if (cancelled) {
          return;
        }

        setState((previous) => ({ ...previous, status: 'offline' }));
        retryHandle = window.setTimeout(() => void start(), 4000);
      }
    };

    void start();

    return () => {
      cancelled = true;
      if (retryHandle !== undefined) {
        window.clearTimeout(retryHandle);
      }
      if (roundTimerRef.current !== null) {
        window.clearTimeout(roundTimerRef.current);
      }
      const connection = connectionRef.current;
      connectionRef.current = null;
      void connection?.stop();
    };
  }, []);

  const call = useCallback(async <T,>(method: string, ...args: unknown[]): Promise<T | null> => {
    const connection = connectionRef.current;
    if (!connection || connection.state !== HubConnectionState.Connected) {
      setState((previous) => ({
        ...previous,
        error: 'errors.offlineAction',
      }));
      return null;
    }

    try {
      return (await connection.invoke(method, ...args)) as T;
    } catch (error) {
      setState((previous) => ({ ...previous, error: describeError(error) }));
      return null;
    }
  }, []);

  const createRoom = useCallback(async () => {
    const result = await call<JoinResult>('CreateRoom');
    if (result && !result.ok) {
      setState((previous) => ({ ...previous, error: describeCode(result.error) }));
    }
  }, [call]);

  const joinRoom = useCallback(
    async (code: string) => {
      const result = await call<JoinResult>('JoinRoom', code);
      if (result && !result.ok) {
        setState((previous) => ({ ...previous, error: describeCode(result.error) }));
      }
    },
    [call],
  );

  const quickMatch = useCallback(async () => {
    const result = await call<JoinResult>('QuickMatch');
    if (!result) {
      return;
    }

    if (!result.ok) {
      setState((previous) => ({ ...previous, error: describeCode(result.error) }));
      return;
    }

    setState((previous) => ({ ...previous, queued: result.queued }));
  }, [call]);

  const pick = useCallback(
    async (handIndex: number) => {
      await call('SubmitPick', handIndex);
    },
    [call],
  );

  const rematch = useCallback(async () => {
    await call('Rematch');
  }, [call]);

  const cancelQueue = useCallback(async () => {
    await call('CancelQueue');
    setState((previous) => ({ ...previous, queued: false }));
  }, [call]);

  const leave = useCallback(async () => {
    await call('Leave');
    setState((previous) => ({
      ...previous,
      room: null,
      round: null,
      over: null,
      queued: false,
    }));
  }, [call]);

  const dismissError = useCallback(() => {
    setState((previous) => ({ ...previous, error: null }));
  }, []);

  return {
    state,
    actions: { createRoom, joinRoom, quickMatch, pick, rematch, leave, cancelQueue, dismissError },
  };
}
