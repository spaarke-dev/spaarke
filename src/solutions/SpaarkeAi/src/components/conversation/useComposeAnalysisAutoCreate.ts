/**
 * useComposeAnalysisAutoCreate — UAT-08 (2026-08-18, owner-approved).
 *
 * When an analysis / review COMPLETES in Compose (e.g. an NDA review), ensure the corresponding
 * `sprk_analysis` record exists and is bound to the review's session — so the work appears in history
 * and can be REOPENED (the "reload prior work from history" flow the owner referenced). Without this the
 * analysis output lived only on the loose chat/document session and never became a first-class, reopenable
 * Analysis record unless the user manually promoted it.
 *
 * Reuses the EXISTING `POST /api/ai/analysis/promote` endpoint (create + bind + already-bound guard, all
 * server-side — the same endpoint HistoryOverlay's "Set related record" uses). No new server surface.
 *
 * SAFETY (binding):
 *  - **Idempotent** — attempted at most ONCE per session per mount (client `attemptedRef` guard); the
 *    server ADDITIONALLY 400s a session already bound to an Analysis, so re-running a review on the same
 *    document never creates a duplicate.
 *  - **Never blocks / never throws** — a failure degrades to "no auto-Analysis" (the user can still set the
 *    related record manually via history). It must NEVER fail or block the analysis flow.
 *  - **Anchor** — the server resolves the anchor from the session's document (`session.DocumentId`); a
 *    session with no document (a transient/unsaved doc) yields a benign 400 that we swallow.
 */
import * as React from "react";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";

export interface UseComposeAnalysisAutoCreateOptions {
  bffBaseUrl: string;
  authenticatedFetch: AuthenticatedFetchFn;
}

export interface UseComposeAnalysisAutoCreateResult {
  /**
   * Ensure an `sprk_analysis` exists + is bound for `sessionId`, named `name`. No-op when `sessionId`
   * is absent, `bffBaseUrl` is empty, or this session was already attempted this mount. Fire-and-forget.
   */
  ensureForSession: (sessionId: string | undefined | null, name: string) => void;
}

export function useComposeAnalysisAutoCreate(
  options: UseComposeAnalysisAutoCreateOptions
): UseComposeAnalysisAutoCreateResult {
  const { bffBaseUrl, authenticatedFetch } = options;
  // One attempt per session per mount — the client half of the idempotency (the server enforces the
  // real once-per-session guarantee by 400ing an already-bound session).
  const attemptedRef = React.useRef<Set<string>>(new Set());

  const ensureForSession = React.useCallback(
    (sessionId: string | undefined | null, name: string): void => {
      if (!sessionId || !bffBaseUrl || attemptedRef.current.has(sessionId)) return;
      attemptedRef.current.add(sessionId);
      void (async () => {
        try {
          const url = buildBffApiUrl(bffBaseUrl, "/api/ai/analysis/promote");
          const response = await authenticatedFetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ sessionId, name }),
          });
          if (!response.ok) {
            // 400 = already bound OR no resolvable anchor (a transient/unsaved doc) — both benign: the
            // review still persists on the session ledger; the Analysis can be set later. Any other
            // status is a transient fault we deliberately swallow (never block the analysis flow).
            // eslint-disable-next-line no-console
            console.debug("[SpaarkeAi] Compose auto-Analysis promote skipped:", response.status);
          }
        } catch (err) {
          // eslint-disable-next-line no-console
          console.debug("[SpaarkeAi] Compose auto-Analysis promote failed (non-fatal):", err);
        }
      })();
    },
    [bffBaseUrl, authenticatedFetch]
  );

  return { ensureForSession };
}

export default useComposeAnalysisAutoCreate;
