/**
 * Unit tests for `useBriefingActions` — R2 task 019 / NFR-05.
 *
 * Contract under test (FR-06):
 *   - Exposes `markAsRead`, `markAllAsRead`, `dismissAll`, `refresh`.
 *   - Actions become no-ops returning `false` when webApi is null.
 *   - Successful mutations bump the `refresh` counter monotonically.
 *   - Failed mutations do NOT bump the counter and return `false`.
 *   - `dismissAll` delegates to `markAllAsRead` per spec note (toasttype=200000000
 *     model — "dismissed" === "mark-all-read" until per-item dismiss-without-read
 *     is added).
 *
 * Each test creates fresh mocks — NO shared state across tests (per spec
 * constraint).
 */

import { renderHook, act, waitFor } from "@testing-library/react";
import { useBriefingActions } from "../src/hooks/useBriefingActions";
import type { IWebApi } from "../src/types/notifications";

// Mock the notificationService functions used by useBriefingActions.
jest.mock("../src/services/notificationService", () => ({
  markNotificationRead: jest.fn(),
  markAllNotificationsRead: jest.fn(),
}));

import {
  markNotificationRead,
  markAllNotificationsRead,
} from "../src/services/notificationService";

function makeWebApi(): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn(),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

describe("useBriefingActions", () => {
  beforeEach(() => {
    (markNotificationRead as jest.Mock).mockReset();
    (markAllNotificationsRead as jest.Mock).mockReset();
    // Silence console.error noise from the hook's failure logging path
    jest.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it("returns false from all actions when webApi is null", async () => {
    const { result } = renderHook(() => useBriefingActions(null));
    expect(result.current.refresh).toBe(0);

    let r1: boolean | undefined;
    let r2: boolean | undefined;
    let r3: boolean | undefined;
    await act(async () => {
      r1 = await result.current.markAsRead("n-1");
      r2 = await result.current.markAllAsRead();
      r3 = await result.current.dismissAll();
    });

    expect(r1).toBe(false);
    expect(r2).toBe(false);
    expect(r3).toBe(false);
    expect(markNotificationRead).not.toHaveBeenCalled();
    expect(markAllNotificationsRead).not.toHaveBeenCalled();
    expect(result.current.refresh).toBe(0);
  });

  it("markAsRead succeeds and bumps refresh", async () => {
    const webApi = makeWebApi();
    (markNotificationRead as jest.Mock).mockResolvedValue({
      success: true,
      data: undefined,
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.markAsRead("n-1");
    });

    expect(ok).toBe(true);
    expect(markNotificationRead).toHaveBeenCalledWith(webApi, "n-1");
    await waitFor(() => {
      expect(result.current.refresh).toBe(1);
    });
  });

  it("markAsRead returns false when service fails — refresh does NOT bump", async () => {
    const webApi = makeWebApi();
    (markNotificationRead as jest.Mock).mockResolvedValue({
      success: false,
      error: { code: "X", message: "fail" },
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.markAsRead("n-2");
    });

    expect(ok).toBe(false);
    expect(result.current.refresh).toBe(0);
  });

  it("markAllAsRead succeeds and bumps refresh", async () => {
    const webApi = makeWebApi();
    (markAllNotificationsRead as jest.Mock).mockResolvedValue({
      success: true,
      data: { succeeded: 7, failed: 0 },
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.markAllAsRead();
    });

    expect(ok).toBe(true);
    expect(markAllNotificationsRead).toHaveBeenCalledWith(webApi);
    await waitFor(() => {
      expect(result.current.refresh).toBe(1);
    });
  });

  it("dismissAll delegates to markAllAsRead", async () => {
    const webApi = makeWebApi();
    (markAllNotificationsRead as jest.Mock).mockResolvedValue({
      success: true,
      data: { succeeded: 1, failed: 0 },
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.dismissAll();
    });

    expect(ok).toBe(true);
    expect(markAllNotificationsRead).toHaveBeenCalledTimes(1);
    expect(markNotificationRead).not.toHaveBeenCalled();
  });

  it("refresh bumps monotonically across successful mutations", async () => {
    const webApi = makeWebApi();
    (markNotificationRead as jest.Mock).mockResolvedValue({
      success: true,
      data: undefined,
    });

    const { result } = renderHook(() => useBriefingActions(webApi));

    await act(async () => {
      await result.current.markAsRead("a");
    });
    await act(async () => {
      await result.current.markAsRead("b");
    });
    await act(async () => {
      await result.current.markAsRead("c");
    });

    await waitFor(() => {
      expect(result.current.refresh).toBe(3);
    });
  });
});
