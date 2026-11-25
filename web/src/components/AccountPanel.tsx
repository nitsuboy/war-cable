import { useState } from 'react';
import type { FormEvent } from 'react';
import type { AccountView, LeaderboardRow } from '../game/types';
import { useI18n } from '../i18n';

interface AccountPanelProps {
  account: AccountView | null;
  leaderboard: LeaderboardRow[];
  onLogin: (username: string, password: string) => Promise<void>;
  onRegister: (username: string, password: string) => Promise<void>;
  onLogout: () => Promise<void>;
}

function Leaderboard({ rows, me }: { rows: LeaderboardRow[]; me?: string }) {
  const { t } = useI18n();

  if (rows.length === 0) {
    return null;
  }

  return (
    <div className="leaderboard">
      <h3>{t('leaderboard.title')}</h3>
      <ol>
        {rows.map((row) => (
          <li
            key={row.username}
            className={me && row.username.toLowerCase() === me.toLowerCase() ? 'me' : undefined}
          >
            <span className="leader-rank">{row.rank}</span>
            <span className="leader-name">{row.username}</span>
            <span className="leader-record">
              {t('leaderboard.record', { wins: row.wins, games: row.games })}
            </span>
            <span className="leader-rating">{row.rating}</span>
          </li>
        ))}
      </ol>
    </div>
  );
}

export function AccountPanel({
  account,
  leaderboard,
  onLogin,
  onRegister,
  onLogout,
}: AccountPanelProps) {
  const { t } = useI18n();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy || username.trim().length === 0 || password.length === 0) {
      return;
    }

    setBusy(true);
    try {
      await (mode === 'login' ? onLogin : onRegister)(username.trim(), password);
    } finally {
      setBusy(false);
      setPassword('');
    }
  };

  if (account) {
    return (
      <section className="panel account-panel">
        <div className="account-head">
          <div>
            <p className="eyebrow">{t('auth.title')}</p>
            <h2>{account.username}</h2>
            <p className="muted small">
              {t('auth.record', {
                wins: account.wins,
                losses: account.losses,
                draws: account.draws,
                games: account.games,
              })}
            </p>
          </div>
          <div className="rating-badge">
            <span>{t('auth.ratingLabel')}</span>
            <strong>{account.rating}</strong>
          </div>
        </div>

        <div className="account-foot">
          <p className="muted small">{t('auth.rankedNote')}</p>
          <button type="button" className="btn ghost small" onClick={() => void onLogout()}>
            {t('auth.logout')}
          </button>
        </div>

        <Leaderboard rows={leaderboard} me={account.username} />
      </section>
    );
  }

  return (
    <section className="panel account-panel">
      <div>
        <p className="eyebrow">{t('auth.title')}</p>
        <p className="muted small account-guest">{t('auth.guest')}</p>
      </div>

      <div className="auth-tabs" role="tablist" aria-label={t('auth.title')}>
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'login'}
          className={`auth-tab${mode === 'login' ? ' active' : ''}`}
          onClick={() => setMode('login')}
        >
          {t('auth.login')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'register'}
          className={`auth-tab${mode === 'register' ? ' active' : ''}`}
          onClick={() => setMode('register')}
        >
          {t('auth.register')}
        </button>
      </div>

      <form className="auth-form" onSubmit={submit}>
        <div className="login-inputs">
        <label className="sr-only" htmlFor="auth-username">
          {t('auth.username')}
        </label>
        <input
          id="auth-username"
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          placeholder={t('auth.username')}
          autoComplete="username"
          maxLength={20}
          spellCheck={false}
        />
        <label className="sr-only" htmlFor="auth-password">
          {t('auth.password')}
        </label>
        <input
          id="auth-password"
          type="password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          placeholder={t('auth.password')}
          autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
          maxLength={128}
        />
        </div>
        <button
          type="submit"
          className="btn gold"
          disabled={busy || username.trim().length === 0 || password.length === 0}
        >
          {busy
            ? t('auth.working')
            : mode === 'login'
              ? t('auth.submitLogin')
              : t('auth.submitRegister')}
        </button>
      </form>

      <p className="auth-hint">{t('auth.hint')}</p>

      <Leaderboard rows={leaderboard} />
    </section>
  );
}
