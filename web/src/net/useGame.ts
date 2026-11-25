import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type {
  AccountView,
  AuthResult,
  HelloResult,
  JoinResult,
  LeaderboardRow,
  MatchOver,
  RoomState,
  RoundResult,
} from '../game/types';
import { clearIdentity, loadIdentity, saveIdentity } from './identity';
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
  invalid_username: 'errors.invalidUsername',
  weak_password: 'errors.weakPassword',
  username_taken: 'errors.usernameTaken',
  invalid_credentials: 'errors.invalidCredentials',
  auth_rate_limited: 'errors.authRateLimited',
};

function errorCode(error: unknown): string {
  const raw = error instanceof Error ? error.message : String(error);
  const marker = raw.lastIndexOf('HubException:');
  return (marker >= 0 ? raw.slice(marker + 'HubException:'.length) : raw).trim();
}

function describeError(error: unknown): TranslationKey {
  return ERROR_KEYS[errorCode(error)] ?? 'errors.unknown';
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
  /** True once the first connection attempt has finished, successful or not. */
  booted: boolean;
  room: RoomState | null;
  round: RoundResult | null;
  over: MatchOver | null;
  error: TranslationKey | null;
  queued: boolean;
  vsBot: boolean;
  account: AccountView | null;
  leaderboard: LeaderboardRow[];
}

const INITIAL: GameSnapshot = {
  status: 'connecting',
  booted: false,
  room: null,
  round: null,
  over: null,
  error: null,
  queued: false,
  vsBot: false,
  account: null,
  leaderboard: [],
};

export function useGame() {
  const [state, setState] = useState<GameSnapshot>(INITIAL);
  const connectionRef = useRef<HubConnection | null>(null);
  const roundTimerRef = useRef<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    let retryHandle: number | undefined;

    const applyHello = (result: HelloResult) => {
      saveIdentity(result);
      if (!cancelled) {
        setState((previous) => ({ ...previous, account: result.account ?? null }));
      }
    };

    // A stored account session can expire or be revoked. Drop it and come back
    // as a guest instead of leaving the client stuck on invalid_identity.
    const hello = async (connection: HubConnection) => {
      const stored = loadIdentity();
      try {
        const result = await connection.invoke<HelloResult>(
          'Hello',
          stored?.playerId ?? null,
          stored?.sessionToken ?? null,
        );
        applyHello(result);
        return result;
      } catch (error) {
        if (!stored || errorCode(error) !== 'invalid_identity') {
          throw error;
        }

        clearIdentity();
        const result = await connection.invoke<HelloResult>('Hello', null, null);
        applyHello(result);
        return result;
      }
    };

    const loadLeaderboard = async (connection: HubConnection) => {
      try {
        const rows = await connection.invoke<LeaderboardRow[]>('Leaderboard', 5);
        if (!cancelled) {
          setState((previous) => ({ ...previous, leaderboard: rows }));
        }
      } catch {
        // Leaderboard is decorative; never block play on it.
      }
    };

    const refreshProfile = async (connection: HubConnection) => {
      try {
        const account = await connection.invoke<AccountView | null>('Profile');
        if (!cancelled) {
          setState((previous) => ({ ...previous, account: account ?? null }));
        }
      } catch {
        // Keep the last known snapshot.
      }
    };

    const handleRoomState = (room: RoomState) => {
      setState((previous) => ({
        ...previous,
        room,
        queued: false,
        error: null,
        vsBot: room.opponent.bot === true,
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

    const handleOver = (over: MatchOver, connection: HubConnection) => {
      setState((previous) => ({
        ...previous,
        over,
        account:
          over.newRating != null && previous.account
            ? { ...previous.account, rating: over.newRating }
            : previous.account,
      }));

      if (over.newRating != null) {
        void refreshProfile(connection);
      }
      void loadLeaderboard(connection);
    };

    const start = async (announce = true) => {
      if (announce) {
        setState((previous) => ({ ...previous, status: 'connecting' }));
      }
      const connection = new HubConnectionBuilder()
        .withUrl(HUB_URL, { withCredentials: false })
        .withAutomaticReconnect(RETRY_DELAYS)
        .configureLogging(import.meta.env.DEV ? LogLevel.Warning : LogLevel.Error)
        .build();

      connection.on('RoomState', handleRoomState);
      connection.on('RoundResult', handleRound);
      connection.on('MatchOver', (over: MatchOver) => handleOver(over, connection));

      connection.onreconnecting(() => {
        setState((previous) => ({ ...previous, status: 'reconnecting' }));
      });

      connection.onreconnected(async () => {
        try {
          await hello(connection);
          setState((previous) => ({ ...previous, status: 'online' }));
          void loadLeaderboard(connection);
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
        setState((previous) => ({
          ...previous,
          status: 'online',
          booted: true,
        }));
        void loadLeaderboard(connection);
      } catch {
        if (cancelled) {
          return;
        }

        setState((previous) => ({
          ...previous,
          status: 'offline',
          booted: true,
        }));
        retryHandle = window.setTimeout(() => void start(false), 4000);
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

  const loadLeaderboard = useCallback(async () => {
    const connection = connectionRef.current;
    if (!connection || connection.state !== HubConnectionState.Connected) {
      return;
    }

    try {
      const rows = await connection.invoke<LeaderboardRow[]>('Leaderboard', 5);
      setState((previous) => ({ ...previous, leaderboard: rows }));
    } catch {
      // Leaderboard is decorative; never block play on it.
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

  const playBot = useCallback(async () => {
    const result = await call<JoinResult>('PlayBot');
    if (!result) {
      return;
    }

    if (!result.ok) {
      setState((previous) => ({ ...previous, error: describeCode(result.error) }));
    }
  }, [call]);

  const authenticate = useCallback(
    async (method: 'Login' | 'Register', username: string, password: string) => {
      const result = await call<AuthResult>(method, username, password);
      if (!result) {
        return;
      }

      if (!result.ok || !result.playerId || !result.sessionToken) {
        setState((previous) => ({ ...previous, error: describeCode(result.error) }));
        return;
      }

      saveIdentity({
        playerId: result.playerId,
        sessionToken: result.sessionToken,
      });
      setState((previous) => ({
        ...previous,
        account: result.account,
        queued: false,
        error: null,
      }));
      void loadLeaderboard();
    },
    [call, loadLeaderboard],
  );

  const login = useCallback(
    (username: string, password: string) => authenticate('Login', username, password),
    [authenticate],
  );

  const register = useCallback(
    (username: string, password: string) => authenticate('Register', username, password),
    [authenticate],
  );

  const logout = useCallback(async () => {
    await call('Logout');

    const connection = connectionRef.current;
    if (!connection || connection.state !== HubConnectionState.Connected) {
      return;
    }

    try {
      const guest = await connection.invoke<HelloResult>('Hello', null, null);
      saveIdentity(guest);
      setState((previous) => ({
        ...previous,
        account: null,
        room: null,
        round: null,
        over: null,
        queued: false,
        vsBot: false,
        error: null,
      }));
    } catch {
      setState((previous) => ({ ...previous, error: 'errors.unknown' }));
    }
  }, [call]);

  const leave = useCallback(async () => {
    await call('Leave');
    setState((previous) => ({
      ...previous,
      room: null,
      round: null,
      over: null,
      queued: false,
      vsBot: false,
    }));
  }, [call]);

  const dismissError = useCallback(() => {
    setState((previous) => ({ ...previous, error: null }));
  }, []);

  return {
    state,
    actions: {
      createRoom,
      joinRoom,
      quickMatch,
      playBot,
      pick,
      rematch,
      leave,
      cancelQueue,
      dismissError,
      login,
      register,
      logout,
    },
  };
}
