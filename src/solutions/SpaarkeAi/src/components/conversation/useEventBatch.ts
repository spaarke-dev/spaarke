/**
 * useEventBatch — the Event entry-path count-complete batching state machine
 * (task 022b / FR-P1-03 / ADR-039) extracted from ConversationPane.tsx by
 * ai-architecture-redesign-r1 task 045 (FR-P3-06 thin-host decomposition —
 * this is the G-P1-deferred "ConversationPane batch-state extraction").
 *
 * Semantics preserved verbatim (G-P1 UAT round-1 Defect-2 fix, 2026-07-05):
 *
 *   - MEMBERSHIP: every attachment chip visible in the strip that is not in a
 *     terminal error state and not accounted to a previously-fired batch
 *     belongs to the current gesture batch.
 *   - SETTLEMENT: a chip settles when its `/documents` promotion 202 lands,
 *     permanently fails, or is removed from the strip.
 *   - FIRE: the instant every expected chip is settled — exactly ONE event
 *     POST per attach gesture. A 30 s fallback timer (anchored at the first
 *     settled promotion) bounds stuck promotions.
 *
 * The fired batch streams through the canonical `runDocumentUploadedEvent`
 * (readSseStream inside — the ONE SSE parse path, NFR-08):
 *   event_classification → classification line · event_output → the STORED
 *   ledger payload (ADR-040 render-follows-store) · event_confirmation /
 *   event_notice → message + chips · chips → next-step Click-path chips.
 * Stream/HTTP failures render ONE stable failure line (ADR-019). ADR-015:
 * logs carry structural counts/flags only.
 *
 * Outbound-message tracking also lives here: a message sent at/after the
 * gesture batch opened is the "typed command accompanying the upload" and is
 * passed VERBATIM as `typedCommand` (the SERVER decides supersede — ADR-039).
 * `getLastSentMessage` is shared with the playbook-options handlers.
 */

import * as React from "react";
import type { AttachmentChip, IChatMessage } from "@spaarke/ui-components";
import {
  runDocumentUploadedEvent,
  formatClassificationMessage,
  formatEventOutputMarkdown,
  formatNoticeMessage,
} from "./DocumentUploadedEventStream";
import { makeLocalAssistantMessage } from "./summarizeRouting";

const EVENT_BATCH_FALLBACK_MS = 30_000;

const EVENT_FAILURE_MESSAGE =
  "Sorry — I couldn't process those files automatically. You can still ask me about them.";

export interface EventBatchDeps {
  bffBaseUrl: string;
  /** Fresh access-token getter (Auth v2 / ADR-028). Never snapshotted. */
  getAccessToken: () => Promise<string>;
  /** Active chat session id, re-read at fire time (session may be created mid-gesture). */
  getSessionId: () => string | null;
  /** Ordered Assistant-message injection (useInjectionQueue.enqueue). */
  enqueueAssistantMessage: (message: IChatMessage) => void;
  /**
   * Raw chip wire array from any carrier event. The consumer-chips controller
   * performs the tolerant parse + replace-only-when-non-empty rendering.
   * Passed as a stable indirection so hook composition stays acyclic.
   */
  onChips: (raw: unknown) => void;
}

export interface EventBatchMachine {
  /**
   * Chip-strip lifecycle sync (called from `onAttachmentsChanged`): prunes
   * removed chips from all bookkeeping sets, settles terminally-errored chips
   * out of the gesture, admits new chips to the open batch (opening it if
   * needed), and runs the count-complete check.
   */
  noteChipsChanged: (chips: AttachmentChip[]) => void;
  /** A chip was dismissed — it leaves the gesture batch (count-complete must not wait). */
  noteChipRemoved: (chipId: string) => void;
  /** Settle a chip WITHOUT a documentId (permanent promotion failure / missing id). */
  markEventFilePromotionFailed: (chipId: string) => void;
  /** A `/documents` promotion 202 landed — settle the chip with its documentId. */
  queueDocumentUploadedEvent: (chipId: string, documentId: string) => void;
  /**
   * spaarkeai-compose-r2 (manual Browse ingest ceremony): fire the Event path
   * for a file that was promoted OUTSIDE the SprkChat attachment strip — a file
   * mounted into Compose via Browse and context-uploaded through
   * `registerComposeActiveDocument`'s `/documents` POST. There is no chip, so we
   * settle a synthetic single-file batch immediately: the file already lives in
   * `session.UploadedFiles` (the `/documents` call added it), so the server's
   * classify rule resolves it and streams `event_classification` → the
   * "Classified …" message, identical to an Assistant-uploaded file. Idempotency
   * (once-per-file) is the CALLER's responsibility (the upload-dedup gate).
   */
  fireForPromotedFile: (sessionFileId: string) => void;
  /** Track the most recent outbound message (typed-command + playbook-options input). */
  noteOutboundMessage: (text: string) => void;
  /** Most recent outbound message text (ADR-015: never rendered, never logged). */
  getLastSentMessage: () => string;
  /**
   * True while a fired Event batch's `runDocumentUploadedEvent` SSE stream (the
   * classify → output → confirmation stream) is in flight. The host locks the
   * composer on this (#2b, UAT 2026-07-18) so a typed instruction can't be sent
   * during the classify window and silently dropped.
   */
  isEventInFlight: boolean;
  /**
   * Session-created reset: promoted ids are session-scoped so the pending
   * queue + failed set + fallback timer clear; membership + batch-open
   * timestamp are KEPT (cold-start gesture: attach → type → first send
   * creates the session while promotions are still in flight).
   */
  resetForSession: () => void;
}

export function useEventBatch(deps: EventBatchDeps): EventBatchMachine {
  const { bffBaseUrl, getAccessToken, getSessionId, enqueueAssistantMessage, onChips } = deps;

  // #2b (UAT 2026-07-18): count of in-flight Event SSE streams so the host can
  // lock the composer during the classify window. A counter (not a bool) because
  // a late/duplicate promotion can open a second batch while the first streams.
  const [inFlightEvents, setInFlightEvents] = React.useState(0);

  const pendingEventFilesRef = React.useRef<Array<{ chipId: string; documentId: string }>>([]);
  const expectedRef = React.useRef<Set<string>>(new Set());
  const failedChipIdsRef = React.useRef<Set<string>>(new Set());
  const accountedChipIdsRef = React.useRef<Set<string>>(new Set());
  const timerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const openedAtRef = React.useRef<number | null>(null);
  const lastSentMessageRef = React.useRef<string>("");
  const lastSentAtRef = React.useRef<number | null>(null);
  /** Render-free chip mirror so the fire callback can order fileIds by upload order. */
  const chipsMirrorRef = React.useRef<AttachmentChip[]>([]);
  /** Indirection so seams declared above the fire callback can trigger it (no TDZ). */
  const fireRef = React.useRef<() => void>(() => undefined);

  /** Count-complete check: fires the moment EVERY expected chip is settled. */
  const maybeFire = React.useCallback((): void => {
    const expected = expectedRef.current;
    if (expected.size === 0) return;
    const settledIds = new Set(pendingEventFilesRef.current.map((p) => p.chipId));
    for (const id of expected) {
      if (!settledIds.has(id) && !failedChipIdsRef.current.has(id)) {
        return; // at least one promotion still in flight — keep waiting.
      }
    }
    fireRef.current();
  }, []);

  /**
   * fireDocumentUploadedEvent — drains the pending promoted-document queue,
   * orders fileIds by the chip strip's upload order, resolves the accompanying
   * typed command, then consumes the Event SSE stream through the canonical
   * runDocumentUploadedEvent helper.
   */
  const fire = React.useCallback((): void => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }

    const pending = pendingEventFilesRef.current;
    pendingEventFilesRef.current = [];
    // Account every chip of this gesture so late/duplicate promotions open a
    // NEW batch instead of re-firing this one.
    for (const id of expectedRef.current) {
      accountedChipIdsRef.current.add(id);
    }
    for (const p of pending) {
      accountedChipIdsRef.current.add(p.chipId);
    }
    expectedRef.current = new Set();
    failedChipIdsRef.current = new Set();
    const openedAt = openedAtRef.current;
    openedAtRef.current = null;

    const sessionId = getSessionId();
    if (pending.length === 0 || !sessionId) return;

    // fileIds in upload order — promotions complete out of order.
    const chipOrder = new Map<string, number>(
      chipsMirrorRef.current.map((c, index) => [c.id, index])
    );
    const fileIds = pending
      .slice()
      .sort(
        (a, b) =>
          (chipOrder.get(a.chipId) ?? Number.MAX_SAFE_INTEGER) -
          (chipOrder.get(b.chipId) ?? Number.MAX_SAFE_INTEGER)
      )
      .map((p) => p.documentId);

    const typedCommand =
      openedAt !== null &&
      lastSentAtRef.current !== null &&
      lastSentAtRef.current >= openedAt &&
      lastSentMessageRef.current.trim().length > 0
        ? lastSentMessageRef.current
        : null;

    // ADR-015: structural signals only — never file ids or command text.
    console.log(
      "[ConversationPane] document-uploaded event dispatch — files:%d typedCommand:%s",
      fileIds.length,
      typedCommand !== null
    );

    setInFlightEvents((n) => n + 1);
    void runDocumentUploadedEvent({
      bffBaseUrl,
      sessionId,
      fileIds,
      typedCommand,
      getAccessToken,
      handlers: {
        onClassification: (data) => {
          enqueueAssistantMessage(makeLocalAssistantMessage(formatClassificationMessage(data)));
        },
        onOutput: (data) => {
          enqueueAssistantMessage(
            makeLocalAssistantMessage(formatEventOutputMarkdown(data.payload))
          );
        },
        onConfirmation: (data) => {
          if (typeof data.message === "string" && data.message.length > 0) {
            enqueueAssistantMessage(makeLocalAssistantMessage(data.message));
          }
        },
        onNotice: (data) => {
          enqueueAssistantMessage(makeLocalAssistantMessage(formatNoticeMessage(data)));
        },
        onChips,
        onError: (message) => {
          // Server `error` content is the safe message by contract.
          enqueueAssistantMessage(makeLocalAssistantMessage(message || EVENT_FAILURE_MESSAGE));
        },
      },
    })
      .catch(() => {
        enqueueAssistantMessage(makeLocalAssistantMessage(EVENT_FAILURE_MESSAGE));
      })
      .finally(() => {
        setInFlightEvents((n) => Math.max(0, n - 1));
      });
  }, [bffBaseUrl, getAccessToken, getSessionId, enqueueAssistantMessage, onChips]);
  fireRef.current = fire;

  const noteChipsChanged = React.useCallback(
    (chips: AttachmentChip[]): void => {
      chipsMirrorRef.current = chips;
      const currentIds = new Set(chips.map((c) => c.id));

      // Prune bookkeeping for chips removed from the strip (re-add re-fires).
      for (const id of Array.from(accountedChipIdsRef.current)) {
        if (!currentIds.has(id)) accountedChipIdsRef.current.delete(id);
      }
      for (const id of Array.from(failedChipIdsRef.current)) {
        if (!currentIds.has(id)) failedChipIdsRef.current.delete(id);
      }

      // Gesture-batch MEMBERSHIP (G-P1 Defect-2 fix): every chip not accounted
      // to a fired batch and not terminally errored joins the CURRENT batch —
      // including chips still 'extracting'. The first chip OPENS the window.
      const expected = expectedRef.current;
      let membershipChanged = false;
      for (const chip of chips) {
        if (accountedChipIdsRef.current.has(chip.id)) continue;
        if (chip.status === "error") {
          // Extraction failed — the chip can never promote; settle it out.
          if (expected.delete(chip.id)) membershipChanged = true;
          accountedChipIdsRef.current.add(chip.id);
          continue;
        }
        if (!expected.has(chip.id)) {
          expected.add(chip.id);
          membershipChanged = true;
        }
      }
      for (const id of Array.from(expected)) {
        if (!currentIds.has(id)) {
          expected.delete(id);
          failedChipIdsRef.current.delete(id);
          membershipChanged = true;
        }
      }
      if (expected.size > 0 && openedAtRef.current === null) {
        openedAtRef.current = Date.now();
      }
      if (membershipChanged) {
        maybeFire();
      }
    },
    [maybeFire]
  );

  const noteChipRemoved = React.useCallback(
    (chipId: string): void => {
      expectedRef.current.delete(chipId);
      failedChipIdsRef.current.delete(chipId);
      accountedChipIdsRef.current.delete(chipId);
      pendingEventFilesRef.current = pendingEventFilesRef.current.filter(
        (p) => p.chipId !== chipId
      );
      maybeFire();
    },
    [maybeFire]
  );

  const markEventFilePromotionFailed = React.useCallback(
    (chipId: string): void => {
      if (!expectedRef.current.has(chipId)) return;
      failedChipIdsRef.current.add(chipId);
      maybeFire();
    },
    [maybeFire]
  );

  const queueDocumentUploadedEvent = React.useCallback(
    (chipId: string, documentId: string): void => {
      pendingEventFilesRef.current.push({ chipId, documentId });
      // Defensive membership: a promotion arriving for an already-fired batch
      // (e.g. retry succeeding late) re-opens a fresh single-file batch.
      accountedChipIdsRef.current.delete(chipId);
      expectedRef.current.add(chipId);
      if (openedAtRef.current === null) {
        openedAtRef.current = Date.now();
      }
      if (timerRef.current === null) {
        timerRef.current = setTimeout(() => {
          timerRef.current = null;
          // ADR-015: structural signal only.
          console.warn(
            "[ConversationPane] event batch fallback fired — settled:%d expected:%d",
            pendingEventFilesRef.current.length,
            expectedRef.current.size
          );
          fireRef.current();
        }, EVENT_BATCH_FALLBACK_MS);
      }
      maybeFire();
    },
    [maybeFire]
  );

  const fireForPromotedFile = React.useCallback(
    (sessionFileId: string): void => {
      // Synthetic single-file batch: no chip strip involvement. A stable synthetic
      // id (never collides with a real chip id) admits the promoted file to a fresh
      // batch and settles it in the same call so `maybeFire` runs the canonical
      // `runDocumentUploadedEvent` immediately (server resolves the file from
      // session.UploadedFiles). Mirrors `queueDocumentUploadedEvent`'s settle path.
      const chipId = `compose-promoted:${sessionFileId}`;
      pendingEventFilesRef.current.push({ chipId, documentId: sessionFileId });
      accountedChipIdsRef.current.delete(chipId);
      expectedRef.current.add(chipId);
      if (openedAtRef.current === null) {
        openedAtRef.current = Date.now();
      }
      // spaarkeai-compose-r2 (compose-ingest strand fix): `expectedRef` is SHARED with the chat
      // attachment strip. If a concurrent chat gesture has a chip still 'extracting' (in `expectedRef`
      // but not yet settled), `maybeFire` will keep WAITING and the compose promoted-file batch never
      // fires. Arm the same fallback timer `queueDocumentUploadedEvent` uses so the promoted file is
      // guaranteed to settle even when a stuck chat chip strands the shared expected set. (A fallback
      // fire that accounts a still-extracting chat chip is safe — its later promotion re-opens a fresh
      // batch, matching the existing chip-path fallback semantics.)
      if (timerRef.current === null) {
        timerRef.current = setTimeout(() => {
          timerRef.current = null;
          // ADR-015: structural signal only.
          console.warn(
            "[ConversationPane] event batch fallback fired (promoted file) — settled:%d expected:%d",
            pendingEventFilesRef.current.length,
            expectedRef.current.size
          );
          fireRef.current();
        }, EVENT_BATCH_FALLBACK_MS);
      }
      maybeFire();
    },
    [maybeFire]
  );

  const noteOutboundMessage = React.useCallback((text: string): void => {
    lastSentMessageRef.current = text;
    lastSentAtRef.current = Date.now();
  }, []);

  const getLastSentMessage = React.useCallback((): string => lastSentMessageRef.current, []);

  const resetForSession = React.useCallback((): void => {
    // Promoted document ids are session-scoped — a queued batch must not fire
    // against a different session. Membership + batch-open timestamp are KEPT
    // (cold-start gesture semantics — see interface JSDoc).
    pendingEventFilesRef.current = [];
    failedChipIdsRef.current = new Set();
    // A prior session's in-flight stream (if any) decrements via its own .finally
    // (Math.max-clamped), so a hard reset to 0 here is safe and unlocks the fresh session.
    setInFlightEvents(0);
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  // Cleanup the fallback timer on unmount.
  React.useEffect(() => {
    return () => {
      if (timerRef.current !== null) {
        clearTimeout(timerRef.current);
      }
    };
  }, []);

  // Stable controller identity (Step 9.5 review): every member is a stable
  // useCallback, so consumers may safely list the controller object itself in
  // dependency arrays without re-triggering effects each render.
  return React.useMemo(
    () => ({
      noteChipsChanged,
      noteChipRemoved,
      markEventFilePromotionFailed,
      queueDocumentUploadedEvent,
      fireForPromotedFile,
      noteOutboundMessage,
      getLastSentMessage,
      isEventInFlight: inFlightEvents > 0,
      resetForSession,
    }),
    [
      noteChipsChanged,
      noteChipRemoved,
      markEventFilePromotionFailed,
      queueDocumentUploadedEvent,
      fireForPromotedFile,
      noteOutboundMessage,
      getLastSentMessage,
      inFlightEvents,
      resetForSession,
    ]
  );
}
