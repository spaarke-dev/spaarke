/**
 * useAuthProbe — bounded retry-with-backoff auth-ready probe
 * (spaarkeai-assistant-enhancements-r2 Phase 0 FIX 3, 2026-08-07).
 *
 * Regression coverage for "the Assistant sometimes fails to load on the FIRST
 * mount after a hard reset; works after a reload." The prior one-shot probe
 * (`App.tsx`'s `AppWithAuth`) permanently latched `isAuthenticated=false` the
 * instant `getAccessToken()` returned an empty string once — no retry, and
 * nothing else re-checked it later (`isAuthenticated` is a sync, non-reactive
 * getter by design). This hook retries with backoff instead.
 */
import { renderHook, waitFor, act } from "@testing-library/react";

jest.mock("@spaarke/auth", () => ({
  getAuthProvider: jest.fn(),
}));

import { getAuthProvider } from "@spaarke/auth";
import { useAuthProbe, AUTH_PROBE_RETRY_DELAYS_MS } from "../useAuthProbe";

const mockedGetAuthProvider = getAuthProvider as jest.Mock;

describe("useAuthProbe", () => {
  beforeEach(() => {
    jest.useFakeTimers();
    mockedGetAuthProvider.mockReset();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it("returns true immediately when the first getAccessToken() call succeeds", async () => {
    const getAccessToken = jest.fn().mockResolvedValue("tok-abc");
    mockedGetAuthProvider.mockReturnValue({ getAccessToken });

    const { result } = renderHook(() => useAuthProbe());

    await waitFor(() => expect(result.current).toBe(true));
    expect(getAccessToken).toHaveBeenCalledTimes(1);
  });

  it("retries with backoff and flips true once a LATER attempt returns a real token (the cold-MSAL-cache case)", async () => {
    // First two attempts return an EMPTY token (mirrors SpaarkeAuthProvider.getAccessToken()
    // swallowing a slow/failed ssoSilent and resolving with '' rather than throwing); the third
    // succeeds — the exact "works after a moment" shape the owner reported as "works after reload".
    const getAccessToken = jest
      .fn()
      .mockResolvedValueOnce("")
      .mockResolvedValueOnce("")
      .mockResolvedValueOnce("tok-eventually");
    mockedGetAuthProvider.mockReturnValue({ getAccessToken });

    const { result } = renderHook(() => useAuthProbe());

    expect(result.current).toBe(false);

    // Attempt 1 already fired (empty) synchronously post-mount; advance past its backoff delay
    // to trigger attempt 2 (also empty), then past ITS delay to trigger attempt 3 (succeeds).
    await act(async () => {
      await jest.advanceTimersByTimeAsync(AUTH_PROBE_RETRY_DELAYS_MS[0]);
    });
    await act(async () => {
      await jest.advanceTimersByTimeAsync(AUTH_PROBE_RETRY_DELAYS_MS[1]);
    });

    await waitFor(() => expect(result.current).toBe(true));
    expect(getAccessToken).toHaveBeenCalledTimes(3);
  });

  it("stays false after exhausting all retries when every attempt returns an empty token", async () => {
    const getAccessToken = jest.fn().mockResolvedValue("");
    mockedGetAuthProvider.mockReturnValue({ getAccessToken });
    const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => undefined);

    const { result } = renderHook(() => useAuthProbe());

    for (const delay of AUTH_PROBE_RETRY_DELAYS_MS) {
      // eslint-disable-next-line no-await-in-loop
      await act(async () => {
        await jest.advanceTimersByTimeAsync(delay);
      });
    }

    expect(result.current).toBe(false);
    // 1 initial attempt + one retry per configured delay.
    expect(getAccessToken).toHaveBeenCalledTimes(AUTH_PROBE_RETRY_DELAYS_MS.length + 1);
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining("exhausted all retries"));

    warnSpy.mockRestore();
  });

  it("does not update state after unmount (no act() warning / no leaked timer callback)", async () => {
    const getAccessToken = jest.fn().mockResolvedValue("");
    mockedGetAuthProvider.mockReturnValue({ getAccessToken });

    const { unmount } = renderHook(() => useAuthProbe());
    unmount();

    // Advancing timers post-unmount must not throw (the effect's `cancelled` guard short-circuits
    // every remaining attempt instead of calling setState on an unmounted component).
    await act(async () => {
      await jest.advanceTimersByTimeAsync(20000);
    });
  });
});
