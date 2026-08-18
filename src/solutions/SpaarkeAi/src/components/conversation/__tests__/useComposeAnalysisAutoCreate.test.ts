/**
 * useComposeAnalysisAutoCreate — UAT-08 (2026-08-18): on a completed Compose analysis/review, ensure a
 * bound sprk_analysis record via POST /api/ai/analysis/promote. These tests lock the honest safety
 * contract: fire once per session, send the right payload, and NEVER throw on a failure/skip.
 */
import { renderHook } from "@testing-library/react";
import { act } from "react";
import { useComposeAnalysisAutoCreate } from "../useComposeAnalysisAutoCreate";

describe("useComposeAnalysisAutoCreate", () => {
  const bffBaseUrl = "https://bff.example.com";

  function okFetch() {
    return jest.fn().mockResolvedValue({ ok: true, status: 201, json: async () => ({ analysisId: "a1" }) });
  }

  it("promotes the session once with { sessionId, name } on the first call", async () => {
    const authenticatedFetch = okFetch();
    const { result } = renderHook(() => useComposeAnalysisAutoCreate({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      result.current.ensureForSession("sess-1", "Acme NDA.docx — Review");
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(String(url)).toContain("/api/ai/analysis/promote");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({ sessionId: "sess-1", name: "Acme NDA.docx — Review" });
  });

  it("is idempotent — a second call for the SAME session does not promote again", async () => {
    const authenticatedFetch = okFetch();
    const { result } = renderHook(() => useComposeAnalysisAutoCreate({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      result.current.ensureForSession("sess-1", "A — Review");
      result.current.ensureForSession("sess-1", "A — Review");
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  });

  it("promotes a DIFFERENT session independently", async () => {
    const authenticatedFetch = okFetch();
    const { result } = renderHook(() => useComposeAnalysisAutoCreate({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      result.current.ensureForSession("sess-1", "A — Review");
      result.current.ensureForSession("sess-2", "B — Review");
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(2);
  });

  it("no-ops (no promote) when the session id is missing", async () => {
    const authenticatedFetch = okFetch();
    const { result } = renderHook(() => useComposeAnalysisAutoCreate({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      result.current.ensureForSession(undefined, "X — Review");
      result.current.ensureForSession(null, "X — Review");
    });

    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it("does NOT throw when promote returns 400 (already bound / no anchor)", async () => {
    const authenticatedFetch = jest.fn().mockResolvedValue({ ok: false, status: 400, json: async () => ({ detail: "already bound" }) });
    const { result } = renderHook(() => useComposeAnalysisAutoCreate({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      await expect(
        (async () => result.current.ensureForSession("sess-1", "X — Review"))()
      ).resolves.not.toThrow();
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  });

  it("does NOT throw when the promote fetch rejects (network error)", async () => {
    const authenticatedFetch = jest.fn().mockRejectedValue(new Error("network down"));
    const { result } = renderHook(() => useComposeAnalysisAutoCreate({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      result.current.ensureForSession("sess-1", "X — Review");
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  });
});
