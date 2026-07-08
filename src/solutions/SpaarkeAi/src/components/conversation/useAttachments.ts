/**
 * useAttachments — attachment-chip lifecycle mirror + promotion queue for the
 * ConversationPane host. Extracted from ConversationPane.tsx by
 * ai-architecture-redesign-r1 task 045 (FR-P3-06 thin-host decomposition).
 *
 * Owns (semantics preserved verbatim):
 *   - The read-only chip mirror keyed off SprkChat's `onAttachmentsChanged`
 *     (SprkChat's `useChatFileAttachment` still owns the chip lifecycle).
 *   - Debounced inline file-confirmation messages on ready transitions
 *     (R5 task 020 / D2-11) + `context.files_staged` PaneEventBus dispatch.
 *   - Held-file capture (`onAttachmentReady`) + the R7 12.3a AUTO-PROMOTE
 *     effect: sequential `/documents` promotion with retry (max 2 attempts,
 *     1 s backoff; G-P1 Defect-2/3 hardening — parallel POSTs raced the
 *     session-manifest read-modify-write server-side).
 *   - The per-file remove cascade (`onAttachmentRemoved`).
 *   - `sessionAttachmentCount` — manifest-promoted ∪ composer-ready (G-P1
 *     round-2 fix: the composer strip empties on stream completion while the
 *     session manifest still holds the files; attachment-requiring chips gate
 *     on the SESSION count).
 *   - `handleBeforeSendMessage` — outbound tracking + the once-per-turn
 *     multi-file combined-summary interjection (R5 FR-03).
 *
 * ADR-015: logs carry chip ids/filenames/status codes only — never extracted
 * text or raw error messages. ADR-028: `authenticatedFetch` for one-shot BFF
 * calls. ADR-039: NO client-side intent detection — natural language flows to
 * the agent turn unchanged; the deterministic interjection is presentation.
 */

import * as React from "react";
import type { AttachmentChip, ChatAttachment, IChatMessage } from "@spaarke/ui-components";
import type { ContextPaneEvent } from "@spaarke/ai-widgets";
import type { EventBatchMachine } from "./useEventBatch";
import {
  routeSummarizeIntent,
  buildFileConfirmationMessage,
  buildMultiFileSummarizeInterjection,
  makeLocalAssistantMessage,
} from "./summarizeRouting";

const READY_CONFIRMATION_DEBOUNCE_MS = 250;
const MAX_PROMOTE_ATTEMPTS = 2;
const PROMOTE_RETRY_DELAY_MS = 1000;

export interface AttachmentsDeps {
  bffBaseUrl: string;
  chatSessionId: string | null;
  /** Whether the host has an active workspace document context. */
  hasActiveWorkspaceDocument: boolean;
  authenticatedFetch: (input: string, init?: RequestInit) => Promise<Response>;
  /** PaneEventBus dispatch (context channel `files_staged`). */
  dispatch: (channel: "context", event: ContextPaneEvent) => void;
  /** Single-slot injection (ready confirmations + interjections). */
  inject: (message: IChatMessage) => void;
  /** The Event-path batching machine (membership/settlement seams). */
  eventBatch: EventBatchMachine;
}

export interface AttachmentsController {
  attachmentChips: AttachmentChip[];
  /** Visible chip count (extracting + ready + error) — "files attached" intent. */
  uploadedFileCount: number;
  /** Manifest-promoted ∪ composer-ready — the Click-precondition count. */
  sessionAttachmentCount: number;
  /** Count of chips promoted to server-side session files ("N indexed"). */
  promotedCount: number;
  handleAttachmentsChanged: (chips: AttachmentChip[]) => void;
  handleAttachmentRemoved: (chip: AttachmentChip, index: number) => void;
  handleAttachmentReady: (attachment: ChatAttachment) => void;
  handleBeforeSendMessage: (messageText: string) => void;
  /** Session-created reset for all per-session refs + promotion state. */
  resetForSession: () => void;
}

export function useAttachments(deps: AttachmentsDeps): AttachmentsController {
  const {
    bffBaseUrl,
    chatSessionId,
    hasActiveWorkspaceDocument,
    authenticatedFetch,
    dispatch,
    inject,
    eventBatch,
  } = deps;
  // Destructured stable methods (each is a stable useCallback inside
  // useEventBatch) so dependency arrays below key on the functions, not the
  // controller object identity (Step 9.5 review — restores the monolith's
  // effect-timing fidelity).
  const {
    noteChipsChanged,
    noteChipRemoved,
    markEventFilePromotionFailed,
    queueDocumentUploadedEvent,
    noteOutboundMessage,
  } = eventBatch;

  const [attachmentChips, setAttachmentChips] = React.useState<AttachmentChip[]>([]);
  const [promotedChipIds, setPromotedChipIds] = React.useState<ReadonlySet<string>>(
    () => new Set<string>()
  );

  // Held original Files (keyed by filename) so the auto-promote effect can
  // POST multipart binary. SprkChat forwards the original `File` reference
  // through `ChatAttachment.file`; TXT/MD fall back to a synthetic File.
  const heldFilesRef = React.useRef<Map<string, File>>(new Map());

  // Once-per-id tracking for ready-transition side effects.
  const dispatchedReadyIdsRef = React.useRef<Set<string>>(new Set());
  const confirmedReadyIdsRef = React.useRef<Set<string>>(new Set());

  // Ready-batch debounce — one consolidated "I have your N files" message.
  const readyConfirmationTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const pendingConfirmFilenamesRef = React.useRef<string[]>([]);

  // Once-per-turn guard for the multi-file combined-summary interjection.
  const emittedSummarizeInterjectionKeysRef = React.useRef<Set<string>>(new Set());

  // Promotion concurrency + attempt bookkeeping (R7 12.3a + 2026-07-04 hardening).
  const pendingPromotionIdsRef = React.useRef<Set<string>>(new Set());
  const promotionAttemptCountRef = React.useRef<Map<string, number>>(new Map());

  const uploadedFileCount = attachmentChips.length;

  const sessionAttachmentCount = React.useMemo(() => {
    const ids = new Set<string>(promotedChipIds);
    for (const chip of attachmentChips) {
      if (chip.status === "ready") ids.add(chip.id);
    }
    return ids.size;
  }, [attachmentChips, promotedChipIds]);

  // Cleanup the debounce timer on unmount.
  React.useEffect(() => {
    return () => {
      if (readyConfirmationTimerRef.current !== null) {
        clearTimeout(readyConfirmationTimerRef.current);
      }
    };
  }, []);

  /** SprkChat `onAttachmentsChanged` — mirror + ready-transition side effects. */
  const handleAttachmentsChanged = React.useCallback(
    (chips: AttachmentChip[]) => {
      setAttachmentChips(chips);

      // Detect ready transitions (once per id; the chip array alone can't
      // express transitions — a chip is 'ready' on its FIRST ready render).
      const readyChipsThisTick: AttachmentChip[] = [];
      for (const chip of chips) {
        if (chip.status !== "ready") continue;
        if (dispatchedReadyIdsRef.current.has(chip.id)) continue;
        readyChipsThisTick.push(chip);
        dispatchedReadyIdsRef.current.add(chip.id);
      }

      // Prune per-id refs for removed chips so re-add re-fires.
      const currentIds = new Set(chips.map((c) => c.id));
      for (const id of Array.from(dispatchedReadyIdsRef.current)) {
        if (!currentIds.has(id)) dispatchedReadyIdsRef.current.delete(id);
      }
      for (const id of Array.from(confirmedReadyIdsRef.current)) {
        if (!currentIds.has(id)) confirmedReadyIdsRef.current.delete(id);
      }

      // Event-path gesture membership (count-complete batching, task 022b).
      noteChipsChanged(chips);

      // Side effect 1: debounced inline confirmation message.
      if (readyChipsThisTick.length > 0) {
        for (const chip of readyChipsThisTick) {
          if (confirmedReadyIdsRef.current.has(chip.id)) continue;
          confirmedReadyIdsRef.current.add(chip.id);
          pendingConfirmFilenamesRef.current.push(chip.filename);
        }
        if (readyConfirmationTimerRef.current !== null) {
          clearTimeout(readyConfirmationTimerRef.current);
        }
        readyConfirmationTimerRef.current = setTimeout(() => {
          const filenames = pendingConfirmFilenamesRef.current;
          pendingConfirmFilenamesRef.current = [];
          readyConfirmationTimerRef.current = null;
          const body = buildFileConfirmationMessage(filenames);
          if (body !== null) {
            inject(makeLocalAssistantMessage(body));
          }
        }, READY_CONFIRMATION_DEBOUNCE_MS);
      }

      // Side effect 2: `context.files_staged` dispatch (R5 task 016; ADR-030
      // typed cast at the dispatch boundary).
      if (readyChipsThisTick.length > 0) {
        const payload: ContextPaneEvent = {
          type: "files_staged",
          stagedFileIds: readyChipsThisTick.map((c) => c.id),
        };
        dispatch("context", payload);
      }
    },
    [dispatch, inject, noteChipsChanged]
  );

  /**
   * SprkChat `onAttachmentRemoved` — per-file dismiss cascade. Per-file
   * manifest/index cleanup endpoints do not exist yet (R5 task 020 deferral);
   * session-end cleanup (R5 task 007 HostedService) reconciles. Logged so the
   * gap stays observable.
   */
  const handleAttachmentRemoved = React.useCallback(
    (chip: AttachmentChip, _index: number) => {
      dispatchedReadyIdsRef.current.delete(chip.id);
      confirmedReadyIdsRef.current.delete(chip.id);
      noteChipRemoved(chip.id);
      heldFilesRef.current.delete(chip.filename);
      setPromotedChipIds((prev) => {
        if (!prev.has(chip.id)) return prev;
        const next = new Set(prev);
        next.delete(chip.id);
        return next;
      });

      if (chatSessionId !== null) {
        // eslint-disable-next-line no-console
        console.info(
          "[ConversationPane] file-chip dismissed; awaiting per-file cleanup endpoint",
          { sessionId: chatSessionId, fileId: chip.id, filename: chip.filename }
        );
      }
    },
    [chatSessionId, noteChipRemoved]
  );

  /**
   * SprkChat `onAttachmentReady` — capture the original `File` (preferred
   * path: `ChatAttachment.file`; fallback: synthetic File from textContent,
   * which works for TXT/MD but not PDF/DOCX) so the auto-promote effect can
   * POST multipart binary.
   */
  const handleAttachmentReady = React.useCallback((attachment: ChatAttachment) => {
    try {
      const heldFile: File =
        attachment.file ??
        new File([attachment.textContent], attachment.filename, {
          type: attachment.contentType || "text/plain",
        });
      heldFilesRef.current.set(attachment.filename, heldFile);
    } catch {
      // Defensive: File construction failed — the promotion step surfaces a
      // descriptive error; the user can fall back to the upload prompt flow.
    }
  }, []);

  /**
   * Auto-promote ready chips (R7 Wave 12.3 Phase 12.3a): promote each ready
   * chip once, as soon as a session id exists and a held File is available.
   * Sequential (never parallel) per the G-P1 manifest-race hardening.
   */
  React.useEffect(() => {
    if (chatSessionId === null) return;

    const skipReasons = attachmentChips.map((c) => {
      if (c.status !== "ready") return { id: c.id, name: c.filename, skip: `status=${c.status}` };
      if (promotedChipIds.has(c.id)) return { id: c.id, name: c.filename, skip: "already-promoted" };
      if (pendingPromotionIdsRef.current.has(c.id)) return { id: c.id, name: c.filename, skip: "pending" };
      if (!heldFilesRef.current.has(c.filename)) return { id: c.id, name: c.filename, skip: "no-heldFile" };
      return { id: c.id, name: c.filename, skip: null as string | null };
    });
    const eligible = skipReasons.filter((r) => r.skip === null);
    if (attachmentChips.length > 0) {
      // ADR-015 tier-1 safe — filenames + statuses only.
      console.log(
        "[ConversationPane] auto-promote scan: chips=%d eligible=%d skipped=%o",
        attachmentChips.length,
        eligible.length,
        skipReasons.filter((r) => r.skip !== null).map((r) => ({ file: r.name, why: r.skip }))
      );
    }
    if (eligible.length === 0) return;

    const documentsUrl = `${bffBaseUrl.replace(/\/$/, "")}/api/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/documents`;

    // Mark all eligible chips pending UP FRONT so an overlapping effect run
    // cannot double-start one.
    const queue: Array<{ chipId: string; chipFilename: string; attemptNumber: number }> = [];
    for (const { id: chipId, name: chipFilename } of eligible) {
      if (!heldFilesRef.current.has(chipFilename)) continue; // defensive re-check
      pendingPromotionIdsRef.current.add(chipId);
      const attemptNumber = (promotionAttemptCountRef.current.get(chipId) ?? 0) + 1;
      promotionAttemptCountRef.current.set(chipId, attemptNumber);
      queue.push({ chipId, chipFilename, attemptNumber });
    }

    const promoteOne = async (
      chipId: string,
      chipFilename: string,
      attemptNumber: number
    ): Promise<void> => {
      const heldFile = heldFilesRef.current.get(chipFilename);
      if (!heldFile) {
        pendingPromotionIdsRef.current.delete(chipId);
        return;
      }
      try {
        console.log(
          "[ConversationPane] /documents promote attempt %d/%d — filename:%s heldFileSize:%d heldFileType:%s",
          attemptNumber,
          MAX_PROMOTE_ATTEMPTS,
          chipFilename,
          heldFile.size,
          heldFile.type
        );
        const form = new FormData();
        form.append("file", heldFile, heldFile.name);
        const response = await authenticatedFetch(documentsUrl, { method: "POST", body: form });
        if (!response.ok) {
          console.error(
            "[ConversationPane] /documents promote failed — attempt:%d status:%d filename:%s",
            attemptNumber,
            response.status,
            chipFilename
          );
          // Retry on 5xx / network only — 4xx won't succeed on retry.
          const shouldRetry =
            attemptNumber < MAX_PROMOTE_ATTEMPTS && (response.status >= 500 || response.status === 0);
          if (shouldRetry) {
            await new Promise((resolve) => setTimeout(resolve, PROMOTE_RETRY_DELAY_MS));
            pendingPromotionIdsRef.current.delete(chipId);
            return;
          }
          // Permanent failure — settle the chip out of the Event gesture batch.
          markEventFilePromotionFailed(chipId);
          return;
        }
        console.log(
          "[ConversationPane] /documents promote OK — attempt:%d filename:%s",
          attemptNumber,
          chipFilename
        );
        promotionAttemptCountRef.current.delete(chipId);
        setPromotedChipIds((prev) => {
          if (prev.has(chipId)) return prev;
          const next = new Set(prev);
          next.add(chipId);
          return next;
        });
        // The 202 body's documentId is the session-file id the Event
        // endpoint's `fileIds` contract requires. Tolerant parse: a missing
        // body skips the Event leg (promotion itself succeeded).
        try {
          const uploadJson = (await response.json()) as { documentId?: string };
          const documentId =
            typeof uploadJson?.documentId === "string" && uploadJson.documentId.length > 0
              ? uploadJson.documentId
              : null;
          if (documentId !== null) {
            queueDocumentUploadedEvent(chipId, documentId);
          } else {
            markEventFilePromotionFailed(chipId);
          }
        } catch {
          markEventFilePromotionFailed(chipId);
        }
      } catch (err) {
        // ADR-015: error name + classification only, never the message.
        const errName = err instanceof Error ? err.name : "unknown";
        const errKind = err instanceof TypeError ? "network-or-cors" : errName;
        console.error(
          "[ConversationPane] /documents promote threw — attempt:%d filename:%s errName:%s errKind:%s",
          attemptNumber,
          chipFilename,
          errName,
          errKind
        );
        const shouldRetry = attemptNumber < MAX_PROMOTE_ATTEMPTS && errKind === "network-or-cors";
        if (shouldRetry) {
          await new Promise((resolve) => setTimeout(resolve, PROMOTE_RETRY_DELAY_MS));
          pendingPromotionIdsRef.current.delete(chipId);
          return;
        }
        markEventFilePromotionFailed(chipId);
      } finally {
        pendingPromotionIdsRef.current.delete(chipId);
      }
    };

    void (async () => {
      for (const item of queue) {
        await promoteOne(item.chipId, item.chipFilename, item.attemptNumber);
      }
    })();
  }, [
    chatSessionId,
    attachmentChips,
    promotedChipIds,
    authenticatedFetch,
    bffBaseUrl,
    queueDocumentUploadedEvent,
    markEventFilePromotionFailed,
  ]);

  /**
   * SprkChat `onBeforeSendMessage` — synchronous BEFORE-send hook. Tracks the
   * outbound message (typed-command twin + playbook-options originalMessage)
   * and emits the once-per-turn multi-file combined-summary interjection.
   *
   * ADR-039 (task 023 / FR-P1-04): NO client-side intent detection here —
   * natural language flows to the agent turn unchanged; chips dispatch by
   * binding_id (Click); uploads fire Event-path rules server-side.
   */
  const handleBeforeSendMessage = React.useCallback(
    (messageText: string): void => {
      noteOutboundMessage(messageText);

      const decision = routeSummarizeIntent(messageText, {
        uploadedFileCount,
        hasActiveWorkspaceDocument,
      });

      // Only the session-files branch with a multi-file payload emits the
      // combined-summary interjection (single-file uses the per-file affordance).
      if (decision.kind !== "session-files") return;
      if (uploadedFileCount < 2) return;

      const readyIds = attachmentChips
        .filter((c) => c.status === "ready")
        .map((c) => c.id)
        .sort()
        .join("|");
      const turnKey = `${messageText.trim().toLowerCase()}::${readyIds}`;
      if (emittedSummarizeInterjectionKeysRef.current.has(turnKey)) return;
      emittedSummarizeInterjectionKeysRef.current.add(turnKey);

      const interjectionBody = buildMultiFileSummarizeInterjection(uploadedFileCount);
      if (interjectionBody === null) return;

      inject(makeLocalAssistantMessage(interjectionBody));
    },
    [noteOutboundMessage, uploadedFileCount, hasActiveWorkspaceDocument, attachmentChips, inject]
  );

  /** Session-created reset (R5 task 020 / 036 semantics preserved). */
  const resetForSession = React.useCallback((): void => {
    emittedSummarizeInterjectionKeysRef.current.clear();
    dispatchedReadyIdsRef.current.clear();
    confirmedReadyIdsRef.current.clear();
    pendingConfirmFilenamesRef.current = [];
    heldFilesRef.current.clear();
    setPromotedChipIds(new Set());
    if (readyConfirmationTimerRef.current !== null) {
      clearTimeout(readyConfirmationTimerRef.current);
      readyConfirmationTimerRef.current = null;
    }
  }, []);

  // Stable controller identity (Step 9.5 review) — changes only when the
  // underlying state values change.
  const promotedCount = promotedChipIds.size;
  return React.useMemo(
    () => ({
      attachmentChips,
      uploadedFileCount,
      sessionAttachmentCount,
      promotedCount,
      handleAttachmentsChanged,
      handleAttachmentRemoved,
      handleAttachmentReady,
      handleBeforeSendMessage,
      resetForSession,
    }),
    [
      attachmentChips,
      uploadedFileCount,
      sessionAttachmentCount,
      promotedCount,
      handleAttachmentsChanged,
      handleAttachmentRemoved,
      handleAttachmentReady,
      handleBeforeSendMessage,
      resetForSession,
    ]
  );
}
