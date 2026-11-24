#!/usr/bin/env node
/**
 * End-to-end smoke test against a running server.
 *
 *   cd web && npm run smoke
 *
 * Expects the server at http://127.0.0.1:5000 with Guard__TurnSeconds=2 so the
 * timeout scenario finishes quickly.
 */
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve as resolvePath } from 'node:path';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const HERE = dirname(fileURLToPath(import.meta.url));
const HUB_URL = process.env.HUB_URL ?? 'http://127.0.0.1:5000/hub';
const matrix = JSON.parse(
  readFileSync(resolvePath(HERE, '../../shared/rules-matrix.json'), 'utf8'),
);

const lookup = new Map(matrix.rows.map((row) => [`${row.a}:${row.b}`, row.result]));

function resolveCards(a, b) {
  const result = lookup.get(`${a}:${b}`);
  assert.ok(result, `Missing matrix entry for ${a} vs ${b}`);
  return result === 'a' ? 'win' : result === 'b' ? 'lose' : 'draw';
}

function randomIndex(length) {
  return Math.floor(Math.random() * length);
}

const clients = [];

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitFor(predicate, timeoutMs, label) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (predicate()) {
      return;
    }
    await sleep(25);
  }
  throw new Error(`Timed out waiting for ${label}`);
}

async function connect(label) {
  const connection = new HubConnectionBuilder()
    .withUrl(HUB_URL, { withCredentials: false })
    .configureLogging(LogLevel.Error)
    .build();

  const client = {
    label,
    connection,
    identity: null,
    state: null,
    states: [],
    rounds: [],
    over: null,
  };

  connection.on('RoomState', (state) => {
    client.state = state;
    client.states.push(state);
  });
  connection.on('RoundResult', (round) => {
    client.rounds.push(round);
  });
  connection.on('MatchOver', (over) => {
    client.over = over;
  });

  await connection.start();
  client.identity = await connection.invoke('Hello', null, null);
  assert.equal(typeof client.identity.playerId, 'string');
  assert.ok(client.identity.sessionToken.length >= 16);
  return client;
}

async function reconnect(client) {
  await client.connection.stop();
  client.state = null;
  client.states = [];
  client.rounds = [];
  client.over = null;

  const connection = new HubConnectionBuilder()
    .withUrl(HUB_URL, { withCredentials: false })
    .configureLogging(LogLevel.Error)
    .build();
  connection.on('RoomState', (state) => {
    client.state = state;
    client.states.push(state);
  });
  connection.on('RoundResult', (round) => {
    client.rounds.push(round);
  });
  connection.on('MatchOver', (over) => {
    client.over = over;
  });
  await connection.start();
  const identity = await connection.invoke(
    'Hello',
    client.identity.playerId,
    client.identity.sessionToken,
  );
  assert.equal(identity.playerId, client.identity.playerId);
  client.connection = connection;
}

async function main() {
  console.log(`Connecting to ${HUB_URL}`);
  const a = await connect('A');
  const b = await connect('B');
  clients.push(a, b);

  // --- private room ---
  const created = await a.connection.invoke('CreateRoom');
  assert.equal(created.ok, true);
  const code = created.roomCode;
  assert.match(code, /^[A-Z0-9]{6}$/);

  const joined = await b.connection.invoke('JoinRoom', code);
  assert.equal(joined.ok, true);
  assert.equal(joined.roomCode, code);

  await waitFor(
    () => a.state?.phase === 'choosing' && b.state?.phase === 'choosing',
    5000,
    'both players in the choosing phase',
  );

  assert.equal(a.state.you.hand.length, 9);
  assert.equal(a.state.opponent.handCount, 9);
  assert.equal(a.state.opponent.hand, undefined, 'opponent hand must never be sent');
  assert.equal(b.state.opponent.hand, undefined, 'opponent hand must never be sent');
  assert.equal(a.state.you.seat, 'A');
  assert.equal(b.state.you.seat, 'B');
  assert.equal(a.state.opponent.picked, false);

  // --- play a full match with random picks ---
  let rounds = 0;
  for (; rounds < 400; rounds += 1) {
    if (a.state.phase === 'over') {
      break;
    }

    await waitFor(
      () =>
        a.state.phase === 'choosing' &&
        !a.state.you.picked &&
        b.state.phase === 'choosing' &&
        !b.state.you.picked,
      5000,
      `round ${rounds + 1} to be ready`,
    );

    const roundNumber = a.state.roundNumber;
    const aBefore = a.rounds.length;
    const bBefore = b.rounds.length;

    await a.connection.invoke('SubmitPick', randomIndex(a.state.you.hand.length));
    await b.connection.invoke('SubmitPick', randomIndex(b.state.you.hand.length));

    await waitFor(
      () => a.rounds.length > aBefore && b.rounds.length > bBefore,
      5000,
      `round ${rounds + 1} result`,
    );

    await waitFor(
      () => a.state.roundNumber > roundNumber || a.state.phase === 'over',
      5000,
      `round ${rounds + 1} state`,
    );

    const roundA = a.rounds[a.rounds.length - 1];
    const roundB = b.rounds[b.rounds.length - 1];

    assert.equal(roundA.reason, 'reveal');
    assert.equal(roundA.outcome, resolveCards(roundA.yourCard, roundA.opponentCard));
    assert.equal(roundB.yourCard, roundA.opponentCard);
    assert.equal(roundB.opponentCard, roundA.yourCard);
    assert.equal(
      roundB.outcome,
      roundA.outcome === 'win' ? 'lose' : roundA.outcome === 'lose' ? 'win' : 'draw',
    );
    assert.ok([-1, 0, 1].includes(roundA.track));
    assert.equal(roundA.captured && roundA.opponentCaptured, false);
  }

  assert.ok(rounds < 400, 'match should finish');
  assert.equal(a.state.phase, 'over');
  assert.equal(b.state.phase, 'over');
  assert.ok(a.over, 'A should receive MatchOver');
  assert.equal(a.over.winner === 'you' ? b.over.winner : a.over.winner, 'opponent');
  assert.equal(a.state.winnerSeat === a.state.yourSeat ? 'you' : 'opponent', a.over.winner);
  assert.equal(Math.max(a.state.you.captures, a.state.opponent.captures), 4);

  console.log(`Match finished in ${rounds + 1} rounds. Winner: ${a.over.winner}`);

  // --- rematch ---
  const overState = a.state;
  await a.connection.invoke('Rematch');
  await b.connection.invoke('Rematch');
  await waitFor(
    () =>
      a.state.phase === 'choosing' &&
      b.state.phase === 'choosing' &&
      a.state.roundNumber === 1,
    5000,
    'rematch to start',
  );
  assert.equal(a.state.you.captures, 0);
  assert.equal(overState.phase, 'over');

  // --- timeout: B picks, A does not ---
  const timeoutBefore = a.rounds.length;
  await b.connection.invoke('SubmitPick', 0);
  await waitFor(() => a.rounds.length > timeoutBefore, 6000, 'timeout round');

  const timeoutA = a.rounds[a.rounds.length - 1];
  const timeoutB = b.rounds[b.rounds.length - 1];
  assert.equal(timeoutA.reason, 'timeout');
  assert.equal(timeoutA.yourCard, null);
  assert.equal(timeoutA.opponentCard, null);
  assert.equal(timeoutA.outcome, 'lose');
  assert.equal(timeoutB.outcome, 'win');
  assert.equal(a.state.you.hand.length, 9, 'timeout must not consume cards');

  // --- reconnect with the same identity ---
  const codeBefore = a.state.code;
  await reconnect(b);
  await waitFor(() => b.state?.code === codeBefore, 5000, 'reattach after reconnect');
  assert.equal(b.state.you.seat, 'B');

  await a.connection.invoke('Leave');
  await b.connection.invoke('Leave');
  await a.connection.stop();
  await b.connection.stop();

  // --- quick match ---
  const c = await connect('C');
  const d = await connect('D');
  clients.push(c, d);

  const queued = await c.connection.invoke('QuickMatch');
  assert.equal(queued.ok, true);
  assert.equal(queued.queued, true);

  const paired = await d.connection.invoke('QuickMatch');
  assert.equal(paired.ok, true);
  assert.equal(paired.queued, false);
  assert.match(paired.roomCode, /^[A-Z0-9]{6}$/);

  await waitFor(
    () => c.state?.phase === 'choosing' && d.state?.phase === 'choosing',
    5000,
    'quick match to start',
  );
  assert.equal(c.state.code, d.state.code);
  assert.equal(c.state.code, paired.roomCode);
  assert.equal(c.state.you.seat, 'A');
  assert.equal(d.state.you.seat, 'B');

  await c.connection.invoke('Leave');
  await d.connection.invoke('Leave');
  await c.connection.stop();
  await d.connection.stop();

  // --- cancel queue leaves the player out of the pool ---
  const e = await connect('E');
  const f = await connect('F');
  clients.push(e, f);

  const enqueued = await e.connection.invoke('QuickMatch');
  assert.equal(enqueued.queued, true);
  await e.connection.invoke('CancelQueue');

  const fQueued = await f.connection.invoke('QuickMatch');
  assert.equal(fQueued.ok, true);
  assert.equal(fQueued.queued, true, 'a cancelled player must not be paired');

  await f.connection.invoke('CancelQueue');
  await e.connection.stop();
  await f.connection.stop();

  console.log('Quick match and queue cancellation verified.');

  console.log('Smoke test passed.');
}

main()
  .then(() => process.exit(0))
  .catch(async (error) => {
    console.error(error);
    await Promise.allSettled(clients.map((client) => client.connection.stop()));
    process.exit(1);
  });
