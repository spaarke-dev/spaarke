/**
 * Unit tests for useLinkedTodosForCommunication
 *
 * Covers smart-todo-decoupling-r3 task 071:
 * - 0 / 1 / N count surfaces correctly
 * - In-memory cache hits across hook re-mounts within the same session
 * - Loading state during the initial fetch
 * - Error state when the BFF call fails
 * - Inert behavior when communicationId is undefined / empty
 * - refresh() forces a re-fetch and updates the cache
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import {
  useLinkedTodosForCommunication,
  __clearLinkedTodosCache,
  __linkedTodosCache,
  type LinkedTodosResponse,
} from '../useLinkedTodosForCommunication';
import { apiClient, ApiClientError } from '@shared/services';

// Mock the apiClient module.
jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

// Typed reference to the mocked apiClient.get
const mockGet = apiClient.get as jest.Mock;

describe('useLinkedTodosForCommunication', () => {
  beforeEach(() => {
    __clearLinkedTodosCache();
    mockGet.mockReset();
  });

  afterEach(() => {
    __clearLinkedTodosCache();
  });

  describe('inert behavior', () => {
    it('returns count=0 and never calls the API when communicationId is undefined', async () => {
      const { result } = renderHook(() => useLinkedTodosForCommunication(undefined));

      expect(result.current.count).toBe(0);
      expect(result.current.todos).toEqual([]);
      expect(result.current.isLoading).toBe(false);
      expect(result.current.error).toBeNull();
      expect(result.current.fromCache).toBe(false);

      // Give effects a chance to run.
      await act(async () => {
        await Promise.resolve();
      });

      expect(mockGet).not.toHaveBeenCalled();
    });

    it('treats empty/whitespace string as undefined', async () => {
      const { result } = renderHook(() => useLinkedTodosForCommunication('   '));

      expect(result.current.count).toBe(0);
      await act(async () => {
        await Promise.resolve();
      });
      expect(mockGet).not.toHaveBeenCalled();
    });
  });

  describe('successful fetch', () => {
    it('renders count=0 when the API returns an empty list (no banner case)', async () => {
      const response: LinkedTodosResponse = { count: 0, todos: [] };
      mockGet.mockResolvedValueOnce(response);

      const { result } = renderHook(() =>
        useLinkedTodosForCommunication('00000000-0000-0000-0000-000000000001')
      );

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.count).toBe(0);
      expect(result.current.todos).toEqual([]);
      expect(result.current.error).toBeNull();
    });

    it('renders count=1 when one todo is linked', async () => {
      const response: LinkedTodosResponse = {
        count: 1,
        todos: [
          { sprk_todoid: 'todo-1', sprk_name: 'Follow up on email', statecode: 0 },
        ],
      };
      mockGet.mockResolvedValueOnce(response);

      const { result } = renderHook(() =>
        useLinkedTodosForCommunication('00000000-0000-0000-0000-000000000002')
      );

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.count).toBe(1);
      expect(result.current.todos).toHaveLength(1);
      expect(result.current.todos[0]?.sprk_name).toBe('Follow up on email');
    });

    it('renders count=N when multiple todos are linked', async () => {
      const response: LinkedTodosResponse = {
        count: 5,
        todos: Array.from({ length: 5 }).map((_, i) => ({
          sprk_todoid: `todo-${i}`,
          sprk_name: `Task ${i}`,
          statecode: 0,
        })),
      };
      mockGet.mockResolvedValueOnce(response);

      const { result } = renderHook(() =>
        useLinkedTodosForCommunication('00000000-0000-0000-0000-000000000003')
      );

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.count).toBe(5);
      expect(result.current.todos).toHaveLength(5);
    });

    it('calls the BFF with the correct linked-todos endpoint', async () => {
      mockGet.mockResolvedValueOnce({ count: 0, todos: [] });

      renderHook(() =>
        useLinkedTodosForCommunication('00000000-0000-0000-0000-000000000004')
      );

      await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(1));
      expect(mockGet).toHaveBeenCalledWith(
        '/api/office/communications/00000000-0000-0000-0000-000000000004/linked-todos'
      );
    });
  });

  describe('cache behavior (NFR-09)', () => {
    it('hits cache on re-mount within the same session — no second API call', async () => {
      const response: LinkedTodosResponse = {
        count: 2,
        todos: [
          { sprk_todoid: 'todo-1', sprk_name: 'First', statecode: 0 },
          { sprk_todoid: 'todo-2', sprk_name: 'Second', statecode: 0 },
        ],
      };
      mockGet.mockResolvedValueOnce(response);

      const id = '00000000-0000-0000-0000-aaaaaaaaaaaa';

      // First render — populates cache.
      const { result, unmount } = renderHook(() => useLinkedTodosForCommunication(id));
      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.count).toBe(2);
      expect(mockGet).toHaveBeenCalledTimes(1);
      unmount();

      // Second render — should hit cache without another API call.
      const { result: result2 } = renderHook(() => useLinkedTodosForCommunication(id));

      // Cache-hit path resolves synchronously: count is correct immediately.
      expect(result2.current.count).toBe(2);
      expect(result2.current.fromCache).toBe(true);
      expect(result2.current.isLoading).toBe(false);
      expect(mockGet).toHaveBeenCalledTimes(1); // not incremented
    });

    it('switching to a different communicationId triggers a new fetch', async () => {
      const idA = '00000000-0000-0000-0000-000000000a01';
      const idB = '00000000-0000-0000-0000-000000000a02';

      mockGet.mockResolvedValueOnce({ count: 1, todos: [{ sprk_todoid: 't', sprk_name: 'A', statecode: 0 }] });
      mockGet.mockResolvedValueOnce({ count: 3, todos: [] });

      const { result, rerender } = renderHook(
        ({ id }: { id: string | undefined }) => useLinkedTodosForCommunication(id),
        { initialProps: { id: idA as string | undefined } }
      );

      await waitFor(() => expect(result.current.count).toBe(1));

      rerender({ id: idB });

      // Wait for both the new API call AND the state update to flush.
      await waitFor(() => {
        expect(mockGet).toHaveBeenCalledTimes(2);
        expect(result.current.count).toBe(3);
      });
    });

    it('refresh() forces re-fetch and updates the cache', async () => {
      const id = '00000000-0000-0000-0000-000000000b01';

      mockGet.mockResolvedValueOnce({ count: 1, todos: [{ sprk_todoid: 't1', sprk_name: 'Old', statecode: 0 }] });
      mockGet.mockResolvedValueOnce({ count: 2, todos: [
        { sprk_todoid: 't1', sprk_name: 'Old', statecode: 0 },
        { sprk_todoid: 't2', sprk_name: 'New', statecode: 0 },
      ] });

      const { result } = renderHook(() => useLinkedTodosForCommunication(id));
      await waitFor(() => expect(result.current.count).toBe(1));
      expect(mockGet).toHaveBeenCalledTimes(1);

      await act(async () => {
        await result.current.refresh();
      });

      expect(result.current.count).toBe(2);
      expect(mockGet).toHaveBeenCalledTimes(2);
      expect(__linkedTodosCache.get(id)?.count).toBe(2);
    });

    it('resets banner state when communicationId becomes undefined (email switched to unsaved)', async () => {
      const id = '00000000-0000-0000-0000-000000000c01';
      mockGet.mockResolvedValueOnce({ count: 4, todos: [] });

      const { result, rerender } = renderHook(
        ({ commId }: { commId: string | undefined }) => useLinkedTodosForCommunication(commId),
        { initialProps: { commId: id as string | undefined } }
      );

      await waitFor(() => expect(result.current.count).toBe(4));

      rerender({ commId: undefined });

      // Effect runs synchronously on the next render — verify no leak.
      expect(result.current.count).toBe(0);
      expect(result.current.todos).toEqual([]);
      expect(result.current.isLoading).toBe(false);
    });
  });

  describe('error handling', () => {
    it('surfaces the error message and does not cache the failure', async () => {
      const apiError = new ApiClientError({
        type: 'about:blank',
        title: 'Server error',
        status: 500,
        detail: 'Dataverse unavailable',
      });
      mockGet.mockRejectedValueOnce(apiError);

      const id = '00000000-0000-0000-0000-000000000d01';
      const { result } = renderHook(() => useLinkedTodosForCommunication(id));

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.error).toBe('Dataverse unavailable');
      expect(result.current.count).toBe(0);
      expect(__linkedTodosCache.has(id)).toBe(false);
    });

    it('handles non-ApiClientError errors gracefully', async () => {
      mockGet.mockRejectedValueOnce(new Error('Network down'));

      const id = '00000000-0000-0000-0000-000000000d02';
      const { result } = renderHook(() => useLinkedTodosForCommunication(id));

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.error).toBe('Network down');
      expect(result.current.count).toBe(0);
    });

    it('handles non-Error throws with a generic message', async () => {
      mockGet.mockRejectedValueOnce('something blew up');

      const id = '00000000-0000-0000-0000-000000000d03';
      const { result } = renderHook(() => useLinkedTodosForCommunication(id));

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.error).toBe('Failed to load linked to-dos');
    });
  });

  describe('loading state', () => {
    it('isLoading is true while the fetch is in flight, false after resolution', async () => {
      let resolveFetch: (v: LinkedTodosResponse) => void = () => undefined;
      mockGet.mockReturnValueOnce(
        new Promise<LinkedTodosResponse>(resolve => {
          resolveFetch = resolve;
        })
      );

      const { result } = renderHook(() =>
        useLinkedTodosForCommunication('00000000-0000-0000-0000-000000000e01')
      );

      // After initial effect runs, isLoading should flip to true.
      await waitFor(() => expect(result.current.isLoading).toBe(true));
      expect(result.current.count).toBe(0);

      await act(async () => {
        resolveFetch({ count: 1, todos: [{ sprk_todoid: 't', sprk_name: 'X', statecode: 0 }] });
        await Promise.resolve();
      });

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.count).toBe(1);
    });
  });

  describe('defensive parsing', () => {
    it('falls back to todos.length when count is missing on the response', async () => {
      mockGet.mockResolvedValueOnce({
        // No count field — should derive from todos.length.
        todos: [
          { sprk_todoid: 't1', sprk_name: 'A', statecode: 0 },
          { sprk_todoid: 't2', sprk_name: 'B', statecode: 0 },
        ],
      });

      const { result } = renderHook(() =>
        useLinkedTodosForCommunication('00000000-0000-0000-0000-000000000f01')
      );

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.count).toBe(2);
    });

    it('coerces missing todos array to empty array', async () => {
      mockGet.mockResolvedValueOnce({ count: 7 });

      const { result } = renderHook(() =>
        useLinkedTodosForCommunication('00000000-0000-0000-0000-000000000f02')
      );

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(result.current.count).toBe(7);
      expect(result.current.todos).toEqual([]);
    });
  });
});
