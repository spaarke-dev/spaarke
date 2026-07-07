/**
 * useConsumerChips — Click entry-path controller (task 023 / FR-P1-04 /
 * ADR-039) extracted from ConversationPane.tsx by ai-architecture-redesign-r1
 * task 045 (FR-P3-06 thin-host decomposition).
 *
 * Next-step chips carry a `binding_id` from the completed Binding's
 * `sprk_chiptransitions`; a chip click flows through the ONE shared
 * `dispatchConsumer(bindingId, args)` helper (canonical SSE consumption +
 * PaneEventBus bridging INSIDE it). The client carries ZERO routing logic and
 * ZERO intent detection — the server resolves the Binding row.
 *
 * Semantics preserved verbatim:
 *   - Single dispatch decision per turn: the chip set is consumed on click and
 *     re-armed from the dispatched stream's next-step `chips` chunk.
 *   - Replace-only-when-non-empty (G-P1 Defect-1 fix): a chip-less carrier
 *     never blanks an already-rendered strip; chips clear only on click
 *     consumption or session change.
 *   - ADR-040 render-follows-store: the dispatched capability's `result` IS
 *     the stored ledger payload; it renders in the conversation surface.
 *   - Attachment-requiring chips gate on the SESSION attachment count
 *     (manifest-promoted ∪ composer-ready — G-P2 round-2 hardening).
 *   - The strip renders INLINE IN THE TRANSCRIPT via SprkChat's
 *     `transcriptFooterSlot`, memoized on the actual chip data so the
 *     slot-keyed auto-scroll fires only when chips change (G-P2 finding 1).
 */

import * as React from "react";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
import {
  createConsumerDispatcher,
  parseConsumerChips,
  type ConsumerChip,
  type DispatchWorkspaceEvent,
} from "@spaarke/ui-components";
import type { IChatMessage } from "@spaarke/ui-components";
import { ConsumerChips } from "./ConsumerChips";
import { formatEventOutputMarkdown } from "./DocumentUploadedEventStream";
import { makeLocalAssistantMessage } from "./summarizeRouting";

export interface ConsumerChipsDeps {
  bffBaseUrl: string;
  /** Fresh access-token getter (Auth v2 / ADR-028). */
  getAccessToken: () => Promise<string>;
  /** Active chat session id getter — re-read per dispatch. */
  getSessionId: () => string | null;
  /** `workspace`-channel PaneEventBus publisher. */
  dispatch: (channel: "workspace", event: WorkspacePaneEvent) => void;
  /** Session-level attachment count (empty-attachments Click precondition). */
  sessionAttachmentCount: number;
  /** Ordered Assistant-message injection (dispatched-output rendering). */
  enqueueAssistantMessage: (message: IChatMessage) => void;
  /** Single-slot injection (stable dispatch-failure line, ADR-019). */
  inject: (message: IChatMessage) => void;
}

export interface ConsumerChipsController {
  /** Memoized strip node for SprkChat's `transcriptFooterSlot`. */
  consumerChipsSlot: React.ReactNode;
  /**
   * Accept a raw chip wire array from any carrier (Event stream `chips`
   * events, `consumer_chips` context events). Tolerant parse; a non-empty
   * set REPLACES the strip; empty/malformed payloads never clear it.
   */
  acceptChips: (raw: unknown) => void;
  /** Chips are session-scoped — clear on session change. */
  resetForSession: () => void;
}

export function useConsumerChips(deps: ConsumerChipsDeps): ConsumerChipsController {
  const {
    bffBaseUrl,
    getAccessToken,
    getSessionId,
    dispatch,
    sessionAttachmentCount,
    enqueueAssistantMessage,
    inject,
  } = deps;

  const [consumerChips, setConsumerChips] = React.useState<ReadonlyArray<ConsumerChip>>([]);

  // The bound dispatcher. Stable per (bffBaseUrl, auth, bus) — the helper
  // re-reads the session id per dispatch via the getter.
  const dispatchConsumer = React.useMemo(
    () =>
      createConsumerDispatcher({
        bffBaseUrl,
        getSessionId,
        getAccessToken,
        publishPaneEvent: (channel, event: DispatchWorkspaceEvent) =>
          dispatch(channel, event as WorkspacePaneEvent),
      }),
    [bffBaseUrl, getSessionId, getAccessToken, dispatch]
  );

  /**
   * Chip click → dispatchConsumer(chip.bindingId, args). Prefill slots
   * forward verbatim as capability args; failures surface as a stable local
   * Assistant line (ADR-019 — never raw server detail).
   */
  const handleConsumerChipClick = React.useCallback(
    (chip: ConsumerChip): void => {
      // Single dispatch decision per turn: consume the chip set on click.
      setConsumerChips([]);

      // ADR-015: structural signal only — never the label/binding values.
      console.log("[ConversationPane] consumer chip dispatched");

      void dispatchConsumer(chip.bindingId, {
        slots: chip.prefillSlots,
        requiresAttachments: chip.requiresAttachments,
        attachmentCount: sessionAttachmentCount,
      })
        .then((dispatched) => {
          // Render the STORED output (ADR-040) + re-arm the strip from the
          // stream's next-step chips (G-P1 Defect-1 fix).
          if (dispatched.result !== undefined && dispatched.result !== null) {
            enqueueAssistantMessage(
              makeLocalAssistantMessage(formatEventOutputMarkdown(dispatched.result))
            );
          }
          if (dispatched.chips && dispatched.chips.length > 0) {
            setConsumerChips(dispatched.chips);
          }
        })
        .catch(() => {
          inject(
            makeLocalAssistantMessage("Sorry — I couldn't run that action. Please try again.")
          );
        });
    },
    [sessionAttachmentCount, dispatchConsumer, enqueueAssistantMessage, inject]
  );

  const consumerChipsSlot = React.useMemo(
    () => (
      <ConsumerChips
        chips={consumerChips}
        attachmentCount={sessionAttachmentCount}
        onChipClick={handleConsumerChipClick}
      />
    ),
    [consumerChips, sessionAttachmentCount, handleConsumerChipClick]
  );

  const acceptChips = React.useCallback((raw: unknown): void => {
    const parsed = parseConsumerChips(raw);
    if (parsed.length > 0) {
      setConsumerChips(parsed);
    }
  }, []);

  const resetForSession = React.useCallback((): void => {
    setConsumerChips([]);
  }, []);

  // Stable controller identity (Step 9.5 review) — changes only with the slot.
  return React.useMemo(
    () => ({ consumerChipsSlot, acceptChips, resetForSession }),
    [consumerChipsSlot, acceptChips, resetForSession]
  );
}
