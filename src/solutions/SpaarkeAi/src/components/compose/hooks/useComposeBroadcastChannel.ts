/**
 * useComposeBroadcastChannel.ts — cross-tab signaling hook for the Compose lock.
 *
 * Project:   spaarkeai-compose-r1
 * Tasks:     051 — multi-tab UX (BroadcastChannel "focus-me" + "force-closed")
 * Extracted: spaarkeai-compose-r1 R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC)
 *
 * Purpose:
 *   Owns the cross-tab signaling pattern for the Compose lock. Each Compose tab
 *   registers a BroadcastChannel keyed by `compose:lock:{sprkDocumentId}` so that:
 *     - "Go to that session" can post a `focus-me` message; the original tab
 *       (listening on the same channel) calls `window.focus()` to surface itself.
 *     - "Force-close other session and open here" posts a `force-closed`
 *       message AFTER the discard call succeeds; the original tab unmounts its
 *       editor (via `onForceClosedFromOther` callback to the reducer).
 *
 * BroadcastChannel scope: same origin only. Cross-profile / cross-incognito
 * multi-tab is NOT covered. For R1 this is acceptable.
 *
 * `window.focus()` works for same-origin tabs in Chromium and Firefox if the
 * page is the active document; if blocked, the dialog just dismisses in this
 * tab and the user switches manually. R1 best-effort.
 *
 * Constraints:
 *   - ADR-022 React 19 — hooks pattern.
 *   - CLAUDE.md §3 sub-agent write boundary — no `.claude/` writes.
 *
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx (consumer)
 */

import * as React from 'react';

/**
 * Shape of messages posted on the `compose:lock:{sprkDocumentId}` channel.
 * Discriminant `type`; `sessionId` allows defensive self-filtering (BroadcastChannel
 * does this by default, but a polyfill could leak own messages).
 */
interface ComposeLockBroadcastMessage {
  type: 'focus-me' | 'force-closed';
  sessionId?: string;
}

export interface UseComposeBroadcastChannelResult {
  /**
   * Post a `focus-me` message to sibling tabs holding the same lock. Best-effort
   * — failure is swallowed. Caller threads in its own sessionId for self-filtering.
   */
  postFocusMe: () => void;
  /**
   * Post a `force-closed` message to sibling tabs. Used AFTER a successful
   * discard call to signal sibling tabs that they no longer hold the lock.
   */
  postForceClosed: () => void;
}

/**
 * Cross-tab BroadcastChannel hook for the Compose lock.
 *
 * @param sprkDocumentId  Dataverse `sprk_documentid` of the open document.
 *                        When empty / undefined, channel is suppressed (jsdom
 *                        also lacks BroadcastChannel — defensive guard).
 * @param sessionId       ChatSession id; threaded into messages for self-filtering.
 * @param onForceClosedFromOther  Called when a SIBLING tab posts `force-closed`.
 *                                Caller should unmount the editor + transition
 *                                to the `'cancelled'` checkout status.
 *
 * @returns `postFocusMe` and `postForceClosed` functions. Stable across renders
 *          while the underlying channel is unchanged.
 */
export function useComposeBroadcastChannel(
  sprkDocumentId: string | undefined,
  sessionId: string,
  onForceClosedFromOther: () => void
): UseComposeBroadcastChannelResult {
  // Build the channel lazily per document id. Suppressed when:
  //   - BroadcastChannel API unavailable (jsdom / older browsers)
  //   - sprkDocumentId is empty (Path B ephemeral — no Dataverse row yet)
  const channel = React.useMemo<BroadcastChannel | null>(() => {
    if (typeof BroadcastChannel === 'undefined') return null;
    if (!sprkDocumentId) return null;
    return new BroadcastChannel(`compose:lock:${sprkDocumentId}`);
  }, [sprkDocumentId]);

  // Listen for sibling messages.
  React.useEffect(() => {
    if (!channel) return;
    const handler = (ev: MessageEvent) => {
      const data = ev.data as ComposeLockBroadcastMessage | undefined;
      if (!data || typeof data !== 'object' || !data.type) return;
      // Defensive: don't react to own messages (some polyfills leak).
      if (data.sessionId && data.sessionId === sessionId) return;

      if (data.type === 'focus-me') {
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] BroadcastChannel focus-me received');
        try {
          window.focus();
        } catch {
          // Browser blocked focus — no-op.
        }
      } else if (data.type === 'force-closed') {
        // eslint-disable-next-line no-console
        console.info('[ComposeWorkspace] BroadcastChannel force-closed received');
        onForceClosedFromOther();
      }
    };
    channel.addEventListener('message', handler);
    return () => {
      channel.removeEventListener('message', handler);
    };
  }, [channel, sessionId, onForceClosedFromOther]);

  // Close the channel on unmount / document change.
  React.useEffect(() => {
    return () => {
      channel?.close();
    };
  }, [channel]);

  const postFocusMe = React.useCallback((): void => {
    try {
      channel?.postMessage({ type: 'focus-me', sessionId });
    } catch {
      // Channel close races etc. — best-effort.
    }
  }, [channel, sessionId]);

  const postForceClosed = React.useCallback((): void => {
    try {
      channel?.postMessage({ type: 'force-closed', sessionId });
    } catch {
      // Best-effort.
    }
  }, [channel, sessionId]);

  return { postFocusMe, postForceClosed };
}
