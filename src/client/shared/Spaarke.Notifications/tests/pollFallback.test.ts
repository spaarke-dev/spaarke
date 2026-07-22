// ts-jest does not transform node_modules (Spaarke.Auth's dist/ is compiled ESM) — mock
// authenticatedFetch directly rather than requireActual-ing the real package.
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: jest.fn(),
}));

// eslint-disable-next-line @typescript-eslint/no-var-requires
const { authenticatedFetch } = jest.requireMock('@spaarke/auth') as { authenticatedFetch: jest.Mock };

import { startPollFallback } from '../src/pollFallback';

describe('startPollFallback', () => {
  beforeEach(() => {
    authenticatedFetch.mockReset();
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('fires an immediate tick against GET /notifications/pending', async () => {
    authenticatedFetch.mockResolvedValue({ json: async () => [] });

    const handle = startPollFallback({ onEvent: jest.fn() });
    await flushMicrotasks();

    expect(authenticatedFetch).toHaveBeenCalledWith('/notifications/pending', { method: 'GET' });
    handle.stop();
  });

  it('delivers each pending item as a poll-sourced NotificationEvent', async () => {
    authenticatedFetch.mockResolvedValue({
      json: async () => [
        { outboxRowId: 'row-1', kind: 'communication-arrived', envelope: { communicationId: 'c1' } },
      ],
    });
    const onEvent = jest.fn();

    const handle = startPollFallback({ onEvent });
    await flushMicrotasks();

    expect(onEvent).toHaveBeenCalledWith({
      outboxRowId: 'row-1',
      kind: 'communication-arrived',
      envelope: { communicationId: 'c1' },
      source: 'poll',
    });
    handle.stop();
  });

  it('handles a wrapped { items: [...] } response shape defensively', async () => {
    authenticatedFetch.mockResolvedValue({
      json: async () => ({ items: [{ outboxRowId: 'row-2', kind: 'suggestion', envelope: {} }] }),
    });
    const onEvent = jest.fn();

    const handle = startPollFallback({ onEvent });
    await flushMicrotasks();

    expect(onEvent).toHaveBeenCalledTimes(1);
    handle.stop();
  });

  it('schedules the next tick at intervalMs after a successful poll', async () => {
    authenticatedFetch.mockResolvedValue({ json: async () => [] });

    const handle = startPollFallback({ intervalMs: 10_000, onEvent: jest.fn() });
    await flushMicrotasks();
    authenticatedFetch.mockClear();

    jest.advanceTimersByTime(9_999);
    expect(authenticatedFetch).not.toHaveBeenCalled();

    jest.advanceTimersByTime(1);
    await flushMicrotasks();
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);

    handle.stop();
  });

  it('backs off exponentially on repeated failures, capped at maxBackoffMs', async () => {
    authenticatedFetch.mockRejectedValue(new Error('network down'));
    const onError = jest.fn();

    const handle = startPollFallback({ intervalMs: 1_000, maxBackoffMs: 3_000, onEvent: jest.fn(), onError });
    await flushMicrotasks();
    expect(onError).toHaveBeenCalledTimes(1);

    // First failure -> next delay = 2_000
    jest.advanceTimersByTime(1_999);
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    jest.advanceTimersByTime(1);
    await flushMicrotasks();
    expect(authenticatedFetch).toHaveBeenCalledTimes(2);
    expect(onError).toHaveBeenCalledTimes(2);

    // Second failure -> next delay = min(4000, 3000) = 3_000 (capped)
    jest.advanceTimersByTime(2_999);
    expect(authenticatedFetch).toHaveBeenCalledTimes(2);
    jest.advanceTimersByTime(1);
    await flushMicrotasks();
    expect(authenticatedFetch).toHaveBeenCalledTimes(3);

    handle.stop();
  });

  it('isolates a throwing onEvent handler: fetch still counts as success, no backoff, no onError', async () => {
    authenticatedFetch.mockResolvedValue({
      json: async () => [{ outboxRowId: 'row-1', kind: 'suggestion', envelope: {} }],
    });
    const onEvent = jest.fn(() => {
      throw new Error('handler bug');
    });
    const onError = jest.fn();
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    const handle = startPollFallback({ intervalMs: 10_000, onEvent, onError });
    await flushMicrotasks();

    // The handler threw, but the FETCH succeeded — must not be reported as a poll failure.
    expect(onError).not.toHaveBeenCalled();
    expect(errorSpy).toHaveBeenCalledTimes(1);

    // Next tick scheduled at the base interval (not backed off), proving success bookkeeping ran.
    authenticatedFetch.mockClear();
    jest.advanceTimersByTime(9_999);
    expect(authenticatedFetch).not.toHaveBeenCalled();
    jest.advanceTimersByTime(1);
    await flushMicrotasks();
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);

    errorSpy.mockRestore();
    handle.stop();
  });

  it('stop() prevents any further ticks', async () => {
    authenticatedFetch.mockResolvedValue({ json: async () => [] });

    const handle = startPollFallback({ intervalMs: 1_000, onEvent: jest.fn() });
    await flushMicrotasks();
    authenticatedFetch.mockClear();

    handle.stop();
    expect(handle.isRunning).toBe(false);

    jest.advanceTimersByTime(60_000);
    await flushMicrotasks();
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });
});

/** Lets pending promise microtasks (the tick()'s awaited fetch/json) resolve under fake timers. */
async function flushMicrotasks(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
}
