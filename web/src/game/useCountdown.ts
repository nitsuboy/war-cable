import { useEffect, useState } from 'react';

/**
 * Counts down from the server-provided remaining time. The server keeps the
 * authority; this is only for the visual timer.
 */
export function useCountdown(deadlineMs: number, paused: boolean) {
  const [remaining, setRemaining] = useState(deadlineMs);

  useEffect(() => {
    if (paused) {
      setRemaining(deadlineMs);
      return;
    }

    const end = Date.now() + deadlineMs;
    setRemaining(deadlineMs);
    const handle = window.setInterval(() => {
      setRemaining(Math.max(0, end - Date.now()));
    }, 100);

    return () => window.clearInterval(handle);
  }, [deadlineMs, paused]);

  return remaining;
}
