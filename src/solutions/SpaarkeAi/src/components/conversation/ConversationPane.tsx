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
import { ChatRegular } from "@fluentui/react-icons";
import { PaneHeader, SprkChat } from "@spaarke/ui-components";
import { useAiSession, useDispatchPaneEvent } from "@spaarke/ai-widgets";
import type { IChatMessage } from "@spaarke/ui-components";
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "../WelcomePanel";
import { useShellStage, useRestoreContext, usePaneCollapseContext } from "../shell/ThreePaneShell";
import { HistoryMenu } from "./HistoryOverlay";
import { CommandHelpPanel } from "./CommandHelpPanel";
import { HelpAffordance } from "./HelpAffordance";
import { useInjectionQueue } from "./useInjectionQueue";
import { useEventBatch } from "./useEventBatch";
import { useAttachments } from "./useAttachments";
import { useConsumerChips } from "./useConsumerChips";
import { useContextEventBridge } from "./useContextEventBridge";
import { usePlaybookSelection } from "./usePlaybookSelection";
import { usePlaybookOptions } from "./usePlaybookOptions";
import { useCommandRouting } from "./useCommandRouting";
import { useSelectionChip } from "./useSelectionChip";
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

  const contextBridge = useContextEventBridge({ dispatch, acceptChips: chips.acceptChips });
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
    },
    [setChatSessionId, clearRefinementPrompts, resetAttachments, resetChips, resetEventBatch]
  );

  const handleHeaderCollapse = React.useCallback(() => {
    paneCollapse?.toggle("assistant");
  }, [paneCollapse]);

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
          <HistoryMenu
            onSelectSession={setChatSessionId}
            bffBaseUrl={bffBaseUrl}
            authenticatedFetch={authenticatedFetch}
          />
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
