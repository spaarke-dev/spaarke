/**
 * useReviewedDocumentAnalysis — UAT (2026-08-18, owner): SAVE-driven Analysis creation.
 *
 * Wired into `ComposeWorkspace.onReviewedDocumentCreated` — fired ONCE, on the FIRST save of a NEW
 * document that had a review/analysis run on it. Creates + binds the `sprk_analysis` for the review's
 * session (reusing the EXISTING `POST /api/ai/analysis/promote` — create + bind + server already-bound
 * guard) so the Summary Memo works and the Analysis is reopenable from history.
 *
 * Model (owner): the Document + Analysis are created on SAVE, not on upload/run. A reopened Analysis
 * (existing document) saves via the replace/version path — `onReviewedDocumentCreated` does NOT fire
 * there (it is create-on-save-only), so the Analysis is created exactly once and never duplicated.
 *
 * SAFETY: idempotent per session per mount (client guard) + the server already-bound 400; fire-and-
 * forget — a failure degrades to "no auto-Analysis" and NEVER blocks or fails the save.
 */
import * as React from "react";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";

export interface UseReviewedDocumentAnalysisOptions {
  bffBaseUrl: string;
  authenticatedFetch: AuthenticatedFetchFn;
}

export function useReviewedDocumentAnalysis(
  options: UseReviewedDocumentAnalysisOptions
): (newSprkDocumentId: string, sessionId: string, documentName: string) => Promise<void> {
  const { bffBaseUrl, authenticatedFetch } = options;
  const attemptedRef = React.useRef<Set<string>>(new Set());

  return React.useCallback(
    async (_newSprkDocumentId: string, sessionId: string, documentName: string): Promise<void> => {
      if (!bffBaseUrl || !sessionId || attemptedRef.current.has(sessionId)) return;
      attemptedRef.current.add(sessionId);
      // Name the Analysis after the document (owner: "{document} — {review type}"). Renameable in history.
      const name = `${documentName?.trim() || "Document"} — Review`;
      try {
        const url = buildBffApiUrl(bffBaseUrl, "/api/ai/analysis/promote");
        const response = await authenticatedFetch(url, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ sessionId, name }),
        });
        if (!response.ok) {
          // 400 = already bound OR no resolvable anchor — benign (the review persists on the session).
          // eslint-disable-next-line no-console
          console.debug("[SpaarkeAi] reviewed-document Analysis promote skipped:", response.status);
        }
      } catch (err) {
        // eslint-disable-next-line no-console
        console.debug("[SpaarkeAi] reviewed-document Analysis promote failed (non-fatal):", err);
      }
    },
    [bffBaseUrl, authenticatedFetch]
  );
}

export default useReviewedDocumentAnalysis;
