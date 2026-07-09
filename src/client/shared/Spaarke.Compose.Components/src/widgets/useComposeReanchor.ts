/**
 * useComposeReanchor.ts — return-from-Word re-anchor fetch hook (FR-27, task 054).
 *
 * Project: spaarkeai-compose-r2, task 054 (Phase 5 Word).
 *
 * Calls the BFF re-anchor endpoint (`POST /api/compose/document/{documentSpeId}/reanchor-annotations`)
 * with the Compose session's prior anchored-annotations, and returns the banded summary the
 * ComposeReanchorBanner + ComposeReanchorConflictPanel render. Invoked by the Workspace after
 * 052/053 detect a new SPE version (design §2.5).
 *
 * Constraints honored (BINDING):
 *   - ADR-028: fetch goes through `@spaarke/auth` `authenticatedFetch` — never a raw `Bearer`
 *     header assembled here. `authenticatedFetch` injects the token + tenant headers.
 *   - ADR-015 Tier-3: prior-anchor `textPattern` is user content; it is sent to the BFF (the
 *     design intent — the server needs it to re-anchor) but NEVER logged to console/telemetry here.
 *
 * @see ./ComposeReanchor.types.ts (PriorAnchorInput, ReanchorAnnotationsResponse, ReanchorSummary)
 * @see src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs (ReanchorAnnotations handler)
 */

import * as React from 'react';
import { useAuth } from '@spaarke/auth';

import type { PriorAnchorInput, ReanchorAnnotationsResponse, ReanchorSummary } from './ComposeReanchor.types';

export interface UseComposeReanchorOptions {
  /** BFF base URL (no trailing slash), e.g. `https://bff.example`. */
  bffBaseUrl: string;
  /**
   * Test/injection escape hatch — bypasses `useAuth().authenticatedFetch` so a test can drive the
   * hook without an MSAL bootstrap. Production hosts must NOT set this.
   */
  fetchOverride?: typeof fetch;
}

export interface ReanchorRequestArgs {
  /** SPE drive-item id of the Compose document that changed in Word. */
  documentSpeId: string;
  /** SPE drive id (for the OBO download on the server). */
  driveId: string;
  /** Tenant id (multi-tenant scoping). */
  tenantId: string;
  /** The Compose session's prior anchored-annotations to re-locate. */
  priorAnchors: PriorAnchorInput[];
}

export interface UseComposeReanchorResult {
  /** The most recent re-anchor summary (null before the first successful call). */
  summary: ReanchorSummary | null;
  /** True while a re-anchor request is in flight. */
  loading: boolean;
  /** A user-safe error message when the last request failed (null otherwise). */
  error: string | null;
  /** Triggers a re-anchor request; resolves with the summary (or throws on failure). */
  reanchor: (args: ReanchorRequestArgs) => Promise<ReanchorSummary>;
  /** Clears the current summary + error (e.g. after the banner is dismissed). */
  reset: () => void;
}

/**
 * Hook that fetches the re-anchor summary for a returned-from-Word document.
 *
 * The request is a single POST; the server downloads the current bytes, extracts paragraph text,
 * scores each prior anchor into bands, persists the summary to Redis, and returns it.
 */
export function useComposeReanchor(options: UseComposeReanchorOptions): UseComposeReanchorResult {
  const { bffBaseUrl, fetchOverride } = options;
  const { authenticatedFetch } = useAuth();

  const [summary, setSummary] = React.useState<ReanchorSummary | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const doFetch = fetchOverride ?? authenticatedFetch;

  const reanchor = React.useCallback(
    async (args: ReanchorRequestArgs): Promise<ReanchorSummary> => {
      setLoading(true);
      setError(null);

      try {
        const url = `${bffBaseUrl}/api/compose/document/${encodeURIComponent(args.documentSpeId)}/reanchor-annotations`;
        const response = await doFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            driveId: args.driveId,
            tenantId: args.tenantId,
            priorAnchors: args.priorAnchors,
          }),
        });

        if (!response.ok) {
          // ADR-019: no raw server detail is surfaced; a single user-safe line.
          const message = `Could not re-anchor annotations (status ${response.status}).`;
          setError(message);
          throw new Error(message);
        }

        const payload = (await response.json()) as ReanchorAnnotationsResponse;
        setSummary(payload.summary);
        return payload.summary;
      } catch (err) {
        if (error === null) {
          setError('Could not re-anchor annotations. Try reloading the document.');
        }
        throw err instanceof Error ? err : new Error(String(err));
      } finally {
        setLoading(false);
      }
    },
    // `error` intentionally excluded — reading it inside would stale-close; the null-guard above is
    // best-effort messaging, not control flow.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [bffBaseUrl, doFetch]
  );

  const reset = React.useCallback((): void => {
    setSummary(null);
    setError(null);
  }, []);

  return { summary, loading, error, reanchor, reset };
}
