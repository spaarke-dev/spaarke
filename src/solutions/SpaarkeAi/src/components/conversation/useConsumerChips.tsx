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
 *
 * task 043 / FR-G1 (Suggested Next Steps cards + reorder-for-display): the
 * chips now render as ranked actionable CARDS (see `ConsumerChips.tsx`'s
 * pill→card visual upgrade) instead of a flat pill strip, with:
 *   - a deterministic, preference-keyed DISPLAY reorder applied via
 *     `reorderChipsForDisplay` (`chipDisplayOrder.ts`) BEFORE render — pure
 *     display-layer sort, no model call, never adds/removes/grants a chip
 *     (ADR-039). See that module's header for the client-preference-source
 *     GAP this task surfaced (no preference is wired through today; the
 *     reorder degrades deterministically to the server-declared order).
 *   - an optional trailing "More" card (`deps.openLibraryModal`) that opens
 *     the EXISTING playbook/capability library modal — no new modal surface.
 */

import * as React from "react";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
import {
  createConsumerDispatcher,
  parseConsumerChips,
  launchSurface,
  type ConsumerChip,
  type DispatchWorkspaceEvent,
  type ResolvedLookup,
} from "@spaarke/ui-components";
import type { IChatMessage } from "@spaarke/ui-components";
import { ConsumerChips } from "./ConsumerChips";
import { reorderChipsForDisplay, type ChipDisplayPreference } from "./chipDisplayOrder";
import {
  formatEventOutputMarkdown,
  isCorrespondenceDraft,
  buildCorrespondenceComposeHtml,
} from "./DocumentUploadedEventStream";
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
  /**
   * task 043 / FR-G1: opens the EXISTING playbook/capability library modal
   * (`usePlaybookOptions.handleOpenLibraryModal`) — wired to the SNS cards'
   * trailing "More" affordance. Omitted → no "More" card renders.
   */
  openLibraryModal?: () => void;
  /**
   * UAT 2026-07-19 (create-matter-from-chip file leg): returns the session's active source
   * file ({ sessionFileId, fileName }) so a surface-launch chip (e.g. the post-classify
   * "Create a matter" chip) carries the uploaded file into the wizard — parity with the
   * TEXT path (ConversationPane.handleSurfaceLaunch). Null when no file is active.
   */
  getActiveSourceFile?: () => { sessionFileId: string; fileName?: string } | null;
  /**
   * task 043 / FR-G1: the deterministic, preference-keyed DISPLAY reorder
   * input (see `chipDisplayOrder.ts`). Omitted/absent → the chips render in
   * the server-declared order (the documented GAP — no client-accessible
   * preference source exists yet; see that module's header).
   */
  chipDisplayPreference?: ChipDisplayPreference | null;
}

export interface ConsumerChipsController {
  /** Memoized strip node for SprkChat's `transcriptFooterSlot`. */
  consumerChipsSlot: React.ReactNode;
  /** R4-10: true while a chip capability (e.g. Summarize) is running — drives a "Working…" spinner. */
  dispatching: boolean;
  /**
   * Accept a raw chip wire array from any carrier (Event stream `chips`
   * events, `consumer_chips` context events). Tolerant parse; a non-empty
   * set REPLACES the strip; empty/malformed payloads never clear it.
   */
  acceptChips: (raw: unknown) => void;
  /**
   * Dispatch an arbitrary Binding id through the SAME shared dispatchConsumer
   * path a Click-path chip uses (render dispatched output as an Assistant
   * message + re-arm the strip from the stream's next-step chips + stable
   * failure line). The single reuse seam for OTHER carriers of a
   * `sprk_playbookconsumer` Binding id — e.g. the OutcomeCard `invoke_capability`
   * next-step chips (`INextStepChip.targetBindingId`) wired by ConversationPane's
   * `onNextStep` (F-4, e2e-completion-audit 2026-07-10). Not a new dispatch
   * path — it is the existing one, hoisted so the host reuses it.
   */
  dispatchBinding: (bindingId: string, args?: { slots?: Record<string, unknown> }) => void;
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
    openLibraryModal,
    getActiveSourceFile,
    chipDisplayPreference,
  } = deps;

  const [consumerChips, setConsumerChips] = React.useState<ReadonlyArray<ConsumerChip>>([]);
  // R4-10: true while a chip capability is running (surfaced as a "Working…" spinner + input lock).
  const [dispatching, setDispatching] = React.useState(false);

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
   * The ONE dispatch+render+re-arm core: dispatchConsumer(bindingId, args) →
   * render the STORED output (ADR-040) as an Assistant message → re-arm the
   * strip from the stream's next-step chips (G-P1 Defect-1 fix). Failures
   * surface as a stable local Assistant line (ADR-019 — never raw server
   * detail). Shared by the Click-path chip strip AND any other carrier of a
   * Binding id (OutcomeCard next-step chips via `dispatchBinding` below).
   */
  const runBindingDispatch = React.useCallback(
    (
      bindingId: string,
      opts?: { slots?: Record<string, unknown>; requiresAttachments?: boolean }
    ): void => {
      // Single dispatch decision per turn: consume the chip set on click.
      setConsumerChips([]);
      // R4-10 (UAT 2026-07-19): surface a "Working…" spinner while the capability runs (the
      // summarize chip in particular had no visible progress). Cleared in .finally().
      setDispatching(true);

      // ADR-015: structural signal only — never the label/binding values.
      console.log("[ConversationPane] consumer chip dispatched");

      void dispatchConsumer(bindingId, {
        slots: opts?.slots,
        requiresAttachments: opts?.requiresAttachments,
        attachmentCount: sessionAttachmentCount,
      })
        .then((dispatched) => {
          // UAT 2026-07-19: a surface-launch capability (create-matter / create-project) hands its
          // drafted output to the WIZARD — rendering that draft in the transcript dumped raw JSON
          // ("{matter_name:…, resolvedLookups:…}") into the chat. Suppress the transcript render for
          // surface-launch; the wizard consumes the draft. Informational capabilities render as before.
          const isSurfaceLaunch =
            dispatched.disposition === "surface_launch" && !!dispatched.consumerType;
          // UAT 2026-07-19 (owner: "a Compose tab with the drafted response"): a draft-correspondence
          // result ("Draft a response" chip) is routed INTO a pre-filled Compose tab via the existing
          // `compose.draft.html` seed (no shared-lib change — the editor already seeds from draft HTML).
          // The transcript gets a short confirmation instead of the raw draft.
          const isCorrespondence = !isSurfaceLaunch && isCorrespondenceDraft(dispatched.result);
          if (isCorrespondence) {
            const html = buildCorrespondenceComposeHtml(dispatched.result as Record<string, unknown>);
            dispatch("workspace", {
              type: "widget_load",
              widgetType: "compose",
              widgetData: { compose: { draft: { html, fileName: "Draft response" } } },
              displayName: "Compose",
            } as unknown as WorkspacePaneEvent);
            enqueueAssistantMessage(
              makeLocalAssistantMessage(
                "I've opened a draft response in the Compose tab — review and edit it there."
              )
            );
          } else if (!isSurfaceLaunch && dispatched.result !== undefined && dispatched.result !== null) {
            enqueueAssistantMessage(
              makeLocalAssistantMessage(formatEventOutputMarkdown(dispatched.result))
            );
          }
          if (dispatched.chips && dispatched.chips.length > 0) {
            setConsumerChips(dispatched.chips);
          }

          // Surface-launch disposition (assistant-enhancements-r1 task 013): the
          // SERVER decided this create capability opens a pre-seeded surface. The
          // client re-materializes the drafted output into the surface via the ONE
          // shared launchSurface (task 012) — a static-registry lookup on
          // consumerType, ZERO intent detection (ADR-039). Fire-and-forget: it
          // never throws and it does NOT block the transcript render above.
          if (isSurfaceLaunch && dispatched.consumerType) {
            const draft =
              dispatched.result && typeof dispatched.result === "object"
                ? (dispatched.result as Record<string, unknown>)
                : {};
            // The constrained-field resolver's output rides `result.resolvedLookups`
            // (server enrichment — task 013 part 3; `{}` until then). Lift it out of
            // draftValues so it never lands in a form free-text field.
            const { resolvedLookups: rawResolved, ...draftValues } = draft as Record<string, unknown> & {
              resolvedLookups?: Record<string, ResolvedLookup>;
            };
            // UAT 2026-07-19 (file leg): carry the session's active source file so the wizard opens
            // with it pre-attached — parity with the TEXT path. Null → no file (as before).
            const activeFile = getActiveSourceFile?.() ?? null;
            const sessionId = getSessionId();
            const fileIds = activeFile?.sessionFileId ? [activeFile.sessionFileId] : undefined;
            void launchSurface({
              consumerType: dispatched.consumerType,
              draftValues,
              resolvedLookups: rawResolved ?? {},
              fileIds,
              source: sessionId ? { sessionId } : undefined,
              provenance: activeFile?.fileName ? { sourceFiles: [activeFile.fileName] } : undefined,
              bffBaseUrl,
            });
          }
        })
        .catch(() => {
          inject(
            makeLocalAssistantMessage("Sorry — I couldn't run that action. Please try again.")
          );
        })
        .finally(() => {
          setDispatching(false);
        });
    },
    [
      sessionAttachmentCount,
      dispatchConsumer,
      enqueueAssistantMessage,
      inject,
      bffBaseUrl,
      getActiveSourceFile,
      getSessionId,
    ]
  );

  /**
   * Chip click → dispatchConsumer(chip.bindingId, args). Prefill slots
   * forward verbatim as capability args; the attachment precondition applies.
   */
  const handleConsumerChipClick = React.useCallback(
    (chip: ConsumerChip): void => {
      runBindingDispatch(chip.bindingId, {
        slots: chip.prefillSlots,
        requiresAttachments: chip.requiresAttachments,
      });
    },
    [runBindingDispatch]
  );

  /**
   * Public reuse seam: dispatch an arbitrary Binding id (e.g. an OutcomeCard
   * `invoke_capability` next-step chip's `targetBindingId`) through the exact
   * same core. Next-step chips carry no attachment precondition.
   */
  const dispatchBinding = React.useCallback(
    (bindingId: string, args?: { slots?: Record<string, unknown> }): void => {
      runBindingDispatch(bindingId, { slots: args?.slots });
    },
    [runBindingDispatch]
  );

  // task 043 / FR-G1: deterministic, preference-keyed DISPLAY reorder — pure
  // sort, no model call, never adds/removes/grants a chip (ADR-039). See
  // chipDisplayOrder.ts for the client-preference-source GAP this surfaces.
  const rankedConsumerChips = React.useMemo(
    () => reorderChipsForDisplay(consumerChips, chipDisplayPreference),
    [consumerChips, chipDisplayPreference]
  );

  const consumerChipsSlot = React.useMemo(
    () => (
      <ConsumerChips
        chips={rankedConsumerChips}
        attachmentCount={sessionAttachmentCount}
        onChipClick={handleConsumerChipClick}
        onMore={openLibraryModal}
      />
    ),
    [rankedConsumerChips, sessionAttachmentCount, handleConsumerChipClick, openLibraryModal]
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
    () => ({ consumerChipsSlot, dispatching, acceptChips, dispatchBinding, resetForSession }),
    [consumerChipsSlot, dispatching, acceptChips, dispatchBinding, resetForSession]
  );
}
