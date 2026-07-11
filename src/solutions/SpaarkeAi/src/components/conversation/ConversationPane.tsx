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
} from "@spaarke/compose-components/context/composeActionBridge";
import { resolveCurrentComposeLedgerRef, buildComposeApplyEvent } from "./composeApplyLeg";
// FR-17 undo/replace (task 034) — the durable ledger-supersession hook + its Assistant affordance.
import { useEditSupersession, EditSupersessionBar } from "./useEditSupersession";
import type { ComposeAssistantToWorkspaceFlow } from "@spaarke/compose-components/types/compose-contracts";
import { formatEventOutputMarkdown } from "./DocumentUploadedEventStream";
import { formatComposeActionResultMarkdown } from "./composeResultFormat";
import { makeLocalAssistantMessage } from "./summarizeRouting";
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
} from "./summarizeRouting";
export type { SummarizeRouteDecision, SummarizeIntentInputs } from "./summarizeRouting";

/**
 * DEF-09: the CONFIRMATION-only Assistant line for a compose EDIT action. The
 * alternative itself materializes as an inline redline IN the Compose document
 * (accept/reject there) — the Assistant must NOT restate the proposed text (that
 * would duplicate the redline and reintroduce the "renders as a chat message, not
 * a redline" defect). Informational compose actions keep their full grounded prose.
 */
export const COMPOSE_EDIT_CONFIRMATION =
  'Drafted an alternative — see the pending redline in the document; accept or reject it there.';

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

  // Session-id getter for the dispatch/event seams (stable across renders).
  const chatSessionIdRef = React.useRef<string | null>(chatSessionId);
  chatSessionIdRef.current = chatSessionId;
  const getSessionId = React.useCallback(() => chatSessionIdRef.current, []);

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
  const { trackAppliedEdit, undo: undoEdit, tryAnother: tryAnotherEdit } = supersession;

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
        if (isEditAction) {
          // DEF-09: confirmation ONLY — the alternative is the inline redline in the
          // document, not a chat message. Do NOT restate the proposed text here.
          injection.enqueue(makeLocalAssistantMessage(COMPOSE_EDIT_CONFIRMATION));
        } else if (dispatched.result !== undefined && dispatched.result !== null) {
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
  const registerComposeActiveDocument = React.useCallback(
    async ({ docxBytes, fileName }: { docxBytes: ArrayBuffer; fileName?: string }): Promise<void> => {
      const sessionId = getSessionId();
      if (!sessionId || !bffBaseUrl) return;
      try {
        const name = fileName ?? "compose-document.docx";
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
        const sessionFileId = uploaded?.documentId;
        if (!sessionFileId) return;
        await authenticatedFetch(`${bffBaseUrl}/api/compose/active-document`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ sessionId, sessionFileId, source: "compose-direct", fileName: name }),
        });
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

          {/* FR-17 undo/replace affordance (task 034) — appears after an AI redline is applied;
              both intents route to durable ledger supersessions (never a DOM undo). */}
          <EditSupersessionBar
            lastEdit={supersession.lastEdit}
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
              onBeforeSendMessage={attachments.handleBeforeSendMessage}
              onMessagesChange={commands.noteMessagesChanged}
              onDecorateOutboundBody={commands.handleDecorateOutboundBody}
              onPlaybookOptions={playbookOptions.handlePlaybookOptions}
              onSelectPlaybook={playbookOptions.handleSelectPlaybook}
              onOpenLibraryModal={playbookOptions.handleOpenLibraryModal}
              onContextEvent={contextBridge.handleContextEvent}
              onCitations={docQaCitation.onCitations}
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
