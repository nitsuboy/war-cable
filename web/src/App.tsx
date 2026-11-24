import { useEffect, useState } from 'react';
import { Lobby } from './components/Lobby';
import { ResultScreen } from './components/ResultScreen';
import { RoundOverlay } from './components/RoundOverlay';
import { Table } from './components/Table';
import { WaitingRoom } from './components/WaitingRoom';
import { BootScreen, ConnectionBanner, ErrorToast } from './components/Status';
import { useGame } from './net/useGame';
import { useI18n } from './i18n';

export default function App() {
  const { t } = useI18n();
  const { state, actions } = useGame();
  const [pendingPick, setPendingPick] = useState<number | null>(null);
  const room = state.room;

  useEffect(() => {
    if (!room || !room.you.picked || room.phase !== 'choosing') {
      setPendingPick(null);
    }
  }, [room, room?.roundNumber, room?.you.picked, room?.phase]);

  const handlePick = (handIndex: number) => {
    if (!room || room.you.picked || pendingPick !== null) {
      return;
    }

    setPendingPick(handIndex);
    void actions.pick(handIndex);
  };

  if (!state.ready) {
    return (
      <div className="app-shell">
        <div className="ambient" aria-hidden="true" />
        <BootScreen status={state.status} onRetry={() => window.location.reload()} />
      </div>
    );
  }

  return (
    <div className="app-shell">
      <div className="ambient" aria-hidden="true" />

      {room ? (
        room.phase === 'waiting' ? (
          <WaitingRoom room={room} onLeave={() => void actions.leave()} />
        ) : room.phase === 'over' && !state.round ? (
          <ResultScreen
            room={room}
            onRematch={() => void actions.rematch()}
            onLeave={() => void actions.leave()}
          />
        ) : (
          <Table
            room={room}
            pendingPick={pendingPick}
            overlayVisible={state.round !== null}
            onPick={handlePick}
            onLeave={() => void actions.leave()}
          />
        )
      ) : (
        <Lobby
          queued={state.queued}
          disabled={state.status !== 'online'}
          onCreate={() => void actions.createRoom()}
          onQuickMatch={() => void actions.quickMatch()}
          onCancelQueue={() => void actions.cancelQueue()}
          onJoin={(code) => void actions.joinRoom(code)}
        />
      )}

      {state.round && room && room.phase !== 'waiting' && <RoundOverlay round={state.round} />}

      {state.status !== 'online' && <ConnectionBanner status={state.status} />}

      {state.error && <ErrorToast message={t(state.error)} onDismiss={actions.dismissError} />}
    </div>
  );
}
