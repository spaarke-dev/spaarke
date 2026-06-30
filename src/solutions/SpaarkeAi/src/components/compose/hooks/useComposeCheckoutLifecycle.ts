/**
 * useComposeCheckoutLifecycle.ts — SPE check-out lifecycle owned at workspace level.
 *
 * Project:   spaarkeai-compose-r1
 * Tasks:     050 (W6) — checkout-on-mount (POST /api/documents/{id}/checkout)
 *            051 (W7) — multi-tab UX (probe-before-acquire + conflict handlers)
 * Extracted: spaarkeai-compose-r1 R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC)
 *
 * Purpose:
 *   Owns the probe-before-acquire pattern (Task 051) and the three conflict
 *   resolution paths (force-close / cancel / cross-user 409):
 *
 *     1. Once the doc is `loaded` and `sprkDocumentId` is present, fire the
 *        GET /checkout-status probe.
 *     2. If the probe reveals THIS user already holds the lock from another
 *        session, dispatch `checkoutSameUserConflict` — the workspace then
 *        renders the ComposeConflictDialog.
 *     3. Otherwise, run the POST /checkout call (`runCheckout`). 200 OK →
 *        `acquired`. 409 → `conflict` (cross-user). 404/403/5xx → `failed`.
 *     4. The `forceCloseAndAcquire` callback handles the "force-close other
 *        session" dialog button: POST /discard, broadcast `force-closed` to
 *        sibling tabs (caller-supplied), then re-run checkout.
 *     5. The `discardAndCancel` callback handles "Cancel — close this tab".
 *
 * The actual BroadcastChannel signaling is OWNED by the sibling hook
 * `useComposeBroadcastChannel`; this hook accepts `postForceClosed` as an
 * injected callback so the two concerns stay decoupled.
 *
 * Constraints:
 *   - ADR-028 Spaarke Auth v2 — uses `authenticatedFetch` + `buildBffApiUrl`.
 *   - ADR-015 Tier 3 — logs status + correlationId only; never document content.
 *   - ADR-022 React 19.
 *   - CLAUDE.md §3 sub-agent write boundary.
 *
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx
 * @see src/server/api/Sprk.Bff.Api/Api/DocumentOperationsEndpoints.cs
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-3-spe-checkout-promotion.md §1, §9
 */

import * as React from 'react';
import { authenticatedFetch, buildBffApiUrl } from '@spaarke/auth';

import type {
  ComposeCheckoutLockedByInfo,
  ComposeWorkspaceAction,
  ComposeWorkspaceState,
} from '../ComposeWorkspace.types';

export interface UseComposeCheckoutLifecycleOptions {
  /** Current workspace reducer state. */
  state: ComposeWorkspaceState;
  /** Reducer dispatch (typed). */
  dispatch: React.Dispatch<ComposeWorkspaceAction>;
  /** BFF base URL (host only). When empty, checkout is suppressed. */
  bffBaseUrl: string;
  /**
   * Optional sibling-tab signaler. Called after a successful discard to
   * notify sibling tabs that they no longer hold the lock. Best-effort.
   */
  postForceClosed?: () => void;
}

export interface UseComposeCheckoutLifecycleResult {
  /**
   * Acquire (or re-acquire) the Dataverse lock for `sprkDocumentId`. Called by:
   *   (a) the internal probe-orchestration effect (post-load, post-Save-promotion),
   *   (b) the `forceCloseAndAcquire` flow (after a successful discard).
   *
   * Dispatches `checkoutRequested` → `checkoutAcquired` | `checkoutConflict` |
   * `checkoutFailed` per the endpoint's response.
   */
  runCheckout: (sprkDocumentId: string) => Promise<void>;
  /**
   * "Force-close other session and open here" — FR-16 verbatim button.
   *
   * 1. POST /api/documents/{sprkDocumentId}/discard
   * 2. On success, broadcast `force-closed` (via `postForceClosed`)
   * 3. Re-run `runCheckout` to acquire a fresh lock in this tab
   * 4. On discard failure, dispatch `checkoutFailed`
   */
  forceCloseAndAcquire: () => Promise<void>;
  /**
   * "Cancel — close this tab" — third option (non-FR-16 escape hatch).
   * Transitions to `'cancelled'`; the host's banner stack surfaces the message.
   */
  discardAndCancel: () => void;
}

/**
 * Workspace-level SPE check-out lifecycle hook.
 *
 * Fires the probe-then-acquire pattern automatically when the document
 * transitions to `loaded` AND `checkoutStatus` is `idle` or `skipped` (the
 * latter covers Path B → Path A promotion on first Save).
 *
 * Returns the three callback handlers needed by ComposeConflictDialog.
 */
export function useComposeCheckoutLifecycle(
  opts: UseComposeCheckoutLifecycleOptions
): UseComposeCheckoutLifecycleResult {
  const { state, dispatch, bffBaseUrl, postForceClosed } = opts;
  const { status, documentRef, checkoutStatus, sessionId } = state;
  const sprkDocumentId = documentRef?.sprkDocumentId;

  // ── runCheckout: POST /checkout, dispatch outcome ──────────────────────────
  const runCheckout = React.useCallback(
    async (id: string): Promise<void> => {
      if (!bffBaseUrl) return;
      const ac = new AbortController();
      const url = buildBffApiUrl(bffBaseUrl, `/documents/${encodeURIComponent(id)}/checkout`);

      // eslint-disable-next-line no-console
      console.info('[ComposeWorkspace] SPE check-out requested', {
        sprkDocumentId: id,
        sessionId,
      });
      dispatch({ kind: 'checkoutRequested' });

      try {
        const response = await authenticatedFetch(url, {
          method: 'POST',
          signal: ac.signal,
        });

        if (ac.signal.aborted) return;

        if (response.ok) {
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] SPE check-out acquired', {
            sprkDocumentId: id,
            status: response.status,
          });
          dispatch({ kind: 'checkoutAcquired' });
          return;
        }

        if (response.status === 409) {
          // 409 — cross-user only (same-user idempotent re-checkout returns 200).
          let lockedBy: ComposeCheckoutLockedByInfo = {
            id: '',
            name: 'Unknown user',
            checkedOutAt: null,
          };
          try {
            const body = (await response.json()) as {
              error?: string;
              detail?: string;
              checkedOutBy?: { id?: string; name?: string; email?: string | null };
              checkedOutAt?: string | null;
            };
            lockedBy = {
              id: body.checkedOutBy?.id ?? '',
              name: body.checkedOutBy?.name ?? 'Another user',
              checkedOutAt: body.checkedOutAt ?? null,
            };
          } catch {
            // Body parse failure — fall through with default unknown-user info.
          }
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] SPE check-out conflict', {
            sprkDocumentId: id,
            lockedByName: lockedBy.name,
            checkedOutAt: lockedBy.checkedOutAt,
          });
          dispatch({ kind: 'checkoutConflict', lockedBy });
          return;
        }

        const failureMessage =
          response.status === 404
            ? 'This document is not yet recorded in Spaarke. The lock will be acquired after first save.'
            : response.status === 403
              ? 'You do not have permission to lock this document.'
              : `Could not acquire document lock (HTTP ${response.status}). You may continue editing — changes will save normally.`;
        dispatch({ kind: 'checkoutFailed', failureMessage });
      } catch (err) {
        if (ac.signal.aborted) return;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({
          kind: 'checkoutFailed',
          failureMessage: `Could not acquire document lock: ${message}`,
        });
      }
    },
    [bffBaseUrl, dispatch, sessionId]
  );

  // Stable ref so the probe effect can call the latest runCheckout without
  // being re-created on every render.
  const runCheckoutRef = React.useRef(runCheckout);
  React.useEffect(() => {
    runCheckoutRef.current = runCheckout;
  }, [runCheckout]);

  // ── Probe-before-acquire orchestration effect ──────────────────────────────
  // Triggers when:
  //   1. status === 'loaded'
  //   2. checkoutStatus is 'idle' (initial) or 'skipped' (Path B → Path A promotion)
  //   3. documentRef + sprkDocumentId present + bffBaseUrl configured
  React.useEffect(() => {
    if (status !== 'loaded') return;
    if (checkoutStatus !== 'idle' && checkoutStatus !== 'skipped') return;
    if (!documentRef) return;

    if (!bffBaseUrl) {
      dispatch({
        kind: 'checkoutFailed',
        failureMessage: 'BFF base URL is not configured. Lock could not be acquired.',
      });
      return;
    }

    if (!sprkDocumentId) {
      // Path B ephemeral: no sprkDocumentId yet. Only transition once.
      if (checkoutStatus === 'idle') {
        dispatch({ kind: 'checkoutSkipped' });
      }
      return;
    }

    const ac = new AbortController();

    const probeUrl = buildBffApiUrl(
      bffBaseUrl,
      `/documents/${encodeURIComponent(sprkDocumentId)}/checkout-status`
    );

    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] SPE check-out probe requested', {
      sprkDocumentId,
      sessionId,
    });
    dispatch({ kind: 'checkoutProbeRequested' });

    (async () => {
      // ── Step 1: Probe checkout-status ────────────────────────────────────
      let probeIsCurrentUser = false;
      let probeCheckedOutAt: string | null = null;
      let probeSucceeded = false;
      try {
        const probeResponse = await authenticatedFetch(probeUrl, {
          method: 'GET',
          signal: ac.signal,
        });
        if (ac.signal.aborted) return;

        if (probeResponse.ok) {
          probeSucceeded = true;
          try {
            const probeBody = (await probeResponse.json()) as {
              isCheckedOut?: boolean;
              checkedOutBy?: { id?: string; name?: string } | null;
              checkedOutAt?: string | null;
              isCurrentUser?: boolean;
            };
            probeIsCurrentUser =
              probeBody.isCheckedOut === true && probeBody.isCurrentUser === true;
            probeCheckedOutAt = probeBody.checkedOutAt ?? null;
            // eslint-disable-next-line no-console
            console.info('[ComposeWorkspace] SPE check-out probe result', {
              sprkDocumentId,
              isCheckedOut: probeBody.isCheckedOut,
              isCurrentUser: probeBody.isCurrentUser,
            });
          } catch {
            probeSucceeded = false;
          }
        } else {
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] SPE check-out probe non-OK', {
            sprkDocumentId,
            status: probeResponse.status,
          });
        }
      } catch (err) {
        if (ac.signal.aborted) return;
        const message = err instanceof Error ? err.message : String(err);
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] SPE check-out probe error', {
          sprkDocumentId,
          error: message,
        });
      }

      if (ac.signal.aborted) return;

      // ── Step 2: Branch on probe result ──────────────────────────────────
      if (probeSucceeded && probeIsCurrentUser) {
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] SPE check-out same-user multi-tab conflict detected', {
          sprkDocumentId,
          checkedOutAt: probeCheckedOutAt,
        });
        dispatch({
          kind: 'checkoutSameUserConflict',
          checkedOutAt: probeCheckedOutAt,
        });
        return;
      }

      // ── Step 3: No same-user conflict → proceed with /checkout ──────────
      await runCheckoutRef.current(sprkDocumentId);
    })();

    return () => ac.abort();
    // Dependencies: re-evaluate on status / sprkDocumentId / bffBaseUrl. The
    // idle/skipped guard prevents double-fire.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, sprkDocumentId, bffBaseUrl, checkoutStatus]);

  // ── forceCloseAndAcquire: POST /discard → broadcast → runCheckout ──────────
  const forceCloseAndAcquire = React.useCallback(async (): Promise<void> => {
    if (!sprkDocumentId || !bffBaseUrl) {
      dispatch({
        kind: 'checkoutFailed',
        failureMessage: 'Cannot force-close: missing document id or BFF configuration.',
      });
      return;
    }

    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Force-close other session', {
      sprkDocumentId,
    });
    dispatch({ kind: 'checkoutDiscarding' });

    const discardUrl = buildBffApiUrl(
      bffBaseUrl,
      `/documents/${encodeURIComponent(sprkDocumentId)}/discard`
    );

    try {
      const discardResponse = await authenticatedFetch(discardUrl, { method: 'POST' });
      if (!discardResponse.ok) {
        if (discardResponse.status === 400) {
          // Lock already released between probe and discard — race-but-OK.
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] Discard 400 — lock already released, proceeding');
        } else {
          const failureMessage =
            discardResponse.status === 403
              ? 'You do not have permission to release this lock.'
              : `Could not force-close other session (HTTP ${discardResponse.status}).`;
          dispatch({ kind: 'checkoutFailed', failureMessage });
          return;
        }
      }

      // eslint-disable-next-line no-console
      console.info('[ComposeWorkspace] Discard succeeded, posting force-closed message', {
        sprkDocumentId,
      });
      postForceClosed?.();

      // Now acquire a fresh lock in this tab.
      await runCheckout(sprkDocumentId);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      dispatch({
        kind: 'checkoutFailed',
        failureMessage: `Could not force-close other session: ${message}`,
      });
    }
  }, [sprkDocumentId, bffBaseUrl, dispatch, postForceClosed, runCheckout]);

  // ── discardAndCancel: "Cancel — close this tab" ────────────────────────────
  const discardAndCancel = React.useCallback((): void => {
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Cancel');
    dispatch({ kind: 'checkoutCancelled' });
  }, [dispatch]);

  return { runCheckout, forceCloseAndAcquire, discardAndCancel };
}
