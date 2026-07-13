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
import { Button, Tooltip } from "@fluentui/react-components";
import { ChatRegular, ChatAddRegular } from "@fluentui/react-icons";
import { PaneHeader, SprkChat, createConsumerDispatcher } from "@spaarke/ui-components";
import { useAiSession, useDispatchPaneEvent, clearExecutionTraceBuffer } from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
import type {
  IChatMessage,
  DispatchWorkspaceEvent,
  DispatchConsumerResult,
  INextStepChip,
} from "@spaarke/ui-components";
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "../WelcomePanel";
// Compose three-pane coordination — ASSISTANT leg (task 104 / E2E-R5). Typed
// receivers for Flow 2 (compose_selection_offer) + Flow 4 (compose_context_offer).
import { ComposeAssistantCoordination } from "./ComposeAssistantCoordination";
import { useShellStage, useRestoreContext, usePaneCollapseContext } from "../shell/ThreePaneShell";
import { assistantTextToDraftHtml } from "./assistantDraftHtml";
import { HistoryMenu } from "./HistoryOverlay";
import { CommandHelpPanel } from "./CommandHelpPanel";
import { HelpAffordance } from "./HelpAffordance";
import { useInjectionQueue } from "./useInjectionQueue";
import { useEventBatch } from "./useEventBatch";
import { useAttachments } from "./useAttachments";
import { useConsumerChips } from "./useConsumerChips";
import { useContextEventBridge } from "./useContextEventBridge";
import { useDocQaCitationBridge } from "./useDocQaCitationBridge";
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
} from "@spaarke/compose-components/context/composeActionBridge";
import { resolveCurrentComposeLedgerRef, buildComposeApplyEvent } from "./composeApplyLeg";
// FR-17 undo/replace (task 034) — the durable ledger-supersession hook + its Assistant affordance.
import { useEditSupersession, EditSupersessionBar } from "./useEditSupersession";
import type { ComposeAssistantToWorkspaceFlow } from "@spaarke/compose-components/types/compose-contracts";
import { formatEventOutputMarkdown } from "./DocumentUploadedEventStream";
import { formatComposeActionResultMarkdown } from "./composeResultFormat";
import { makeLocalAssistantMessage, makeComposeEditControlsMessage } from "./summarizeRouting";
import { routeReviseIntent } from "./composeReviseRouting";
import {
  detectReviseThisDocumentIntent,
  REVISE_MOUNT_ASK_MESSAGE,
  type RevisionIntent,
} from "./composeReviseRouting";
import { ReviseIntentChips } from "./ReviseIntentChips";
import { useCapabilityDiscovery } from "./useCapabilityDiscovery";
import {
  AuthLoadingState,
  PlaybookHeaderStrip,
  PlaybookToast,
  RestoreBanners,
  RefinementChipBar,
  FilesAttachedIndicator,
  useConversationPaneLayoutStyles,
} from "./ConversationPaneChrome";

// Public pure-helper surface (tests import these from '../ConversationPane').
export {
  SUMMARIZE_SLASH_PREFIX,
  SUMMARIZE_PROMPT_FIRST_INTERJECTION,
  FILE_CONFIRMATION_MAX_NAMES,
  routeSummarizeIntent,
  buildFileConfirmationMessage,
  buildMultiFileSummarizeInterjection,
  makeLocalAssistantMessage,
  makeComposeEditControlsMessage,
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
  REVISE_CHIP_OPTIONS,
} from "./composeReviseRouting";
export type {
  RevisionIntent,
  ReviseRouteDecision,
  RevisionIntentSuggestion,
  ReviseThisDocumentDetection,
  ReviseChipOption,
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

  // Wave 3 Part 1 (Test #1 real repro): a file uploaded to the ASSISTANT (chat) — then "revise this
  // document" → disambiguation → "Open in Compose" — was NEVER mounted in Compose, so the Compose-side
  // registration never fired. Capture the uploaded file's server id when the chat attachment promotes
  // (the `/documents` 202's `documentId`, threaded from useAttachments) and cache it as the active
  // source document (most-recent-upload-wins). A chat upload has no Compose document session yet, so
  // `documentSessionId` stays undefined — the compose.upload seed needs only { sessionId, sessionFileId }.
  const handleSessionFileUploaded = React.useCallback(
    ({ sessionFileId, fileName }: { sessionFileId: string; fileName: string }): void => {
      activeSourceDocRef.current = { sessionFileId, fileName };
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
  const mountActiveSourceDocInCompose = React.useCallback((): boolean => {
    const active = activeSourceDocRef.current;
    const sessionId = chatSessionIdRef.current;
    if (!active?.sessionFileId || !sessionId) return false;
    dispatch("workspace", {
      type: "widget_load",
      widgetType: "workspace",
      widgetData: {
        layoutName: "Compose",
        compose: {
          upload: {
            sessionId,
            sessionFileId: active.sessionFileId,
            fileName: active.fileName,
          },
        },
      },
      displayName: "Compose",
    } as WorkspacePaneEvent);
    return true;
  }, [dispatch]);

  const handleOpenInCompose = React.useCallback(
    (content: string): void => {
      if (mountActiveSourceDocInCompose()) return;
      if (!content) return;
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "workspace",
        widgetData: {
          layoutName: "Compose",
          compose: { draft: { html: assistantTextToDraftHtml(content) } },
        },
        displayName: "Compose",
      } as WorkspacePaneEvent);
    },
    [mountActiveSourceDocInCompose, dispatch]
  );

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
  const { capabilities: launchableCapabilities } = useCapabilityDiscovery({
    bffBaseUrl,
    authenticatedFetch,
    enabled: reviseCapabilityNeeded,
  });
  const reviseBindingId = React.useMemo<string | null>(
    () =>
      launchableCapabilities.find((c) => c.consumerType === "compose-revise-document")?.bindingId ??
      null,
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

  // ── Behaviour hooks (see module map in the header) ────────────────────────
  const injection = useInjectionQueue();

  // Stable-ref indirection keeps eventBatch → chips composition acyclic.
  const acceptChipsRef = React.useRef<(raw: unknown) => void>(() => undefined);
  const eventBatch = useEventBatch({
    bffBaseUrl,
    getAccessToken,
    getSessionId,
    enqueueAssistantMessage: injection.enqueue,
    onChips: React.useCallback((raw: unknown) => acceptChipsRef.current(raw), []),
  });

  const attachments = useAttachments({
    bffBaseUrl,
    chatSessionId,
    hasActiveWorkspaceDocument: entityContext !== null,
    authenticatedFetch,
    dispatch,
    inject: injection.inject,
    eventBatch,
    onSessionFileUploaded: handleSessionFileUploaded,
  });

  const chips = useConsumerChips({
    bffBaseUrl,
    getAccessToken,
    getSessionId,
    dispatch,
    sessionAttachmentCount: attachments.sessionAttachmentCount,
    enqueueAssistantMessage: injection.enqueue,
    inject: injection.inject,
  });
  acceptChipsRef.current = chips.acceptChips;

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
            const confirmationText =
              request.revisionScope === "whole-document"
                ? COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION
                : COMPOSE_EDIT_CONFIRMATION;
            injection.enqueue(
              ledgerRef
                ? makeComposeEditControlsMessage(confirmationText, {
                    ledgerRef,
                    bindingId: request.bindingId,
                  })
                : makeLocalAssistantMessage(confirmationText)
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
    // queue don't re-register every render.
    [actionQueue, injection, emitComposeApplyLeg, trackAppliedEdit]
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

  // A revise-intent chip click. Reads the CURRENT (post-mount) document session id — the reactive
  // back-fill first, then the ref (belt-and-suspenders) — so the dispatch never captures a stale
  // null. If the mount hasn't established the session yet, ask the user to retry rather than misroute
  // to the chat session (which is exactly the prose-narration bug this wave fixes).
  const handleReviseIntentPick = React.useCallback(
    (revisionIntent: RevisionIntent, instruction?: string): void => {
      const documentSessionId =
        activeComposeDocSessionId ?? activeSourceDocRef.current?.documentSessionId ?? null;
      if (!documentSessionId) {
        injection.enqueue(
          makeLocalAssistantMessage(
            "Your document is still loading into Compose — give it a moment, then pick a revision type again."
          )
        );
        return;
      }
      setReviseChipsPending(false);
      dispatchReviseDocument(revisionIntent, instruction, documentSessionId);
    },
    [activeComposeDocSessionId, injection, dispatchReviseDocument]
  );

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

  const registerComposeActiveDocument = React.useCallback(
    async ({
      docxBytes,
      fileName,
      documentSessionId,
    }: {
      docxBytes: ArrayBuffer;
      fileName?: string;
      documentSessionId?: string;
    }): Promise<void> => {
      const sessionId = getSessionId();
      if (!sessionId || !bffBaseUrl) return;
      try {
        const name = fileName ?? "compose-document.docx";
        // Wave 3 Part 3: dedup the upload so tab_change re-registrations don't re-upload / duplicate.
        const cacheKey = `${name}:${docxBytes.byteLength}`;
        let sessionFileId = activeDocUploadCacheRef.current.get(cacheKey);
        if (!sessionFileId) {
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
            `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/documents`,
            { method: "POST", body: form }
          );
          if (!uploadResp.ok) return;
          const uploaded = (await uploadResp.json()) as { documentId?: string };
          sessionFileId = uploaded?.documentId;
          if (!sessionFileId) return;
          activeDocUploadCacheRef.current.set(cacheKey, sessionFileId);
        }
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
          }),
        });
        // Wave 3 Part 1: remember this as the session's ACTIVE source document so "Open in Compose"
        // opens THAT document (compose.upload seed) rather than seeding the assistant message prose.
        activeSourceDocRef.current = { sessionFileId, documentSessionId, fileName: name };
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
    [getSessionId, bffBaseUrl, authenticatedFetch]
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
  const { handleDecorateOutboundBody } = commands;
  const handleDecorateOutboundBodyWithRevise = React.useCallback(
    async (body: Record<string, unknown>): Promise<Record<string, unknown> | null> => {
      const messageText = typeof body.message === "string" ? body.message : "";
      const detection = detectReviseThisDocumentIntent(messageText);
      const hasActiveSourceDoc =
        activeSourceDocRef.current?.sessionFileId != null && chatSessionIdRef.current != null;
      if (detection.isReviseThisDocument && hasActiveSourceDoc) {
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
      }
      return handleDecorateOutboundBody(body);
    },
    [handleDecorateOutboundBody, mountActiveSourceDocInCompose, injection]
  );

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
      // Wave 4: a fresh session clears any pending revise flow + the document-session back-fill.
      setReviseChipsPending(false);
      setPendingNamedRevise(null);
      setActiveComposeDocSessionId(null);
      // R5-D (2026-07-07): the execution-trace replay buffer is session-scoped —
      // a fresh session must not replay the previous session's tool calls.
      clearExecutionTraceBuffer();
    },
    [setChatSessionId, clearRefinementPrompts, resetAttachments, resetChips, resetEventBatch]
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

  // ── Auth loading guard (gate on isAuthenticated — never a token snapshot) ──
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
      <PaneHeader
        title="Assistant"
        icon={<ChatRegular />}
        onCollapse={paneCollapse ? handleHeaderCollapse : undefined}
        expanded={!(paneCollapse?.isCollapsed("assistant") ?? false)}
        rightSlot={
          <>
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
            <HistoryMenu
              onSelectSession={setChatSessionId}
              bffBaseUrl={bffBaseUrl}
              authenticatedFetch={authenticatedFetch}
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
        {showWelcomePanel && <WelcomePanel />}

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
            />
          )}

          {/* Wave 4 (end-to-end revise): after a natural-language "revise this document" auto-mounts
              the chat-uploaded file, the mount-then-ask message is injected into the transcript and
              these four intent chips appear. Clicking one dispatches compose-revise-document into the
              document session (inline redline), NOT a narrated chat turn. */}
          {reviseChipsPending && <ReviseIntentChips onPick={handleReviseIntentPick} />}

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
              // Click-path chips render INLINE IN THE TRANSCRIPT (G-P2 finding 1);
              // the node is memoized so slot-keyed auto-scroll fires only on change.
              transcriptFooterSlot={chips.consumerChipsSlot}
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
              onMessagesChange={commands.noteMessagesChanged}
              onDecorateOutboundBody={handleDecorateOutboundBodyWithRevise}
              onPlaybookOptions={playbookOptions.handlePlaybookOptions}
              onSelectPlaybook={playbookOptions.handleSelectPlaybook}
              onOpenLibraryModal={playbookOptions.handleOpenLibraryModal}
              onContextEvent={contextBridge.handleContextEvent}
              onCitations={docQaCitation.onCitations}
              // DEF-08 Part B: "Open in Compose" per-message affordance (opt-in; opens+seeds a
              // reused Compose draft tab with the message text).
              onOpenInCompose={handleOpenInCompose}
              // DEF-12: per-message Accept / Reject / Try-another controls on the compose-edit
              // confirmation message. Wired to the EXISTING handlers; SprkChat renders them only on
              // the message whose composeEdit.ledgerRef === activeComposeEditLedgerRef (the live edit).
              onComposeEditAccept={handleAcceptComposeEdit}
              onComposeEditReject={handleUndoEdit}
              onComposeEditTryAnother={handleReplaceEdit}
              activeComposeEditLedgerRef={supersession.lastEdit?.ledgerRef ?? null}
              // F-4: activate OutcomeCard next-step chips — routes an
              // invoke_capability chip's targetBindingId through the shared
              // dispatchConsumer path (see handleNextStep above).
              onNextStep={handleNextStep}
            />
            <HelpAffordance onClick={() => commands.setHelpPanelOpen(true)} />
            <CommandHelpPanel
              open={commands.helpPanelOpen}
              onClose={() => commands.setHelpPanelOpen(false)}
            />
          </div>
        </div>
      </div>

      {playbook.toastPlaybookName !== null && <PlaybookToast name={playbook.toastPlaybookName} />}
    </div>
  );
}
