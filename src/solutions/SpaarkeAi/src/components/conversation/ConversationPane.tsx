/**
 * ConversationPane.tsx — THIN HOST for the SpaarkeAi Assistant pane.
 * Decomposed by ai-architecture-redesign-r1 task 045 (FR-P3-06) from a
 * 3,172-line monolith to layout + session context + PaneEventBus wiring only
 * (operator budget ≤300 lines). Behaviour lives in the focused sibling
 * modules imported below — each carries the full docs for its concern
 * (Event batching, attachments/promotion, Click-path chips, trace bridge,
 * playbook selection/options, command routing, selection chip, chrome).
 *
 * Dead paths removed (verified never-invoked): `dispatchSummarizeIntent` +
 * the prompt-first `pendingSummarizeInterjection` rendering surface, and the
 * welcome `pendingMessage` prompt entry (WelcomePanel is heading-only since
 * task 068). The pure `routeSummarizeIntent` contract is re-exported below.
 *
 * @see ADR-021 (tokens) · ADR-039 (Event/Click/Text; no client routing) ·
 *      ADR-040 (render-follows-store)
 */

import * as React from "react";
import {
  Button,
  Tooltip,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
} from "@fluentui/react-components";
import { ChatRegular, ChatAddRegular, DismissRegular } from "@fluentui/react-icons";
import { PaneHeader, SprkChat, createConsumerDispatcher, RichFilePreviewDialog, createXrmNavigationService, createXrmDataService, searchUsersAndContacts, launchSurface, resolveSurfaceLaunch, launchSummarizeFilesWizard, SendEmailDialog } from "@spaarke/ui-components";
import { WelcomeStartCards } from "./WelcomeStartCards";
import { QuickStartModal } from "./QuickStartModal";
import { MemoryDialog } from "./MemoryDialog";
import { useAiSession, useDispatchPaneEvent, usePaneEvent, clearExecutionTraceBuffer } from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
// task 033 (FR-17 wizard→review auto-run bridge): the typed compose-seed shape the wizard hand-off
// listener narrows `widget_load{widgetType:'compose'}` payloads to (composeSessionId / analysisId /
// subDomain / autoRunReview — declared additively on the SAME seed every compose door already uses).
import type { ComposeWidgetSeed } from "../workspace/composeWidgetData";
import type {
  IChatMessage,
  DispatchWorkspaceEvent,
  DispatchConsumerResult,
  INextStepChip,
  ResolvedLookup,
} from "@spaarke/ui-components";
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "../WelcomePanel";
// Compose three-pane coordination — ASSISTANT leg (task 104 / E2E-R5). Typed
// receivers for Flow 2 (compose_selection_offer) + Flow 4 (compose_context_offer).
import { ComposeAssistantCoordination } from "./ComposeAssistantCoordination";
import { useShellStage, useRestoreContext, usePaneCollapseContext } from "../shell/ThreePaneShell";
import { HistoryMenu } from "./HistoryOverlay";
import { AssistantToolMenu } from "./AssistantToolMenu";
// task 042 (FR-F3 / F5) — the "My Assistant" stated-profile questionnaire + write/erase path.
import { MyAssistantDialog } from "../assistant/MyAssistantDialog";
import { useMyAssistant } from "../assistant/useMyAssistant";
import { CommandHelpPanel } from "./CommandHelpPanel";
// R5-3 (UAT 2026-07-20): HelpAffordance ("?" icon) removed from the UI; /help still opens the panel.
import { useInjectionQueue } from "./useInjectionQueue";
import { useEventBatch } from "./useEventBatch";
import { useAttachments } from "./useAttachments";
import { FileAttachSessionPrompt } from "./FileAttachSessionPrompt";
import { useConsumerChips } from "./useConsumerChips";
// spaarke-notification-spine-r1 task 051 (FR-16): the proactive-suggestion renderer branch —
// a SIBLING of the Click-path chips, subscribing to the Layer-C spine's `kind=suggestion`
// pushes via the ONE host-wide `@spaarke/notifications` client (task 021).
// UAT round-4 (item #9): "Rerun a full analysis" card — offered after a QUICK-depth agreement
// review completes. Client-local/session-turn-scoped (no outbox row, no BFF re-ground). It reuses
// the SuggestionCard presentational component, which is why SuggestionCard.tsx is retained even
// though assistant-enhancements-r2 task 001 (FR-E1) removed the spine-driven suggestion surface.
import { useRerunFullAnalysisCard } from "./useRerunFullAnalysisCard";
import { useContextEventBridge } from "./useContextEventBridge";
import { useDocQaCitationBridge } from "./useDocQaCitationBridge";
import { useNdaReviewAdvisoryCommentsBridge, isNdaReviewResult } from "./useNdaReviewAdvisoryCommentsBridge";
import { useNdaReviewRunProgress } from "./useNdaReviewRunProgress";
import { NdaReviewProgressModal } from "./NdaReviewProgressModal";
import { usePlaybookSelection } from "./usePlaybookSelection";
import { usePlaybookOptions } from "./usePlaybookOptions";
import { useCommandRouting } from "./useCommandRouting";
import { useSelectionChip } from "./useSelectionChip";
import { useSerialActionQueue, type ComposeActionRequest } from "./useSerialActionQueue";
// Deep-import the cross-pane bridge hook (not the `@spaarke/compose-components`
// barrel) so this Assistant-pane module does NOT transitively pull the TipTap
// editor widgets — mirrors ComposeEditor/ComposeWorkspace's `@spaarke/ai-widgets/events`
// deep-import rationale. Resolves in both Vite (alias → src dir) and jest.
import {
  useRegisterComposeActionDispatcher,
  useRegisterComposeActiveDocumentHandler,
  useComposeRedlineAccept,
  useComposeInsertSuggestion,
  useComposeSave,
  useRegisterComposeSaveCompletedHandler,
} from "@spaarke/compose-components/context/composeActionBridge";
import { resolveCurrentComposeLedgerRef, buildComposeApplyEvent } from "./composeApplyLeg";
// FR-17 undo/replace (task 034) — the durable ledger-supersession hook + its Assistant affordance.
import { useEditSupersession, EditSupersessionBar } from "./useEditSupersession";
import type { ComposeAssistantToWorkspaceFlow } from "@spaarke/compose-components/types/compose-contracts";
import { formatEventOutputMarkdown, toDisplayList, type EventClassificationData } from "./DocumentUploadedEventStream";
import { formatComposeActionResultMarkdown, extractComposeEditExplanation } from "./composeResultFormat";
import { makeLocalAssistantMessage, makeComposeEditControlsMessage, makeSavedToDmsMessage, buildFileConfirmationMessage, buildComposeAttachedToAssistantMessage, makeFileStatusMessage } from "./summarizeRouting";
import { routeReviseIntent } from "./composeReviseRouting";
import { detectDraftDocumentIntent } from "./composeDraftRouting";
import { LOCAL_CHIP, buildReviseInComposeChip, buildNdaReviewChip, getDocumentReviewCapability } from "./localActionChips";
import { buildChipPreference, recordChipUsage } from "./chipPreference";
import {
  detectReviseThisDocumentIntent,
  detectSectionRewriteIntent,
  REVISE_MOUNT_ASK_MESSAGE,
  type RevisionIntent,
  type ComposeDocAction,
} from "./composeReviseRouting";
import { ComposeDocActionChips } from "./ReviseIntentChips";
import { useCapabilityDiscovery } from "./useCapabilityDiscovery";
// task 021 (FR-07/08/09 interactive orientation + confirmation gate): the review-intent text
// detector (checked BEFORE detectReviseThisDocumentIntent — see the decorate hook below) and the
// stateful gate controller. Deep-import useComposeLaunch from the shared context module (NOT the
// `@spaarke/compose-components` barrel) — mirrors this file's existing composeActionBridge
// deep-import rationale (avoids transitively pulling the TipTap editor widgets into the Assistant
// pane bundle); composeLaunchContext.ts is a READ-ONLY canonical reference this task consumes,
// never modifies (Spaarke.Compose.Components is off-limits this wave — task 012 owns it).
import { detectAgreementReviewIntent, normalizeReviewDepth, type ReviewDepth } from "./agreementReviewRouting";
import { useAgreementReviewGate } from "./useAgreementReviewGate";
// task 031 (DEF-09 routing): the pure waiter/timeout seam that resolves the reviewed file's
// REAL document session (backfilled by registerComposeActiveDocument), so the review dispatch's
// `sessionIdOverride` can target the SAME session ComposeWorkspace reads compose-outputs from.
import { createDocumentSessionWaiter } from "./documentSessionWaiter";
import { useComposeLaunch } from "@spaarke/compose-components/context/composeLaunchContext";
import {
  AuthLoadingState,
  PlaybookHeaderStrip,
  PlaybookToast,
  RestoreBanners,
  RefinementChipBar,
  FilesAttachedIndicator,
  UploadProgressIndicator,
  AssistantModelTierPicker,
  AssistantModelTier,
  useConversationPaneLayoutStyles,
} from "./ConversationPaneChrome";

// Public pure-helper surface (tests import these from '../ConversationPane').
export {
  SUMMARIZE_SLASH_PREFIX,
  SUMMARIZE_PROMPT_FIRST_INTERJECTION,
  FILE_CONFIRMATION_MAX_NAMES,
  routeSummarizeIntent,
  buildFileConfirmationMessage,
  buildComposeAttachedToAssistantMessage,
  buildMultiFileSummarizeInterjection,
  makeLocalAssistantMessage,
  makeComposeEditControlsMessage,
  makeSavedToDmsMessage,
} from "./summarizeRouting";
export type { SummarizeRouteDecision, SummarizeIntentInputs } from "./summarizeRouting";
// DEF-11 whole-document revise disambiguation (tests import these from '../ConversationPane').
export {
  routeReviseIntent,
  REVISE_SLASH_PREFIX,
  REVISE_DISAMBIGUATION_MESSAGE,
  REVISION_INTENTS,
  REVISION_INTENT_SUGGESTIONS,
  // Wave 4 (end-to-end revise) — natural-language "revise this document" detection + chip surface.
  detectReviseThisDocumentIntent,
  REVISE_MOUNT_ASK_MESSAGE,
  // FIX #1 (editor-centric reframe) — document-level action chips replace the revision-type chips.
  COMPOSE_DOC_ACTION_CHIPS,
} from "./composeReviseRouting";
export type {
  RevisionIntent,
  ReviseRouteDecision,
  RevisionIntentSuggestion,
  ReviseThisDocumentDetection,
  ComposeDocAction,
  ComposeDocActionChip,
} from "./composeReviseRouting";

/**
 * DEF-09 / DEF-12: the SUMMARY-ONLY confirmation line for a compose EDIT action. The alternative
 * materializes as an inline redline IN the Compose document; the Assistant must NOT restate the
 * proposed text (that would duplicate the redline and reintroduce the "renders as a chat message,
 * not a redline" defect) NOR the reasoning (which lives in the Context Execution Trace). DEF-12
 * attaches the Accept / Reject / Try-another controls to THIS message (the Assistant is the AI↔user
 * interaction surface — Word Copilot parity), so the copy no longer says "accept or reject it there".
 * Informational compose actions keep their full grounded prose.
 */
export const COMPOSE_EDIT_CONFIRMATION =
  'I revised the selected text — review the tracked change in the document, then accept, reject, or try another.';

/**
 * DEF-11: the SUMMARY-ONLY confirmation line for a WHOLE-DOCUMENT revise action
 * (`compose-revise-document` — align-clauses / flag-risks / improve-clarity / custom over the
 * entire open document, not a selection). Same rationale as {@link COMPOSE_EDIT_CONFIRMATION} — the
 * multi-change redline (or the review-flag comments) live in the document itself, never restated
 * here. Selected purely by `request.revisionScope === 'whole-document'` in `dispatchComposeAction`;
 * Accept/Reject/Try-another on this message route through the SAME handlers (Accept-all / Reject-all
 * fall out of `usePendingRedline`'s base-ledgerRef semantics, not a different code path here).
 */
export const COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION =
  'I revised the document — review the tracked changes in the document, then accept, reject, or try another.';

/**
 * agreements-r1 task 042 (FR-12) — best-effort extraction of a resolvable clause-LOCATION string for
 * a compose EDIT confirmation, if the dispatch's request or result happens to carry one.
 *
 * **Wiring status (2026-07-31)**: neither shipped caller of `enqueueComposeAction`
 * (`ComposeAiToolbar.tsx`'s inline-toolbar dispatch, `ComposeEditor.tsx`'s `dispatchNoteToolRequest` —
 * both in the read-only-for-this-task `@spaarke/compose-components` package) currently populates a
 * location field on `args.slots`, and none of the compose-draft-alternative / compose-revise-document
 * result schemas (`infra/dataverse/outputschemas/compose-*.schema.json`) carry one either.
 * `deriveClauseLocationLabel` (`clauseLocation.ts`) is computed today ONLY for the gutter's own note
 * card (`ComposeCommentGutter.tsx`) — it is never threaded into the AI-action dispatch that reaches
 * this pane. Closing that gap is a `ComposeEditor.tsx` change (one field added to
 * `dispatchNoteToolRequest`'s `slots`) outside this task's file boundary (task 042 owns
 * `ConversationPane.tsx` only — see `notes/042-execution-notes.md`).
 *
 * This extraction is written FORWARD-COMPATIBLE / defensive rather than hard-coded to "always empty":
 * it checks the established `locationLabel` field name (mirrors `ComposeEditor.tsx`'s own
 * `locationLabel: deriveClauseLocationLabel(...)` convention) plus the `sectionRef`/`location` names
 * already used elsewhere in this file's result-shape handling (`useNdaReviewAdvisoryCommentsBridge.ts`),
 * on BOTH the outbound request's forwarded slots and the dispatched result payload. The moment a future
 * change populates any of these, {@link withComposeEditLocationHeader} activates the bold header with
 * ZERO further change here. Until then this consistently returns `null`.
 */
function extractComposeEditLocationLabel(request: ComposeActionRequest, result: unknown): string | null {
  const asTrimmedString = (value: unknown): string | null =>
    typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
  const asRecord = (value: unknown): Record<string, unknown> | null =>
    value !== null && typeof value === "object" && !Array.isArray(value) ? (value as Record<string, unknown>) : null;

  const slots = asRecord(request.args?.slots);
  const fromRequest =
    asTrimmedString(slots?.locationLabel) ?? asTrimmedString(slots?.sectionRef) ?? asTrimmedString(slots?.location);
  if (fromRequest) return fromRequest;

  const record = asRecord(result);
  return (
    asTrimmedString(record?.locationLabel) ?? asTrimmedString(record?.sectionRef) ?? asTrimmedString(record?.location)
  );
}

/**
 * agreements-r1 task 042 (FR-12) — prepend a BOLD clause-location header to a compose EDIT
 * confirmation, with clear whitespace separating it from the summary/explanation body below. Renders
 * the header as a markdown `###` heading — the exact same `.sprk-markdown h3` rule (`SPRK_MARKDOWN_CSS`,
 * `@spaarke/ui-components/services/renderMarkdown`) every other Assistant message already renders
 * through: `font-weight: var(--fontWeightBold)`, `margin-top: var(--spacingVerticalL)`,
 * `margin-bottom: var(--spacingVerticalS)` — Fluent v9 SEMANTIC TOKENS, dark-mode-safe by construction
 * (ADR-021), with NO new styling surface added by this task (`ConversationPane.tsx` never renders its
 * own JSX for message content — `content` is a markdown string SprkChat renders).
 *
 * When `locationLabel` is unresolved (today's universal case — see
 * {@link extractComposeEditLocationLabel}) the header is OMITTED rather than replaced with a filler
 * string: (1) it keeps the confirmation BYTE-IDENTICAL to pre-042 behavior in that case, which is the
 * exact scenario `ConversationPane.compose-edit-controls.test.tsx` locks with an exact `.toBe` match
 * (ADR-041 "existing tests pass untouched"); (2) a repeated, non-distinguishing filler label on every
 * entry of a batch ("Clause update", "Clause update", …) would not actually help a reviewer tell
 * entries apart — the "graceful… no undefined" requirement is satisfied by cleanly omitting the header,
 * never by fabricating one.
 */
function withComposeEditLocationHeader(confirmationText: string, locationLabel: string | null): string {
  return locationLabel ? `### ${locationLabel}\n\n${confirmationText}` : confirmationText;
}

/**
 * agreements-r1 task 033 (FR-17) — the DISTINCT bridge-failure surface (ADR-019): the wizard armed
 * an auto-run review, but the document never finished registering as a session file (the DEF-10
 * register's upload failed or never landed) within the watchdog window. The Analysis itself is
 * already durable and consistent (created + bound by the wizard BEFORE the hand-off) — only the
 * auto-run leg degraded, and the recovery is the user's normal conversational trigger. Deliberately
 * different copy from the wizard's own session-mint failure warning (bind failure) and from the
 * dispatch path's own error surface (dispatch failure) — three legs, three distinct surfaces.
 */
export const WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE =
  'I couldn\'t start the agreement review automatically — the document didn\'t finish preparing. ' +
  'Your analysis was created and saved; open the document tab and ask me to "review this document" to run the review.';

/**
 * task 033: how long the auto-run watchdog waits for the DEF-10 register to land the session file
 * before surfacing {@link WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE} and standing down. Generous vs.
 * the normal path (doc load + upload is seconds; the server manifest probe alone is ~5s) so a slow
 * network never false-alarms, while a genuinely failed bridge still surfaces within the session.
 */
export const WIZARD_AUTO_RUN_WATCHDOG_MS = 30_000;

export function ConversationPane(): React.JSX.Element {
  const styles = useConversationPaneLayoutStyles();

  // ── Session context (AiSessionProvider; function-based auth per §H-4) ─────
  const {
    isAuthenticated,
    authenticatedFetch,
    getAccessToken,
    bffBaseUrl,
    chatSessionId,
    setChatSessionId,
    clearChatSession,
    playbookId,
    setPlaybookId,
    entityContext,
    streaming,
  } = useAiSession();

  const { toLoading, reset } = useShellStage();
  const restoreCtx = useRestoreContext();
  const paneCollapse = usePaneCollapseContext();
  const dispatch = useDispatchPaneEvent();
  // task 021 (FR-09 "skip when subDomain already present — explicit path owns it, 023"): a non-null
  // `subDomain` here means the app was launched already-oriented (composeMode='editor' ribbon launch,
  // or a server-seeded `workspace_open_tab` — main.tsx's SpaarkeAiWorkspaceRenderer). The interactive
  // gate (below) skips classification entirely when this is set — read-only consumption of the
  // shared launch context (composeLaunchContext.ts), never written here.
  const explicitComposeLaunch = useComposeLaunch();

  // Wave 3 Part 1 (UAT-R3 Test #1) — the client-side "active source document" pointer. When a chat
  // upload / browse-direct-upload / opened host document has been registered as the session's active
  // document (via `registerComposeActiveDocument` below — the single client chokepoint that learns a
  // server sessionFileId), we remember its `{ sessionFileId, documentSessionId }` here so "Open in
  // Compose" opens THAT source document instead of seeding the assistant message prose. Null until a
  // source document is registered (a fresh chat with no upload → the message-seed fallback).
  const activeSourceDocRef = React.useRef<{
    sessionFileId: string;
    documentSessionId?: string;
    fileName?: string;
  } | null>(null);

  // task 031 (DEF-09 routing) — the pure waiter/timeout seam (documentSessionWaiter.ts) that resolves
  // a REVIEWED file's REAL document session, keyed by that file's session-file id (never a different
  // file's stale value), backfilled by `registerComposeActiveDocument` below (the SAME reactive
  // conduit the "revise this document" flow's `activeComposeDocSessionId` already uses). One instance
  // per pane mount; reset on a fresh chat session (`handleSessionCreated`).
  const documentSessionWaiterRef = React.useRef(createDocumentSessionWaiter());
  const awaitDocumentSessionIdFor = React.useCallback(
    (fileId: string): Promise<string | null> => documentSessionWaiterRef.current.awaitDocumentSessionId(fileId),
    []
  );

  // #2 double-classify fix (UAT 2026-07-18) — CROSS-PATH dedup registry keyed by filename. A chat file
  // upload promotes via useAttachments' auto-promote (`POST /documents` → classify #1). FIX #7 then
  // auto-loads that SAME file into Compose, and ComposeWorkspace hands the bytes back to
  // `registerComposeActiveDocument`, whose LOCAL byte-cache never saw the chat promotion → a SECOND
  // `POST /documents` → classify #2 (~13 s later) + a duplicate ingest ceremony. The chat promotion
  // records `fileName → sessionFileId` here; `registerComposeActiveDocument` consults it FIRST and
  // reuses the existing sessionFileId (skipping the re-upload, the re-classify, and the ceremony —
  // it still establishes the doc-session pointer). Session-scoped; cleared on session-created.
  const promotedFileIdsByNameRef = React.useRef<Map<string, string>>(new Map());

  // Wave 3 Part 1 (Test #1 real repro): a file uploaded to the ASSISTANT (chat) — then "revise this
  // document" → disambiguation → "Open in Compose" — was NEVER mounted in Compose, so the Compose-side
  // registration never fired. Capture the uploaded file's server id when the chat attachment promotes
  // (the `/documents` 202's `documentId`, threaded from useAttachments) and cache it as the active
  // source document (most-recent-upload-wins). A chat upload has no Compose document session yet, so
  // `documentSessionId` stays undefined — the compose.upload seed needs only { sessionId, sessionFileId }.
  const handleSessionFileUploaded = React.useCallback(
    ({ sessionFileId, fileName }: { sessionFileId: string; fileName: string }): void => {
      activeSourceDocRef.current = { sessionFileId, fileName };
      // #2 double-classify fix: record the chat-promoted id by filename so the Compose auto-load's
      // `registerComposeActiveDocument` reuses it instead of re-uploading (+ re-classifying) the file.
      if (fileName) promotedFileIdsByNameRef.current.set(fileName, sessionFileId);
      // R2 (race-safe revise): a chat "revise this document" that arrived BEFORE this upload
      // back-filled `activeSourceDocRef` is BUFFERED (see handleDecorateOutboundBodyWithRevise +
      // the effect below). Bump this token so that buffered-revise effect re-runs the moment the
      // source-doc id lands — mount + revise then fire regardless of ordering (never a chat-session
      // agent turn that narrates the revise as prose).
      setSourceDocReadyToken((t) => t + 1);
    },
    []
  );

  // Wave 3 Part 1: the "Open in Compose" per-message affordance.
  //   (1) When the chat session has an ACTIVE uploaded/source document, open THAT document — dispatch
  //       the SAME `compose.upload` seed SendWorkspaceArtifactHandler produces
  //       ({layoutName:"Compose", compose:{ upload:{ sessionId, sessionFileId } }}), NOT the message
  //       prose. This fixes Test #1: a revise-disambiguation message no longer seeds the disambiguation
  //       PROSE into the editor — it opens the file being revised.
  //   (2) With NO active source document, fall back to seeding the message text as an editable draft
  //       (DEF-08 Part B — a genuine AI-drafted document, or the manual "open this as a document"
  //       affordance). The drafted body rides inline as `compose.draft.html` (client-direct).
  // Both dispatch by layout NAME only — the WorkspacePane widget_load handler resolves the Compose
  // layout id + REUSES the single Compose tab (no layouts fetch in the Assistant pane).
  // Wave 4 (end-to-end revise): auto-mount the active chat-uploaded source document into Compose by
  // dispatching the SAME `compose.upload` seed "Open in Compose" produces. Reused by BOTH the
  // per-message affordance below AND the natural-language "revise this document" flow (the mount must
  // precede the revise dispatch so a document session is established — see the decorate hook). Returns
  // true when a mount was dispatched (an active source document exists), false otherwise.
  // Mount a SPECIFIC session file into Compose via the DIRECT 'compose' widget (the `compose.upload`
  // seed shape is consumed verbatim by ComposeDirectWidget.buildLaunchFromSeed). WorkspacePane reuses
  // the single Compose tab per distinct file. Returns true when a mount was dispatched.
  const mountFileInCompose = React.useCallback(
    (sessionFileId: string, fileName?: string, activeWorkType?: string): boolean => {
      const sessionId = chatSessionIdRef.current;
      if (!sessionFileId || !sessionId) return false;
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "compose",
        widgetData: {
          compose: {
            upload: { sessionId, sessionFileId, fileName },
            // task 021 (FR-07 orientation writes): threaded through the SAME
            // `ComposeWidgetSeed.activeWorkType` field task 041 (hub) already wired end-to-end
            // (`buildLaunchFromSeed` -> `<ComposeWorkspace activeWorkType>` -> `getToolsForSurface`).
            // Omitted (every pre-021 caller) preserves the exact prior wire shape — ComposeEditor's
            // own `'*'` unscoped default applies.
            ...(activeWorkType ? { activeWorkType } : {}),
          },
        },
        displayName: "Compose",
      } as WorkspacePaneEvent);
      return true;
    },
    [dispatch]
  );

  const mountActiveSourceDocInCompose = React.useCallback((): boolean => {
    const active = activeSourceDocRef.current;
    if (!active?.sessionFileId) return false;
    return mountFileInCompose(active.sessionFileId, active.fileName);
  }, [mountFileInCompose]);

  // R4-4 (UAT 2026-07-19): "Revise the file" on-demand action (replaces the auto-open-on-attach).
  // Mounts EVERY promoted (indexed) session file into Compose — multiple files open in separate
  // Compose tabs; the single active file falls back through mountActiveSourceDocInCompose.
  const handleReviseInCompose = React.useCallback((): void => {
    const promoted = promotedFileIdsByNameRef.current;
    if (promoted.size > 0) {
      for (const [fileName, sessionFileId] of promoted) {
        mountFileInCompose(sessionFileId, fileName);
      }
      return;
    }
    mountActiveSourceDocInCompose();
  }, [mountFileInCompose, mountActiveSourceDocInCompose]);

  // FIX #10a: the generic per-message "Open in Compose" affordance was REMOVED (owner decision — it
  // did not reliably work and was not always appropriate). Mounting now happens only via INTENTIONAL
  // affordances: the natural-language "revise this document" flow (which calls
  // `mountActiveSourceDocInCompose` below) and the server-driven `workspace_open_tab` seed — NOT an
  // auto-appended per-message link. `handleOpenInCompose` + the `onOpenInCompose` prop wiring are gone.

  // Session-id getter for the dispatch/event seams (stable across renders).
  const chatSessionIdRef = React.useRef<string | null>(chatSessionId);
  chatSessionIdRef.current = chatSessionId;
  const getSessionId = React.useCallback(() => chatSessionIdRef.current, []);

  // Wave 4 (end-to-end revise) — resolve the `compose-revise-document` Binding id CLIENT-SIDE from
  // the closed catalog (ADR-039: bindingId comes only from capability discovery, never invented). The
  // action declares `surfaces: "assistant,compose"`, so it is returned on the default `assistant`
  // surface. This closes the honest boundary the original DEF-11 disambiguation noted ("no
  // capability-discovery seam resolves compose-revise-document's Binding id client-side") — that seam
  // (task 041) shipped and this action is on it. Null until the fetch resolves / when the catalog is
  // unreachable (chips fail-soft).
  // Deferred read: the catalog fetch stays inert until the FIRST natural-language revise request in
  // this session flips `reviseCapabilityNeeded` (in the decorate hook below). This keeps a
  // mounted-but-idle Assistant pane from issuing an eager background `/api/ai/capabilities` request
  // (which would also perturb the fetch call-sequence other ConversationPane surfaces assert on). The
  // fetch kicks off the moment a revise is detected — well before the user reads the ask message and
  // clicks a chip, so the bindingId is resolved by dispatch time.
  const [reviseCapabilityNeeded, setReviseCapabilityNeeded] = React.useState<boolean>(false);
  // R7-4 (UAT 2026-07-21): the SAME deferred capability-discovery seam also resolves the
  // `compose-draft-document` bindingId for the substantial-output → Compose route. Enable the
  // discovery fetch when EITHER a revise OR a draft-document intent is first detected (both stay
  // inert on a mounted-but-idle pane).
  const [draftCapabilityNeeded, setDraftCapabilityNeeded] = React.useState<boolean>(false);
  // task 022 (NDA review card): the SAME deferred capability-discovery seam resolves the
  // classified document type's review-capability bindingId — enabled the moment classification
  // flags a reviewable upload (below), well before the user reads the card and clicks it. Zero
  // hardcoded GUID, zero new BFF read (reuses GET /api/ai/capabilities — the same closed-catalog
  // projection revise/draft use).
  const [ndaReviewCapabilityNeeded, setNdaReviewCapabilityNeeded] = React.useState<boolean>(false);
  // task 064 (ai-advanced-capabilities-analysis-hub-r1, spec §13.5 / FR-22): the consumerType to
  // resolve a bindingId for, set by `handleNdaClassified` from the matched
  // `DocumentReviewCapability` (registry lookup by classified docType — generalizes the prior
  // hardcoded "nda-review" consumerType so a different work-type's clause-review capability can
  // be wired in later via a one-line registry addition, not a code fork). Declared here (before
  // the `ndaReviewBindingId` memo below) so the memo can depend on it.
  const [ndaReviewConsumerType, setNdaReviewConsumerType] = React.useState<string>("nda-review");
  // task 021 — the SAME deferred capability-discovery seam resolves the `agreement-classify`
  // bindingId, enabled the moment the review-intent text detector first fires (see the decorate
  // hook below) — well before classification actually needs to dispatch.
  const [agreementReviewGateNeeded, setAgreementReviewGateNeeded] = React.useState<boolean>(false);
  const { capabilities: launchableCapabilities } = useCapabilityDiscovery({
    bffBaseUrl,
    authenticatedFetch,
    enabled:
      reviseCapabilityNeeded || draftCapabilityNeeded || ndaReviewCapabilityNeeded || agreementReviewGateNeeded,
  });
  const reviseBindingId = React.useMemo<string | null>(
    () =>
      launchableCapabilities.find((c) => c.consumerType === "compose-revise-document")?.bindingId ??
      null,
    [launchableCapabilities]
  );
  // R7-4: the `compose-draft-document` Binding id from the SAME closed capability catalog (ADR-039:
  // bindingId only from discovery, never invented). The action is on the `assistant` surface, so it
  // is returned on the default discovery. Null until the fetch resolves / when the catalog is
  // unreachable (the decorate branch buffers the request until it resolves).
  const draftBindingId = React.useMemo<string | null>(
    () =>
      launchableCapabilities.find((c) => c.consumerType === "compose-draft-document")?.bindingId ??
      null,
    [launchableCapabilities]
  );

  // FIX #1 (Summarize doc-action chip) — resolve the EXISTING `compose-summarize` Binding id from the
  // SAME closed capability catalog (ADR-039: bindingId only from discovery). Dispatched informationally
  // on the CHAT session via the shared `dispatchBinding` path (below) — no new dispatch mechanism.
  const summarizeBindingId = React.useMemo<string | null>(
    () =>
      launchableCapabilities.find((c) => c.consumerType === "compose-summarize")?.bindingId ?? null,
    [launchableCapabilities]
  );

  // task 022 — the classified document type's review-capability Binding id, resolved from the
  // SAME closed capability catalog (ADR-039: bindingId only from discovery, never
  // invented/hardcoded — portable across environments exactly like
  // reviseBindingId/draftBindingId/summarizeBindingId above). task 064: consumerType is now
  // `ndaReviewConsumerType` (set from the matched `DocumentReviewCapability` by classified
  // docType) rather than a hardcoded "nda-review" literal — defaults to "nda-review" so behavior
  // for the NDA docType is unchanged.
  const ndaReviewBindingId = React.useMemo<string | null>(
    () => launchableCapabilities.find((c) => c.consumerType === ndaReviewConsumerType)?.bindingId ?? null,
    [launchableCapabilities, ndaReviewConsumerType]
  );

  // task 021 (FR-07/08/09 interactive orientation + confirmation gate) — the `agreement-classify`
  // Binding id, resolved from the SAME closed capability catalog (ADR-039: bindingId only from
  // discovery, never invented/hardcoded — portable across environments exactly like the bindingIds
  // above). Enables the SAME deferred capability-discovery fetch the moment review-intent is first
  // detected (see `agreementReviewGateNeeded` below), so it is resolved by the time the gate needs it.
  const classifyBindingId = React.useMemo<string | null>(
    () => launchableCapabilities.find((c) => c.consumerType === "agreement-classify")?.bindingId ?? null,
    [launchableCapabilities]
  );

  // Wave 4 — the natural-language revise flow's client state:
  //  - `reviseChipsPending`: after auto-mount + the mount-then-ask message, show the four intent chips.
  //  - `pendingNamedRevise`: the user NAMED an intent in the original message ("flag risks in this
  //    document") → mount + apply THAT intent directly, but only ONCE the document session is
  //    registered (the effect below fires when `activeComposeDocSessionId` back-fills).
  //  - `activeComposeDocSessionId`: the post-mount document session id, back-filled REACTIVELY by
  //    `registerComposeActiveDocument` (below) so the named-intent effect can await it — never a
  //    captured stale null.
  const [reviseChipsPending, setReviseChipsPending] = React.useState<boolean>(false);
  const [pendingNamedRevise, setPendingNamedRevise] = React.useState<{
    revisionIntent: RevisionIntent;
    instruction?: string;
  } | null>(null);
  const [activeComposeDocSessionId, setActiveComposeDocSessionId] = React.useState<string | null>(
    null
  );
  const reviseDispatchSeqRef = React.useRef(0);

  // R7-4 — a substantial-output "draft a document" request detected BEFORE capability discovery has
  // resolved the `compose-draft-document` bindingId. Buffered here + dispatched by the effect below
  // the moment `draftBindingId` back-fills (mirrors the named-intent revise buffer above).
  const [pendingDraftDocument, setPendingDraftDocument] = React.useState<{ request: string } | null>(
    null
  );

  // R2 (race-safe revise) — when a natural-language "revise this document" arrives BEFORE the
  // uploaded file's registration has back-filled `activeSourceDocRef.sessionFileId`, we BUFFER the
  // intent here (rather than falling through to a chat-session agent turn that narrates it as prose)
  // and fire the mount + revise once the upload back-fills. `sourceDocReadyToken` bumps in
  // `handleSessionFileUploaded` so the buffered-revise effect re-runs on back-fill.
  const [pendingReviseThisDocument, setPendingReviseThisDocument] = React.useState<{
    namedIntent: RevisionIntent | null;
  } | null>(null);
  const [sourceDocReadyToken, setSourceDocReadyToken] = React.useState(0);
  // task 021 — the SAME race-safe buffer, for a natural-language "review this document" (agreement
  // review-intent) that arrives BEFORE the upload's registration has back-filled
  // `activeSourceDocRef.sessionFileId`. Reuses the SAME `sourceDocReadyToken` bump (a generic
  // "a source doc just became ready" signal, not revise-specific) — the effect below fires the
  // gate once the file is available.
  const [pendingAgreementReview, setPendingAgreementReview] = React.useState<boolean>(false);
  // task 023 (FR-09 explicit door) — the SAME race-safe buffer, for a "review this document" that
  // arrives when the session was launched ALREADY oriented (`explicitComposeLaunch?.subDomain`).
  // Carries the bound subDomainKey (captured at detection time, not re-read later) so the buffered
  // effect below calls `agreementReviewGate.runExplicit` directly — no classification gate, ever.
  // task 070 (UAT2 review-depth selector): `reviewDepth` is OPTIONAL on this buffer — the TEXT-path
  // trigger (handleDecorateOutboundBodyWithRevise) leaves it undefined (runExplicit then asks ONE
  // depth-choice turn); the WIZARD hand-off listener below sets it from the finish-time seed (the
  // wizard already resolved a depth, so no further ask — see runExplicit's two-mode contract).
  const [pendingExplicitAgreementReview, setPendingExplicitAgreementReview] = React.useState<{
    subDomainKey: string;
    reviewDepth?: ReviewDepth;
  } | null>(null);
  // task 033 (FR-17 wizard→review auto-run bridge) — wizard hand-off state:
  //  - `wizardAutoRunHandledRef`: once-per-Analysis dedupe for the workspace-channel hand-off
  //    listener (a re-dispatched/duplicate `widget_load` for the SAME analysisId must not re-adopt
  //    or re-arm; a SECOND wizard run in the same pane session — a NEW analysisId — legitimately
  //    fires again). Deliberately NOT session-scoped-reset: the bind/arm for an Analysis happens
  //    exactly once per pane mount regardless of intervening session switches.
  //  - `wizardAutoRunWatchdog`: armed alongside the explicit-door buffer when the WIZARD (not text)
  //    armed it — drives the bounded bridge-failure surface (the buffered effect never fires when
  //    the DEF-10 register's upload fails, and the TEXT door's indefinite-buffer semantics are
  //    correct for a human mid-conversation but wrong for a machine-armed run the user never sees).
  const wizardAutoRunHandledRef = React.useRef<Set<string>>(new Set());
  const [wizardAutoRunWatchdog, setWizardAutoRunWatchdog] = React.useState<{
    analysisId: string;
  } | null>(null);
  // UAT round-6 (item #15b) — once-per-mount guard for cold-load Compose-tab session RE-ADOPTION.
  // When WorkspacePane restores a persisted home-surface Compose tab (item #13), it dispatches
  // `widget_load{compose}` threading the tab's `composeSessionId` (but NO `analysisId` — the direct-
  // Compose door is unbound). This pane adopts that session so the Assistant transcript CONTINUES on
  // return instead of cold-starting "back at the Review an NDA step". Adopted at most once per mount.
  const restoreSessionAdoptedRef = React.useRef<boolean>(false);

  // ── Behaviour hooks (see module map in the header) ────────────────────────
  const injection = useInjectionQueue();

  // task 022 (NDA review card): the classified upload waiting on the "Review an NDA" card,
  // if any. Set by handleNdaClassified (below) purely from the ONE existing classifier's
  // (chat-classify / CLS-CHAT@v1) already-produced `docType` output — never a second
  // classification mechanism (ADR-039). Cleared on click-dispatch, dismiss, or a fresh
  // session (handleSessionCreated) — never left stale across sessions.
  //
  // task 064 (ai-advanced-capabilities-analysis-hub-r1, spec §13.5 / FR-22): widened with
  // `cardLabel` so the Suggested-Next-Steps card text is driven by the matched
  // `DocumentReviewCapability` (localActionChips.ts) rather than a hardcoded "Review an NDA"
  // string — generalizes the docType gate to work-type without changing NDA's behavior.
  const [ndaReviewFile, setNdaReviewFile] = React.useState<{
    fileId: string;
    fileName: string;
    cardLabel: string;
  } | null>(null);
  // ai-advanced-capabilities-nda-r1 follow-up (UAT 2026-07-26): render-free mirror of ndaReviewFile so
  // the stable-`[]`-deps `getAppendedLocalChips` callback (below) can read the current NDA state when
  // `acceptChips` builds the Suggested-Next-Steps strip. Kept in sync at render (auto-tracks set AND
  // every clear site) and set synchronously in handleNdaClassified so it is already populated if the
  // classification and the chips frame land in the same tick (the pipeline order — promote → classify →
  // chips — normally sets it a frame earlier, but this makes the append race-free either way).
  const ndaReviewFileRef = React.useRef<{ fileId: string; fileName: string; cardLabel: string } | null>(null);
  ndaReviewFileRef.current = ndaReviewFile;
  const handleNdaClassified = React.useCallback((data: EventClassificationData): void => {
    // task 064: the classified docType is resolved against the DOCUMENT_REVIEW_CAPABILITIES
    // registry (localActionChips.ts) instead of a hardcoded `docType !== "nda"` comparison. The
    // registry's only entry today is "nda" (unchanged behavior); a second work-type's
    // clause-review capability is a one-line registry addition once its Action/Binding exists —
    // no further code change here or in the discovery/dispatch below.
    const capability = getDocumentReviewCapability(data.docType);
    // Negative case (task 022 acceptance criterion 3): any docType with no registered review
    // capability — including an absent/unclassified result — leaves ndaReviewFile untouched, so
    // no card renders.
    if (!capability || !data.fileId) return;
    // Kick off the deferred capability-discovery fetch NOW (well before the user reads the
    // card and clicks it) so ndaReviewBindingId is resolved by dispatch time.
    setNdaReviewCapabilityNeeded(true);
    setNdaReviewConsumerType(capability.consumerType);
    const file = { fileId: data.fileId, fileName: data.fileName ?? "the document", cardLabel: capability.cardLabel };
    ndaReviewFileRef.current = file; // same-tick guarantee for getAppendedLocalChips (see ref note above)
    setNdaReviewFile(file);
  }, []);

  // Stable-ref indirection keeps eventBatch → chips composition acyclic.
  const acceptChipsRef = React.useRef<(raw: unknown) => void>(() => undefined);
  const eventBatch = useEventBatch({
    bffBaseUrl,
    getAccessToken,
    getSessionId,
    enqueueAssistantMessage: injection.enqueue,
    onChips: React.useCallback((raw: unknown) => acceptChipsRef.current(raw), []),
    onClassified: handleNdaClassified,
  });

  // CHAT-4 (UAT 2026-07-19): track the live transcript length so the get-started cards render
  // whenever the transcript is EMPTY — including a restored-but-empty session (where the old
  // `chatSessionId === null` gate was false). Kept in sync via SprkChat's onMessagesChange.
  //
  // task 024 (spec FR-08): declared here (moved up from its original spot further below) so
  // `hasPriorMessages` is available for the `useAttachments` call immediately below — a file
  // attach mid-chat needs to know, AT ATTACH TIME, whether the chat already has messages.
  const [chatMessageCount, setChatMessageCount] = React.useState(0);
  // UAT round-6 (item #15b): a ref mirror so the bus-driven restore-adoption listener reads the CURRENT
  // transcript length synchronously (its guard must never clobber a conversation the user is mid-way
  // through). usePaneEvent always invokes the latest closure, so this stays fresh.
  const chatMessageCountRef = React.useRef(chatMessageCount);
  chatMessageCountRef.current = chatMessageCount;

  const attachments = useAttachments({
    bffBaseUrl,
    chatSessionId,
    hasActiveWorkspaceDocument: entityContext !== null,
    hasPriorMessages: chatMessageCount > 0,
    authenticatedFetch,
    dispatch,
    inject: injection.inject,
    eventBatch,
    onSessionFileUploaded: handleSessionFileUploaded,
  });

  // task 043 / FR-G1: same stable-ref indirection as acceptChipsRef above —
  // `usePlaybookOptions.handleOpenLibraryModal` is declared further below
  // (needs bffBaseUrl/authenticatedFetch/chatSessionId already in scope
  // here), so the SNS cards' "More" affordance reaches it through a ref
  // rather than reordering hook declarations.
  const openLibraryModalRef = React.useRef<() => void>(() => undefined);
  // UAT R4-6 / R4-11: local-action chips (Send as email / Save to document / Ask about these
  // files) route here. `handleDocAction` (the reused editor/email bridges) is declared further
  // below, so the chip strip reaches it through a ref rather than reordering hook declarations.
  const localChipActionRef = React.useRef<(actionId: string) => void>(() => undefined);
  // nda-r1 follow-up: the NDA-REVIEW advisory-comments bridge (`emitFromResult`, defined further below
  // with the other Compose bridges) is reached from the chips controller through this ref so the "Review
  // an NDA" card path also materializes flagged clauses as Compose comments (not just raw JSON).
  const ndaReviewEmitRef = React.useRef<(result: unknown) => void>(() => undefined);
  // UAT round-4 (item #9): the "Rerun a full analysis" card's action needs
  // `agreementReviewGate.rerunThorough`, but `agreementReviewGate` is constructed AFTER `chips`
  // (it depends on `chips.dispatchBinding`) — reached through a ref, mirroring `ndaReviewEmitRef`
  // immediately above. `useRerunFullAnalysisCard` itself is declared here (before `chips`) so its
  // stable `showFor` callback can be threaded into `useConsumerChips`'s `onQuickReviewComplete` dep.
  const rerunThoroughRef = React.useRef<(fileId: string, subDomainKey?: string) => Promise<void>>(
    async () => undefined
  );
  const rerunFullAnalysisCard = useRerunFullAnalysisCard({
    onRerun: React.useCallback((fileId: string, subDomainKey?: string) => {
      void rerunThoroughRef.current(fileId, subDomainKey);
    }, []),
  });
  // UAT round-5 #9 — center-screen progress modal for a running NDA review. Driven by the three real
  // client transitions: dispatch-start (onChipDispatched, gated to the NDA binding), NDA-shaped terminal
  // result (onDispatchResult), and dispatch-settle-without-result (a chips.dispatching effect → fail).
  // Refs let the stable []-deps chip callbacks reach the current hook + binding id without re-subscribing.
  const ndaRun = useNdaReviewRunProgress();
  const ndaRunRef = React.useRef(ndaRun);
  ndaRunRef.current = ndaRun;
  const ndaReviewBindingIdRef = React.useRef<string | null>(null);
  ndaReviewBindingIdRef.current = ndaReviewBindingId;
  // UAT round-3 (item #8): broadcast the progress modal's visibility on the PaneEventBus so
  // ReviewCompleteToast (shell/ReviewCompleteToast.tsx) can suppress a redundant completion toast
  // while the modal itself is STILL showing the outcome (no double-notification per the decided
  // matrix: dialog open+visible -> no toast; dismissed+on-tab -> no toast, existing active-tab
  // suppression already covers it; dismissed+off-tab -> toast fires, the 071 notify-me case). The
  // modal is now non-blocking (item #8), so "open+visible while on a DIFFERENT workspace tab" is a
  // real reachable state that did not exist before this fix.
  React.useEffect(() => {
    dispatch("workspace", { type: "nda_review_progress_visibility", progressVisible: ndaRun.visible });
  }, [ndaRun.visible, dispatch]);
  // R5-9 (UAT 2026-07-20): "Send as email" / Quick Start "Send Email" open the shared Email Compose
  // modal (SendEmailDialog / EmailComposer). `emailSeed` non-null = open; it carries the pre-fill
  // (subject / body / suggested recipients) captured from the last "Draft a response" result.
  const lastCorrespondenceDraftRef = React.useRef<Record<string, unknown> | null>(null);
  const [emailSeed, setEmailSeed] = React.useState<{
    initialTo?: string[];
    initialSubject?: string;
    initialBody?: string;
  } | null>(null);
  // R6-5 (UAT 2026-07-21): recipient (To/Cc/Bcc) directory lookup for the email modal. Reuses the
  // same host-context Xrm.WebApi contacts/users search the standard CommunicationPage composer uses
  // (searchUsersAndContacts → systemuser + contact; NO BFF/OBO per DATA-ACCESS-DECISION-CRITERIA).
  const emailLookupDataService = React.useMemo(() => createXrmDataService(), []);
  const handleSearchRecipients = React.useCallback(
    (query: string) => searchUsersAndContacts(emailLookupDataService, query),
    [emailLookupDataService]
  );
  // P1-8 (UAT 2026-07-18): the chips' trailing "More…" affordance now opens Quick Start
  // (the playbook library is retired). Owned here so the `openLibraryModalRef` the chips
  // reach through (below) points at this modal instead of the library modal.
  const [quickStartOpen, setQuickStartOpen] = React.useState(false);
  // ai-advanced-capabilities-analysis-hub-r1: which Quick Start tab opens. The
  // Assistant menu / chips "More…" open 'create'; the Analysis grid `+ New`
  // (via the `open_quick_start` intent below) opens 'analysis'.
  const [quickStartTab, setQuickStartTab] = React.useState<"create" | "analysis">("create");
  // UAT 2026-07-21 (#8): "What the Assistant remembers about you" review/delete dialog (host-owned so
  // it has authenticatedFetch + bffBaseUrl), opened from the ⋮ Assistant Tools "Memory" entry.
  const [memoryOpen, setMemoryOpen] = React.useState(false);
  // D-043-01 (option c): drive the Suggested-Next-Steps DISPLAY reorder from the user's preference.
  // The LEARNED signal (recent dispatch usage, localStorage) is the live source; a durable STATED
  // override layers in via the `null` seam below once a structured `sprk_userprofile` order becomes
  // client-projectable (buildChipPreference gives stated precedence when non-empty). `chipUsageTick`
  // bumps after each dispatch so the preference re-reads usage and the next chip strip re-ranks.
  const [chipUsageTick, setChipUsageTick] = React.useState(0);
  const chipDisplayPreference = React.useMemo(
    // eslint-disable-next-line react-hooks/exhaustive-deps -- chipUsageTick is the intended recompute trigger
    () => buildChipPreference(/* statedOrder seam */ null),
    [chipUsageTick]
  );
  // UAT (2026-08-03): a file registered via the Compose→Assistant ingest path
  // (`registerComposeActiveDocument`) is a real, actionable session file (its bytes are uploaded as a
  // ChatSessionFile), but it lands ONLY in `activeSourceDocRef` (+ the `sourceDocReadyToken` bump) —
  // NOT in the composer `attachmentChips` / `promotedChipIds` that `useAttachments.sessionAttachmentCount`
  // counts. So the just-classified follow-on cards (Summarize this file / Draft a response / Review an NDA
  // — all `requiresAttachments`) greyed out even though the Assistant clearly had the file. Fold the active
  // source document into the attachment-gate count so those cards enable when a source doc is present.
  // Keyed on `sourceDocReadyToken` (which bumps on every register) so the ref read stays REACTIVE — the
  // memoized chip slot re-renders when a Compose-registered file becomes available.
  const composeSourceDocCount = React.useMemo(
    // eslint-disable-next-line react-hooks/exhaustive-deps -- sourceDocReadyToken is the reactive recompute trigger for the ref read
    () => (activeSourceDocRef.current?.sessionFileId ? 1 : 0),
    [sourceDocReadyToken]
  );
  const chips = useConsumerChips({
    bffBaseUrl,
    getAccessToken,
    getSessionId,
    dispatch,
    // Gate on composer attachments UNION the Compose-registered active source doc (see composeSourceDocCount).
    sessionAttachmentCount: Math.max(attachments.sessionAttachmentCount, composeSourceDocCount),
    enqueueAssistantMessage: injection.enqueue,
    inject: injection.inject,
    openLibraryModal: React.useCallback(() => openLibraryModalRef.current(), []),
    // UAT R4-6 / R4-11: local-action chips route through the ref to `handleLocalChipAction` (below).
    onLocalChipAction: React.useCallback((actionId: string) => localChipActionRef.current(actionId), []),
    // nda-r1 follow-up: every Binding dispatch result on the chip/card path flows to the NDA-REVIEW
    // advisory-comments bridge (via ref — defined below). Materializes flagged clauses as Compose
    // comments for the "Review an NDA" card; safe no-op for every non-NDA result shape.
    onDispatchResult: React.useCallback((result: unknown) => {
      ndaReviewEmitRef.current(result);
      // UAT round-5 #9 — an NDA-shaped terminal result completes the progress modal.
      if (isNdaReviewResult(result)) ndaRunRef.current.complete();
    }, []),
    // UAT round-4 (item #9): a QUICK-depth review just completed — arm the "Rerun a full
    // analysis" card (useRerunFullAnalysisCard.showFor is itself a stable, []-deps callback).
    onQuickReviewComplete: rerunFullAnalysisCard.showFor,
    // R5-1: append "Revise in Compose" as an in-line card alongside the post-attach cards, once at
    // least one file is indexed. Reads the promoted-files ref so it reflects current state.
    // nda-r1 follow-up (UAT 2026-07-26): also append "Review an NDA" (FIRST — the primary action for a
    // just-classified NDA) when the classifier flagged the upload, replacing the old top-of-pane
    // notification card. Both read refs so this stays a stable `[]`-deps callback.
    getAppendedLocalChips: React.useCallback(
      () => [
        // task 064: card label comes from the matched DocumentReviewCapability (defaults to
        // "Review an NDA" for the NDA docType — unchanged behavior).
        ...(ndaReviewFileRef.current ? [buildNdaReviewChip(ndaReviewFileRef.current.cardLabel)] : []),
        ...(promotedFileIdsByNameRef.current.size > 0 ? [buildReviseInComposeChip()] : []),
      ],
      []
    ),
    // R5-9: remember the last drafted correspondence so "Send as email" can seed the modal.
    onCorrespondenceDraft: React.useCallback((result: Record<string, unknown>) => {
      lastCorrespondenceDraftRef.current = result;
    }, []),
    // UAT 2026-07-19: the post-classify "Create a matter" chip carries the uploaded file into the
    // wizard (parity with the text path) — read the session's active source doc.
    getActiveSourceFile: React.useCallback(
      () =>
        activeSourceDocRef.current?.sessionFileId
          ? {
              sessionFileId: activeSourceDocRef.current.sessionFileId,
              fileName: activeSourceDocRef.current.fileName,
            }
          : null,
      []
    ),
    // D-043-01: the preference that reorders the suggested-next-step cards (learned usage today).
    chipDisplayPreference,
    // D-043-01: record each dispatched Binding as learned usage, then re-read the preference.
    onChipDispatched: React.useCallback((bindingId: string) => {
      recordChipUsage(bindingId);
      setChipUsageTick((t) => t + 1);
      // UAT round-5 #9 — when the NDA-review binding is the one dispatched (from the "Review an NDA"
      // card OR its chip), open the center-screen progress modal.
      if (ndaReviewBindingIdRef.current && bindingId === ndaReviewBindingIdRef.current) {
        ndaRunRef.current.begin();
        // UAT round-6 (item #15a) — THE dispatch-time chokepoint. Every review path (chip-quick,
        // typed-gate, wizard-auto-run, rerun-thorough) funnels through this ONE `runBindingDispatch`
        // callback, so stamping the cross-navigation resume flag HERE captures it for all of them —
        // regardless of whether the user later dismisses the progress modal. WorkspacePane stamps the
        // active home-surface Compose tab's persisted `run:{inFlight,dispatchedAt}` off this signal;
        // completion clears it via `compose_advisory_comments`, failure via the `{false}` emit below.
        dispatch("workspace", { type: "nda_review_dispatch_active", dispatchActive: true });
      }
    }, [dispatch]),
  });
  acceptChipsRef.current = chips.acceptChips;

  // task 021 (FR-07/08/09 interactive orientation + confirmation gate) — the stateful gate
  // controller. Declared AFTER `chips` (needs `chips.dispatchBinding`/`chips.acceptChips`) and
  // AFTER `classifyBindingId`/`ndaReviewBindingId` (both resolved above via the SAME
  // capability-discovery seam). `dataService` reuses the SAME Xrm.WebApi adapter instance the
  // email-recipient lookup already created (§11 — one instance, two read-only consumers).
  const agreementReviewGate = useAgreementReviewGate({
    bffBaseUrl,
    getAccessToken,
    getSessionId,
    dispatch,
    dataService: emailLookupDataService,
    classifyBindingId,
    reviewBindingId: ndaReviewBindingId,
    mountFileInCompose,
    dispatchReviewBinding: chips.dispatchBinding,
    acceptChips: chips.acceptChips,
    enqueueAssistantMessage: injection.enqueue,
    inject: injection.inject,
    // task 031 (DEF-09 routing): resolves the reviewed file's REAL document session so the gate's
    // dispatch(es) can target it via sessionIdOverride — see documentSessionWaiter.ts header.
    awaitDocumentSessionId: awaitDocumentSessionIdFor,
  });
  // UAT round-4 (item #9): publish `rerunThorough` to the ref the "Rerun a full analysis" card
  // reaches (declared above, before `chips`) — see that ref's doc comment.
  rerunThoroughRef.current = agreementReviewGate.rerunThorough;

  // task 023 (classifier-path lookup-write seam) — resolves the CLASSIFIER-determined subDomain for
  // a session being promoted (`HistoryOverlay`'s "Promote to Analysis…"), so the promote flow can
  // write the resolved `sprk_agreementtype` lookup onto the new Analysis (persistence-matches-
  // routing; see `agreementTypeLookupWrite.ts`). Only the CURRENT session's classifier resolution is
  // knowable client-side (`agreementReviewGate` is session-scoped, reset in `handleSessionCreated`)
  // — promoting a DIFFERENT/older session from the History list deliberately returns `null` rather
  // than guessing (never a fabricated lookup value). An EXPLICIT-door bind is NOT surfaced here — it
  // already has a persisted lookup from its own door (wizard/deep-link/open-existing), so it needs
  // no NEW write (see `getLastResolvedSubDomainKey`'s own doc comment).
  const getLastResolvedSubDomainKey = agreementReviewGate.getLastResolvedSubDomainKey;
  const resolveClassifiedSubDomainForSession = React.useCallback(
    (sessionId: string): string | null => {
      if (sessionId !== chatSessionIdRef.current) return null;
      return getLastResolvedSubDomainKey();
    },
    [getLastResolvedSubDomainKey]
  );

  // UAT round-5 #9 — when a dispatch settles WITHOUT an NDA result having completed the run, mark it
  // failed. `fail` no-ops unless the run is still `running` (a successful `complete` already won, since
  // `onDispatchResult` runs before the dispatch's `.finally` clears `dispatching`). `ndaRun.fail` is a
  // stable callback, so this effect only re-runs when `chips.dispatching` actually flips.
  const ndaRunFail = ndaRun.fail;
  React.useEffect(() => {
    if (!chips.dispatching) ndaRunFail();
  }, [chips.dispatching, ndaRunFail]);

  // UAT round-6 (item #15a) — clear the cross-navigation resume flag on review FAILURE. `ndaRun.status`
  // reaches 'error' ONLY when `fail()` fired while a review run was still 'running' (a genuine review
  // failure — a successful `complete()` already transitioned to 'complete' and fail() no-ops). Emitting
  // the `{dispatchActive:false}` clear exactly here means a failed run doesn't leave a stale in-flight
  // flag that would resume a dead spinner+poll on a later return. The SUCCESS clear rides the separate
  // `compose_advisory_comments` completion event (fires even for a zero-findings clean review), so this
  // effect is failure-only.
  React.useEffect(() => {
    if (ndaRun.status === "error") {
      dispatch("workspace", { type: "nda_review_dispatch_active", dispatchActive: false });
    }
  }, [ndaRun.status, dispatch]);

  // Shared Xrm navigation service — drives the saved-preview record "Open record" action below
  // (~line 2952). assistant-enhancements-r2 task 001 (FR-E1) removed the spine-driven
  // proactive-suggestion surface (useSuggestionCards) that formerly also consumed this service's
  // openRecordModal; the service is retained for the saved-preview path.
  const previewNavigationService = React.useMemo(() => createXrmNavigationService(), []);

  // ── spaarkeai-assistant-enhancements-r1 P0(b): TEXT/agent-path surface launch ──
  // The BFF's BindingCapabilityTool emits a `surface_launch` SSE event when the
  // agent turn selected a create capability whose Binding routes to a client-owned
  // surface (create-matter/-event/-task, …). Open the pre-seeded surface via the
  // SAME shared launchSurface + resolvedLookups-lift the chip/Click path uses
  // (useConsumerChips) — ZERO intent detection (ADR-039: consumerType IS the
  // server's routing decision; the server already grounded the draft + ids).
  const handleSurfaceLaunch = React.useCallback(
    (payload: { consumerType: string; payload?: Record<string, unknown> | null }) => {
      if (!payload.consumerType) {
        return;
      }

      // Registry-driven surface routing (no per-consumerType literals). The client
      // launch registry (surfaceLaunchRegistry.ts) is the single source of truth for
      // "which surface does this capability open"; we branch purely on the resolved
      // entry's `kind`:
      //  - workspace-tab / layout → open via the PaneEventBus `widget_load` channel
      //    (Class-2a in-app tabs: grids/widgets, e.g. list-tasks → "My Tasks" grid).
      //    Any future grid/widget surface is ONE registry entry — no code branch here.
      //  - wizard / oob-form → fall through to the sessionStorage hand-off (launchSurface).
      // The capability that opens a workspace tab drafts nothing — ignore any payload.
      const surfaceEntry = resolveSurfaceLaunch(payload.consumerType);
      if (surfaceEntry && (surfaceEntry.kind === "workspace-tab" || surfaceEntry.kind === "layout")) {
        dispatch("workspace", {
          type: "widget_load",
          widgetType: surfaceEntry.surface,
          widgetData: surfaceEntry.widgetData ?? {},
          displayName: surfaceEntry.title,
        } as WorkspacePaneEvent);
        return;
      }

      const draft =
        payload.payload && typeof payload.payload === "object" ? payload.payload : {};
      // Lift the server-enriched `resolvedLookups` out of draftValues so the
      // closed-set ids pre-select dropdowns and never land in a free-text field.
      const { resolvedLookups: rawResolved, ...draftValues } = draft as Record<string, unknown> & {
        resolvedLookups?: Record<string, ResolvedLookup>;
      };
      // W-2/W-5 file leg (UAT 2026-07-17): carry the session's file(s) BY REFERENCE — sessionId +
      // the session documentId(s) are all the wizard needs to fetch the binary
      // (GET .../documents/{id}/content) and attach it to the new record via its upload+link
      // pipeline. Never inline binary (envelope invariant).
      //
      // R7-3 (UAT 2026-07-21): "create a matter from this file" opened the wizard with NO file
      // loaded. Root cause: this path carried ONLY the single `activeSourceDocRef`, which is often
      // still null right after an upload (it back-fills on the summarize/draft flow, not every
      // upload). Prefer ALL promoted session files — the SAME source Quick Start uses
      // (`promotedFileIdsByNameRef`) — falling back to the single active source doc. Keeps the
      // server's drafted field values (this is still the server-driven surface_launch path).
      const sessionId = getSessionId();
      const promoted = Array.from(promotedFileIdsByNameRef.current.entries());
      let fileIds: string[] | undefined;
      let sourceFiles: string[] | undefined;
      if (promoted.length > 0) {
        fileIds = promoted.map(([, id]) => id);
        sourceFiles = promoted.map(([name]) => name);
      } else {
        const activeFile = activeSourceDocRef.current;
        fileIds = activeFile?.sessionFileId ? [activeFile.sessionFileId] : undefined;
        sourceFiles = activeFile?.fileName ? [activeFile.fileName] : undefined;
      }
      void launchSurface({
        consumerType: payload.consumerType,
        draftValues,
        resolvedLookups: rawResolved ?? {},
        fileIds,
        source: sessionId ? { sessionId } : undefined,
        provenance: sourceFiles && sourceFiles.length > 0 ? { sourceFiles } : undefined,
        bffBaseUrl,
      });
    },
    [bffBaseUrl, getSessionId, dispatch]
  );

  // UAT R4-12: the session's attached-file context for Quick Start wizards. Unlike the chip/text
  // surface-launch (which carries the single ACTIVE source file), Quick Start carries ALL promoted
  // session files (the user expects both uploaded files to reach the wizard). By reference only —
  // session id + session file ids + display names; the wizard fetches binaries itself. Null when none.
  const getQuickStartFileContext = React.useCallback((): {
    sessionId: string | null;
    fileIds: string[];
    fileNames: string[];
  } | null => {
    const entries = Array.from(promotedFileIdsByNameRef.current.entries());
    if (entries.length === 0) return null;
    return {
      sessionId: getSessionId(),
      fileIds: entries.map(([, sessionFileId]) => sessionFileId),
      fileNames: entries.map(([fileName]) => fileName),
    };
  }, [getSessionId]);

  // ── P1-1 (UAT 2026-07-18): cold-open get-started cards ────────────────────
  // Three quick-start actions on the welcome stage, each reusing an EXISTING
  // launch mechanism (no new launcher — CLAUDE.md §11).
  const handleWelcomeSummarize = React.useCallback(() => {
    launchSummarizeFilesWizard({ bffBaseUrl });
  }, [bffBaseUrl]);
  const handleWelcomeCreateMatter = React.useCallback(() => {
    void launchSurface({ consumerType: "create-matter", bffBaseUrl });
  }, [bffBaseUrl]);
  const handleWelcomeCompose = React.useCallback(() => {
    // Open a blank Compose tab (same widget_load contract the add-to-DMS fallback uses).
    dispatch("workspace", {
      type: "widget_load",
      widgetType: "compose",
      widgetData: { source: "welcome-compose" },
      displayName: "Compose",
    } as WorkspacePaneEvent);
  }, [dispatch]);

  // ── Serial action queue (FR-18) ────────────────────────────────────────
  // Rapid, distinct AI actions (e.g. FR-14 toolbar's Compare then Draft) must
  // run strictly one-at-a-time through the shipped dispatchConsumer seam —
  // see useSerialActionQueue for the full ordering rationale + §11
  // justification. Own bound dispatcher (mirrors useConsumerChips's
  // createConsumerDispatcher usage): kept independent so this queue's
  // serialization guarantee holds regardless of which future caller (toolbar,
  // chip, or other) reaches it. `dispatchComposeAction` is the ready-made
  // enqueue+render entry point the FR-14 toolbar (task 030) hand-off wires
  // into at integration (contract-only dependency — see
  // useSerialActionQueue's contract-naming note); mounting it now keeps the
  // queue live and independently testable ahead of that integration.
  const composeActionDispatcher = React.useMemo(
    () =>
      createConsumerDispatcher({
        bffBaseUrl,
        getSessionId,
        getAccessToken,
        publishPaneEvent: (channel, event: DispatchWorkspaceEvent) => dispatch(channel, event as WorkspacePaneEvent),
        // UAT-R3 defect #3c (task 112): the Compose editor tab has NO renderer
        // subscribed to the `workspace`-channel section-reveal bridge
        // (`useComposeWorkspaceReceivers` only reacts to `compose_context_insert`
        // / `compose_assistant_insert` / `compose_qa_highlight`) — those events
        // were dead output ("nothing else happens"), and awaiting their paced
        // reveal needlessly delayed this Promise for a renderer nobody mounts.
        // Suppressed HERE ONLY (this dispatcher instance, scoped to the Compose
        // surface) — `useConsumerChips`'s own `createConsumerDispatcher` call
        // is untouched, so the general Assistant/chip surface keeps rendering
        // dispatched results into the WorkspacePane exactly as before
        // (ADR-030: additive, default-false option; shared contract for other
        // surfaces unchanged).
        suppressWorkspaceSectionBridge: true,
      }),
    [bffBaseUrl, getSessionId, getAccessToken, dispatch]
  );
  const actionQueue = useSerialActionQueue(composeActionDispatcher);

  // ── FR-13 Step 3: draft-alternative APPLY leg (design §3 Flow 5 + §7.2) ──
  // After a Compose action dispatches, a `compose-draft-alternative` writes a
  // `compose`-disposition SessionOutput to the ledger (ADR-040 store-before-
  // render). The Assistant then emits the EXISTING `workspace.compose_assistant_insert`
  // discriminant REFERENCING that stored entry (`ledgerRef = {bindingId}@t{n}`) —
  // NEVER the edit payload; ComposeWorkspace re-materializes the pending redline
  // FROM the ledger. Informational actions write no compose output → no emit
  // (resolveCurrentComposeLedgerRef gates on bindingId). Fire-and-forget + fully
  // soft-fail: ComposeWorkspace's refresh-materialize path recovers regardless.
  // Uses the ledger READ endpoint (no new route) + an EXISTING discriminant
  // (zero new PaneEventBus discriminants — ADR-030).
  const emitComposeApplyLeg = React.useCallback(
    async (bindingId: string, sessionIdOverride?: string): Promise<string | null> => {
      // DEF-09: for a compose EDIT action the ledger write landed in the editor's
      // DOCUMENT session, so the apply-leg READ + the Flow-5 event MUST use that same
      // session (not the chat session) — otherwise the ledgerRef resolves to null and
      // no inline redline appears. Informational actions omit it (chat session; they
      // resolve no compose output anyway → null → no emit).
      const sessionId = sessionIdOverride ?? getSessionId();
      if (!sessionId || !bffBaseUrl) return null;
      try {
        const url = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/compose-outputs`;
        const response = await authenticatedFetch(url, { method: "GET" });
        if (!response.ok) return null; // 404 = no compose outputs yet — nothing to apply
        const outputs = (await response.json()) as unknown;
        const ledgerRef = resolveCurrentComposeLedgerRef(outputs, bindingId);
        if (!ledgerRef) return null; // not a compose-writing action (e.g. explain/compare)
        // Flow 5 emit — `compose_assistant_insert` is now a TYPED discriminant on
        // the `workspace` channel (task 104), so the built event is assignable
        // directly with no cast (was `as unknown as WorkspacePaneEvent`).
        dispatch("workspace", buildComposeApplyEvent(ledgerRef, bindingId, sessionId));
        // Return the applied compose ledger key so the FR-17 undo/replace affordance (task 034)
        // can target THIS edit for a durable supersession.
        return ledgerRef;
      } catch {
        // Non-fatal: the compose SSE frame + ComposeWorkspace refresh-materialize
        // path (ADR-040) still recover the drafted content on next load.
        return null;
      }
    },
    [getSessionId, bffBaseUrl, authenticatedFetch, dispatch]
  );

  // ── FR-17 undo/replace via ledger supersession (task 034) ────────────────
  // "undo that" / "try another approach" retract the last AI-applied redline as a DURABLE ledger
  // supersession (a new superseding `compose` SessionOutput), never a client DOM undo (ADR-040). The
  // hook re-materializes via the SAME Flow-5 apply signal above (references the ledger entry, not the
  // payload — ADR-030) + task 033's usePendingRedline. `dispatchApply` wraps the workspace-channel
  // dispatch so the hook stays decoupled from the bus.
  const dispatchApply = React.useCallback(
    (event: ComposeAssistantToWorkspaceFlow) => dispatch("workspace", event as WorkspacePaneEvent),
    [dispatch]
  );
  const supersession = useEditSupersession({ bffBaseUrl, getSessionId, authenticatedFetch, dispatchApply });
  // Destructure the memoized callbacks so downstream useCallbacks depend on stable identities.
  const { trackAppliedEdit, clearTrackedEdit, undo: undoEdit, tryAnother: tryAnotherEdit } = supersession;

  // DEF-12 — the editor's redline-accept, published by ComposeWorkspace into the cross-pane bridge.
  // Null when no live editor is registered (standalone mount / no Compose tab open). The Assistant's
  // per-message "Accept" routes through this to the EXISTING `usePendingRedline.accept` in the editor.
  const acceptRedlineViaBridge = useComposeRedlineAccept();

  // R4 — "Insert into document". The editor's insert-suggestion conduit, published by ComposeWorkspace
  // into the cross-pane bridge. UAT 2026-07-19: the per-message insert button was removed (noise), so
  // the conduit is no longer wired to SprkChat — kept subscribed here for a future TARGETED affordance.
  void useComposeInsertSuggestion();

  // FIX #1b — the editor's Save conduit (create-on-save / save-to-matter), published by ComposeWorkspace
  // into the cross-pane bridge. Null when no live editor is registered (no Compose tab open) — the
  // "Add the document to the DMS" chip then falls back to re-activating the Compose tab.
  const composeSave = useComposeSave();

  // FIX #7a — the document persisted by the last Compose Save, surfaced to the File Preview modal that
  // the chat "Open preview" affordance opens. `savedPreview` holds the id + display name; `previewOpen`
  // gates the modal. Reuses the shared `RichFilePreviewDialog` + the BFF `GET /api/documents/{id}/
  // preview-url` endpoint (§11 reuse — no new component/service), mirroring ComposeWorkspace's #1(b) wiring.
  const [savedPreview, setSavedPreview] = React.useState<{ documentId: string; fileName?: string } | null>(null);
  const [previewOpen, setPreviewOpen] = React.useState(false);

  // FIX #7a — the preview modal's "Open record" action reuses the ONE `previewNavigationService`
  // hoisted above (also drives the task-052 suggestion-card modal open).

  // ai-advanced-capabilities-nda-r1 task 031 — NDA-REVIEW advisory comments. A client-derived
  // projection of the SAME ledgered NDA-REVIEW result (ADR-040; no second model call, no new
  // server disposition). Detects the NDA-REVIEW output shape structurally (mirrors
  // composeResultFormat.ts's mutually-exclusive-required-fields convention) and, on a match,
  // emits `compose_advisory_comments` on the workspace channel so
  // useComposeWorkspaceReceivers materializes a comment thread per flagged clause. See
  // useNdaReviewAdvisoryCommentsBridge.ts for the full rationale.
  const ndaReviewAdvisoryComments = useNdaReviewAdvisoryCommentsBridge({ dispatch, getSessionId });
  // nda-r1 follow-up: publish emitFromResult to the ref the chips controller reaches (declared above the
  // useConsumerChips call), so the "Review an NDA" card dispatch materializes flagged clauses as comments.
  ndaReviewEmitRef.current = ndaReviewAdvisoryComments.emitFromResult;

  const dispatchComposeAction = React.useCallback(
    (request: ComposeActionRequest): Promise<DispatchConsumerResult> => {
      // DEF-09: an editor-materializing compose EDIT action (Draft alternative) carries
      // the Compose editor's DOCUMENT session id. Route the dispatch to THAT session
      // (via args.sessionIdOverride) so the `compose` SessionOutput lands where
      // ComposeWorkspace reads compose-outputs to materialize the inline redline — the
      // WRITE and the redline-materialize READ must coincide. Informational actions omit
      // it (chat session dispatch + Assistant-rendered prose), unchanged.
      const documentSessionId = request.documentSessionId;
      const isEditAction = typeof documentSessionId === 'string' && documentSessionId.length > 0;
      const enqueueRequest: ComposeActionRequest = isEditAction
        ? { ...request, args: { ...(request.args ?? {}), sessionIdOverride: documentSessionId } }
        : request;

      return actionQueue.enqueue(enqueueRequest).then((dispatched) => {
        if (!isEditAction && dispatched.result !== undefined && dispatched.result !== null) {
          // UAT-R3 defect #3b (task 112): INFORMATIONAL actions render full grounded
          // prose. Try the 5 Compose action shapes first; fall back to the general
          // Event-path formatter (which still degrades genuinely unknown shapes to the
          // ```json``` fence — that last-resort branch is preserved verbatim).
          const formatted =
            formatComposeActionResultMarkdown(dispatched.result) ?? formatEventOutputMarkdown(dispatched.result);
          injection.enqueue(makeLocalAssistantMessage(formatted));
          // task 031 — a client-derived projection alongside the prose above (NDA-REVIEW's
          // {overallRisk, flaggedSections[]} shape is not one of the 5 known Compose action
          // shapes, so it renders via the formatEventOutputMarkdown fallback AND, here,
          // separately materializes as document comments; no-op for any other action's result).
          ndaReviewAdvisoryComments.emitFromResult(dispatched.result);
        }
        // Draft-alternative apply leg (Flow 5) — references the ledger entry, never the payload.
        // Capture the applied compose ledger key so the FR-17 undo/replace affordance targets THIS
        // edit (task 034). Reads the DOCUMENT session for an edit action (DEF-09) so the ledgerRef
        // resolves. Informational actions resolve no compose output → no track → no affordance.
        void emitComposeApplyLeg(request.bindingId, documentSessionId).then((ledgerRef) => {
          if (isEditAction) {
            // DEF-09 + DEF-12: SUMMARY-ONLY confirmation — the alternative is the inline redline in
            // the document, not a chat message. DEF-12 attaches the Accept/Reject/Try-another controls
            // to THIS message via `composeEdit` metadata (ledgerRef), so it must be injected AFTER the
            // apply leg resolves the ledgerRef. If (defensively) the ledger didn't resolve, fall back
            // to a plain confirmation (no controls — there is no addressable edit to act on).
            // DEF-11: a whole-document revise uses the document-scoped confirmation copy; a
            // selection edit (DEF-09, unchanged) keeps the original wording. Same controls either way.
            const baseConfirmation =
              request.revisionScope === "whole-document"
                ? COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION
                : COMPOSE_EDIT_CONFIRMATION;
            // UAT round-8 #7 — the reviewer asked for a Copilot-style explanation of WHAT/WHY changed
            // (the summary-only confirmation gave no detail). Append the model's own rationale/summary
            // from the edit result — the explanation ONLY, never the proposed text (that IS the redline).
            const explanation = extractComposeEditExplanation(dispatched.result);
            const confirmationText = explanation
              ? `${baseConfirmation}\n\n**What I changed:** ${explanation}`
              : baseConfirmation;
            // task 042 (FR-12) — bold, separated clause-location header when resolvable (see
            // extractComposeEditLocationLabel's doc comment for current wiring status + fallback).
            const locationLabel = extractComposeEditLocationLabel(request, dispatched.result);
            const finalConfirmationText = withComposeEditLocationHeader(confirmationText, locationLabel);
            injection.enqueue(
              ledgerRef
                ? makeComposeEditControlsMessage(finalConfirmationText, {
                    ledgerRef,
                    bindingId: request.bindingId,
                  })
                : makeLocalAssistantMessage(finalConfirmationText)
            );
          }
          if (ledgerRef) {
            trackAppliedEdit({ ledgerRef, bindingId: request.bindingId, request, sessionId: documentSessionId });
          }
        });
        return dispatched;
      });
    },
    // Depend on the memoized `trackAppliedEdit` (stable), not the whole `supersession` object (new
    // identity each render) — keeps dispatchComposeAction stable so the bridge registration + serial
    // queue don't re-register every render. Same reasoning for
    // `ndaReviewAdvisoryComments.emitFromResult` (itself stable — memoized on `[dispatch,
    // getSessionId]`, both stable — see useNdaReviewAdvisoryCommentsBridge.ts) over the whole
    // `ndaReviewAdvisoryComments` object, which is a fresh literal every render.
    [actionQueue, injection, emitComposeApplyLeg, trackAppliedEdit, ndaReviewAdvisoryComments.emitFromResult]
  );

  // FR-17 affordance handlers (task 034). "Try another approach" passes the CURRENT
  // dispatchComposeAction so the fresh Draft-Alternative re-runs through the serial queue + apply leg
  // (which re-materializes + re-tracks the new edit); passing it at call time avoids a definition cycle.
  const handleUndoEdit = React.useCallback(() => {
    void undoEdit();
  }, [undoEdit]);
  const handleReplaceEdit = React.useCallback(() => {
    void tryAnotherEdit(dispatchComposeAction);
  }, [tryAnotherEdit, dispatchComposeAction]);

  // DEF-12 — Accept control on the Assistant confirmation message. Routes to the EXISTING editor
  // accept (`usePendingRedline.accept`) via the compose bridge, then clears the tracked edit so the
  // controls disappear (the redline is now committed — nothing to retract). Reject and Try-another
  // reuse the shipped `handleUndoEdit` / `handleReplaceEdit` (useEditSupersession.undo / tryAnother),
  // which operate on the tracked `lastEdit` — the live edit whose ledgerRef this message carries
  // (SprkChat only renders the controls on that message). No parallel accept/reject logic.
  const handleAcceptComposeEdit = React.useCallback(
    (ledgerRef: string) => {
      acceptRedlineViaBridge?.(ledgerRef);
      clearTrackedEdit();
    },
    [acceptRedlineViaBridge, clearTrackedEdit]
  );

  // FIX #3 — "Keep redline". Dismiss the Assistant action prompt but LEAVE the pending redline marks in
  // place so the user keeps editing. Clears ONLY the tracked edit (so the per-message controls hide) —
  // does NOT call `acceptRedlineViaBridge` and does NOT undo/reject. The redline marks + the per-change
  // on-click Accept/Reject popover remain in the editor (no editor mutation, no ledger write).
  const handleKeepComposeEdit = React.useCallback(() => {
    clearTrackedEdit();
  }, [clearTrackedEdit]);

  // DEF-11 — whole-document revise disambiguation. Mirrors the `/summarize` prompt-first
  // pattern (`routeSummarizeIntent` in `useAttachments.handleBeforeSendMessage`): a synchronous
  // BEFORE-send hook that may inject a deterministic LOCAL interjection alongside the outbound
  // message — it never cancels or rewrites the send itself (ADR-039: no client-side capability
  // routing). A bare `/revise` is ambiguous (no intent, no instruction) → inject the two-path
  // disambiguation copy (highlight a section directly vs. whole-document) naming the four
  // `compose-revise-document` intents as typed `/revise <intent>` follow-ups (`routeReviseIntent`
  // parses those deterministically on the NEXT send). `/revise <intent>` already specifies intent →
  // no interjection; the caller dispatches through the SAME `dispatchComposeAction` path as any other
  // Compose edit action (bindingId still comes from capability discovery, never from this routing).
  //
  // NOTE (honest boundary, not silently skipped): the four intents are NOT rendered as REAL
  // clickable suggestion chips here. `useConsumerChips`'s wire parser (`parseConsumerChips`)
  // unconditionally drops any chip missing a non-empty `bindingId` — there is no capability-discovery
  // seam yet that resolves a `compose-revise-document` Binding id client-side (this is a brand-new
  // BFF action; no scope/binding lookup exists for it, unlike the toolbar's stub-then-register
  // pattern in `ComposeAiToolbar`, which the chip pipeline cannot express since it silently
  // filters incomplete chips rather than rendering a disabled affordance). Wiring fabricated chip
  // ids would render broken buttons; the text fallback covers the same four options via the
  // `/revise <intent>` follow-up, which IS fully wired and tested (`composeReviseRouting.test.ts`).
  const handleBeforeSendMessage = React.useCallback(
    (messageText: string): void => {
      attachments.handleBeforeSendMessage(messageText);
      const decision = routeReviseIntent(messageText);
      if (decision.kind === "disambiguate") {
        injection.enqueue(makeLocalAssistantMessage(decision.interjection));
      }
    },
    [attachments, injection]
  );

  // Wave 4 (end-to-end revise) — dispatch `compose-revise-document` for the WHOLE open document into
  // the DOCUMENT session so the edits materialize as an inline redline (+ comments for flag-risks),
  // NOT narrated prose. This reuses the SHIPPED `dispatchComposeAction` edit path verbatim: a
  // non-empty `documentSessionId` makes it an EDIT (routes to the doc session, confirmation-only,
  // no prose — DEF-09), and `revisionScope: 'whole-document'` selects the DEF-11 confirmation copy.
  // The bindingId comes ONLY from capability discovery (ADR-039); documentText is resolved
  // SERVER-SIDE from the registered document session's file (ContextBinder file-operand path when the
  // args omit a documentText operand) — see the wave notes. `instruction` rides only for `custom`.
  const dispatchReviseDocument = React.useCallback(
    (revisionIntent: RevisionIntent, instruction: string | undefined, documentSessionId: string): void => {
      if (!reviseBindingId) {
        injection.enqueue(
          makeLocalAssistantMessage(
            "Sorry — the document-revision capability isn't available right now. Please try again."
          )
        );
        return;
      }
      const slots: Record<string, unknown> = { revisionIntent };
      if (instruction && instruction.trim().length > 0) {
        slots.instruction = instruction.trim();
      }
      void dispatchComposeAction({
        id: `compose-revise-document#${(reviseDispatchSeqRef.current += 1)}`,
        bindingId: reviseBindingId,
        args: { slots },
        documentSessionId,
        revisionScope: "whole-document",
      });
    },
    [reviseBindingId, dispatchComposeAction, injection]
  );

  // FIX #1 (editor-centric reframe) — a DOCUMENT-LEVEL action chip click (summarize / add-to-DMS /
  // draft-email). These replace the old revision-type chips: whole-document revision is now driven
  // from the editor (highlight text → inline toolbar), so the post-mount chips offer document-level
  // actions instead, each REUSING an existing mechanism (no new services — CLAUDE.md §11):
  //  - "summarize"   → dispatch the shipped `compose-summarize` capability on the CHAT session via the
  //                    shared `chips.dispatchBinding` path (informational; renders as an Assistant
  //                    message, exactly like any other Click-path consumer chip).
  //  - "add-to-dms"  → hand off to the workspace pane (which owns the Compose editor) to run the
  //                    create-on-save / save-to-matter flow for the mounted document and open the File
  //                    Preview modal for the created Document (NOT a workspace mount). A confirmation
  //                    message keeps the flow sensible even if the doc isn't saved yet (offer to open it).
  //  - "draft-email" → dispatch the Email workspace `widget_load` using the EXACT interop contract the
  //                    WorkspacePane owner's handler expects (the stub tab itself is created by that owner).
  // task 022 — "Review an NDA" card click: (1) open the classified file in the Compose tab via
  // the EXISTING `mountFileInCompose` helper (the SAME dynamic per-file widget_load seed
  // "Revise document" already uses — the `nda-review` surfaceLaunchRegistry entry documents the
  // same "compose" surface identity for a future TEXT-path surface_launch dispatch, but a
  // static registry `widgetData` can't carry a per-click file id, so the CLICK path here reuses
  // the existing dynamic helper directly rather than routing through resolveSurfaceLaunch);
  // (2) dispatch NDA-REVIEW on that file through the SAME shared Click-path
  // `chips.dispatchBinding` seam every other consumer chip uses (ADR-039 — one dispatch
  // mechanism), scoping the run to just this file via `slots: { fileIds }` (the identical
  // wire shape the server's own chip transitions already use — EventRulesService.cs).
  const handleReviewNda = React.useCallback((): void => {
    if (!ndaReviewFile) return;
    const { fileId, fileName, cardLabel } = ndaReviewFile;
    mountFileInCompose(fileId, fileName);
    if (ndaReviewBindingId) {
      // UAT round-3 (item #7): the direct chip/card click no longer dispatches immediately — it now
      // presents the SAME one-turn Quick/Thorough depth ask task 070 built for the gate's other
      // branches (reusing its `pendingDepthRef`/chip machinery, not a second mechanism). The chip
      // click already committed the type (no `subDomain` slot either way — unchanged pre-070 wire
      // shape), so the depth pick is the ONLY remaining question; `runDirectReview` handles the
      // `awaitDocumentSessionIdFor`/`sessionIdOverride` threading (task 031 DEF-09) once answered.
      agreementReviewGate.runDirectReview(fileId, fileName);
    } else {
      // Capability discovery hasn't resolved yet (or the catalog is unreachable) — the file still
      // opens in Compose above; tell the user the review itself didn't start (never a silent drop).
      // task 064: message derives from the matched capability's cardLabel instead of a hardcoded
      // "NDA review" string, so it reads correctly for any registered document-review type.
      injection.enqueue(
        makeLocalAssistantMessage(`Sorry — "${cardLabel}" isn't available right now. Please try again.`)
      );
    }
    setNdaReviewFile(null);
  }, [ndaReviewFile, ndaReviewBindingId, mountFileInCompose, agreementReviewGate, injection]);

  const handleDocAction = React.useCallback(
    (action: ComposeDocAction): void => {
      // R6-2: the user acted on a doc-action chip — clear the revise-context flag so the strip
      // returns to the consumer cards (otherwise the doc-action row stays pinned all session).
      setReviseChipsPending(false);
      switch (action) {
        case "summarize": {
          if (!summarizeBindingId) {
            injection.enqueue(
              makeLocalAssistantMessage(
                "Sorry — the summarize capability isn't available right now. Please try again."
              )
            );
            return;
          }
          // Same shared dispatchConsumer path the other chat consumer chips use (chat session).
          chips.dispatchBinding(summarizeBindingId, { slots: undefined });
          return;
        }
        case "add-to-dms": {
          // FIX #1b — trigger the ACTUAL create-on-save via the cross-pane bridge conduit (the editor
          // owns the create-on-save / save-to-matter flow). On success ComposeWorkspace fires the
          // save-completed conduit, and `handleComposeSaveCompleted` posts a PERSISTENT "Saved to the
          // DMS." chat message with an "Open preview" affordance — no transient banner. When no live
          // editor is registered (defensive: no Compose tab open), fall back to re-activating the
          // single 'compose' tab so the user can save from the editor.
          if (composeSave) {
            void composeSave();
          } else {
            dispatch("workspace", {
              type: "widget_load",
              widgetType: "compose",
              widgetData: { source: "compose-add-to-dms" },
              displayName: "Compose",
            } as WorkspacePaneEvent);
            injection.enqueue(
              makeLocalAssistantMessage(
                "Open the document in the Compose editor, then save it to add it to the DMS."
              )
            );
          }
          return;
        }
        case "draft-email": {
          // EMAIL path — dispatch the Email workspace widget_load with the EXACT interop contract so it
          // interops with the WorkspacePane owner's handler.
          dispatch("workspace", {
            type: "widget_load",
            layoutName: "Email",
            widgetType: "email",
            widgetData: { source: "compose-reporting-email" },
          } as WorkspacePaneEvent);
          return;
        }
        default:
          return;
      }
    },
    [summarizeBindingId, chips, injection, dispatch, composeSave]
  );

  // UAT R4-6 / R4-11 — local-action chips (Send as email / Save to document / Ask about these files).
  // Each REUSES an existing affordance (CLAUDE.md §11), never a net-new capability:
  //  - Send as email     → the `draft-email` Email-widget bridge (handleDocAction)
  //  - Save to document   → the `add-to-dms` Compose create-on-save bridge (handleDocAction)
  //  - Ask about these files → an honest prompt nudge: the files are already attached to the session,
  //    so the user's next chat turn is grounded in them — no capability is faked or dispatched.
  const handleLocalChipAction = React.useCallback(
    (actionId: string): void => {
      switch (actionId) {
        case LOCAL_CHIP.sendAsEmail: {
          // R5-9: open the shared Email Compose modal, seeded from the last drafted correspondence
          // (subject / body / suggested recipients). Falls back to a blank composer if none.
          const draft = lastCorrespondenceDraftRef.current;
          setEmailSeed({
            initialSubject:
              draft && typeof draft.subject === "string" ? draft.subject : undefined,
            initialBody: draft && typeof draft.body === "string" ? draft.body : undefined,
            initialTo: draft ? toDisplayList(draft.recipients_suggestion) : undefined,
          });
          return;
        }
        case LOCAL_CHIP.saveToDocument:
          handleDocAction("add-to-dms");
          return;
        case LOCAL_CHIP.askAboutFiles:
          injection.enqueue(
            makeLocalAssistantMessage(
              "Ask me anything about the attached file(s) — type your question below and I'll answer using their contents."
            )
          );
          return;
        case LOCAL_CHIP.reviseInCompose:
          // R5-1: same on-demand open-in-Compose the files tray used to trigger.
          handleReviseInCompose();
          return;
        case LOCAL_CHIP.ndaReview:
          // nda-r1 follow-up (UAT 2026-07-26): the "Review an NDA" Suggested-Next-Steps card runs the
          // SAME mount-in-Compose + dispatch-nda-review flow the old top-of-pane card used.
          handleReviewNda();
          return;
        case LOCAL_CHIP.agreementReviewConfirmQuick:
        case LOCAL_CHIP.agreementReviewConfirmThorough:
        case LOCAL_CHIP.agreementReviewGeneral:
        case LOCAL_CHIP.agreementReviewBoth:
        case LOCAL_CHIP.agreementReviewDepthQuick:
        case LOCAL_CHIP.agreementReviewDepthThorough:
          // task 021 — the confirmation-gate chips (below-threshold confirm+depth / pick-another /
          // non-agreement general-review escape hatch / composite "Both") all route back to the
          // gate controller. task 070 — the standalone depth-choice chips (auto-proceed / explicit-
          // door ask / composite post-pick) route the SAME way; the controller resolves the pending
          // decision and dispatches the review.
          agreementReviewGate.handleGateChipAction(actionId);
          return;
        default:
          // task 021 — the composite choice-of-lens chips carry a dynamic per-candidate id
          // (`local:agreement-review-lens:{subDomainKey}`) that cannot be a fixed `case` label; the
          // gate controller itself no-ops on any id it doesn't recognize (defensive, never throws).
          if (actionId.startsWith("local:agreement-review-lens:")) {
            agreementReviewGate.handleGateChipAction(actionId);
          }
          return;
      }
    },
    [handleDocAction, injection, handleReviseInCompose, handleReviewNda, agreementReviewGate]
  );
  localChipActionRef.current = handleLocalChipAction;

  // FIX #7a — the save-completed conduit handler (registered on the bridge below). ComposeWorkspace
  // calls it after a successful create-on-save with the persisted document's id + filename. We inject
  // a PERSISTENT local Assistant message ("Saved '{filename}' to the DMS.") carrying `savedPreview`
  // metadata so SprkChat renders an "Open preview" button, and remember the id so the modal can open.
  const handleComposeSaveCompleted = React.useCallback(
    ({ documentRecordId, fileName }: { documentRecordId: string; fileName?: string }): void => {
      if (!documentRecordId) return;
      setSavedPreview({ documentId: documentRecordId, fileName });
      injection.enqueue(makeSavedToDmsMessage(fileName, documentRecordId));
    },
    [injection]
  );
  useRegisterComposeSaveCompletedHandler(handleComposeSaveCompleted);

  // FIX #7a — the chat "Open preview" affordance (SprkChat message action). Opens the File Preview
  // modal for the message's saved document id. The id rides on the message metadata, so a session with
  // multiple saves opens the correct document per message.
  const handleOpenSavedPreview = React.useCallback(
    (documentId: string, fileName?: string): void => {
      setSavedPreview({ documentId, fileName });
      setPreviewOpen(true);
    },
    []
  );

  // FIX #7a — fetch the ephemeral iframe preview URL for the saved document (RichFilePreview's
  // fetchPreviewUrl contract). Same endpoint + shape ComposeWorkspace's #1(b) wiring uses; reuses the
  // already-available `authenticatedFetch` + `bffBaseUrl` (no new service — §11).
  const fetchSavedPreviewUrl = React.useCallback(async (): Promise<string | null> => {
    const docId = savedPreview?.documentId;
    if (!docId || !bffBaseUrl) return null;
    try {
      const response = await authenticatedFetch(
        `${bffBaseUrl}/api/documents/${encodeURIComponent(docId)}/preview-url`,
        { method: "GET" }
      );
      if (!response.ok) return null;
      const data = (await response.json()) as { previewUrl?: string };
      return data.previewUrl ?? null;
    } catch {
      return null; // non-fatal — the modal shows its own "preview not available" fallback
    }
  }, [savedPreview?.documentId, bffBaseUrl, authenticatedFetch]);

  // FR-13 Step 1: publish `dispatchComposeAction` into the cross-pane Compose
  // action bridge so the inline AI toolbar (workspace pane, ComposeAiToolbar's
  // `enqueueComposeAction`) routes THROUGH this Assistant-pane serial queue
  // (FR-18) via a DIRECT dispatchConsumer call — NOT a PaneEventBus event
  // (Spike 0 / design §7.2). No-op when rendered outside a bridge provider
  // (e.g. isolated tests / standalone LegalWorkspace mount).
  useRegisterComposeActionDispatcher(dispatchComposeAction);

  // task 113 (UAT defect 4): host-side registration of a Compose-direct (Browse) mount with the
  // active chat session. ComposeWorkspace hands us the mounted file's bytes by a DIRECT call (not
  // the PaneEventBus — ADR-015 keeps the bus content-free); we (1) land them as a ChatSessionFile
  // via the EXISTING chat upload endpoint so chat "summarize this document" sees them (no parallel
  // byte pipeline — CLAUDE.md §11), then (2) mark it the session's active document (POST
  // /api/compose/active-document) so a later "edit in Compose" mounts THIS file, not a stale one.
  // `@spaarke/auth` fetch (ADR-028). Fully soft-fail: on failure only chat-visibility is lost; the
  // Compose Save path is unaffected. No-op outside the bridge provider (standalone LegalWorkspace).
  // Wave 3 Part 3 dedup cache: `${fileName}:${byteLength}` → sessionFileId. The Part-2 registration
  // uploads the bytes ONCE per (session, file); a subsequent re-registration for the SAME file — which
  // Part 3 fires on every tab_change so ActiveDocument re-points to the viewed tab (most-recent-active-
  // wins) — reuses the cached sessionFileId and re-POSTs the active-document POINTER only (no duplicate
  // ChatSessionFile, no redundant upload). Cleared per-session by the session-created reset below.
  const activeDocUploadCacheRef = React.useRef<Map<string, string>>(new Map());

  // spaarkeai-compose-r2 (concurrent same-file dedup): `activeDocUploadCacheRef` is written only AFTER
  // the `/documents` upload resolves, so two registrations for the SAME file firing in the SAME tick
  // (this handler runs on load AND tab_change AND visibility toggles) both miss the cache → two POSTs
  // → two distinct sessionFileIds → the once-per-file ceremony gate (keyed by sessionFileId) passes for
  // each → DUPLICATE "I have your file" + duplicate classify. This in-flight map (keyed by the same
  // `cacheKey`) collapses truly-concurrent same-file registrations into ONE upload promise — both
  // callers await the SAME promise → resolve to the SAME sessionFileId → the ceremony Set dedups.
  // Mirrors the `ensureSessionInFlightRef` single-create pattern. Cleared per-session below.
  const activeDocUploadInFlightRef = React.useRef<Map<string, Promise<string | undefined>>>(new Map());

  // spaarkeai-compose-r2 (manual Browse ingest ceremony): a file mounted into Compose via Browse and
  // context-uploaded here must reach the SAME Assistant "ingest ceremony" as an Assistant-uploaded
  // file — (1) an "I have your file: X" loaded message and (2) a "Classified 'X' as <type> (N%)"
  // message from the Event path. `registerComposeActiveDocument` runs on load AND on every tab_change
  // AND on visibility toggles, so the ceremony MUST fire ONCE per newly-loaded file. This set is the
  // once-per-file gate keyed by the promoted `sessionFileId` (the id the `/documents` upload minted);
  // a re-register / tab switch / withdraw for the same file finds the id already present and skips
  // both messages + the classify fire. Cleared per-session by the session-created reset below.
  const composeIngestCeremonyFiredRef = React.useRef<Set<string>>(new Set());

  // spaarkeai-compose-r2 (cold Workspaces-menu Compose bug): lazily obtain the pane's chat session.
  // A Compose tab opened COLD from the Workspaces menu (no prior Assistant interaction) has NO chat
  // session, so a Browse-mounted file's `registerComposeActiveDocument` used to early-return and the
  // bytes never became a ChatSessionFile — the Assistant could not see the file. This helper mints one
  // ON DEMAND using the SAME mechanism `useCommandRouting.createNewSession` (task 097) + SprkChat's
  // own first-message create use: POST /api/ai/chat/sessions (empty body) → `setChatSessionId`. That
  // pushes the id into AiSessionProvider so the created session becomes the pane's ACTIVE session (the
  // Assistant, SprkChat, every sibling read the SAME id) — NOT a throwaway. The in-flight promise ref
  // dedups two near-simultaneous registrations into a SINGLE create. `chatSessionIdRef` is updated
  // synchronously (ahead of the setChatSessionId-driven re-render) so the caller's subsequent
  // upload + active-document POST — and any same-tick `getSessionId()` reader — see the new id at once.
  const ensureSessionInFlightRef = React.useRef<Promise<string | null> | null>(null);
  const ensureChatSession = React.useCallback(async (): Promise<string | null> => {
    const existing = getSessionId();
    if (existing) return existing;
    if (ensureSessionInFlightRef.current) return ensureSessionInFlightRef.current;
    if (!bffBaseUrl) return null;
    const create = (async (): Promise<string | null> => {
      try {
        const response = await authenticatedFetch(`${bffBaseUrl}/api/ai/chat/sessions`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({}),
        });
        if (!response.ok) return null;
        const json = (await response.json()) as { sessionId?: string };
        const newId =
          typeof json?.sessionId === "string" && json.sessionId.length > 0 ? json.sessionId : null;
        if (newId !== null) {
          // Make it the pane's ACTIVE session: update the render-free session ref SYNCHRONOUSLY (so a
          // same-tick getSessionId() sees it before the setChatSessionId re-render) and push it into
          // AiSessionProvider so the remounting SprkChat + the Assistant continue with THIS session.
          chatSessionIdRef.current = newId;
          setChatSessionId(newId);
        }
        return newId;
      } catch {
        return null;
      } finally {
        ensureSessionInFlightRef.current = null;
      }
    })();
    ensureSessionInFlightRef.current = create;
    return create;
  }, [getSessionId, bffBaseUrl, authenticatedFetch, setChatSessionId]);

  const registerComposeActiveDocument = React.useCallback(
    async ({
      docxBytes,
      fileName,
      documentSessionId,
      visible,
    }: {
      docxBytes: ArrayBuffer;
      fileName?: string;
      documentSessionId?: string;
      // R3 ("Visible to assistant"): omitted / true = register visible (every auto-register path);
      // false = the toggle turned OFF → withdraw (POST active-document with visible:false so the
      // sibling server agent clears ChatSession.ActiveDocument).
      visible?: boolean;
    }): Promise<void> => {
      if (!bffBaseUrl) return;
      // spaarkeai-compose-r2 (cold Workspaces-menu Compose bug): a Compose tab opened COLD from the
      // Workspaces menu has no chat session yet. Do NOT early-return (that left the Browse-mounted
      // file invisible to the Assistant) — LAZILY create/obtain the pane's session BEFORE the upload +
      // active-document POST so the bytes land as a ChatSessionFile the Assistant can see. The created
      // session becomes the pane's ACTIVE session (ensureChatSession → setChatSessionId). A WITHDRAW
      // (visible === false) has nothing to withdraw with no session → skip the create and return.
      let sessionId = getSessionId();
      if (!sessionId) {
        if (visible === false) return;
        sessionId = await ensureChatSession();
        if (!sessionId) return;
      }
      try {
        const name = fileName ?? "compose-document.docx";
        // Wave 3 Part 3: dedup the upload so tab_change re-registrations don't re-upload / duplicate.
        const cacheKey = `${name}:${docxBytes.byteLength}`;
        // #2 double-classify fix (UAT 2026-07-18): consult the CROSS-PATH registry FIRST. When this
        // file was already promoted by the chat auto-promote path (FIX #7 auto-load of a chat upload),
        // reuse that sessionFileId — skipping the re-upload, the server re-classify, AND the ingest
        // ceremony (all already done by the chat path). Falls through to the byte-cache / fresh upload
        // for a genuine Browse-mounted file the Assistant never saw. Seed the byte-cache too so later
        // same-tab re-registrations hit it directly.
        let sessionFileId =
          activeDocUploadCacheRef.current.get(cacheKey) ??
          promotedFileIdsByNameRef.current.get(name);
        if (sessionFileId) {
          activeDocUploadCacheRef.current.set(cacheKey, sessionFileId);
        }
        if (!sessionFileId) {
          // In-flight guard: collapse truly-concurrent same-file registrations into ONE upload so
          // only a single POST /documents fires (see `activeDocUploadInFlightRef` rationale above).
          const capturedSessionId = sessionId;
          let uploadPromise = activeDocUploadInFlightRef.current.get(cacheKey);
          if (!uploadPromise) {
            uploadPromise = (async (): Promise<string | undefined> => {
              try {
                const form = new FormData();
                form.append(
                  "file",
                  new Blob([docxBytes], {
                    type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                  }),
                  name
                );
                form.append("filename", name);
                const uploadResp = await authenticatedFetch(
                  `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(capturedSessionId)}/documents`,
                  { method: "POST", body: form }
                );
                if (!uploadResp.ok) return undefined;
                const uploaded = (await uploadResp.json()) as { documentId?: string };
                const id = uploaded?.documentId;
                if (id) activeDocUploadCacheRef.current.set(cacheKey, id);
                return id;
              } finally {
                // Drop the in-flight entry once settled; on failure a later re-register retries fresh.
                activeDocUploadInFlightRef.current.delete(cacheKey);
              }
            })();
            activeDocUploadInFlightRef.current.set(cacheKey, uploadPromise);
          }
          sessionFileId = await uploadPromise;
          if (!sessionFileId) return;

          // spaarkeai-compose-r2 (manual Browse ingest ceremony): this is the FIRST successful upload
          // of this file into the session (the cache-miss branch runs once per (session, file)). Bring
          // the Browse-opened file to the same Assistant ceremony as an Assistant-uploaded file:
          //   (1) the "I have your file: X" loaded message (same helper useAttachments emits on a chip
          //       reaching 'ready'), and
          //   (2) the "Classified 'X' as <type> (N% confidence)" message via the Event path — the file
          //       already lives in session.UploadedFiles (this /documents call added it), so the
          //       server's classify rule resolves it and streams event_classification.
          // A WITHDRAW (visible === false) is NOT an ingest — never run the ceremony for it. The
          // ceremony set is a once-per-file guard so a re-register that somehow reaches a fresh upload
          // still can't double-emit.
          if (visible !== false && !composeIngestCeremonyFiredRef.current.has(sessionFileId)) {
            composeIngestCeremonyFiredRef.current.add(sessionFileId);
            // UAT 2026-07-24: a file opened/uploaded in the Compose widget IS now a ChatSessionFile
            // (this upload added it) — the Assistant can act on it — but the user only saw the two
            // collapsed "File attached"/"File classified" entries and thought it was NOT attached in
            // the Assistant. Lead with a PROSE affordance telling the user the file is now available
            // here — the mirror of the Assistant→Compose "opened in Compose" message. Emitted FIRST so
            // it sits above the two collapsed status entries (matches the reverse-direction ceremony).
            injection.enqueue(makeLocalAssistantMessage(buildComposeAttachedToAssistantMessage(name)));
            const confirmation = buildFileConfirmationMessage([name]);
            if (confirmation !== null) {
              // P1-5: compact, collapsed-by-default file entry (was a full chat bubble).
              injection.enqueue(makeFileStatusMessage(confirmation, "File attached"));
            }
            eventBatch.fireForPromotedFile(sessionFileId);
          }
        }
        // Wave 3 Part 1: remember this as the session's ACTIVE source document so "Open in Compose"
        // opens THAT document (compose.upload seed) rather than seeding the assistant message prose.
        // Set BEFORE the active-document POST so a POST failure (chat-visibility loss) does NOT leave
        // this pointing at a STALE prior file — the bytes are already uploaded (sessionFileId is real).
        activeSourceDocRef.current = { sessionFileId, documentSessionId, fileName: name };
        // task 031 (DEF-09 routing): back-fill the document-session waiter keyed by THIS file's
        // session-file id — resolves any in-flight `awaitDocumentSessionIdFor(sessionFileId)` call
        // (the agreement-review dispatch's routing wait) and remembers the value for a repeat review
        // of an already-registered document. No-op when `documentSessionId` is undefined (a
        // pointer-only registration with no session yet).
        documentSessionWaiterRef.current.notify(sessionFileId, documentSessionId);
        // task 033 (FR-17): bump the GENERIC source-doc readiness signal now that
        // `activeSourceDocRef.sessionFileId` is real via THIS (Compose-register) path too — the
        // race-safe buffered effects (notably the explicit-door auto-run, armed by the wizard
        // hand-off BEFORE this register lands) re-run on it. Mirrors `handleSessionFileUploaded`'s
        // bump exactly (the token is documented as "a generic 'a source doc just became ready'
        // signal, not revise-specific"); pre-033 the chat-upload path was simply the only bump
        // site because no machine-armed intent could precede a Compose-side registration. Effects
        // whose own pending state is unset no-op on the re-run (each gates on its buffer).
        setSourceDocReadyToken((t) => t + 1);
        // Wave 3 Part 2 (DEF-11 TEXT-path close): thread the tab's `documentSessionId` so the server
        // sets ChatSession.ActiveDocument.DocumentSessionId → BindingCapabilityTool routes a typed
        // revise/draft into THIS document session (redline in the open doc), not the chat session.
        await authenticatedFetch(`${bffBaseUrl}/api/compose/active-document`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            sessionId,
            sessionFileId,
            source: "compose-direct",
            fileName: name,
            documentSessionId,
            // R3: default ON (every auto-register path omits it) — the sibling server agent maps a
            // false here to withdrawing this document from the session's active-document / chat context.
            visible: visible ?? true,
          }),
        });
        // Wave 4 (end-to-end revise): publish the document session id REACTIVELY so the named-intent
        // revise effect can fire the dispatch the moment the mount establishes the session (the chip
        // path reads the same value at click time). Only a registration carrying a real document
        // session id counts — a pointer-only pre-DEF-11 registration leaves it untouched.
        if (documentSessionId) {
          setActiveComposeDocSessionId(documentSessionId);
        }
      } catch {
        // Non-fatal: the direct upload just won't be chat-visible; the Compose Save path is unaffected.
      }
    },
    [getSessionId, bffBaseUrl, authenticatedFetch, ensureChatSession, injection, eventBatch]
  );
  useRegisterComposeActiveDocumentHandler(registerComposeActiveDocument);
  // ADR-015: structural signal only (queue depth + in-flight correlation id —
  // never the action's bindingId/args/content). Also keeps `dispatchComposeAction`
  // + queue state live/observable ahead of the task-030 toolbar hand-off.
  React.useEffect(() => {
    if (actionQueue.inFlightId !== null || actionQueue.pendingCount > 0) {
      console.log(
        "[ConversationPane] serial action queue — inFlight:%s pending:%d",
        actionQueue.inFlightId,
        actionQueue.pendingCount
      );
    }
  }, [actionQueue.inFlightId, actionQueue.pendingCount, dispatchComposeAction]);

  // FR-35 Doc Q&A ephemeral highlight (task 072, stretch) — bridges SprkChat's
  // existing citation mechanism to the Compose workspace/context choreography.
  // See useDocQaCitationBridge.ts for the full ADR-039/015 rationale.
  const docQaCitation = useDocQaCitationBridge({ dispatch, getSessionId });

  const contextBridge = useContextEventBridge({
    dispatch,
    // R2-D (2026-07-07): `workspace_open_tab` SSE frames bridge to the workspace
    // channel — same PaneEventBus dispatcher, explicit leg.
    dispatchWorkspace: dispatch,
    acceptChips: chips.acceptChips,
  });
  const playbook = usePlaybookSelection({ setPlaybookId, toLoading, reset, dispatch });
  const playbookOptions = usePlaybookOptions({
    bffBaseUrl,
    authenticatedFetch,
    chatSessionId,
    inject: injection.inject,
    getLastSentMessage: eventBatch.getLastSentMessage,
  });
  // task 043 / FR-G1: the SNS cards' "More" affordance opens the SAME
  // existing library modal the `/playbooks` hard slash + playbook_options
  // Library link already use — no parallel modal surface. Empty attachment
  // ids: the SNS "More" entry is a generic library browse, not tied to a
  // specific candidate-confidence flow (mirrors useCommandRouting's
  // `openLibraryModal([])` call for the same reason).
  // P1-8: the SNS/post-upload "More…" card opens Quick Start (was the retired playbook library).
  openLibraryModalRef.current = () => {
    setQuickStartTab("create");
    setQuickStartOpen(true);
  };
  const commands = useCommandRouting({
    bffBaseUrl,
    authenticatedFetch,
    chatSessionId,
    setChatSessionId,
    entityContext,
    inject: injection.inject,
    openLibraryModal: playbookOptions.handleOpenLibraryModal,
  });
  const selection = useSelectionChip({ noteTabFocus: commands.noteTabFocus });

  // Wave 4 (end-to-end revise) — the outbound-body decoration hook. A natural-language "revise this
  // document" about a CHAT-UPLOADED source document must NOT reach the agent turn (that dispatches
  // `compose-revise-document` to the CHAT session, where — no Compose tab open — the tool result is
  // narrated as prose instead of redlined; the root cause this wave fixes). Instead we CANCEL the
  // send (return null — the same hard-slash suppression lever) and run the client-orchestrated flow:
  //   1. AUTO-MOUNT the source document into Compose (establishes the document session + back-fills
  //      ActiveDocument.DocumentSessionId per Wave 3).
  //   2a. If the user NAMED an intent ("flag risks in this document") → apply it directly, gated on
  //       the document session being registered (the effect below).
  //   2b. Otherwise → show the mount-then-ask message + the four intent chips.
  // Gated on an active source document being present; every other message delegates verbatim to the
  // command-routing decorate (hard-slash / soft-slash / reference resolution — ADR-039 unchanged).
  // ai-advanced-capabilities-nda-r1 task 011 (spec FR-04b): the Assistant's runtime model-tier
  // picker selection. `null` (the default) means "unset" — decorateOutboundBody below omits the
  // wire field entirely, so the dispatched Action's own sprk_modeltier governs exactly as before
  // this control existed. A non-null selection is added to the outbound message body and rides
  // sprk_modeltieroverride through the ONE tier→deployment resolver (ADR-016, task 010). Declared
  // here (ABOVE handleDecorateOutboundBodyWithRevise) rather than beside the other simple UI state
  // further down, because that callback's dependency array closes over it immediately below.
  const [modelTierOverride, setModelTierOverride] = React.useState<AssistantModelTier | null>(null);

  const { handleDecorateOutboundBody } = commands;
  const handleDecorateOutboundBodyWithRevise = React.useCallback(
    async (body: Record<string, unknown>): Promise<Record<string, unknown> | null> => {
      // ai-advanced-capabilities-nda-r1 task 011: only add the wire field when the user has picked a
      // tier — omitted entirely on the (default) unset path, so the outbound body shape is byte-
      // identical to pre-task-011 for every session that never touches the picker.
      if (modelTierOverride !== null) {
        body.modelTierOverride = modelTierOverride;
      }

      const messageText = typeof body.message === "string" ? body.message : "";
      const hasActiveSourceDoc =
        activeSourceDocRef.current?.sessionFileId != null && chatSessionIdRef.current != null;

      // task 021 (FR-07/08/09 interactive orientation + confirmation gate) — CHECKED FIRST, before
      // the generic revise detector below: "review this document" (a review/assessment verb, no
      // revise-specific verb/keyword) routes here instead of the whole-document revise flow
      // (compose-revise-document) — design Lens 3d's canonical trigger phrase. task 023 (FR-09 the
      // ONE routing decision point for both doors, per project instruction "not two"): when the
      // session was launched ALREADY oriented (`explicitComposeLaunch?.subDomain` — the wizard /
      // deep-link / open-existing envelope, task 022 delivers it on all three doors), the classifier
      // NEVER gates the run — `runExplicit` binds the pack DETERMINISTICALLY (no confirm chips, no
      // re-route); the classifier still runs, but only as a NON-BLOCKING, warn-only sanity check
      // (ADR-041 — see agreementReviewGate.runExplicit / resolveAgreementReviewSanityMismatch). With
      // no explicit subDomain, the classifier confirmation gate (`runGate`) runs as before. Negative
      // criterion (project constraint, BOTH branches): a bare "review" with no document reference,
      // or with NO active/uploading document, falls through unchanged to the normal agent turn —
      // neither door ever fires on text alone.
      if (detectAgreementReviewIntent(messageText)) {
        const explicitSubDomain = explicitComposeLaunch?.subDomain;
        if (hasActiveSourceDoc || attachments.uploadedFileCount > 0) {
          // ALWAYS buffer through an effect (never dispatch inline here) — mirrors the
          // revise/draft-document race-safe pattern: the review bindingId is resolved by a DEFERRED
          // capability-discovery fetch enabled by `agreementReviewGateNeeded` just below, so it is
          // virtually never ready on THIS same synchronous call (the very first trigger in a
          // session). The buffered effect(s) below fire once BOTH the source file is registered AND
          // the needed bindingId has resolved.
          setAgreementReviewGateNeeded(true);
          if (explicitSubDomain) {
            setPendingExplicitAgreementReview({ subDomainKey: explicitSubDomain });
          } else {
            setPendingAgreementReview(true);
          }
          return null; // suppress the agent turn — the explicit bind or the classify->confirm->dispatch gate orchestrates
        }
        // No attached/target document — the negative criterion: fall through to the normal agent
        // turn below (never fire either door on text alone).
      }

      const detection = detectReviseThisDocumentIntent(messageText);

      // R6-6 (UAT 2026-07-21): the document is ALREADY open in Compose (a live document session).
      // A revise/rewrite instruction should update the OPEN document, not answer in the chat pane.
      if (activeComposeDocSessionId != null) {
        // A specific-SECTION rewrite with nothing highlighted → the right tool is the in-document
        // "Draft alternative" on the selected text, not a whole-document redline. Point the user there.
        if (detectSectionRewriteIntent(messageText) && selection.selectionChip === null) {
          injection.enqueue(
            makeLocalAssistantMessage(
              'To rewrite a specific section, highlight it in the document and choose "Draft alternative" from the selection toolbar. To rewrite the whole document, just say "rewrite the document".'
            )
          );
          return null;
        }
        // A whole-document revise/rewrite → redline the open document (reuses the shipped edit path).
        if (detection.isReviseThisDocument) {
          dispatchReviseDocument(
            detection.namedIntent ?? "custom",
            detection.namedIntent ? undefined : messageText,
            activeComposeDocSessionId
          );
          return null;
        }
      }

      if (detection.isReviseThisDocument) {
        if (hasActiveSourceDoc) {
          const mounted = mountActiveSourceDocInCompose();
          if (mounted) {
            // Kick off the deferred capability read now so the compose-revise-document bindingId is
            // resolved by the time the user clicks a chip / the named-intent effect fires.
            setReviseCapabilityNeeded(true);
            if (detection.namedIntent) {
              // Apply the named intent once the mount registers the document session (effect below).
              setPendingNamedRevise({ revisionIntent: detection.namedIntent });
            } else {
              injection.enqueue(makeLocalAssistantMessage(REVISE_MOUNT_ASK_MESSAGE));
              setReviseChipsPending(true);
            }
            return null; // suppress the agent turn — the client orchestrates mount → ask/apply
          }
        } else if (attachments.uploadedFileCount > 0) {
          // R2 (race-safe revise): an upload is IN FLIGHT but its registration hasn't back-filled
          // `activeSourceDocRef.sessionFileId` yet (the user sent "revise this document" immediately
          // after attaching). BUFFER the intent and SUPPRESS the agent turn — falling through here
          // would dispatch compose-revise-document to the CHAT session with no Compose tab open, and
          // the tool result would be narrated as prose (no mount, no redline). The buffered-revise
          // effect below fires the mount + revise the moment `onSessionFileUploaded` back-fills.
          setPendingReviseThisDocument({ namedIntent: detection.namedIntent ?? null });
          setReviseCapabilityNeeded(true);
          return null;
        }
      }

      // R7-4 (UAT 2026-07-21): a substantial-output ask ("write a brief on X", "draft a memo",
      // "analyze this agreement") should produce an editor-ready DOCUMENT in a Compose tab, NOT a
      // long answer dumped into the chat. Route it to the shipped `compose-draft-document` capability
      // (disposition = compose): the client dispatches it, `runBindingDispatch` opens a Compose tab
      // from the result's `body_html` + posts a short confirmation + follow-on cards, and we SUPPRESS
      // the raw agent turn. Deterministic (no reliance on the model picking the tool). The bindingId
      // comes only from capability discovery (ADR-039); buffer until it resolves so the fetch latency
      // never drops the request. Placed AFTER the revise branches so an open-document revise wins.
      if (detectDraftDocumentIntent(messageText).isDraftDocument) {
        setDraftCapabilityNeeded(true);
        if (draftBindingId) {
          chips.dispatchBinding(draftBindingId, { slots: { request: messageText } });
        } else {
          // Discovery not settled yet — acknowledge (never a silent dead-end) and buffer; the effect
          // below dispatches the moment `draftBindingId` back-fills.
          injection.enqueue(
            makeLocalAssistantMessage("Preparing your document — I'll open it in the Compose tab.")
          );
          setPendingDraftDocument({ request: messageText });
        }
        return null;
      }

      return handleDecorateOutboundBody(body);
    },
    [
      handleDecorateOutboundBody,
      mountActiveSourceDocInCompose,
      injection,
      attachments.uploadedFileCount,
      activeComposeDocSessionId,
      dispatchReviseDocument,
      selection.selectionChip,
      draftBindingId,
      chips,
      modelTierOverride,
      explicitComposeLaunch,
    ]
  );

  // R7-4 — fire a BUFFERED "draft a document" request the moment capability discovery resolves the
  // `compose-draft-document` bindingId. Mirrors the named-intent revise effect: waits for BOTH a
  // pending request AND the resolved bindingId, then dispatches once and clears the buffer.
  React.useEffect(() => {
    if (!pendingDraftDocument || !draftBindingId) return;
    const { request } = pendingDraftDocument;
    setPendingDraftDocument(null);
    chips.dispatchBinding(draftBindingId, { slots: { request } });
  }, [pendingDraftDocument, draftBindingId, chips]);

  // Wave 4 — fire a NAMED-intent revise the moment the auto-mount registers the document session.
  // `activeComposeDocSessionId` back-fills reactively from `registerComposeActiveDocument` (post-
  // mount), so this never captures a stale null: it waits for a real document session id before
  // dispatching, then clears the pending state (once per named request).
  React.useEffect(() => {
    // Wait for ALL THREE preconditions: a pending named request, the document session (mount
    // registered), AND the resolved bindingId (capability fetch settled). Not clearing the pending
    // state until the bindingId is ready avoids a race where the mount registers before the deferred
    // catalog read returns — the effect simply re-runs when `reviseBindingId` resolves.
    if (!pendingNamedRevise || !activeComposeDocSessionId || !reviseBindingId) return;
    const { revisionIntent, instruction } = pendingNamedRevise;
    setPendingNamedRevise(null);
    dispatchReviseDocument(revisionIntent, instruction, activeComposeDocSessionId);
  }, [pendingNamedRevise, activeComposeDocSessionId, reviseBindingId, dispatchReviseDocument]);

  // R2 (race-safe revise) — fire a BUFFERED "revise this document" the moment the uploaded source
  // doc's registration back-fills `activeSourceDocRef.sessionFileId` (signalled by
  // `sourceDocReadyToken` bumping in `handleSessionFileUploaded`). This mirrors the named-intent
  // effect above but gates on the UPLOAD readiness instead of the post-mount document session: it
  // auto-mounts the now-registered source doc into Compose, then either applies the named intent
  // (via `pendingNamedRevise` → the effect above, once the mount registers the doc session) or shows
  // the mount-then-ask message + chips. Net: "revise" always ends in a mounted doc + doc-session
  // routing, regardless of whether the message beat the upload.
  React.useEffect(() => {
    if (!pendingReviseThisDocument) return;
    if (activeSourceDocRef.current?.sessionFileId == null || chatSessionIdRef.current == null) return;
    const mounted = mountActiveSourceDocInCompose();
    if (!mounted) return;
    const { namedIntent } = pendingReviseThisDocument;
    setPendingReviseThisDocument(null);
    setReviseCapabilityNeeded(true);
    if (namedIntent) {
      setPendingNamedRevise({ revisionIntent: namedIntent });
    } else {
      injection.enqueue(makeLocalAssistantMessage(REVISE_MOUNT_ASK_MESSAGE));
      setReviseChipsPending(true);
    }
  }, [sourceDocReadyToken, pendingReviseThisDocument, mountActiveSourceDocInCompose, injection]);

  // task 021 — the race-safe buffer for a natural-language "review this document" (agreement
  // review-intent). Fires the classify+gate only once BOTH preconditions are met: (1) the source
  // file is registered (`sourceDocReadyToken` — the same generic upload-readiness signal the
  // revise buffer above consumes; already true immediately when `hasActiveSourceDoc` was true at
  // detection time) AND (2) the deferred capability-discovery fetch has resolved
  // `classifyBindingId` (mirrors the `pendingDraftDocument`/`draftBindingId` wait above) — every
  // decorate-hook trigger buffers through here UNCONDITIONALLY (never dispatches inline) precisely
  // because `classifyBindingId` is virtually never resolved on the same synchronous call that
  // first flips `agreementReviewGateNeeded`.
  const runAgreementReviewGate = agreementReviewGate.runGate;
  React.useEffect(() => {
    if (!pendingAgreementReview) return;
    const active = activeSourceDocRef.current;
    if (active?.sessionFileId == null || chatSessionIdRef.current == null) return;
    if (!classifyBindingId) return;
    setPendingAgreementReview(false);
    void runAgreementReviewGate(active.sessionFileId, active.fileName);
  }, [sourceDocReadyToken, pendingAgreementReview, runAgreementReviewGate, classifyBindingId]);

  // task 023 (FR-09 explicit door) — the race-safe buffer for a "review this document" arriving on
  // an ALREADY-oriented session. Gates on `ndaReviewBindingId` (the review dispatch target) rather
  // than `classifyBindingId` — the explicit door dispatches deterministically and does not need the
  // classifier ready to proceed (the sanity check inside `runExplicit` degrades gracefully via
  // `runClassify`'s own null-safe contract if it resolves later or never). Both bindingIds come from
  // the SAME shared capability-discovery fetch (`agreementReviewGateNeeded` enables it above), so in
  // practice they resolve together.
  const runExplicitAgreementReview = agreementReviewGate.runExplicit;
  React.useEffect(() => {
    if (!pendingExplicitAgreementReview) return;
    const active = activeSourceDocRef.current;
    if (active?.sessionFileId == null || chatSessionIdRef.current == null) return;
    if (!ndaReviewBindingId) return;
    const { subDomainKey, reviewDepth } = pendingExplicitAgreementReview;
    setPendingExplicitAgreementReview(null);
    void runExplicitAgreementReview(active.sessionFileId, active.fileName, subDomainKey, reviewDepth);
  }, [sourceDocReadyToken, pendingExplicitAgreementReview, runExplicitAgreementReview, ndaReviewBindingId]);

  // task 033 (FR-17) — the wizard auto-run BRIDGE-FAILURE watchdog (ADR-019 distinct surfacing).
  // Armed by the wizard hand-off listener alongside the explicit-door buffer. Three outcomes:
  //  - SUCCESS: the buffered effect above consumed `pendingExplicitAgreementReview` (dispatch
  //    fired) → this effect re-runs, sees the buffer empty, and stands down silently.
  //  - BRIDGE FAILURE: the DEF-10 register never landed a sessionFileId (upload failed / Compose
  //    never mounted) → the buffer is still full when the timer fires → clear it + surface the
  //    DISTINCT bridge-failure message. The Analysis stays consistent (created + bound before the
  //    hand-off); recovery is the user's normal conversational "review this document".
  //  - SESSION RESET: `handleSessionCreated` clears the buffer → same silent stand-down as success.
  // The timer never resets on unrelated re-renders (deps are the two state objects, identity-stable
  // until actually changed), and a text-armed buffer (no watchdog) keeps its shipped
  // indefinite-buffer semantics untouched.
  React.useEffect(() => {
    if (!wizardAutoRunWatchdog) return undefined;
    if (!pendingExplicitAgreementReview) {
      setWizardAutoRunWatchdog(null);
      return undefined;
    }
    const timer = setTimeout(() => {
      setPendingExplicitAgreementReview(null);
      setWizardAutoRunWatchdog(null);
      injection.enqueue(makeLocalAssistantMessage(WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE));
    }, WIZARD_AUTO_RUN_WATCHDOG_MS);
    return () => clearTimeout(timer);
  }, [wizardAutoRunWatchdog, pendingExplicitAgreementReview, injection]);

  // R4-4 (UAT 2026-07-19) — the FIX #7 AUTO-LOAD-on-attach effect was REMOVED. Owner reversed the
  // earlier decision: attaching files should NOT auto-open Compose (uploading 2 files spawned 2 Compose
  // tabs, which was jarring). Opening a file in Compose is now an ON-DEMAND action — the "Revise in
  // Compose" affordance in the files tray (handleReviseInCompose) — plus the existing intentional
  // natural-language "revise this document" flow. `sourceDocReadyToken` still drives the race-safe
  // buffered-revise effect above; it no longer triggers an auto-mount.

  // ── SprkChat session callbacks ────────────────────────────────────────────
  // R7 12.3a: clear the persisted id BEFORE SprkChat creates a fresh session.
  const handleSessionStale = React.useCallback(
    (_staleSessionId: string): void => {
      console.warn(
        "[ConversationPane] chat session stale — clearing persisted id, awaiting fresh session"
      );
      clearChatSession();
    },
    [clearChatSession]
  );

  // Session-created reset — deps key on STABLE reset methods (Step 9.5).
  const { clearRefinementPrompts } = selection;
  const { resetForSession: resetAttachments } = attachments;
  const { resetForSession: resetChips } = chips;
  const { resetForSession: resetEventBatch } = eventBatch;
  // UAT round-4 (item #9): the "Rerun a full analysis" card's own reset.
  const { resetForSession: resetRerunFullAnalysisCard } = rerunFullAnalysisCard;
  const handleSessionCreated = React.useCallback(
    (session: IChatSession) => {
      if (!session?.sessionId) return;
      setChatSessionId(session.sessionId);
      clearRefinementPrompts();
      resetAttachments();
      resetChips();
      resetEventBatch();
      // Wave 3: a fresh session has no active source document and no cached uploads.
      activeSourceDocRef.current = null;
      activeDocUploadCacheRef.current.clear();
      activeDocUploadInFlightRef.current.clear();
      // #2 double-classify fix: the cross-path promoted-id registry is session-scoped.
      promotedFileIdsByNameRef.current.clear();
      // spaarkeai-compose-r2: the ingest-ceremony guard is session-scoped (keyed by the session's
      // file ids) — a fresh session must re-run the ceremony for its own re-uploaded files.
      composeIngestCeremonyFiredRef.current.clear();
      // Wave 4: a fresh session clears any pending revise flow + the document-session back-fill.
      setReviseChipsPending(false);
      setPendingNamedRevise(null);
      setActiveComposeDocSessionId(null);
      // R2: a fresh session drops any buffered race-safe revise intent.
      setPendingReviseThisDocument(null);
      // R5-D (2026-07-07): the execution-trace replay buffer is session-scoped —
      // a fresh session must not replay the previous session's tool calls.
      clearExecutionTraceBuffer();
      // task 022: a fresh session has no pending "Review an NDA" card.
      setNdaReviewFile(null);
      // task 064: reset the resolved consumerType to the default alongside the file — keeps the
      // two pieces of state consistent even though only `ndaReviewFile` gates behavior.
      setNdaReviewConsumerType("nda-review");
      // task 021: a fresh session drops any pending confirmation-gate decision + per-file resolved
      // cache (a new session has no already-oriented files) and any buffered race-safe gate intent.
      agreementReviewGate.resetForSession();
      setPendingAgreementReview(false);
      // task 023: a fresh session also drops any buffered EXPLICIT-door review intent.
      setPendingExplicitAgreementReview(null);
      // task 031: a fresh session drops any known/pending document-session routing state — a prior
      // session's file ids/session ids must never leak into a new session's resolution.
      documentSessionWaiterRef.current.reset();
      // UAT round-4 (item #9): a fresh session has no pending "Rerun a full analysis" card — it is
      // session-turn-scoped plain state, never persisted, so a prior session's card must not survive.
      resetRerunFullAnalysisCard();
    },
    [
      setChatSessionId,
      clearRefinementPrompts,
      resetAttachments,
      resetChips,
      resetEventBatch,
      agreementReviewGate,
      resetRerunFullAnalysisCard,
    ]
  );

  const handleHeaderCollapse = React.useCallback(() => {
    paneCollapse?.toggle("assistant");
  }, [paneCollapse]);

  // ── "New session" header affordance (G-P3 UAT round-4 R4-5, 2026-07-07) ────
  // Sessions resume across hard refreshes by design (persisted chatSessionId);
  // this is the user's control to start over: clear the persisted id (localStorage
  // + sessionStorage via AiSessionProvider), then remount SprkChat — it mounts
  // with sessionId=undefined, mints a fresh session, and onSessionCreated resets
  // attachments/chips/refinement state (the existing handleSessionCreated leg).
  // Deliberately NOT history browsing/deletion — that is the named r2 memory
  // scope; the existing History menu remains the only history surface.
  const { startNewSession } = commands;
  const handleNewSession = React.useCallback(() => {
    clearChatSession();
    startNewSession();
  }, [clearChatSession, startNewSession]);

  // R5-5 (UAT 2026-07-20): selecting a History entry must LOAD that session's transcript.
  // Previously `onSelectSession={setChatSessionId}` only updated the id in state — nothing
  // re-read it, so nothing happened. Adopt the selected id AND remount SprkChat (bump the
  // remount key) so its mount-effect resumes THIS session (resumeSession → loadHistory fetches
  // the transcript). Same remount seam as handleNewSession, minus the clear.
  const handleSelectHistorySession = React.useCallback(
    (sessionId: string) => {
      setChatSessionId(sessionId);
      startNewSession();
    },
    [setChatSessionId, startNewSession]
  );

  // ai-advanced-capabilities-analysis-hub-r1 task 031 (FR-11): a DIFFERENT pane (the
  // AnalysisHubWidget grid's row-open handler) asks this pane to adopt an EXISTING session —
  // e.g. reopening an Analysis from the hub grid. Reuses the IDENTICAL mechanism the History
  // menu already uses (`handleSelectHistorySession`): adopt the id + remount SprkChat, whose
  // own `resumeSession()`/`loadHistory()` mount-effect restores the transcript (TTL-safe —
  // `ChatSessionManager.GetSessionAsync` already falls back Redis→Cosmos→Dataverse per the
  // task-025 hardening). No new restore mechanism; additive `conversation.session_switch`
  // discriminant (ADR-030).
  usePaneEvent("conversation", (event) => {
    if (event.type === "session_switch" && event.sessionId) {
      handleSelectHistorySession(event.sessionId);
    } else if (event.type === "open_quick_start") {
      // ai-advanced-capabilities-analysis-hub-r1: the Analysis grid `+ New`
      // asks us to open the ONE Quick Start modal on a given tab. Default to
      // 'create' if unspecified.
      setQuickStartTab(event.quickStartTab ?? "create");
      setQuickStartOpen(true);
    }
  });

  // task 033 (FR-17) — the wizard→review auto-run hand-off listener. The Create-Analysis wizard's
  // finish hook dispatches the SAME `widget_load{widgetType:'compose'}` seed WorkspacePane consumes
  // to open the document, now additionally carrying `{ composeSessionId, analysisId, subDomain,
  // autoRunReview }` (ComposeWidgetSeed, agreement work-type only). This pane rides the SAME bus
  // event (no new channel/event type — ADR-030) to do its two legs:
  //  (1) ADOPT the wizard-minted ANALYSIS-OWNED session as the pane's active chat session — the
  //      IDENTICAL adopt+remount mechanism the History menu / hub-grid `session_switch` use
  //      (`handleSelectHistorySession`), plus the SAME synchronous ref update `ensureChatSession`
  //      documents (so a same-tick / pre-re-render `getSessionId()` reader — notably the DEF-10
  //      register's lazy-create guard — sees the adopted id and never mints a SECOND session).
  //      ComposeWorkspace independently resumes this SAME session as its document session
  //      (`initialSessionId` → BFF Load resume), so chat ≡ document session: the register's file
  //      upload, the review's `sessionIdOverride` dispatch, and the compose-outputs read all
  //      target ONE session (DEF-09 holds; no file-id impedance).
  //  (2) ARM the shipped explicit review door (task 023's buffer — `runExplicit` on the picked
  //      sub-domain's pack, deterministic, no classifier gate) + the bridge-failure watchdog. The
  //      buffered effect fires when the DEF-10 register lands the sessionFileId (the register's
  //      `sourceDocReadyToken` bump) AND capability discovery resolves the review bindingId.
  // A seed WITHOUT the hand-off fields (every non-wizard compose open — upload door, Browse,
  // "Open in Compose", revise mounts) returns immediately: zero behavior change.
  usePaneEvent("workspace", (event) => {
    const evt = event as WorkspacePaneEvent & {
      widgetType?: string;
      widgetData?: { compose?: ComposeWidgetSeed };
    };
    if (evt.type !== "widget_load" || evt.widgetType !== "compose") return;
    const seed = evt.widgetData?.compose;
    const composeSessionId = seed?.composeSessionId;
    const analysisId = seed?.analysisId;

    // UAT round-6 (item #15b) — RESTORE re-adoption branch. WorkspacePane's item-#13 cold-load restore
    // reopens a persisted home-surface Compose tab with `composeSessionId` threaded but NO `analysisId`
    // (the direct-Compose door is unbound — no wizard hand-off). On cold reload the Assistant pane's own
    // session (persisted via AiSessionProvider's sessionStorage) can be lost across the code-page iframe
    // teardown, so it cold-starts "back at the Review an NDA step". Re-adopt the restored tab's session
    // — the SAME `handleSelectHistorySession` mechanism the wizard branch below and the History menu use
    // — so the transcript CONTINUES on return.
    //
    // GUARD (never clobber an active conversation the user started after returning): adopt AT MOST once
    // per mount, only when this pane is on a FRESH/DEFAULT session — i.e. it hasn't already adopted the
    // restored session (`chatSessionIdRef !== composeSessionId`) AND the current transcript is empty
    // (`chatMessageCountRef.current === 0`). The restore `widget_load` is dispatched macrotask-early on
    // cold load (WorkspacePane defers it after `tabRestoreSettled`), before the user could realistically
    // send a message; the empty-transcript check is the belt-and-braces "fresh session" gate. If the
    // Assistant legitimately resumed the SAME review session already, the id-equality check makes this a
    // no-op; if it resumed a DIFFERENT session that already has messages, the empty-transcript check
    // blocks the clobber.
    if (composeSessionId && !analysisId) {
      if (restoreSessionAdoptedRef.current) return;
      if (chatSessionIdRef.current === composeSessionId) return; // already on the restored session
      if (chatMessageCountRef.current > 0) return; // an active conversation exists — never clobber
      restoreSessionAdoptedRef.current = true;
      // Synchronous ref update FIRST (ensureChatSession's documented pattern) so any same-tick
      // getSessionId() reader sees the adopted session before the re-render lands.
      chatSessionIdRef.current = composeSessionId;
      handleSelectHistorySession(composeSessionId);
      return;
    }

    if (!composeSessionId || !analysisId) return; // not a wizard hand-off seed
    if (wizardAutoRunHandledRef.current.has(analysisId)) return; // once per Analysis
    wizardAutoRunHandledRef.current.add(analysisId);

    if (chatSessionIdRef.current !== composeSessionId) {
      // Synchronous ref update FIRST (ensureChatSession's own documented pattern) so any
      // same-tick getSessionId() reader sees the adopted session before the re-render lands.
      chatSessionIdRef.current = composeSessionId;
      handleSelectHistorySession(composeSessionId);
    }

    if (seed?.autoRunReview === true && seed.subDomain) {
      // Enable the SAME deferred capability-discovery fetch the text door uses, so the review
      // bindingId resolves by the time the buffered effect needs it (never dispatch inline here —
      // the bindingId is virtually never resolved on this synchronous tick).
      setAgreementReviewGateNeeded(true);
      // task 070 (UAT2 review-depth selector): the wizard's "Analysis Details" step now carries an
      // additive `reviewDepth` toggle (defaults to Thorough). Reading it here — normalized, never
      // trusting the wire value blindly — means `runExplicit` dispatches IMMEDIATELY at the picked
      // depth instead of inserting a post-open ask (see runExplicit's two-mode contract).
      setPendingExplicitAgreementReview({
        subDomainKey: seed.subDomain,
        reviewDepth: normalizeReviewDepth(seed.reviewDepth),
      });
      setWizardAutoRunWatchdog({ analysisId });
    }
  });

  // ── OutcomeCard next-step chips (F-4, e2e-completion-audit 2026-07-10) ──────
  // A completed side-effect's OutcomeCard renders DECLARED next-step chips
  // (the Binding's `sprk_chiptransitions`, threaded C#→SSE→TS via SprkChat's
  // `onNextStep`). Without this handler OutcomeCard disables every chip
  // (OutcomeCard.tsx defensive `disabled={!onNextStep}`), so they ship
  // visible-but-dead. Activate them by routing an `invoke_capability` chip's
  // `targetBindingId` (a `sprk_playbookconsumer` Binding id) through the SAME
  // shared dispatchConsumer path the Click-path strip uses — no new dispatch
  // path (ADR-039: bindingId in, stream out; server resolves the Binding).
  // `navigate` chips open their server-composed `targetUrl`; `dismiss` is a
  // no-op. The dispatch's rendered output + re-armed strip come free via
  // `chips.dispatchBinding`.
  const { dispatchBinding } = chips;
  const handleNextStep = React.useCallback(
    (chip: INextStepChip): void => {
      if (chip.actionKind === "invoke_capability" && chip.targetBindingId) {
        dispatchBinding(chip.targetBindingId, { slots: undefined });
        return;
      }
      if (chip.actionKind === "navigate" && chip.targetUrl && typeof window !== "undefined") {
        window.open(chip.targetUrl, "_blank", "noopener,noreferrer");
      }
      // `dismiss` (or an invoke_capability chip with no Binding id) → no-op.
    },
    [dispatchBinding]
  );

  // R7 12.3a: normalize restored SessionRestoreMessage[] → IChatMessage[].
  const restoredInitialMessages = React.useMemo<IChatMessage[] | undefined>(() => {
    if (!restoreCtx?.recentMessages || restoreCtx.recentMessages.length === 0) return undefined;
    return restoreCtx.recentMessages.map((m) => ({
      role: m.role === "User" || m.role === "Assistant" || m.role === "System" ? m.role : "User",
      content: m.content,
      timestamp: m.timestamp,
    }));
  }, [restoreCtx?.recentMessages]);

  // FIX #1a — the post-mount document-level action chips (Summarize / Add to DMS / Draft email) now
  // render INSIDE the transcript footer, directly BENEATH the "Your file is available to edit…" ask
  // message (next-step affordances), instead of ABOVE the whole chat. Combined with the Click-path
  // consumer chips in the SAME footer slot (both land at the transcript's bottom edge). Memoized so
  // SprkChat's slot-keyed auto-scroll fires only when the slot content actually changes.
  //
  // React #300 (hooks-count mismatch) fix: this useMemo MUST live ABOVE the auth guard below. On a
  // cold `composeMode=editor` load `isAuthenticated` is false on the first render (auth probe pending)
  // then true after it resolves — a hook placed BELOW the guard would run in the second render but
  // not the first, changing the hook count and throwing "rendered more hooks than during the previous
  // render". Keep every hook call above the guard.
  // R6-2 (UAT 2026-07-21): show ONE chip row at a time. Right after a "revise the document" mount,
  // the compose-context doc-action chips (Summarize / Add-to-DMS / Draft-email) are the relevant
  // next-steps, so they REPLACE the generic consumer cards instead of stacking a second row. Once
  // the user acts (handleDocAction clears reviseChipsPending), the consumer cards resume.
  const transcriptFooter = React.useMemo(
    () => (
      <>
        {/* UAT round-6 (item #14): the "Rerun a full analysis" card renders FIRST in the transcript
            footer — i.e. INLINE in the transcript, directly beneath the last message, which right after
            a Quick review is the "Quick scan — … I've finished reviewing…" completion message. It
            scrolls WITH the conversation (it lives inside SprkChat's transcriptFooterSlot) instead of
            floating pinned at the top of the pane (the owner's round-6 complaint). Renders nothing until
            a quick run arms it; single-slot (never stacks). */}
        {rerunFullAnalysisCard.cardSlot}
        {reviseChipsPending ? <ComposeDocActionChips onAction={handleDocAction} /> : chips.consumerChipsSlot}
      </>
    ),
    [rerunFullAnalysisCard.cardSlot, reviseChipsPending, handleDocAction, chips.consumerChipsSlot]
  );

  // task 042 (FR-F3) — My Assistant questionnaire: open-state, cold-start gate, write/erase path.
  // Defensive by construction (inert with no Xrm user id, e.g. jsdom/non-MDA hosts). MUST live ABOVE
  // the auth guard below (Rules of Hooks / React #300 — see the transcriptFooter note above).
  const myAssistant = useMyAssistant({ authenticatedFetch, bffBaseUrl });

  // MA-1 (UAT 2026-07-19): the questionnaire no longer auto-opens. When the profile is incomplete we
  // show a dismissible "complete your profile" nudge instead; dismissal is session-scoped.
  const [profileNudgeDismissed, setProfileNudgeDismissed] = React.useState(false);
  const showProfileNudge =
    myAssistant.available && myAssistant.needsProfile && !profileNudgeDismissed;

  // ── Auth loading guard (gate on isAuthenticated — never a token snapshot) ──
  // NOTE (Rules of Hooks): every React.use* call MUST appear ABOVE this early return — see the
  // React #300 note on `transcriptFooter` above. Do not add hooks below this line.
  if (!isAuthenticated) {
    return (
      <div className={styles.root}>
        <AuthLoadingState />
      </div>
    );
  }

  // Welcome heading shows only with no session, no entity, and no playbook.
  const showWelcomePanel =
    chatSessionId === null && entityContext === null && playbookId === undefined;

  // CHAT-4 (UAT 2026-07-19): the get-started CARDS show whenever the transcript is empty (any
  // session state) and there's no entity/playbook focus — so a restored-but-empty session gets the
  // suggestions instead of SprkChat's bare "No messages yet". SprkChat's built-in empty state is
  // suppressed (hideEmptyState) while we render our own.
  const showWelcomeCards =
    chatMessageCount === 0 && entityContext === null && playbookId === undefined;

  const predefinedPrompts =
    selection.refinementPrompts.length > 0 ? selection.refinementPrompts : undefined;

  const hostContext = entityContext
    ? {
        entityType: entityContext.entityType as string,
        entityId: entityContext.entityId,
        workspaceType: "spaarke-ai",
      }
    : undefined;

  return (
    <div className={styles.root}>
      {/* UAT round-5 #9 — center-screen live-progress popup while an NDA review runs (portals to
          document.body, so its placement in the tree is immaterial). UAT round-3 item #8: now
          non-blocking (modalType="non-modal") + dismissible via ndaRun.visible/dismiss — see
          useNdaReviewRunProgress.ts. */}
      <NdaReviewProgressModal
        status={ndaRun.status}
        visible={ndaRun.visible}
        onClose={ndaRun.close}
        onDismiss={ndaRun.dismiss}
      />
      <PaneHeader
        title="Assistant"
        icon={<ChatRegular />}
        onCollapse={paneCollapse ? handleHeaderCollapse : undefined}
        expanded={!(paneCollapse?.isCollapsed("assistant") ?? false)}
        rightSlot={
          // P2-2 (UAT 2026-07-18): header controls reordered to History / New session /
          // Tools (left→right, Claude-Code style). History is now an icon-only trigger.
          <>
            <HistoryMenu
              onSelectSession={handleSelectHistorySession}
              bffBaseUrl={bffBaseUrl}
              authenticatedFetch={authenticatedFetch}
              resolveClassifiedSubDomain={resolveClassifiedSubDomainForSession}
              dataService={emailLookupDataService}
            />
            {/* R4-5: New session — clears the persisted session id and remounts
                SprkChat to mint a fresh session. PaneHeader's rightSlot already
                stops propagation, so the header collapse never fires. */}
            <Tooltip content="New session" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<ChatAddRegular />}
                aria-label="New session"
                onClick={(e) => {
                  e.stopPropagation();
                  handleNewSession();
                }}
              />
            </Tooltip>
            {/* Task 040 (FR-F1) — the Assistant tool drop-down (Quick Start +
                My Assistant). Mirrors HistoryMenu/ContextPaneMenu/
                WorkspacePaneMenu — a second, independent Menu trigger in this
                rightSlot. task 042 (FR-F3): "My Assistant" opens the stated-profile
                questionnaire. */}
            <AssistantToolMenu
              // R7-1 (UAT 2026-07-21): route "Quick Start" to the ONE ConversationPane-owned modal
              // (below) instead of AssistantToolMenu's own instance. That modal carries the session
              // file context + email handler + the R7-2 next-step injection, and — because opening it
              // never remounts SprkChat — the follow-up suggestion pills survive (the dual-modal path
              // was the one that lost them). AssistantToolMenu delegates to this prop when supplied.
              onQuickStart={() => {
                setQuickStartTab("create");
                setQuickStartOpen(true);
              }}
              onMyAssistant={myAssistant.openDialog}
              highlightMyAssistant={myAssistant.needsProfile}
              // #8 (UAT 2026-07-21): the "Memory" entry opens the remembers-about-you review dialog.
              onMemory={() => setMemoryOpen(true)}
            />
          </>
        }
      />

      {playbook.activePlaybookName !== null && (
        <PlaybookHeaderStrip
          name={playbook.activePlaybookName}
          onChangePlaybook={playbook.handleChangePlaybook}
        />
      )}

      <div className={styles.content} role="region" aria-label="AI Chat">
        {/* MA-1 (UAT 2026-07-19): dismissible "complete your profile" nudge — replaces the old
            jarring auto-open of the My Assistant questionnaire. Clicking "Set up" opens the dialog. */}
        {showProfileNudge && (
          <MessageBar intent="info" data-testid="assistant-profile-nudge">
            <MessageBarBody>
              Personalize your assistant — tell it your role, focus areas, and preferences so it can
              tailor its help.
            </MessageBarBody>
            <MessageBarActions
              containerAction={
                <Button
                  appearance="transparent"
                  aria-label="Dismiss"
                  icon={<DismissRegular />}
                  onClick={() => setProfileNudgeDismissed(true)}
                  data-testid="assistant-profile-nudge-dismiss"
                />
              }
            >
              <Button
                size="small"
                onClick={myAssistant.openDialog}
                data-testid="assistant-profile-nudge-setup"
              >
                Set up
              </Button>
            </MessageBarActions>
          </MessageBar>
        )}

        {/* assistant-enhancements-r2 task 001 (FR-E1): the spine-driven proactive-suggestion
            surface (banner + suggestion-card stack, formerly `{suggestions.suggestionSlot}`) was
            removed here. The notification spine, NotificationsClient, and Daily Briefing are
            unaffected; the reactive Suggested-Next-Steps chips render via the transcript footer. */}

        {/* UAT round-4 (item #9): "Rerun a full analysis" — a persistent act-on CARD (not a chip;
            ASSISTANT-UI-ELEMENT-CRITERIA.md) offered after a QUICK-depth review completes.
            UAT round-6 (item #14): the card MOVED OUT of this top-of-pane region (it read as pinned
            above the notifications area) and INTO the transcript footer (SprkChat.transcriptFooterSlot,
            see `transcriptFooter` above), so it renders inline beneath the "Quick scan…" completion
            message and scrolls WITH the conversation. */}

        {/* nda-r1 follow-up (UAT 2026-07-26): "Review an NDA" moved OUT of this top-of-pane
            notification slot (it read as "hidden" above the fold) and INTO the Suggested-Next-Steps
            strip as an in-line card, alongside "Summarize this file / Revise document". See
            getAppendedLocalChips + LOCAL_CHIP.ndaReview → handleReviewNda. Rendered only when the
            classifier flagged the upload as a registered document-review type (host gates on
            ndaReviewFileRef; task 064 generalized the docType→capability match to a small registry
            in localActionChips.ts, DOCUMENT_REVIEW_CAPABILITIES — today only "nda" is registered,
            so behavior is unchanged). */}

        {showWelcomePanel && <WelcomePanel />}
        {showWelcomeCards && (
          <WelcomeStartCards
            onSummarize={handleWelcomeSummarize}
            onCreateMatter={handleWelcomeCreateMatter}
            onCompose={handleWelcomeCompose}
            // R4-2 (UAT 2026-07-19): "More…" opens the Quick Start modal (same modal the ⋮ menu uses).
            onMore={() => {
              setQuickStartTab("create");
              setQuickStartOpen(true);
            }}
          />
        )}

        <div className={styles.chatWrapper}>
          <RestoreBanners
            hasStaleEntities={restoreCtx?.hasStaleEntities ?? false}
            conversationSummary={restoreCtx?.conversationSummary}
          />

          {/* Compose three-pane coordination — Assistant leg (Flows 2 + 4).
              Renders nothing until a compose flow fires (task 104). */}
          <ComposeAssistantCoordination />

          {/* FR-17 undo/replace (task 034) → DEF-12: the Undo/Try-another BUTTONS moved onto the
              Assistant confirmation message (the AI↔user interaction surface). This bar is kept
              ERROR-ONLY — passing `lastEdit={null}` suppresses its action buttons while still
              surfacing a failed-supersession MessageBar. Both accept/undo/tryAnother intents still
              route to durable ledger supersessions (never a DOM undo). */}
          <EditSupersessionBar
            lastEdit={null}
            busy={supersession.busy}
            error={supersession.error}
            onUndo={handleUndoEdit}
            onTryAnother={handleReplaceEdit}
            onDismissError={supersession.clearError}
          />

          {selection.selectionChip !== null && (
            <RefinementChipBar
              chip={selection.selectionChip}
              onClick={selection.handleChipClick}
              onDismiss={selection.handleChipDismiss}
            />
          )}

          {attachments.uploadedFileCount > 0 && (
            <FilesAttachedIndicator
              uploadedFileCount={attachments.uploadedFileCount}
              promotedCount={attachments.promotedCount}
              // decision-1 (UAT 2026-07-19): give the files their own collapsible section — a
              // dropdown lists each filename when there's more than one.
              files={attachments.attachmentChips}
              // R5-1 (UAT 2026-07-20): "Revise in Compose" moved OUT of this tray row and into an
              // in-line action card alongside the post-attach cards (see getAppendedLocalChips +
              // LOCAL_CHIP.reviseInCompose). No onRevise button here anymore.
            />
          )}

          {/* UP-10 (UAT 2026-07-19): live "Attaching file… / Classifying file…" progress with a
              spinner while the composer is locked during the ingest window, so the user knows to wait. */}
          <UploadProgressIndicator
            attaching={attachments.isPromoting}
            classifying={eventBatch.isEventInFlight}
            // R4-10 (UAT 2026-07-19): "Working…" while a chip capability (e.g. Summarize) runs.
            working={chips.dispatching}
          />

          {/* FIX #1a: the post-mount DOCUMENT-LEVEL action chips (Summarize / Add to DMS / Draft
              reporting email) moved BELOW the ask message — they now render inside SprkChat's
              transcriptFooterSlot (see `transcriptFooter`), beneath the "Your file is available to
              edit…" message, as next-step affordances. */}

          <div className={styles.sprkChatFlex}>
            <SprkChat
              key={commands.sprkChatRemountKey}
              apiBaseUrl={bffBaseUrl}
              authenticatedFetch={authenticatedFetch}
              getAccessToken={getAccessToken}
              sessionId={chatSessionId ?? undefined}
              initialMessages={restoredInitialMessages}
              playbookId={playbookId}
              onSessionCreated={handleSessionCreated}
              onSessionStale={handleSessionStale}
              // #2b (UAT 2026-07-18): lock the composer during the upload/classify window so a typed
              // instruction can't be sent mid-ingest and silently dropped. SprkChat already locks on
              // its own `streaming`/`extracting` states; this adds the two windows it can't see — the
              // `/documents` promotion POST (attachments.isPromoting) and the Event classify SSE stream
              // (eventBatch.isEventInFlight).
              inputBusy={attachments.isPromoting || eventBatch.isEventInFlight || chips.dispatching}
              // CHAT-6 (UAT 2026-07-19): the SpaarkeAi Assistant treats slash commands as an
              // advanced affordance — hide the toolbar Prompt button (slash menu still reachable via `/`).
              hidePromptMenu
              // CHAT-4 (UAT 2026-07-19): we render our own WelcomeStartCards on an empty transcript,
              // so suppress SprkChat's bare "No messages yet".
              hideEmptyState={showWelcomeCards}
              // CHAT-5 (UAT 2026-07-19): a taller, friendlier composer. The placeholder greets on a
              // fresh/empty transcript and reverts to the neutral prompt once the conversation starts.
              inputPlaceholder={chatMessageCount === 0 ? "Let's get started…" : "Type a message…"}
              // R4-3 (UAT 2026-07-19): taller composer (~2× the prior default).
              inputMinRows={6}
              // Click-path chips render INLINE IN THE TRANSCRIPT (G-P2 finding 1); FIX #1a adds the
              // post-mount document-action chips ABOVE them in the SAME footer slot (both beneath the
              // last message). The node is memoized so slot-keyed auto-scroll fires only on change.
              transcriptFooterSlot={transcriptFooter}
              // ai-advanced-capabilities-nda-r1 task 011 (FR-04b): the runtime model-tier picker,
              // rendered directly above the composer via SprkChat's `aboveInputSlot` seam (the
              // Click-path next-step chip strip's former slot — see types.ts doc; this is now its
              // only consumer). Disabled during the same in-flight windows the composer itself locks
              // on, so the picker can't change mid-turn.
              aboveInputSlot={
                <AssistantModelTierPicker
                  value={modelTierOverride}
                  onChange={setModelTierOverride}
                  disabled={attachments.isPromoting || eventBatch.isEventInFlight || chips.dispatching}
                />
              }
              onPlaybookChange={playbook.handlePlaybookChange}
              predefinedPrompts={predefinedPrompts}
              hostContext={hostContext}
              onPaneEvent={streaming.onPaneEvent ?? null}
              onAttachmentReady={attachments.handleAttachmentReady}
              onAttachmentsChanged={attachments.handleAttachmentsChanged}
              onAttachmentRemoved={attachments.handleAttachmentRemoved}
              injectLocalMessage={injection.pendingInjection}
              onLocalMessageInjected={injection.handleLocalMessageInjected}
              onBeforeSendMessage={handleBeforeSendMessage}
              onMessagesChange={(msgs) => {
                // CHAT-4: keep the local transcript-length in sync so the get-started cards toggle
                // with the empty/non-empty state, then defer to the existing command-routing note.
                setChatMessageCount(msgs.length);
                commands.noteMessagesChanged(msgs);
              }}
              onDecorateOutboundBody={handleDecorateOutboundBodyWithRevise}
              onPlaybookOptions={playbookOptions.handlePlaybookOptions}
              onSelectPlaybook={playbookOptions.handleSelectPlaybook}
              onOpenLibraryModal={playbookOptions.handleOpenLibraryModal}
              onContextEvent={contextBridge.handleContextEvent}
              // spaarkeai-assistant-enhancements-r1 P0(b): TEXT/agent-path create-flow
              // launch — opens the pre-seeded surface (matter/event/task wizard) when
              // the agent selects a `surface_launch`-disposition capability.
              onSurfaceLaunch={handleSurfaceLaunch}
              onCitations={docQaCitation.onCitations}
              // UAT 2026-07-19: the per-message "Insert into document" button was noise — it appeared
              // after essentially every Assistant message and was rarely the relevant action (even the
              // P1-3 length gate wasn't selective enough). Removed by not passing `onInsertToCompose`.
              // `insertSuggestionToCompose` (the bridge conduit) stays available for a future TARGETED
              // insert affordance (e.g. a dedicated "Insert" only on a genuine compose-draft message).
              // onInsertToCompose intentionally omitted.
              // FIX #10a: the generic per-message "Open in Compose" affordance was removed —
              // `onOpenInCompose` is intentionally NOT passed (no auto-appended mount link).
              // DEF-12: per-message Accept / Reject / Try-another controls on the compose-edit
              // confirmation message. Wired to the EXISTING handlers; SprkChat renders them only on
              // the message whose composeEdit.ledgerRef === activeComposeEditLedgerRef (the live edit).
              onComposeEditAccept={handleAcceptComposeEdit}
              onComposeEditReject={handleUndoEdit}
              onComposeEditTryAnother={handleReplaceEdit}
              // FIX #3: "Keep redline" — clears the action prompt without mutating the editor/ledger.
              onComposeEditKeep={handleKeepComposeEdit}
              activeComposeEditLedgerRef={supersession.lastEdit?.ledgerRef ?? null}
              // F-4: activate OutcomeCard next-step chips — routes an
              // invoke_capability chip's targetBindingId through the shared
              // dispatchConsumer path (see handleNextStep above).
              onNextStep={handleNextStep}
              // FIX #7a: "Open preview" on the persistent "Saved to the DMS" message — opens the File
              // Preview modal for that document (savedPreview metadata carries the id per message).
              onOpenSavedPreview={handleOpenSavedPreview}
            />
            {/* R5-3 (UAT 2026-07-20): the floating "?" HelpAffordance was removed — no longer
                accurate/useful. The /help slash command still opens CommandHelpPanel below. */}
            <CommandHelpPanel
              open={commands.helpPanelOpen}
              onClose={() => commands.setHelpPanelOpen(false)}
            />
          </div>
        </div>
      </div>

      {/* P1-8: Quick Start opened from the chips' "More…" card (see openLibraryModalRef) AND, since
          R7-1, from the ⋮ "Quick Start" menu (onQuickStart above) — the ONE modal for both entries. */}
      <QuickStartModal
        open={quickStartOpen}
        onClose={() => setQuickStartOpen(false)}
        initialTab={quickStartTab}
        // ai-advanced-capabilities-analysis-hub-r1: the Analysis tab's "Agreement Review"
        // card. Close Quick Start, then ask WorkspacePane (which injects the wizard's
        // Xrm services + resolves regarding from the host record) to host the Create
        // Analysis wizard AS A MODAL. The wizard does not take a tab; on finish it opens
        // its result tab as today.
        onCreateAnalysis={(workTypeValue, workTypeLabel) => {
          setQuickStartOpen(false);
          dispatch("workspace", {
            type: "open_create_analysis_wizard",
            analysisWorkType: workTypeValue,
            analysisWorkTypeLabel: workTypeLabel,
          });
        }}
        getFileContext={getQuickStartFileContext}
        // R5-9: Quick Start "Send Email" opens the shared Email Compose modal (blank), not the
        // playbook-library web resource. Close Quick Start first so the two modals don't stack.
        onSendEmail={() => {
          setQuickStartOpen(false);
          setEmailSeed({});
        }}
        // R7-2 (UAT 2026-07-21): a launched wizard opens in a separate tab, leaving the Assistant
        // pane with no next step. Inject a determinative next-step message so the pane never
        // dead-ends; the consumer cards (transcriptFooter) remain as follow-on actions. Only the
        // wizard/surface cards navigate away — the in-app cards (Send Email / Meeting) don't need it.
        onCardLaunched={(cardId) => {
          const WIZARD_CARDS = [
            "create-matter-wizard",
            "create-project-wizard",
            "assign-work",
            "document-upload-wizard",
            "find-similar-wizard",
          ];
          if (WIZARD_CARDS.includes(cardId)) {
            injection.enqueue(
              makeLocalAssistantMessage(
                "I've started that for you in a separate tab. When you're back, you can attach a file, ask a question about it, or pick another next step below.",
              ),
            );
          }
        }}
      />

      {/* task 024 (spec FR-08): attaching a file to a LIVE chat (prior messages already exist)
          prompts new-session vs add-to-current — never a silent default. "New session" pairs the
          hook's file-carryover bookkeeping (prepareFilesForNewSession) with the EXISTING
          "New session" seam (handleNewSession — clear + remount, task 021/022/023's archive
          semantics: the prior session stays fully retrievable from History regardless, since
          ListRecentSessionsAsync is not filtered by the Dataverse archived marker) so no new
          client-side session/fork logic is introduced here. */}
      <FileAttachSessionPrompt
        pending={attachments.pendingFileSessionChoice}
        onChooseNewSession={() => {
          attachments.prepareFilesForNewSession();
          handleNewSession();
        }}
        onChooseAddToCurrent={attachments.chooseAddToCurrentSession}
        onDismiss={attachments.dismissFileSessionChoice}
      />

      {/* #8 (UAT 2026-07-21): "What the Assistant remembers about you" — review + forget over the
          shipped GET/DELETE /api/memory/user endpoints. Opened from the ⋮ Assistant Tools "Memory" entry. */}
      <MemoryDialog
        open={memoryOpen}
        onClose={() => setMemoryOpen(false)}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
      />

      {/* R5-9 (UAT 2026-07-20): the shared Email Compose modal (SendEmailDialog → EmailComposer).
          Opened by the post-Draft "Send as email" chip (seeded from the draft) and the Quick Start
          "Send Email" card (blank). Client-only — the send runs inside the engine via authenticatedFetch. */}
      <SendEmailDialog
        open={emailSeed !== null}
        onClose={() => setEmailSeed(null)}
        initialTo={emailSeed?.initialTo}
        initialSubject={emailSeed?.initialSubject}
        initialBody={emailSeed?.initialBody}
        initialBodyFormat="PlainText"
        onSearchRecipients={handleSearchRecipients}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        onSent={() => setEmailSeed(null)}
      />

      {playbook.toastPlaybookName !== null && <PlaybookToast name={playbook.toastPlaybookName} />}

      {/* FIX #7a: File Preview modal for the document persisted by "Add to DMS". Opened from the
          persistent "Saved to the DMS" chat message's "Open preview" action. Reuses the shared
          RichFilePreviewDialog + the BFF preview-url endpoint (no new component/service — §11).
          Rendered only once a Save has surfaced a document id. */}
      {savedPreview ? (
        <RichFilePreviewDialog
          open={previewOpen}
          documentId={savedPreview.documentId}
          documentName={savedPreview.fileName ?? "Document"}
          onClose={() => setPreviewOpen(false)}
          fetchPreviewUrl={fetchSavedPreviewUrl}
          onOpenFile={() => undefined}
          onEmailDocument={() => undefined}
          onCopyLink={() => undefined}
          onOpenRecord={() => {
            void previewNavigationService.openRecord("sprk_document", savedPreview.documentId);
          }}
        />
      ) : null}

      {/* task 042 (FR-F3 / FR-E1 / F5) — My Assistant questionnaire. Cold-start gate + on-demand
          launch from the Tools menu; writes sprk_userprofile (keyed upsert + N:N + profilecompletedon)
          and hosts the GDPR erasure action. Rendered only when a Dataverse user context is available. */}
      {myAssistant.available ? (
        <MyAssistantDialog
          open={myAssistant.open}
          onClose={myAssistant.closeDialog}
          coldStart={myAssistant.coldStart}
          practiceAreas={myAssistant.practiceAreas}
          workOffices={myAssistant.workOffices}
          initialValues={myAssistant.initialValues}
          onSubmit={myAssistant.onSubmit}
          loading={myAssistant.loading}
        />
      ) : null}
    </div>
  );
}
