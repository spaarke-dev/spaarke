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
  isComposeDraftDocument,
  buildDraftDocumentComposeSeed,
} from "./DocumentUploadedEventStream";
import { makeLocalAssistantMessage } from "./summarizeRouting";
import { isNdaReviewResult } from "./useNdaReviewAdvisoryCommentsBridge";
import {
  isLocalChip,
  buildPostDraftLocalChips,
  buildAskAboutFilesChip,
} from "./localActionChips";

/**
 * UAT round-4 (item #9): the QUICK-review-completion trigger reads `fileId`/`subDomain` back off
 * the SAME dispatch `slots` task 070's quick-scan caveat already reads `reviewDepth` from — never
 * a re-classification, never a re-fetch. `slots.fileIds` is typed `unknown` (a generic
 * `Record<string, unknown>` — the wire shape every review dispatch populates with `[fileId]`, see
 * `dispatchReview`/`dispatchDirectReview` in useAgreementReviewGate.ts); this extracts the first
 * entry defensively (never throws on a malformed/legacy payload).
 */
function extractFirstFileIdSlot(slots: Record<string, unknown> | undefined): string | undefined {
  const raw = slots?.fileIds;
  if (!Array.isArray(raw) || raw.length === 0) return undefined;
  const first = raw[0];
  return typeof first === "string" && first.length > 0 ? first : undefined;
}

/** Same defensive-extraction shape as {@link extractFirstFileIdSlot}, for a single string slot. */
function extractStringSlot(slots: Record<string, unknown> | undefined, key: string): string | undefined {
  const value = slots?.[key];
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

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
   * UAT R4-6 / R4-11: handler for a client-only "local action" chip (a chip whose
   * bindingId is `local:*` — Send as email / Save to document / Ask about these
   * files). These are NOT dispatch bindings; the host routes them to the editor
   * bridges / a prompt nudge instead of `dispatchConsumer`. See `localActionChips.ts`.
   */
  onLocalChipAction?: (actionId: string) => void;
  /**
   * UAT R5-1: extra client-only local chips to APPEND when a chip carrier delivers a card set
   * (e.g. the post-attach classify chips) — used for "Revise in Compose". Returns [] when not
   * applicable (e.g. no indexed file). Kept a getter so it reads current attachment state.
   */
  getAppendedLocalChips?: () => ReadonlyArray<ConsumerChip>;
  /**
   * UAT R5-9: notified with the raw draft-correspondence result whenever "Draft a response" runs,
   * so the host can seed the Send-as-email modal (subject / body / suggested recipients) from it.
   */
  onCorrespondenceDraft?: (result: Record<string, unknown>) => void;
  /**
   * task 043 / FR-G1: the deterministic, preference-keyed DISPLAY reorder
   * input (see `chipDisplayOrder.ts`). Omitted/absent → the chips render in
   * the server-declared order (the documented GAP — no client-accessible
   * preference source exists yet; see that module's header).
   */
  chipDisplayPreference?: ChipDisplayPreference | null;
  /**
   * D-043-01 (option c): notified with the Binding id each time a real chip dispatches, so the host
   * can record LEARNED usage that feeds `chipDisplayPreference` back on the next render. Local `local:*`
   * action chips never reach here (they route through `onLocalChipAction`). Omitted → no usage tracking.
   */
  onChipDispatched?: (bindingId: string) => void;

  /**
   * nda-r1 follow-up: notified with the TERMINAL `dispatched.result` of each Binding dispatch on the
   * chip/card path (the "Review an NDA" card dispatches through here, NOT through dispatchComposeAction).
   * The host wires this to the NDA-REVIEW advisory-comments bridge (`emitFromResult`), which is a safe
   * structural no-op for every non-NDA result shape — so the flagged clauses materialize as Compose
   * document comments instead of only rendering as raw JSON in the transcript. Omitted → no projection.
   */
  onDispatchResult?: (result: unknown) => void;

  /**
   * UAT round-4 (item #9): notified when an NDA-REVIEW dispatch completes at QUICK depth — the
   * "Rerun a full analysis" card trigger seam (`useRerunFullAnalysisCard.ts`). Carries the SAME
   * `fileId`/`subDomain` this dispatch's own `slots` already carried (no re-classification, no
   * re-upload). NEVER fires for a `thorough` (or absent) `reviewDepth` — no card for an
   * already-thorough review. Omitted → no card offered.
   */
  onQuickReviewComplete?: (info: { fileId: string; subDomain?: string }) => void;
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
   *
   * task 021 (FR-08 "both" composite dispatch): returns the settled Promise so a
   * caller that must run MULTIPLE dispatches SEQUENTIALLY (ADR-016 — one pack's
   * review at a time, never concurrent) can `await` completion before firing the
   * next one. Every EXISTING caller (chip clicks, effects) calls this as a bare
   * statement and ignores the return value — this widening is purely additive.
   * `opts.resultLabel`, when supplied, is threaded into the NDA-REVIEW-shaped
   * completion message (see `runBindingDispatch`) so a per-pack outcome can be
   * labelled ("...under the **Employment** lens...") instead of the generic
   * "the NDA" phrasing; omitted preserves the exact original message.
   *
   * agreements-r1 task 031 (DEF-09 routing): `opts.sessionIdOverride`, when supplied,
   * is threaded verbatim to `dispatchConsumer`'s own `sessionIdOverride` (the SAME
   * additive per-dispatch session-target the Compose EDIT toolbar path already uses —
   * `ConversationPane.dispatchComposeAction`) so THIS dispatch's `compose`-disposition
   * SessionOutput writes into a caller-chosen session (e.g. the agreement-review's
   * DOCUMENT session) instead of the bound chat session. Omitted preserves the exact
   * original (chat-session) behavior for every pre-031 caller.
   */
  dispatchBinding: (
    bindingId: string,
    args?: { slots?: Record<string, unknown>; resultLabel?: string; sessionIdOverride?: string }
  ) => Promise<void>;
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
    onLocalChipAction,
    getAppendedLocalChips,
    onDispatchResult,
    onCorrespondenceDraft,
    chipDisplayPreference,
    onChipDispatched,
    onQuickReviewComplete,
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
      opts?: {
        slots?: Record<string, unknown>;
        requiresAttachments?: boolean;
        /**
         * task 021 (FR-08 "both" composite dispatch): an optional per-pack label
         * ("Employment", "NDA") threaded into the NDA-REVIEW-shaped completion message
         * below, so a sequential multi-pack run reads as distinct per-pack outcomes
         * instead of N identical "the NDA" lines. Omitted preserves the exact original
         * message (every pre-021 caller).
         */
        resultLabel?: string;
        /**
         * task 031 (DEF-09 routing): per-dispatch session-id override, threaded verbatim
         * to `dispatchConsumer`'s own `sessionIdOverride`. Omitted preserves the exact
         * original chat-session-bound behavior (every pre-031 caller).
         */
        sessionIdOverride?: string;
      }
    ): Promise<void> => {
      // Single dispatch decision per turn: consume the chip set on click.
      setConsumerChips([]);
      // R4-10 (UAT 2026-07-19): surface a "Working…" spinner while the capability runs (the
      // summarize chip in particular had no visible progress). Cleared in .finally().
      setDispatching(true);

      // D-043-01: record this Binding as LEARNED usage (feeds the chip-display reorder next render).
      onChipDispatched?.(bindingId);

      // ADR-015: structural signal only — never the label/binding values.
      console.log("[ConversationPane] consumer chip dispatched");

      return dispatchConsumer(bindingId, {
        slots: opts?.slots,
        requiresAttachments: opts?.requiresAttachments,
        attachmentCount: sessionAttachmentCount,
        // task 031 (DEF-09 routing): threads a caller-chosen session (e.g. the agreement-review's
        // DOCUMENT session) instead of the bound chat session. Undefined for every existing caller.
        sessionIdOverride: opts?.sessionIdOverride,
      })
        .then((dispatched) => {
          // nda-r1 follow-up: hand the terminal result to the host's advisory-comments bridge so the
          // "Review an NDA" card path (which dispatches through here, not dispatchComposeAction) also
          // materializes flagged clauses as Compose document comments. Safe no-op for non-NDA shapes.
          onDispatchResult?.(dispatched.result);
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
          // R7-4 (UAT 2026-07-21): a `compose-draft-document` result (substantial output — a brief /
          // memo / analysis) carries editor-ready `body_html`. Route it INTO a Compose tab (verbatim
          // seed) and show only a short confirmation in the transcript — never dump the full document
          // into chat. Same `compose.draft.html` seed the correspondence route uses (no shared-lib change).
          const isDraftDocument =
            !isSurfaceLaunch && !isCorrespondence && isComposeDraftDocument(dispatched.result);
          // R7-7 (UAT 2026-07-21): an NDA-REVIEW result is a large `{overallRisk, flaggedSections[]}`
          // JSON that the bridge materializes as in-document Review Notes + the Review Summary. Dumping
          // that raw JSON into the transcript is noise — suppress it and post a short completion line.
          const isNdaReview =
            !isSurfaceLaunch && !isCorrespondence && !isDraftDocument && isNdaReviewResult(dispatched.result);
          if (isCorrespondence) {
            // R5-9: hand the raw draft to the host so a subsequent "Send as email" chip can seed
            // the Email Compose modal (subject / body / suggested recipients).
            onCorrespondenceDraft?.(dispatched.result as Record<string, unknown>);
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
          } else if (isDraftDocument) {
            const { html, title } = buildDraftDocumentComposeSeed(
              dispatched.result as Record<string, unknown>
            );
            dispatch("workspace", {
              type: "widget_load",
              widgetType: "compose",
              widgetData: { compose: { draft: { html, fileName: title } } },
              displayName: "Compose",
            } as unknown as WorkspacePaneEvent);
            enqueueAssistantMessage(
              makeLocalAssistantMessage(
                `"${title}" has been prepared in the Compose tab — review and edit it there.`
              )
            );
          } else if (isNdaReview) {
            // R7-7: never render the raw analysis JSON — the findings live in the Compose Review
            // Notes + Review Summary. Just confirm completion in the transcript.
            // task 021 (FR-08 "both" composite dispatch): `opts.resultLabel`, when supplied, names
            // the pack this run was measured against so a sequential multi-pack "both" run reads as
            // distinct per-pack outcomes; omitted preserves the exact original generic wording.
            // task 070 (UAT2 review-depth selector): a Quick-depth run carries a visible caveat —
            // "Quick scan — not a full advisory review." — read back from the SAME dispatch args
            // this closure already threads (`opts.slots`), the least-invasive seam: `reviewDepth`
            // never round-trips server-side (the terminal AnalysisChunk only carries the LLM's
            // output, never the original request args — SessionDispatchOrchestrator reads
            // `reviewDepth` only to resolve the model tier, see ResolveReviewDepthModelTierOverride),
            // so the client — the only place that still knows which depth it picked — renders the
            // caveat itself. Compose.Components (AgreementReviewSummaryPanel / ComposeEditor) is
            // off-limits this wave (071's territory) and, per audit, no longer renders a live
            // risk/disclaimer banner there anyway (overallRisk is `@deprecated`/ignored) — this
            // conversation-side confirmation message is the correct, reachable seam. Absent for
            // every Thorough run (the default) — zero visible change to the pre-070 wording.
            const quickScanCaveat =
              opts?.slots?.reviewDepth === "quick" ? "**Quick scan — not a full advisory review.** " : "";
            enqueueAssistantMessage(
              makeLocalAssistantMessage(
                opts?.resultLabel
                  ? `${quickScanCaveat}I've finished reviewing under the **${opts.resultLabel}** lens. Open the **Review Summary** and the in-document **Review Notes** in the Compose tab to see the flagged clauses and assessments.`
                  : `${quickScanCaveat}I've finished reviewing the NDA. Open the **Review Summary** and the in-document **Review Notes** in the Compose tab to see the flagged clauses and assessments.`
              )
            );
            // UAT round-4 (item #9): a QUICK run just completed — offer the "Rerun a full
            // analysis" CARD (useRerunFullAnalysisCard.ts), reusing the SAME fileId/subDomain
            // this dispatch's own slots already carried (no re-classification, no re-upload).
            // Thorough runs (the default) and any non-"quick" value never reach here.
            if (opts?.slots?.reviewDepth === "quick") {
              const fileId = extractFirstFileIdSlot(opts.slots);
              if (fileId) {
                onQuickReviewComplete?.({
                  fileId,
                  subDomain: extractStringSlot(opts.slots, "subDomain"),
                });
              }
            }
          } else if (!isSurfaceLaunch && dispatched.result !== undefined && dispatched.result !== null) {
            enqueueAssistantMessage(
              makeLocalAssistantMessage(formatEventOutputMarkdown(dispatched.result))
            );
          }
          // Re-arm the strip from the completed Binding's server chips, AUGMENTED with the
          // client-only local next-actions for specific outcomes (UAT R4-6 / R4-11). The
          // dispatchable actions ("Create a matter", "Draft a response") stay server-declared
          // on the binding's sprk_chiptransitions; local chips add the non-binding companions.
          const serverChips = dispatched.chips ?? [];
          let nextChips: ReadonlyArray<ConsumerChip> = serverChips;
          if (isCorrespondence) {
            // R4-6: after "Draft a response" opens a Compose tab, offer Send-as-email +
            // Save-to-document (client bridges) before the server "Create a matter" chip.
            nextChips = [...buildPostDraftLocalChips(), ...serverChips];
          } else if (isDraftDocument) {
            // R7-4: after a drafted document opens in Compose, offer the same post-draft companions
            // (Send as email / Save to document) so the user is never left without a next step.
            nextChips = [...buildPostDraftLocalChips(), ...serverChips];
          } else if (dispatched.consumerType === "chat-summarize") {
            // R4-11: after a summary, offer "Ask about these files" alongside the server
            // Create-a-matter / Draft-a-response chips (replaces the old "Summarize again").
            // R6-1: also offer "Revise document" when a file is indexed (getAppendedLocalChips).
            nextChips = [
              ...serverChips,
              buildAskAboutFilesChip(),
              ...(getAppendedLocalChips?.() ?? []),
            ];
          }
          if (nextChips.length > 0) {
            setConsumerChips(nextChips);
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
      onCorrespondenceDraft,
      getAppendedLocalChips,
      onChipDispatched,
      onDispatchResult,
      onQuickReviewComplete,
    ]
  );

  /**
   * Chip click → dispatchConsumer(chip.bindingId, args). Prefill slots
   * forward verbatim as capability args; the attachment precondition applies.
   */
  const handleConsumerChipClick = React.useCallback(
    (chip: ConsumerChip): void => {
      // UAT R4-6 / R4-11: a `local:*` chip is a client bridge / prompt nudge, NOT a Binding —
      // consume the strip and route to the host handler instead of dispatchConsumer (which would
      // fail on a non-existent binding). See localActionChips.ts.
      if (isLocalChip(chip.bindingId)) {
        setConsumerChips([]);
        onLocalChipAction?.(chip.bindingId);
        return;
      }
      runBindingDispatch(chip.bindingId, {
        slots: chip.prefillSlots,
        requiresAttachments: chip.requiresAttachments,
      });
    },
    [runBindingDispatch, onLocalChipAction]
  );

  /**
   * Public reuse seam: dispatch an arbitrary Binding id (e.g. an OutcomeCard
   * `invoke_capability` next-step chip's `targetBindingId`) through the exact
   * same core. Next-step chips carry no attachment precondition.
   */
  const dispatchBinding = React.useCallback(
    (
      bindingId: string,
      args?: { slots?: Record<string, unknown>; resultLabel?: string; sessionIdOverride?: string }
    ): Promise<void> =>
      runBindingDispatch(bindingId, {
        slots: args?.slots,
        resultLabel: args?.resultLabel,
        sessionIdOverride: args?.sessionIdOverride,
      }),
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

  const acceptChips = React.useCallback(
    (raw: unknown): void => {
      const parsed = parseConsumerChips(raw);
      if (parsed.length > 0) {
        // R5-1: append client-only local chips (e.g. "Revise in Compose") to the delivered
        // card set so they sit in line with the post-attach cards, not as a separate button.
        const appended = getAppendedLocalChips?.() ?? [];
        setConsumerChips(appended.length > 0 ? [...parsed, ...appended] : parsed);
      }
    },
    [getAppendedLocalChips]
  );

  const resetForSession = React.useCallback((): void => {
    setConsumerChips([]);
  }, []);

  // Stable controller identity (Step 9.5 review) — changes only with the slot.
  return React.useMemo(
    () => ({ consumerChipsSlot, dispatching, acceptChips, dispatchBinding, resetForSession }),
    [consumerChipsSlot, dispatching, acceptChips, dispatchBinding, resetForSession]
  );
}
