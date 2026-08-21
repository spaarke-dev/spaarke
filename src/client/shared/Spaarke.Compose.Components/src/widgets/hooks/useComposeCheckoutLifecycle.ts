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
/**
 * FR-S09 item 4 (r8 task 016): read an HTTP status off a thrown transport error.
 *
 * `authenticatedFetch` (ADR-028) RETURNS only when `response.ok` — every non-2xx is THROWN as an
 * `ApiError` carrying `.status`. That is why the `if (response.ok)` / `if (response.status === 409)`
 * blocks that used to live in this file could never execute: by the time control reached them the
 * response was, by construction, already a success. The conflict banner, the 404 and 403 copy, and
 * the force-close race handler were all written, reviewed, shipped — and unreachable.
 *
 * The read is STRUCTURAL rather than `err instanceof ApiError`, for the reason `classifySaveFailure`
 * documents in `ComposeWorkspace.tsx`: `instanceof` fails silently when `@spaarke/auth` resolves to
 * two copies across a bundle boundary, and a silent fall-through to the generic message is the exact
 * defect being removed here. Returns null when the throw carried no HTTP exchange (offline, DNS,
 * abort) — a genuinely different case, which the callers state differently.
 */
function readErrorStatus(err: unknown): number | null {
  const status = (err as { status?: unknown } | null | undefined)?.status;
  return typeof status === 'number' && status >= 100 && status <= 599 ? status : null;
}

/**
 * FR-S09 item 4 (r8 task 016): recover the lock holder from a thrown 409.
 *
 * The checkout endpoint's conflict body carries `checkedOutBy` + `checkedOutAt`; `authenticatedFetch`
 * parses it onto `ApiError.problemDetails` (the body advertises `status`/`title` so it is recognised
 * as ProblemDetails — see `DocumentOperationsEndpoints`). Falls back to a named-but-unknown user
 * rather than throwing: knowing SOMEONE holds the lock is the load-bearing part; the name is the
 * courtesy.
 */
function readLockedBy(err: unknown): ComposeCheckoutLockedByInfo {
  const fallback: ComposeCheckoutLockedByInfo = { id: '', name: 'Another user', checkedOutAt: null };
  const details = (err as { problemDetails?: Record<string, unknown> | null } | null | undefined)?.problemDetails;
  if (!details) return fallback;
  const by = details['checkedOutBy'] as { id?: unknown; name?: unknown } | null | undefined;
  const at = details['checkedOutAt'];
  return {
    id: typeof by?.id === 'string' ? by.id : '',
    name: typeof by?.name === 'string' && by.name ? by.name : 'Another user',
    checkedOutAt: typeof at === 'string' ? at : null,
  };
}

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

      // FR-S09 item 4 (r8 task 016): status routing lives in the CATCH, because that is where every
      // non-2xx actually arrives. The `if (response.ok)` / 409 / 404 / 403 ladder this replaces was
      // dead from the day `authenticatedFetch` began throwing — so a document locked by a colleague
      // reported "Could not acquire document lock: HTTP 409" with no name and no force-close
      // affordance, and the carefully-written conflict copy below never rendered once.
      try {
        const response = await authenticatedFetch(url, {
          method: 'POST',
          signal: ac.signal,
        });

        if (ac.signal.aborted) return;

        // Reaching here means 2xx — the ONLY thing authenticatedFetch returns.
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] SPE check-out acquired', {
          sprkDocumentId: id,
          status: response.status,
        });
        dispatch({ kind: 'checkoutAcquired' });
      } catch (err) {
        if (ac.signal.aborted) return;
        const status = readErrorStatus(err);

        // 409 — cross-user only (a same-user idempotent re-checkout returns 200). This is the branch
        // that unlocks the conflict dialog, so its absence was not cosmetic: the user had no way to
        // see WHO held the document or to take it back.
        if (status === 409) {
          const lockedBy = readLockedBy(err);
          // eslint-disable-next-line no-console
          console.info('[ComposeWorkspace] SPE check-out conflict', {
            sprkDocumentId: id,
            lockedByName: lockedBy.name,
            checkedOutAt: lockedBy.checkedOutAt,
          });
          dispatch({ kind: 'checkoutConflict', lockedBy });
          return;
        }

        const message = err instanceof Error ? err.message : String(err);
        const failureMessage =
          status === 404
            ? 'This document is not yet recorded in Spaarke. The lock will be acquired after first save.'
            : status === 403
              ? 'You do not have permission to lock this document.'
              : status !== null
                ? `Could not acquire document lock (HTTP ${status}). You may continue editing — changes will save normally.`
                : // No HTTP exchange happened at all (offline / DNS / CORS): say so rather than
                  // implying the server refused. Editing is unaffected either way.
                  `Could not acquire document lock: ${message}. You may continue editing — changes will save normally.`;
        dispatch({ kind: 'checkoutFailed', failureMessage });
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

    const probeUrl = buildBffApiUrl(bffBaseUrl, `/documents/${encodeURIComponent(sprkDocumentId)}/checkout-status`);

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

        // FR-S09 item 4 (r8 task 016): reaching this line means 2xx — the only thing authenticatedFetch
        // returns. The `if (probeResponse.ok) { ... } else { ... }` that used to wrap this block is gone
        // in both halves: the `else` could not execute, and a condition that is necessarily true is the
        // same defect wearing the opposite sign. A non-2xx lands in the catch below, which logs and
        // leaves `probeSucceeded` false — the soft-fail this probe has always wanted.
        probeSucceeded = true;
        try {
          const probeBody = (await probeResponse.json()) as {
            isCheckedOut?: boolean;
            checkedOutBy?: { id?: string; name?: string } | null;
            checkedOutAt?: string | null;
            isCurrentUser?: boolean;
          };
          probeIsCurrentUser = probeBody.isCheckedOut === true && probeBody.isCurrentUser === true;
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

    const discardUrl = buildBffApiUrl(bffBaseUrl, `/documents/${encodeURIComponent(sprkDocumentId)}/discard`);

    // FR-S09 item 4 (r8 task 016): THE defect this item is named for. The `if (!discardResponse.ok)`
    // block below was unreachable, and the case it protected is the one that actually happens: the
    // other session releases its lock between the probe and the discard, SharePoint answers 400
    // "nothing to discard", and that 400 is a SUCCESS — the lock is gone, which is what the user asked
    // for. Because the block was dead, the 400 threw instead, and the user was told "Could not
    // force-close other session" while staring at a conflict dialog that has no dismiss button. The
    // one action available to them reported failure every time it worked.
    let discarded = false;
    try {
      await authenticatedFetch(discardUrl, { method: 'POST' });
      discarded = true;
    } catch (err) {
      const status = readErrorStatus(err);
      if (status === 400) {
        // Lock already released between probe and discard — race-but-OK. Proceed to acquire.
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] Discard 400 — lock already released, proceeding');
        discarded = true;
      } else {
        const message = err instanceof Error ? err.message : String(err);
        const failureMessage =
          status === 403
            ? 'You do not have permission to release this lock.'
            : status !== null
              ? `Could not force-close other session (HTTP ${status}).`
              : `Could not force-close other session: ${message}`;
        dispatch({ kind: 'checkoutFailed', failureMessage });
      }
    }

    if (!discarded) return;

    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Discard succeeded, posting force-closed message', {
      sprkDocumentId,
    });
    postForceClosed?.();

    // Now acquire a fresh lock in this tab. `runCheckout` owns its own error handling and never
    // throws, so it is deliberately OUTSIDE the try above — a failure to re-acquire must report
    // itself as a checkout failure, not as "could not force-close".
    await runCheckout(sprkDocumentId);
  }, [sprkDocumentId, bffBaseUrl, dispatch, postForceClosed, runCheckout]);

  // ── discardAndCancel: "Cancel — close this tab" ────────────────────────────
  const discardAndCancel = React.useCallback((): void => {
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Cancel');
    dispatch({ kind: 'checkoutCancelled' });
  }, [dispatch]);

  return { runCheckout, forceCloseAndAcquire, discardAndCancel };
}
