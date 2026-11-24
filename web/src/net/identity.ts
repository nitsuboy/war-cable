import type { Identity } from '../game/types';

const STORAGE_KEY = 'dragon-duel.identity.v1';

export function loadIdentity(): Identity | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<Identity>;
    if (
      typeof parsed.playerId === 'string' &&
      typeof parsed.sessionToken === 'string' &&
      parsed.playerId.length >= 8 &&
      parsed.sessionToken.length >= 16
    ) {
      return { playerId: parsed.playerId, sessionToken: parsed.sessionToken };
    }
  } catch {
    // Corrupt or unavailable storage: start a fresh identity.
  }

  return null;
}

export function saveIdentity(identity: Identity): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(identity));
  } catch {
    // Private mode or blocked storage: the session still works until reload.
  }
}
