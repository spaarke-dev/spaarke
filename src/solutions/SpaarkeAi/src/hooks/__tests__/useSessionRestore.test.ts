/**
 * useSessionRestore — a missing session is "not found", not a restore error.
 *
 * ThreePaneShell shows the warning "Session not found. Starting a new session." when `isNotFound` is
 * set, and the error toast "Failed to restore session: …" otherwise. The hook set `isNotFound` only for
 * a returned 404; `@spaarke/auth`'s authenticatedFetch THROWS ApiError(404) instead, so a stale session
 * link produced the error toast. The fetch here throws the REAL ApiError (the `@spaarke/auth` jest stub
 * re-exports the real class).
 */
import { renderHook, waitFor } from "@testing-library/react";
import { ApiError } from "@spaarke/auth";
import { useSessionRestore } from "../useSessionRestore";

beforeEach(() => {
  jest.spyOn(console, "warn").mockImplementation(() => undefined);
  jest.spyOn(console, "error").mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
});

it("a thrown 404 sets isNotFound (the warning path), not a restore error", async () => {
  const fetchImpl = jest.fn().mockRejectedValue(new ApiError("Session not found", 404, { title: "Not Found", status: 404 }));
  const { result } = renderHook(() => useSessionRestore("sess-gone", "https://bff.example.com", fetchImpl, true));

  await waitFor(() => expect(result.current.isRestoring).toBe(false));
  await waitFor(() => expect(result.current.isNotFound).toBe(true));
  expect(result.current.restoreError).toBe("Session not found");
  expect(result.current.restoreSpec).toBeNull();
});

it("another thrown failure is a restore error, not 'not found'", async () => {
  const fetchImpl = jest.fn().mockRejectedValue(new ApiError("HTTP 500", 500, null));
  const { result } = renderHook(() => useSessionRestore("sess-1", "https://bff.example.com", fetchImpl, true));

  await waitFor(() => expect(result.current.restoreError).toBe("HTTP 500"));
  expect(result.current.isNotFound).toBe(false);
});
