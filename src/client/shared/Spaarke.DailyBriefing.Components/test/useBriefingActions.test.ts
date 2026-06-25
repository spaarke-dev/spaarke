/**
 * Unit tests for `useBriefingActions`.
 *
 * Two contracts under test:
 *
 * (a) R2 task 019 / NFR-05 — existing surface (FR-06):
 *   - Exposes `markAsRead`, `markAllAsRead`, `dismissAll`, `refresh`.
 *   - Actions become no-ops returning `false` when webApi is null.
 *   - Successful mutations bump the `refresh` counter monotonically.
 *   - Failed mutations do NOT bump the counter and return `false`.
 *   - `dismissAll` delegates to `markAllAsRead` per spec note (the
 *     `toasttype = 200000000` model — "dismissed" === "mark-all-read" until
 *     per-item dismiss-without-read is added).
 *
 * (b) R3 task 030 — three new per-item handlers (FR-4 / FR-5 / FR-6):
 *   - `markChecked(id, options?)` — `markBriefingChecked` orchestration.
 *   - `markRemoved(id, options?)` — `markBriefingRemoved` orchestration.
 *   - `extendTtl(id, currentTtl, options?)` — `extendBriefingTtl` orchestration.
 *   For each handler, the success path MUST:
 *     1. Fire `onOptimistic(id)` once, before the service call.
 *     2. Invoke the service function with the correct args.
 *     3. Fire `onSuccess(payload)` once (payload is the service return).
 *     4. Bump `refresh` by 1; return `true`.
 *   For each handler, the failure path MUST:
 *     1. Fire `onOptimistic(id)` once, before the service call.
 *     2. Fire `onRevert(id)` once, BEFORE `onError`.
 *     3. Fire `onError(err)` once with the service-error detail.
 *     4. NOT bump `refresh`; return `false`.
 *
 * Each test creates fresh mocks — NO shared state across tests (per spec
 * constraint).
 *
 * Naming note (R3 task 030):
 *   The service module no longer exports the transitional aliases
 *   `markNotificationRead` / `markAllNotificationsRead` (task 020 added them;
 *   task 030 removed them after the hook rewired to canonical names). Tests
 *   below mock the canonical names directly.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useBriefingActions } from '../src/hooks/useBriefingActions';
import type { IWebApi } from '../src/types/notifications';

// Mock the notificationService functions used by useBriefingActions.
jest.mock('../src/services/notificationService', () => ({
  markBriefingChecked: jest.fn(),
  markAllBriefingsChecked: jest.fn(),
  markBriefingRemoved: jest.fn(),
  extendBriefingTtl: jest.fn(),
}));

import {
  markBriefingChecked,
  markAllBriefingsChecked,
  markBriefingRemoved,
  extendBriefingTtl,
} from '../src/services/notificationService';

function makeWebApi(): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn(),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

describe('useBriefingActions', () => {
  beforeEach(() => {
    (markBriefingChecked as jest.Mock).mockReset();
    (markAllBriefingsChecked as jest.Mock).mockReset();
    (markBriefingRemoved as jest.Mock).mockReset();
    (extendBriefingTtl as jest.Mock).mockReset();
    // Silence console.error noise from the hook's failure logging path
    jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  // -------------------------------------------------------------------------
  // Existing FR-06 surface (R2 task 019 contract — preserved unchanged)
  // -------------------------------------------------------------------------

  it('returns false from all actions when webApi is null', async () => {
    const { result } = renderHook(() => useBriefingActions(null));
    expect(result.current.refresh).toBe(0);

    let r1: boolean | undefined;
    let r2: boolean | undefined;
    let r3: boolean | undefined;
    await act(async () => {
      r1 = await result.current.markAsRead('n-1');
      r2 = await result.current.markAllAsRead();
      r3 = await result.current.dismissAll();
    });

    expect(r1).toBe(false);
    expect(r2).toBe(false);
    expect(r3).toBe(false);
    expect(markBriefingChecked).not.toHaveBeenCalled();
    expect(markAllBriefingsChecked).not.toHaveBeenCalled();
    expect(result.current.refresh).toBe(0);
  });

  it('markAsRead succeeds and bumps refresh', async () => {
    const webApi = makeWebApi();
    (markBriefingChecked as jest.Mock).mockResolvedValue({
      success: true,
      data: undefined,
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.markAsRead('n-1');
    });

    expect(ok).toBe(true);
    expect(markBriefingChecked).toHaveBeenCalledWith(webApi, 'n-1');
    await waitFor(() => {
      expect(result.current.refresh).toBe(1);
    });
  });

  it('markAsRead returns false when service fails — refresh does NOT bump', async () => {
    const webApi = makeWebApi();
    (markBriefingChecked as jest.Mock).mockResolvedValue({
      success: false,
      error: { code: 'X', message: 'fail' },
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.markAsRead('n-2');
    });

    expect(ok).toBe(false);
    expect(result.current.refresh).toBe(0);
  });

  it('markAllAsRead succeeds and bumps refresh', async () => {
    const webApi = makeWebApi();
    (markAllBriefingsChecked as jest.Mock).mockResolvedValue({
      success: true,
      data: { succeeded: 7, failed: 0 },
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.markAllAsRead();
    });

    expect(ok).toBe(true);
    expect(markAllBriefingsChecked).toHaveBeenCalledWith(webApi);
    await waitFor(() => {
      expect(result.current.refresh).toBe(1);
    });
  });

  it('dismissAll delegates to markAllAsRead', async () => {
    const webApi = makeWebApi();
    (markAllBriefingsChecked as jest.Mock).mockResolvedValue({
      success: true,
      data: { succeeded: 1, failed: 0 },
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.dismissAll();
    });

    expect(ok).toBe(true);
    expect(markAllBriefingsChecked).toHaveBeenCalledTimes(1);
    expect(markBriefingChecked).not.toHaveBeenCalled();
  });

  it('refresh bumps monotonically across successful mutations', async () => {
    const webApi = makeWebApi();
    (markBriefingChecked as jest.Mock).mockResolvedValue({
      success: true,
      data: undefined,
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    await act(async () => {
      await result.current.markAsRead('a');
    });
    await act(async () => {
      await result.current.markAsRead('b');
    });
    await act(async () => {
      await result.current.markAsRead('c');
    });

    await waitFor(() => {
      expect(result.current.refresh).toBe(3);
    });
  });

  // -------------------------------------------------------------------------
  // R3 task 030 — markChecked (FR-4)
  // -------------------------------------------------------------------------

  describe('markChecked (FR-4)', () => {
    it('no-ops to false when webApi is null and fires no callbacks', async () => {
      const onOptimistic = jest.fn();
      const onSuccess = jest.fn();
      const onRevert = jest.fn();
      const onError = jest.fn();

      const { result } = renderHook(() => useBriefingActions(null));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.markChecked('n-1', { onOptimistic, onSuccess, onRevert, onError });
      });

      expect(ok).toBe(false);
      expect(onOptimistic).not.toHaveBeenCalled();
      expect(onSuccess).not.toHaveBeenCalled();
      expect(onRevert).not.toHaveBeenCalled();
      expect(onError).not.toHaveBeenCalled();
      expect(markBriefingChecked).not.toHaveBeenCalled();
    });

    it('success: fires onOptimistic before service call, then onSuccess, bumps refresh', async () => {
      const webApi = makeWebApi();
      (markBriefingChecked as jest.Mock).mockResolvedValue({ success: true, data: undefined });
      const callOrder: string[] = [];
      const onOptimistic = jest.fn(() => callOrder.push('optimistic'));
      const onSuccess = jest.fn(() => callOrder.push('success'));
      const onRevert = jest.fn();
      const onError = jest.fn();
      (markBriefingChecked as jest.Mock).mockImplementation(async () => {
        callOrder.push('service');
        return { success: true, data: undefined };
      });

      const { result } = renderHook(() => useBriefingActions(webApi));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.markChecked('n-42', { onOptimistic, onSuccess, onRevert, onError });
      });

      expect(ok).toBe(true);
      expect(onOptimistic).toHaveBeenCalledTimes(1);
      expect(onOptimistic).toHaveBeenCalledWith('n-42');
      expect(markBriefingChecked).toHaveBeenCalledWith(webApi, 'n-42');
      expect(onSuccess).toHaveBeenCalledTimes(1);
      expect(onSuccess).toHaveBeenCalledWith(undefined);
      expect(onRevert).not.toHaveBeenCalled();
      expect(onError).not.toHaveBeenCalled();
      expect(callOrder).toEqual(['optimistic', 'service', 'success']);
      await waitFor(() => {
        expect(result.current.refresh).toBe(1);
      });
    });

    it('error: fires onOptimistic, then onRevert BEFORE onError, does NOT bump refresh', async () => {
      const webApi = makeWebApi();
      const errDetail = { code: 'BRIEFING_MARK_CHECKED_ERROR', message: 'Dataverse rejected update' };
      (markBriefingChecked as jest.Mock).mockResolvedValue({ success: false, error: errDetail });
      const callOrder: string[] = [];
      const onOptimistic = jest.fn(() => callOrder.push('optimistic'));
      const onSuccess = jest.fn();
      const onRevert = jest.fn(() => callOrder.push('revert'));
      const onError = jest.fn(() => callOrder.push('error'));

      const { result } = renderHook(() => useBriefingActions(webApi));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.markChecked('n-43', { onOptimistic, onSuccess, onRevert, onError });
      });

      expect(ok).toBe(false);
      expect(onOptimistic).toHaveBeenCalledWith('n-43');
      expect(onRevert).toHaveBeenCalledWith('n-43');
      expect(onError).toHaveBeenCalledWith(errDetail);
      expect(onSuccess).not.toHaveBeenCalled();
      // Revert MUST precede error so the UI is consistent before the toast renders.
      expect(callOrder).toEqual(['optimistic', 'revert', 'error']);
      expect(result.current.refresh).toBe(0);
    });

    it('works without options (callbacks all optional)', async () => {
      const webApi = makeWebApi();
      (markBriefingChecked as jest.Mock).mockResolvedValue({ success: true, data: undefined });

      const { result } = renderHook(() => useBriefingActions(webApi));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.markChecked('n-44');
      });

      expect(ok).toBe(true);
      expect(markBriefingChecked).toHaveBeenCalledWith(webApi, 'n-44');
      await waitFor(() => {
        expect(result.current.refresh).toBe(1);
      });
    });
  });

  // -------------------------------------------------------------------------
  // R3 task 030 — markRemoved (FR-5)
  // -------------------------------------------------------------------------

  describe('markRemoved (FR-5)', () => {
    it('no-ops to false when webApi is null', async () => {
      const { result } = renderHook(() => useBriefingActions(null));
      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.markRemoved('n-1');
      });
      expect(ok).toBe(false);
      expect(markBriefingRemoved).not.toHaveBeenCalled();
    });

    it('success: fires onOptimistic → service → onSuccess, bumps refresh', async () => {
      const webApi = makeWebApi();
      (markBriefingRemoved as jest.Mock).mockResolvedValue({ success: true, data: undefined });
      const onOptimistic = jest.fn();
      const onSuccess = jest.fn();
      const onRevert = jest.fn();
      const onError = jest.fn();

      const { result } = renderHook(() => useBriefingActions(webApi));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.markRemoved('n-52', { onOptimistic, onSuccess, onRevert, onError });
      });

      expect(ok).toBe(true);
      expect(onOptimistic).toHaveBeenCalledWith('n-52');
      expect(markBriefingRemoved).toHaveBeenCalledWith(webApi, 'n-52');
      expect(onSuccess).toHaveBeenCalledWith(undefined);
      expect(onRevert).not.toHaveBeenCalled();
      expect(onError).not.toHaveBeenCalled();
      await waitFor(() => {
        expect(result.current.refresh).toBe(1);
      });
    });

    it('error: fires onOptimistic, then onRevert BEFORE onError, does NOT bump refresh', async () => {
      const webApi = makeWebApi();
      const errDetail = { code: 'BRIEFING_MARK_REMOVED_ERROR', message: 'Network failure' };
      (markBriefingRemoved as jest.Mock).mockResolvedValue({ success: false, error: errDetail });
      const callOrder: string[] = [];
      const onOptimistic = jest.fn(() => callOrder.push('optimistic'));
      const onSuccess = jest.fn();
      const onRevert = jest.fn(() => callOrder.push('revert'));
      const onError = jest.fn(() => callOrder.push('error'));

      const { result } = renderHook(() => useBriefingActions(webApi));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.markRemoved('n-53', { onOptimistic, onSuccess, onRevert, onError });
      });

      expect(ok).toBe(false);
      expect(onSuccess).not.toHaveBeenCalled();
      expect(onError).toHaveBeenCalledWith(errDetail);
      expect(callOrder).toEqual(['optimistic', 'revert', 'error']);
      expect(result.current.refresh).toBe(0);
    });
  });

  // -------------------------------------------------------------------------
  // R3 task 030 — extendTtl (FR-6)
  // -------------------------------------------------------------------------

  describe('extendTtl (FR-6)', () => {
    it('no-ops to false when webApi is null', async () => {
      const { result } = renderHook(() => useBriefingActions(null));
      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.extendTtl('n-1', 604800);
      });
      expect(ok).toBe(false);
      expect(extendBriefingTtl).not.toHaveBeenCalled();
    });

    it('success: forwards currentTtl to service, fires onSuccess with new TTL value', async () => {
      const webApi = makeWebApi();
      // Service computes newTtl = currentTtl + 604800 and returns it.
      const currentTtl = 604800;
      const newTtl = currentTtl + 604800; // 1209600 seconds (14 days)
      (extendBriefingTtl as jest.Mock).mockResolvedValue({ success: true, data: newTtl });
      const onOptimistic = jest.fn();
      const onSuccess = jest.fn();
      const onRevert = jest.fn();
      const onError = jest.fn();

      const { result } = renderHook(() => useBriefingActions(webApi));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.extendTtl('n-62', currentTtl, {
          onOptimistic,
          onSuccess,
          onRevert,
          onError,
        });
      });

      expect(ok).toBe(true);
      expect(onOptimistic).toHaveBeenCalledWith('n-62');
      expect(extendBriefingTtl).toHaveBeenCalledWith(webApi, 'n-62', currentTtl);
      // onSuccess receives the NEW TTL value so the consumer toast can render
      // the new effective expiry date.
      expect(onSuccess).toHaveBeenCalledTimes(1);
      expect(onSuccess).toHaveBeenCalledWith(newTtl);
      expect(onRevert).not.toHaveBeenCalled();
      expect(onError).not.toHaveBeenCalled();
      await waitFor(() => {
        expect(result.current.refresh).toBe(1);
      });
    });

    it('error: fires onOptimistic, then onRevert BEFORE onError, does NOT bump refresh', async () => {
      const webApi = makeWebApi();
      const errDetail = { code: 'BRIEFING_EXTEND_TTL_ERROR', message: 'ttl write rejected' };
      (extendBriefingTtl as jest.Mock).mockResolvedValue({ success: false, error: errDetail });
      const callOrder: string[] = [];
      const onOptimistic = jest.fn(() => callOrder.push('optimistic'));
      const onSuccess = jest.fn();
      const onRevert = jest.fn(() => callOrder.push('revert'));
      const onError = jest.fn(() => callOrder.push('error'));

      const { result } = renderHook(() => useBriefingActions(webApi));

      let ok: boolean | undefined;
      await act(async () => {
        ok = await result.current.extendTtl('n-63', 0, { onOptimistic, onSuccess, onRevert, onError });
      });

      expect(ok).toBe(false);
      expect(extendBriefingTtl).toHaveBeenCalledWith(webApi, 'n-63', 0);
      expect(onSuccess).not.toHaveBeenCalled();
      expect(onError).toHaveBeenCalledWith(errDetail);
      expect(callOrder).toEqual(['optimistic', 'revert', 'error']);
      expect(result.current.refresh).toBe(0);
    });
  });
});
