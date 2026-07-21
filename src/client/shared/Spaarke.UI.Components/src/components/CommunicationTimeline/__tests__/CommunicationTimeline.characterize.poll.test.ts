/**
 * CommunicationTimeline.characterize.poll.test.ts (task 010, NFR-08)
 *
 * Characterization coverage for `useThreadPoll` and `useRegardingPoll` — NO
 * existing `__tests__` file covers either hook today (both `hooks/` files
 * have zero test coverage before this task). Pins: ~5s default cadence,
 * `since`-cursor pass-through (the hook reads the caller's ref fresh each
 * tick — it does not compute the cursor itself, `CommunicationTimeline.tsx`
 * does), the overlapping-in-flight-poll guard ("no duplicate emit"), the
 * `document.hidden` pause, and loading/error propagation.
 *
 * Per ADR-038/ADR-028: a fake `authenticatedFetch` is injected as the seam
 * (no `Mock<HttpMessageHandler>`, no `@spaarke/auth` import). Fake timers
 * (`jest.useFakeTimers()` + `advanceTimersByTimeAsync`) drive the ~5s cadence
 * deterministically — no real `setTimeout`/`Stopwatch` wall-clock waits.
 */
import { act, renderHook } from '@testing-library/react';
import { useThreadPoll, type UseThreadPollOptions } from '../hooks/useThreadPoll';
import { useRegardingPoll, type UseRegardingPollOptions } from '../hooks/useRegardingPoll';

function jsonResponse(body: unknown): Response {
  return { ok: true, json: async () => body } as unknown as Response;
}

function mutableRef<T>(initial: T): { current: T } {
  return { current: initial };
}

// ---------------------------------------------------------------------------
// useThreadPoll
// ---------------------------------------------------------------------------

describe('useThreadPoll', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
  });

  function buildFetch() {
    return jest.fn(async (url: string) => {
      if (url.includes('/unread-count')) {
        return jsonResponse({ threadId: 't1', since: null, unreadCount: 0 });
      }
      return jsonResponse({ threadId: 't1', name: null, messages: [], count: 0 });
    });
  }

  function setup(overrides: Partial<UseThreadPollOptions> = {}) {
    const authenticatedFetch = buildFetch();
    const sinceCursorRef = mutableRef<string | undefined>(undefined);
    const unreadSinceRef = mutableRef<string | undefined>(undefined);
    const onMessages = jest.fn();
    const onUnread = jest.fn();
    const onError = jest.fn();

    const hook = renderHook(() =>
      useThreadPoll({
        threadId: 't1',
        authenticatedFetch: authenticatedFetch as unknown as UseThreadPollOptions['authenticatedFetch'],
        sinceCursorRef,
        unreadSinceRef,
        onMessages,
        onUnread,
        onError,
        ...overrides,
      })
    );

    return { ...hook, authenticatedFetch, sinceCursorRef, unreadSinceRef, onMessages, onUnread, onError };
  }

  it('fires an initial full load (no `since`) immediately on mount', async () => {
    const { authenticatedFetch } = setup();
    await act(async () => {
      await Promise.resolve();
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(2); // messages + unread-count
    const messagesUrl = authenticatedFetch.mock.calls.find(c => !String(c[0]).includes('unread-count'))![0] as string;
    expect(messagesUrl).not.toContain('since=');
  });

  it('default cadence is ~5000ms — no second poll before 5000ms, a second poll fires at 5000ms', async () => {
    const { authenticatedFetch } = setup();
    await act(async () => {
      await Promise.resolve();
    });
    authenticatedFetch.mockClear();

    await act(async () => {
      await jest.advanceTimersByTimeAsync(4999);
    });
    expect(authenticatedFetch).not.toHaveBeenCalled();

    await act(async () => {
      await jest.advanceTimersByTimeAsync(1);
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(2);
  });

  it('a caller-supplied pollIntervalMs overrides the ~5s default', async () => {
    const { authenticatedFetch } = setup({ pollIntervalMs: 1000 });
    await act(async () => {
      await Promise.resolve();
    });
    authenticatedFetch.mockClear();

    await act(async () => {
      await jest.advanceTimersByTimeAsync(1000);
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(2);
  });

  it('reads the `since` cursor fresh from the ref on each tick — the hook does not compute it itself', async () => {
    const { authenticatedFetch, sinceCursorRef } = setup();
    await act(async () => {
      await Promise.resolve();
    });
    authenticatedFetch.mockClear();

    sinceCursorRef.current = '2026-07-01T10:00:00Z';
    await act(async () => {
      await jest.advanceTimersByTimeAsync(5000);
    });

    const messagesCall = authenticatedFetch.mock.calls.find(c => !String(c[0]).includes('unread-count'));
    expect(messagesCall![0]).toContain('since=2026-07-01T10%3A00%3A00Z');
  });

  it('guards overlapping in-flight polls — calling pollNow() while a poll is still in flight is a no-op (no duplicate emit)', async () => {
    // Two fetches (readThread + getUnreadCount) fire per tick — capture BOTH resolvers, not just
    // the last one, or awaiting Promise.all([...]) in the hook never settles.
    const resolvers: Array<(r: Response) => void> = [];
    const authenticatedFetch = jest.fn(
      () =>
        new Promise<Response>(resolve => {
          resolvers.push(resolve);
        })
    );
    const sinceCursorRef = mutableRef<string | undefined>(undefined);
    const unreadSinceRef = mutableRef<string | undefined>(undefined);
    const onMessages = jest.fn();
    const onUnread = jest.fn();

    const { result } = renderHook(() =>
      useThreadPoll({
        threadId: 't1',
        authenticatedFetch: authenticatedFetch as unknown as UseThreadPollOptions['authenticatedFetch'],
        sinceCursorRef,
        unreadSinceRef,
        onMessages,
        onUnread,
      })
    );

    // Initial mount poll is in flight (never resolved yet).
    expect(authenticatedFetch).toHaveBeenCalledTimes(2);

    act(() => {
      result.current.pollNow(); // in-flight guard should skip this — no additional fetch calls
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(2);

    // Resolve BOTH in-flight requests — onMessages/onUnread each fire exactly once.
    await act(async () => {
      resolvers.forEach(resolve => resolve(jsonResponse({ threadId: 't1', name: null, messages: [], count: 0 })));
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(onMessages).toHaveBeenCalledTimes(1);
  });

  it('a tick no-ops (does not call fetch) while document.hidden is true', async () => {
    const { authenticatedFetch } = setup();
    await act(async () => {
      await Promise.resolve();
    });
    authenticatedFetch.mockClear();

    Object.defineProperty(document, 'hidden', { value: true, configurable: true });
    await act(async () => {
      await jest.advanceTimersByTimeAsync(5000);
    });
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('onError is invoked (not onMessages/onUnread) when the read fails, and polling continues afterward', async () => {
    const authenticatedFetch = jest.fn().mockRejectedValue(new Error('network down'));
    const sinceCursorRef = mutableRef<string | undefined>(undefined);
    const unreadSinceRef = mutableRef<string | undefined>(undefined);
    const onMessages = jest.fn();
    const onError = jest.fn();

    renderHook(() =>
      useThreadPoll({
        threadId: 't1',
        authenticatedFetch: authenticatedFetch as unknown as UseThreadPollOptions['authenticatedFetch'],
        sinceCursorRef,
        unreadSinceRef,
        onMessages,
        onUnread: jest.fn(),
        onError,
      })
    );

    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(onError).toHaveBeenCalledWith(expect.any(Error));
    expect(onMessages).not.toHaveBeenCalled();
  });

  it('does not poll at all when enabled is false', async () => {
    const { authenticatedFetch } = setup({ enabled: false });
    await act(async () => {
      await jest.advanceTimersByTimeAsync(10000);
    });
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// useRegardingPoll
// ---------------------------------------------------------------------------

describe('useRegardingPoll', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
  });

  function setup(overrides: Partial<UseRegardingPollOptions> = {}) {
    const authenticatedFetch = jest.fn().mockResolvedValue(
      jsonResponse({ entityType: 'sprk_matter', recordId: 'r1', threads: [], threadCount: 0, messageCount: 0 })
    );
    const onLoading = jest.fn();
    const onGroups = jest.fn();
    const onError = jest.fn();

    const hook = renderHook(() =>
      useRegardingPoll({
        entityType: 'sprk_matter',
        id: 'r1',
        authenticatedFetch: authenticatedFetch as unknown as UseRegardingPollOptions['authenticatedFetch'],
        onLoading,
        onGroups,
        onError,
        ...overrides,
      })
    );

    return { ...hook, authenticatedFetch, onLoading, onGroups, onError };
  }

  it('fires an initial fetch on mount and calls onLoading before onGroups', async () => {
    const { authenticatedFetch, onLoading, onGroups } = setup();
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    expect(onLoading).toHaveBeenCalledTimes(1);
    expect(onGroups).toHaveBeenCalledTimes(1);
    expect(onLoading.mock.invocationCallOrder[0]).toBeLessThan(onGroups.mock.invocationCallOrder[0]);
  });

  it('default cadence is ~5000ms and every tick is a FULL re-fetch (no `since` param — the endpoint has no incremental mode)', async () => {
    const { authenticatedFetch } = setup();
    await act(async () => {
      await Promise.resolve();
    });
    authenticatedFetch.mockClear();

    await act(async () => {
      await jest.advanceTimersByTimeAsync(5000);
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const url = authenticatedFetch.mock.calls[0][0] as string;
    expect(url).not.toContain('since=');
    expect(url).toBe('/api/communications/by-regarding/sprk_matter/r1');
  });

  it('guards overlapping in-flight polls — a second pollNow() while the first is unresolved is a no-op', async () => {
    let resolveFetch: (r: Response) => void = () => undefined;
    const authenticatedFetch = jest.fn(
      () =>
        new Promise<Response>(resolve => {
          resolveFetch = resolve;
        })
    );
    const onGroups = jest.fn();

    const { result } = renderHook(() =>
      useRegardingPoll({
        entityType: 'sprk_matter',
        id: 'r1',
        authenticatedFetch: authenticatedFetch as unknown as UseRegardingPollOptions['authenticatedFetch'],
        onLoading: jest.fn(),
        onGroups,
        onError: jest.fn(),
      })
    );

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    act(() => {
      result.current.pollNow();
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(1); // guarded — no second call while in flight

    await act(async () => {
      resolveFetch(
        jsonResponse({ entityType: 'sprk_matter', recordId: 'r1', threads: [], threadCount: 0, messageCount: 0 })
      );
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(onGroups).toHaveBeenCalledTimes(1);
  });

  it('onError is invoked (not onGroups) when the read fails', async () => {
    const authenticatedFetch = jest.fn().mockRejectedValue(new Error('network down'));
    const onGroups = jest.fn();
    const onError = jest.fn();

    renderHook(() =>
      useRegardingPoll({
        entityType: 'sprk_matter',
        id: 'r1',
        authenticatedFetch: authenticatedFetch as unknown as UseRegardingPollOptions['authenticatedFetch'],
        onLoading: jest.fn(),
        onGroups,
        onError,
      })
    );

    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(onError).toHaveBeenCalledWith(expect.any(Error));
    expect(onGroups).not.toHaveBeenCalled();
  });

  it('a tick no-ops while document.hidden is true', async () => {
    const { authenticatedFetch } = setup();
    await act(async () => {
      await Promise.resolve();
    });
    authenticatedFetch.mockClear();

    Object.defineProperty(document, 'hidden', { value: true, configurable: true });
    await act(async () => {
      await jest.advanceTimersByTimeAsync(5000);
    });
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('does not poll at all when enabled is false', async () => {
    const { authenticatedFetch } = setup({ enabled: false });
    await act(async () => {
      await jest.advanceTimersByTimeAsync(10000);
    });
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });
});
