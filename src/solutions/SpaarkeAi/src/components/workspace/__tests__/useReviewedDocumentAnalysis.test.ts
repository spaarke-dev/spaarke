/**
 * useReviewedDocumentAnalysis — UAT (2026-08-18): SAVE-driven Analysis create+bind for a reviewed
 * document. Locks the safety contract: promote once per session with a "{document} — Review" name,
 * and NEVER throw on a skip/failure.
 */
import { renderHook } from "@testing-library/react";
import { act } from "react";
import { useReviewedDocumentAnalysis } from "../useReviewedDocumentAnalysis";

describe("useReviewedDocumentAnalysis", () => {
  const bffBaseUrl = "https://bff.example.com";
  const okFetch = () =>
    jest.fn().mockResolvedValue({ ok: true, status: 201, json: async () => ({ analysisId: "a1" }) });

  it("promotes the review session once, named '{document} — Review'", async () => {
    const authenticatedFetch = okFetch();
    const { result } = renderHook(() => useReviewedDocumentAnalysis({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      await result.current("doc-1", "sess-1", "Acme NDA.docx");
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(String(url)).toContain("/api/ai/analysis/promote");
    expect(JSON.parse(init.body)).toEqual({ sessionId: "sess-1", name: "Acme NDA.docx — Review" });
  });

  it("is idempotent per session (a second create-on-save on the same session does not re-promote)", async () => {
    const authenticatedFetch = okFetch();
    const { result } = renderHook(() => useReviewedDocumentAnalysis({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      await result.current("doc-1", "sess-1", "A.docx");
      await result.current("doc-1", "sess-1", "A.docx");
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  });

  it("no-ops when the session id is missing", async () => {
    const authenticatedFetch = okFetch();
    const { result } = renderHook(() => useReviewedDocumentAnalysis({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      await result.current("doc-1", "", "A.docx");
    });

    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it("does NOT throw when promote returns 400 (already bound / no anchor)", async () => {
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue({ ok: false, status: 400, json: async () => ({ detail: "already bound" }) });
    const { result } = renderHook(() => useReviewedDocumentAnalysis({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      await expect(result.current("doc-1", "sess-1", "A.docx")).resolves.toBeUndefined();
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  });

  it("does NOT throw when the promote fetch rejects (network error)", async () => {
    const authenticatedFetch = jest.fn().mockRejectedValue(new Error("network down"));
    const { result } = renderHook(() => useReviewedDocumentAnalysis({ bffBaseUrl, authenticatedFetch }));

    await act(async () => {
      await expect(result.current("doc-1", "sess-1", "A.docx")).resolves.toBeUndefined();
    });
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  });
});
