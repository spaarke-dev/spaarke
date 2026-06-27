/**
 * Unit tests for `useBriefingNotifications` — R2 task 019 / NFR-05.
 *
 * Contract under test (FR-06):
 *   - Returns `channels`, `totalUnreadCount`, `loadingState`, `error`, `refetch`.
 *   - Stays idle while webApi is null (welcome-screen / left-nav timing).
 *   - Triggers `fetchAndGroupNotifications` once webApi becomes available.
 *   - Computes `totalUnreadCount` from successful channels only.
 *   - `refetch()` re-runs the effect.
 *   - Surfaces errors when `fetchAndGroupNotifications` throws.
 *
 * Each test creates fresh mocks — NO shared state across tests (per spec
 * constraint: "Each hook test uses independent mocks").
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useBriefingNotifications } from '../src/hooks/useBriefingNotifications';
import type { ChannelFetchResult, IWebApi } from '../src/types/notifications';

// Mock the notificationService module.
jest.mock('../src/services/notificationService', () => ({
  fetchAndGroupNotifications: jest.fn(),
  // Other exports kept untouched (only fetchAndGroupNotifications is used here).
}));

import { fetchAndGroupNotifications } from '../src/services/notificationService';

// Helper: build a fresh, independent webApi mock per test.
function makeWebApi(): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn(),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

describe('useBriefingNotifications', () => {
  beforeEach(() => {
    (fetchAndGroupNotifications as jest.Mock).mockReset();
  });

  it('stays idle when webApi is null', () => {
    const { result } = renderHook(() => useBriefingNotifications(null));
    expect(result.current.loadingState).toBe('idle');
    expect(result.current.channels).toEqual([]);
    expect(result.current.totalUnreadCount).toBe(0);
    expect(fetchAndGroupNotifications).not.toHaveBeenCalled();
  });

  it('fetches and groups notifications when webApi is provided', async () => {
    const webApi = makeWebApi();
    const fakeChannels: ChannelFetchResult[] = [
      {
        status: 'success',
        group: {
          meta: { category: 'new-emails', label: 'New Emails', iconName: 'Mail', order: 4 },
          items: [],
          unreadCount: 5,
        },
      },
      {
        status: 'success',
        group: {
          meta: { category: 'tasks-overdue', label: 'Overdue Tasks', iconName: 'Warning', order: 1 },
          items: [],
          unreadCount: 3,
        },
      },
    ];
    (fetchAndGroupNotifications as jest.Mock).mockResolvedValue(fakeChannels);

    const { result } = renderHook(() => useBriefingNotifications(webApi));

    await waitFor(() => {
      expect(result.current.loadingState).toBe('loaded');
    });

    expect(result.current.channels).toEqual(fakeChannels);
    expect(result.current.totalUnreadCount).toBe(8); // 5 + 3
    expect(result.current.error).toBeUndefined();
  });

  it('ignores per-channel errors when computing totalUnreadCount', async () => {
    const webApi = makeWebApi();
    const fakeChannels: ChannelFetchResult[] = [
      {
        status: 'success',
        group: {
          meta: { category: 'new-emails', label: 'New Emails', iconName: 'Mail', order: 4 },
          items: [],
          unreadCount: 7,
        },
      },
      { status: 'error', category: 'matter-activity', error: 'channel timeout' },
    ];
    (fetchAndGroupNotifications as jest.Mock).mockResolvedValue(fakeChannels);

    const { result } = renderHook(() => useBriefingNotifications(webApi));

    await waitFor(() => {
      expect(result.current.loadingState).toBe('loaded');
    });

    expect(result.current.totalUnreadCount).toBe(7);
    expect(result.current.channels).toHaveLength(2);
  });

  it('surfaces error state when fetch rejects', async () => {
    const webApi = makeWebApi();
    (fetchAndGroupNotifications as jest.Mock).mockRejectedValue(new Error('Dataverse exploded'));

    const { result } = renderHook(() => useBriefingNotifications(webApi));

    await waitFor(() => {
      expect(result.current.loadingState).toBe('error');
    });

    expect(result.current.error).toBe('Dataverse exploded');
    expect(result.current.channels).toEqual([]);
  });

  it('refetch() triggers a second call to fetchAndGroupNotifications', async () => {
    const webApi = makeWebApi();
    (fetchAndGroupNotifications as jest.Mock).mockResolvedValue([]);

    const { result } = renderHook(() => useBriefingNotifications(webApi));

    await waitFor(() => {
      expect(result.current.loadingState).toBe('loaded');
    });
    expect(fetchAndGroupNotifications).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.refetch();
    });

    await waitFor(() => {
      expect(fetchAndGroupNotifications).toHaveBeenCalledTimes(2);
    });
  });
});
