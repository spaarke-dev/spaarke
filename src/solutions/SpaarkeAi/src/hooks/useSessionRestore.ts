/**
 * useSessionRestore — Fetches and applies session restore state from the BFF API.
 *
 * When a sessionId URL parameter is present, this hook:
 *   1. Calls GET /api/ai/chat/sessions/{sessionId}/restore
 *   2. Returns the restore spec so ThreePaneShell can initialise all three panes
 *   3. Logs timing for NFR compliance (<500ms p95)
 *
 * @see SessionRestoreService (BFF) — backend restore logic
 * @see ThreePaneShell — consumer of the restore spec
 * @see AIPU2-106 — session restore E2E task
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { buildBffApiUrl } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Restore response types (mirrors BFF SessionRestoreResponse)
// ---------------------------------------------------------------------------

export interface SessionRestoreMessage {
  role: string;
  content: string;
  timestamp: string;
}

export interface SessionRestoreSpec {
  sessionId: string;
  playbookId: string | null;
  stage: string;
  widgetStates: Record<string, string>;
  conversationSummary: string | null;
  recentMessages: SessionRestoreMessage[];
  hasStaleEntities: boolean;
  restoreLatencyMs: number;
}

export interface UseSessionRestoreResult {
  /** The restore spec, null while loading or on error. */
  restoreSpec: SessionRestoreSpec | null;
  /** Whether the restore fetch is in progress. */
  isRestoring: boolean;
  /** Error message if restore failed (404 or network error). */
  restoreError: string | null;
  /** Whether the restore returned 404 (session not found). */
  isNotFound: boolean;
}

/**
 * Fetches session restore state from the BFF.
 *
 * Only fires when sessionId, bffBaseUrl, and token are all present.
 * The fetch runs exactly once per sessionId (guarded by a ref).
 */
export function useSessionRestore(
  sessionId: string | undefined,
  bffBaseUrl: string,
  token: string | null
): UseSessionRestoreResult {
  const [restoreSpec, setRestoreSpec] = useState<SessionRestoreSpec | null>(null);
  const [isRestoring, setIsRestoring] = useState<boolean>(false);
  const [restoreError, setRestoreError] = useState<string | null>(null);
  const [isNotFound, setIsNotFound] = useState<boolean>(false);

  // Guard against double-fetch (React StrictMode double-mount).
  const fetchedRef = useRef<string | null>(null);

  useEffect(() => {
    if (!sessionId || !bffBaseUrl || !token) return;
    if (fetchedRef.current === sessionId) return;
    fetchedRef.current = sessionId;

    let cancelled = false;
    const t0 = performance.now();

    const fetchRestore = async (): Promise<void> => {
      setIsRestoring(true);
      setRestoreError(null);
      setIsNotFound(false);

      try {
        const url = buildBffApiUrl(bffBaseUrl, `/ai/chat/sessions/${sessionId}/restore`);
        const response = await fetch(url, {
          method: "GET",
          headers: {
            Accept: "application/json",
            Authorization: `Bearer ${token}`,
          },
        });

        if (cancelled) return;

        if (response.status === 404) {
          setIsNotFound(true);
          setRestoreError("Session not found");
          console.warn(`[SessionRestore] Session ${sessionId} not found (404)`);
          return;
        }

        if (!response.ok) {
          const detail = await response.text().catch(() => "");
          setRestoreError(`Restore failed: ${response.status} ${response.statusText}`);
          console.error(`[SessionRestore] Restore failed:`, response.status, detail);
          return;
        }

        const spec = (await response.json()) as SessionRestoreSpec;
        if (!cancelled) {
          setRestoreSpec(spec);

          const totalMs = Math.round(performance.now() - t0);
          const serverMs = spec.restoreLatencyMs;
          console.info(
            `[SessionRestore] Restored session ${sessionId} in ${totalMs}ms ` +
              `(server=${serverMs}ms, client=${totalMs - serverMs}ms, ` +
              `messages=${spec.recentMessages.length}, ` +
              `widgets=${Object.keys(spec.widgetStates).length}, ` +
              `stale=${spec.hasStaleEntities})`
          );

          if (totalMs > 500) {
            console.warn(
              `[SessionRestore] Total restore time ${totalMs}ms exceeds 500ms NFR target`
            );
          }
        }
      } catch (err) {
        if (!cancelled) {
          const msg = err instanceof Error ? err.message : String(err);
          setRestoreError(msg);
          console.error("[SessionRestore] Restore fetch error:", err);
        }
      } finally {
        if (!cancelled) {
          setIsRestoring(false);
        }
      }
    };

    void fetchRestore();
    return () => {
      cancelled = true;
    };
  }, [sessionId, bffBaseUrl, token]);

  return { restoreSpec, isRestoring, restoreError, isNotFound };
}
