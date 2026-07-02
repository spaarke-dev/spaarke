/**
 * useComposeHeartbeatGate.ts — SPE check-out heartbeat, gated on lock acquisition.
 *
 * Project:   spaarkeai-compose-r1
 * Tasks:     R3 — FU-1 heartbeat-gate fix (code-review)
 *            Originally W4-045 (ComposeEditor `useComposeHeartbeat`); hoisted to
 *            ComposeWorkspace as part of the R2 decomposition refactor.
 *
 * Bug fixed (FU-1):
 *   ComposeEditor.tsx previously fired the 3-min heartbeat regardless of the
 *   Dataverse-side check-out state. After a force-close (Task 051), a cancelled
 *   tab would continue heart-beating a lock it no longer held. The server-side
 *   same-user guard returns 404, harmlessly — but it's wasted HTTP traffic.
 *
 * Fix:
 *   Gate the heartbeat on `checkoutStatus === 'acquired'`. The hook now lives
 *   at the workspace level (which owns the checkout reducer state) so the
 *   gating signal is local to the timer effect.
 *
 * Per Spike #3 §4.1 LOCKED behaviour:
 *   - Interval: 3 minutes (sliding)
 *   - Gate: `document.visibilityState === 'visible'` AND `checkoutStatus === 'acquired'`
 *   - Endpoint: POST `/api/compose/document/{documentId}/heartbeat`
 *   - Failure mode: log warning + swallow (no UX impact; W7-052 wires BFF side)
 *
 * The hook ONLY runs when:
 *   1. `checkoutStatus === 'acquired'` (FU-1 fix — the key new gate)
 *   2. `sprkDocumentId` is present (heartbeat is keyed on the Dataverse id, which
 *      exists post-promotion-on-first-Save per design.md §8)
 *   3. `bffBaseUrl` is configured
 *
 * Constraints:
 *   - ADR-022 React 19.
 *   - ADR-028 Spaarke Auth v2 — uses `authenticatedFetch` from `@spaarke/auth`.
 *   - ADR-015 Tier 3 — logs status only (Tier 1 safe); never logs document content.
 *
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx (consumer)
 * @see src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx (heartbeat removed from here)
 */

import * as React from 'react';
import { authenticatedFetch } from '@spaarke/auth';

import type { ComposeCheckoutStatus } from '../ComposeWorkspace.types';

/** 3-minute sliding interval per Spike #3 §1 (LOCKED). */
const HEARTBEAT_INTERVAL_MS = 3 * 60 * 1000;

/**
 * SPE check-out heartbeat hook, gated on lock acquisition.
 *
 * @param checkoutStatus       Current SPE check-out lifecycle status. Heartbeat
 *                             only fires when this is exactly `'acquired'` (FU-1 fix).
 * @param sprkDocumentId       Dataverse `sprk_documentid`. Required for the
 *                             heartbeat endpoint URL. Empty / undefined suppresses.
 * @param bffBaseUrl           BFF base URL (host only). Empty / undefined suppresses.
 */
export function useComposeHeartbeatGate(
  checkoutStatus: ComposeCheckoutStatus,
  sprkDocumentId: string | undefined,
  bffBaseUrl: string | undefined
): void {
  React.useEffect(() => {
    // ── FU-1 gate: only heartbeat when the lock is actually held ────────────
    if (checkoutStatus !== 'acquired') return;
    if (!sprkDocumentId || !bffBaseUrl) return;

    const tick = async () => {
      // Visibility gate per Spike #3 §4.1: backgrounded tabs do NOT count as
      // "still editing".
      if (typeof document !== 'undefined' && document.visibilityState !== 'visible') {
        return;
      }
      try {
        const url = `${bffBaseUrl}/api/compose/document/${sprkDocumentId}/heartbeat`;
        const response = await authenticatedFetch(url, { method: 'POST' });
        if (!response.ok) {
          // Defensive: 404 = endpoint not deployed yet (W7-052 lands later);
          // 401/403 = auth issue (upstream concern); 5xx = transient.
          // eslint-disable-next-line no-console
          console.info(
            `[ComposeWorkspace] heartbeat returned ${response.status} for document ${sprkDocumentId} — swallowing (W7-052 wires endpoint)`
          );
        }
      } catch (err) {
        // Network errors: log + swallow. The 15-min stale-sweep on the BFF
        // side handles orphan locks regardless of client-side success.
        // eslint-disable-next-line no-console
        console.info(
          `[ComposeWorkspace] heartbeat fetch failed for document ${sprkDocumentId} — swallowing`,
          err instanceof Error ? err.message : String(err)
        );
      }
    };

    // Initial heartbeat on entering 'acquired', then 3-min sliding.
    const timer = setInterval(tick, HEARTBEAT_INTERVAL_MS);
    void tick();

    return () => clearInterval(timer);
  }, [checkoutStatus, sprkDocumentId, bffBaseUrl]);
}
