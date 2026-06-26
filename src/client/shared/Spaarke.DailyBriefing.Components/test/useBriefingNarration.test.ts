/**
 * Unit tests for `useBriefingNarration` — R4 task 033 / FR-15.
 *
 * Contract under test (FR-15 / AC-15):
 *   - On initial render with `loadingState !== 'loaded'`, no fetch fires.
 *   - Once `loadingState === 'loaded'` and `channels` contain data, `/narrate`
 *     is fetched and result populates `tldr` + `channelNarratives`.
 *   - When `channels` reference changes (consumer refetched after a successful
 *     mark-read / mark-all / dismiss action — see DailyBriefingApp.tsx
 *     Effect 2 → `useBriefingNotifications.refetch()`), the hook refetches.
 *     This is the AC-15 regression test: prior to task 033 the hook had a
 *     `hasFetchedRef` cache that suppressed this refetch.
 *   - Unrelated re-renders (same `channels` reference, same `loadingState`)
 *     do NOT trigger a fetch.
 *   - When `channels` has no successful data, `isUnavailable` is set and no
 *     network call fires.
 *   - In-flight fetches that complete after a newer fetch has superseded
 *     them MUST NOT overwrite the newer fetch's state (the `cancelled`
 *     closure flag in the cleanup function guards this).
 *
 * Mocks:
 *   - `briefingService.fetchBriefingNarration` is mocked so the hook's
 *     network boundary is fully controlled and tests are deterministic.
 *
 * Each test creates a fresh mock — NO shared state across tests.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useBriefingNarration } from '../src/hooks/useBriefingNarration';
import type { ChannelFetchResult, LoadingState, NotificationItem } from '../src/types/notifications';

// Mock the briefingService module so the hook's network boundary is isolated.
jest.mock('../src/services/briefingService', () => ({
  fetchBriefingNarration: jest.fn(),
}));

import { fetchBriefingNarration } from '../src/services/briefingService';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

function makeItem(id: string, title: string): NotificationItem {
  return {
    id,
    title,
    body: `Body for ${id}`,
    category: 'new-emails',
    priority: 'normal',
    actionUrl: `/n/${id}`,
    regardingName: 'Acme Matter',
    regardingEntityType: 'sprk_matter',
    regardingId: 'matter-1',
    isRead: false,
    isAiGenerated: false,
    createdOn: '2026-06-26T10:00:00Z',
    dueDate: null,
  };
}

function makeChannels(itemIds: string[]): ChannelFetchResult[] {
  return [
    {
      status: 'success',
      group: {
        meta: { category: 'new-emails', label: 'New Emails', iconName: 'Mail', order: 4 },
        items: itemIds.map(id => makeItem(id, `Item ${id}`)),
        unreadCount: itemIds.length,
      },
    },
  ];
}

function makeNoDataChannels(): ChannelFetchResult[] {
  // All channels in error state — `channels.some(ch => ch.status === 'success')`
  // returns false, so the hook reports `isUnavailable: 'No notification data
  // to narrate.'` without firing a network call.
  return [{ status: 'error', category: 'new-emails', error: 'boom' }];
}

function makeSuccessResult(tldrSummary: string) {
  return {
    status: 'success' as const,
    data: {
      tldr: {
        summary: tldrSummary,
        keyTakeaways: ['k1'],
        topAction: 'top action',
        categoryCount: 1,
        priorityItemCount: 1,
      },
      channelNarratives: [],
      generatedAtUtc: '2026-06-26T10:00:00Z',
    },
  };
}

describe('useBriefingNarration', () => {
  beforeEach(() => {
    (fetchBriefingNarration as jest.Mock).mockReset();
    jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  // -------------------------------------------------------------------------
  // Gating: loadingState
  // -------------------------------------------------------------------------

  it('does not fetch while loadingState is "idle"', () => {
    renderHook(() => useBriefingNarration(makeChannels(['a']), 'idle' as LoadingState));
    expect(fetchBriefingNarration).not.toHaveBeenCalled();
  });

  it('does not fetch while loadingState is "loading"', () => {
    renderHook(() => useBriefingNarration(makeChannels(['a']), 'loading' as LoadingState));
    expect(fetchBriefingNarration).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // Initial fetch
  // -------------------------------------------------------------------------

  it('fires exactly one fetch on initial render when channels have data and loadingState is "loaded"', async () => {
    (fetchBriefingNarration as jest.Mock).mockResolvedValue(makeSuccessResult('first'));

    const channels = makeChannels(['a']);
    const { result } = renderHook(() => useBriefingNarration(channels, 'loaded' as LoadingState));

    await waitFor(() => {
      expect(fetchBriefingNarration).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(result.current.tldr?.summary).toBe('first');
    });
  });

  it('sets isUnavailable and does NOT call fetch when no successful channels are present', () => {
    const { result } = renderHook(() => useBriefingNarration(makeNoDataChannels(), 'loaded' as LoadingState));
    expect(fetchBriefingNarration).not.toHaveBeenCalled();
    expect(result.current.isUnavailable).toBe(true);
    expect(result.current.unavailableReason).toBe('No notification data to narrate.');
  });

  // -------------------------------------------------------------------------
  // R4 task 033 / FR-15 / AC-15: refetch on channels change
  // -------------------------------------------------------------------------

  it('REFETCHES when the `channels` reference changes (FR-15 / AC-15)', async () => {
    (fetchBriefingNarration as jest.Mock)
      .mockResolvedValueOnce(makeSuccessResult('first'))
      .mockResolvedValueOnce(makeSuccessResult('second'));

    let channels = makeChannels(['a']);
    const { result, rerender } = renderHook(
      ({ ch, ls }: { ch: ChannelFetchResult[]; ls: LoadingState }) => useBriefingNarration(ch, ls),
      { initialProps: { ch: channels, ls: 'loaded' as LoadingState } }
    );

    await waitFor(() => {
      expect(fetchBriefingNarration).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(result.current.tldr?.summary).toBe('first');
    });

    // Simulate `useBriefingNotifications.refetch()` returning a new
    // `channels` reference (different item id; in production this happens
    // after a successful mark-read / Check / Remove / Keep action).
    channels = makeChannels(['b']);
    rerender({ ch: channels, ls: 'loaded' as LoadingState });

    await waitFor(() => {
      expect(fetchBriefingNarration).toHaveBeenCalledTimes(2);
    });
    await waitFor(() => {
      expect(result.current.tldr?.summary).toBe('second');
    });
  });

  it('does NOT refetch when the same `channels` reference is passed across re-renders', async () => {
    (fetchBriefingNarration as jest.Mock).mockResolvedValue(makeSuccessResult('first'));

    const channels = makeChannels(['a']);
    const { rerender } = renderHook(
      ({ ch, ls }: { ch: ChannelFetchResult[]; ls: LoadingState }) => useBriefingNarration(ch, ls),
      { initialProps: { ch: channels, ls: 'loaded' as LoadingState } }
    );

    await waitFor(() => {
      expect(fetchBriefingNarration).toHaveBeenCalledTimes(1);
    });

    // Three unrelated re-renders with the same `channels` reference.
    rerender({ ch: channels, ls: 'loaded' as LoadingState });
    rerender({ ch: channels, ls: 'loaded' as LoadingState });
    rerender({ ch: channels, ls: 'loaded' as LoadingState });

    // Still only one call — the dependency array reference is identical.
    expect(fetchBriefingNarration).toHaveBeenCalledTimes(1);
  });

  // -------------------------------------------------------------------------
  // Cancellation guard
  // -------------------------------------------------------------------------

  it('does not apply stale results when a newer fetch supersedes an in-flight one', async () => {
    // First call: never resolves (simulates in-flight when next channels arrive).
    // Second call: resolves with 'second'.
    let resolveFirst: (value: unknown) => void = () => {};
    (fetchBriefingNarration as jest.Mock)
      .mockImplementationOnce(
        () =>
          new Promise(res => {
            resolveFirst = res;
          })
      )
      .mockResolvedValueOnce(makeSuccessResult('second'));

    let channels = makeChannels(['a']);
    const { result, rerender } = renderHook(
      ({ ch, ls }: { ch: ChannelFetchResult[]; ls: LoadingState }) => useBriefingNarration(ch, ls),
      { initialProps: { ch: channels, ls: 'loaded' as LoadingState } }
    );

    await waitFor(() => {
      expect(fetchBriefingNarration).toHaveBeenCalledTimes(1);
    });

    // New channels arrive; the first fetch is now stale.
    channels = makeChannels(['b']);
    rerender({ ch: channels, ls: 'loaded' as LoadingState });

    await waitFor(() => {
      expect(fetchBriefingNarration).toHaveBeenCalledTimes(2);
    });
    await waitFor(() => {
      expect(result.current.tldr?.summary).toBe('second');
    });

    // Now resolve the FIRST (stale) promise. The `cancelled` closure flag
    // must prevent it from overwriting the 'second' result.
    await act(async () => {
      resolveFirst(makeSuccessResult('first-stale'));
      // Let microtasks drain.
      await Promise.resolve();
    });

    expect(result.current.tldr?.summary).toBe('second');
  });

  // -------------------------------------------------------------------------
  // Fallback paths
  // -------------------------------------------------------------------------

  it('falls back to template bullets when service returns "unavailable"', async () => {
    (fetchBriefingNarration as jest.Mock).mockResolvedValue({
      status: 'unavailable',
      reason: 'AI is offline',
    });

    const channels = makeChannels(['a']);
    const { result } = renderHook(() => useBriefingNarration(channels, 'loaded' as LoadingState));

    await waitFor(() => {
      expect(result.current.isUnavailable).toBe(true);
    });
    expect(result.current.unavailableReason).toBe('AI is offline');
    expect(result.current.channelNarratives.length).toBeGreaterThan(0);
  });

  it('falls back to template bullets when service returns "error"', async () => {
    (fetchBriefingNarration as jest.Mock).mockResolvedValue({
      status: 'error',
      message: 'boom',
    });

    const channels = makeChannels(['a']);
    const { result } = renderHook(() => useBriefingNarration(channels, 'loaded' as LoadingState));

    await waitFor(() => {
      expect(result.current.error).toBe('boom');
    });
    expect(result.current.channelNarratives.length).toBeGreaterThan(0);
  });
});
