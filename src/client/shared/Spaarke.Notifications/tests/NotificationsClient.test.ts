jest.mock('../src/negotiate', () => ({
  connectSignalR: jest.fn(),
}));
jest.mock('../src/pollFallback', () => ({
  startPollFallback: jest.fn(),
}));

import { connectSignalR } from '../src/negotiate';
import { startPollFallback } from '../src/pollFallback';
import { NotificationsClient } from '../src/NotificationsClient';

const mockedConnectSignalR = connectSignalR as jest.Mock;
const mockedStartPollFallback = startPollFallback as jest.Mock;

/** A minimal `HubConnection` stand-in. `.stop()` simulates real SignalR behavior by
 * synchronously invoking the registered `onclose` handler, same as the live SDK does. */
function makeFakeConnection() {
  let oncloseHandler: (() => void) | undefined;
  return {
    onclose: jest.fn((cb: () => void) => {
      oncloseHandler = cb;
    }),
    onreconnecting: jest.fn(),
    onreconnected: jest.fn(),
    stop: jest.fn(async () => {
      oncloseHandler?.();
    }),
    // Test-only helper to fire onclose as if it were a genuine reconnect-exhaustion event.
    __fireClose(): void {
      oncloseHandler?.();
    },
  };
}

describe('NotificationsClient', () => {
  beforeEach(() => {
    mockedConnectSignalR.mockReset();
    mockedStartPollFallback.mockReset();
    mockedStartPollFallback.mockReturnValue({ stop: jest.fn(), isRunning: true });
  });

  it('dispatches a live signal to the handler registered for its kind', async () => {
    let onSignal: ((s: { outboxRowId: string; kind: string }) => void) | undefined;
    mockedConnectSignalR.mockImplementation(async cb => {
      onSignal = cb;
      return makeFakeConnection();
    });

    const client = new NotificationsClient();
    const handler = jest.fn();
    client.registerHandler('communication-arrived', handler);
    await client.start();

    onSignal?.({ outboxRowId: 'row-1', kind: 'communication-arrived' });

    expect(handler).toHaveBeenCalledWith({
      outboxRowId: 'row-1',
      kind: 'communication-arrived',
      envelope: undefined,
      source: 'live',
    });
    expect(client.connectionState).toBe('connected');
  });

  it('start() starts poll-fallback AND rethrows on negotiate/connect failure', async () => {
    const err = new Error('negotiate failed');
    mockedConnectSignalR.mockRejectedValue(err);

    const client = new NotificationsClient();

    await expect(client.start()).rejects.toBe(err);

    expect(mockedStartPollFallback).toHaveBeenCalledTimes(1);
    expect(client.connectionState).toBe('polling');
  });

  it('stop() does not leave a poll loop running (regression: connection.stop() itself fires onclose)', async () => {
    const fakeConnection = makeFakeConnection();
    mockedConnectSignalR.mockResolvedValue(fakeConnection);

    const client = new NotificationsClient();
    await client.start();
    expect(client.connectionState).toBe('connected');

    await client.stop();

    // Without the `stopping` guard, connection.stop()'s internal onclose would call
    // startPolling() right as the client is shutting down, leaking a poll loop forever.
    expect(mockedStartPollFallback).not.toHaveBeenCalled();
    expect(client.connectionState).toBe('stopped');
  });

  it('a genuine disconnect (reconnect exhausted, NOT caused by stop()) still starts poll-fallback', async () => {
    const fakeConnection = makeFakeConnection();
    mockedConnectSignalR.mockResolvedValue(fakeConnection);

    const client = new NotificationsClient();
    await client.start();

    fakeConnection.__fireClose();

    expect(client.connectionState).toBe('polling');
    expect(mockedStartPollFallback).toHaveBeenCalledTimes(1);
  });

  it('dispatches a re-polled pending row only ONCE (issue #688 — repeating toast regression)', async () => {
    mockedConnectSignalR.mockRejectedValue(new Error('negotiate failed'));

    const client = new NotificationsClient();
    const handler = jest.fn();
    client.registerHandler('communication-arrived', handler);
    await client.start().catch(() => undefined);

    const pollOptions = mockedStartPollFallback.mock.calls[0][0];
    const row = { outboxRowId: 'row-1', kind: 'communication-arrived', envelope: {}, source: 'poll' };

    // The pending endpoint returns every still-undismissed row on every tick — simulate three ticks.
    pollOptions.onEvent(row);
    pollOptions.onEvent(row);
    pollOptions.onEvent(row);

    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('suppresses re-delivery across a poll-loop restart (reconnecting → reconnected → closed)', async () => {
    const fakeConnection = makeFakeConnection();
    let onSignal: ((s: { outboxRowId: string; kind: string }) => void) | undefined;
    mockedConnectSignalR.mockImplementation(async cb => {
      onSignal = cb;
      return fakeConnection;
    });

    const client = new NotificationsClient();
    const handler = jest.fn();
    client.registerHandler('communication-arrived', handler);
    await client.start();

    // Live push delivers row-1, then the connection dies → poll fallback starts and
    // the still-pending row-1 comes back from the pending endpoint.
    onSignal?.({ outboxRowId: 'row-1', kind: 'communication-arrived' });
    fakeConnection.__fireClose();

    const pollOptions = mockedStartPollFallback.mock.calls[0][0];
    pollOptions.onEvent({ outboxRowId: 'row-1', kind: 'communication-arrived', envelope: {}, source: 'poll' });
    // A genuinely new row still gets through.
    pollOptions.onEvent({ outboxRowId: 'row-2', kind: 'communication-arrived', envelope: {}, source: 'poll' });

    expect(handler).toHaveBeenCalledTimes(2);
    expect(handler.mock.calls[0][0].outboxRowId).toBe('row-1');
    expect(handler.mock.calls[1][0].outboxRowId).toBe('row-2');
  });

  it('clears the seen-row set on stop() so a restarted client re-delivers pending rows (next-load semantic)', async () => {
    mockedConnectSignalR.mockRejectedValue(new Error('negotiate failed'));

    const client = new NotificationsClient();
    const handler = jest.fn();
    client.registerHandler('communication-arrived', handler);
    await client.start().catch(() => undefined);

    const firstPollOptions = mockedStartPollFallback.mock.calls[0][0];
    firstPollOptions.onEvent({ outboxRowId: 'row-1', kind: 'communication-arrived', envelope: {}, source: 'poll' });
    expect(handler).toHaveBeenCalledTimes(1);

    await client.stop();

    // New session: handlers must re-register (stop() cleared them), and the same still-pending
    // row is delivered once more — that is the intended FR-06 next-load behavior, not a dup.
    client.registerHandler('communication-arrived', handler);
    await client.start().catch(() => undefined);
    const secondPollOptions = mockedStartPollFallback.mock.calls[1][0];
    secondPollOptions.onEvent({ outboxRowId: 'row-1', kind: 'communication-arrived', envelope: {}, source: 'poll' });

    expect(handler).toHaveBeenCalledTimes(2);
  });

  it('unregisters handlers and stops the connection on stop()', async () => {
    const fakeConnection = makeFakeConnection();
    mockedConnectSignalR.mockResolvedValue(fakeConnection);

    const client = new NotificationsClient();
    await client.start();
    await client.stop();

    expect(fakeConnection.stop).toHaveBeenCalledTimes(1);
    expect(client.connectionState).toBe('stopped');
  });
});
