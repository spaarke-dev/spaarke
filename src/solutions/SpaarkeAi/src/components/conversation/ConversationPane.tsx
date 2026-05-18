/**
 * ConversationPane.tsx — R2 left pane for the SpaarkeAi three-pane shell.
 *
 * Replaces R1's LeftPane + ChatPanel combination. Composes:
 *   - Tab bar: "Chat" and "History" tabs (mirrors R1 LeftPane pattern)
 *   - Chat tab: WelcomePanel (no session, no entity) or SprkChat (session active)
 *   - History tab: ChatHistoryPanel (session list with resume/delete)
 *
 * Key R1 → R2 migration changes:
 *   - Auth and session state consumed from useAiSession() (R2 AiSessionProvider)
 *     instead of useStandaloneAi() (R1 StandaloneAiProvider).
 *   - SprkChat's onPaneEvent callback bridges to AiSessionProvider's
 *     streaming.onPaneEvent, which routes SSE events to the typed PaneEventBus.
 *     Multiple panes (WorkspacePane, ContextPaneController) subscribe independently.
 *   - onSessionCreated and onPlaybookChange update AiSessionProvider state,
 *     which persists to sessionStorage identically to the R1 behaviour.
 *   - ShellStageContext transitions are driven from here:
 *       first message sent  → toLoading()
 *       stream starts       → (bus handles active-chat via widget_load)
 *       welcome prompt click → toLoading()
 *
 * SprkChat prop preservation (R1 → R2 mapping):
 *   apiBaseUrl         ← bffBaseUrl (same value, same meaning)
 *   accessToken        ← token
 *   sessionId          ← chatSessionId
 *   playbookId         ← playbookId
 *   onSessionCreated   ← handleSessionCreated (updates setChatSessionId)
 *   onPlaybookChange   ← handlePlaybookChange (updates setPlaybookId)
 *   predefinedPrompts  ← from pendingMessage (welcome flow)
 *   hostContext        ← derived from entityContext (same mapping as R1)
 *   onPaneEvent        ← streaming.onPaneEvent (routes to PaneEventBus channels)
 *
 * Stage-aware rendering:
 *   No session + no entity + no pending message → WelcomePanel
 *   Otherwise → SprkChat
 *
 * @see ChatPanel.tsx (R1) — the component this replaces
 * @see LeftPane.tsx (R1) — the tab wrapper this replaces
 * @see AiSessionProvider.tsx — session + streaming + PaneEventBus routing (R2)
 * @see WelcomePanel.tsx — welcome experience (unchanged from R1)
 * @see ChatHistoryPanel.tsx — history tab panel (now wired to useAiSession)
 * @see ADR-021 — Fluent v9, dark mode via FluentProvider (no hardcoded colors)
 * @see ADR-022 — React 19 Code Pages (hooks, functional components, bundled)
 */

import * as React from "react";
import { makeStyles, tokens, Button, Spinner, Text } from "@fluentui/react-components";
import { ChatRegular, HistoryRegular } from "@fluentui/react-icons";
import { SprkChat } from "@spaarke/ui-components";
import { useAiSession } from "@spaarke/ai-widgets";
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "../WelcomePanel";
import { ChatHistoryPanel } from "../ChatHistoryPanel";
import { useShellStage } from "../shell/ThreePaneShell";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Which tab is active in the left pane. */
type LeftPaneView = "chat" | "history";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // ── Tab bar (mirrors R1 LeftPane.tsx tabBar pattern) ─────────────────────
  tabBar: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground1,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    paddingTop: "2px",
    paddingBottom: "0px",
    gap: tokens.spacingHorizontalXS,
    minHeight: "36px",
  },
  tabButton: {
    borderRadius: "0px",
    height: "36px",
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
  },
  tabButtonActive: {
    borderBottomWidth: "2px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorBrandStroke1,
    color: tokens.colorBrandForeground1,
  },

  // ── Pane content area ─────────────────────────────────────────────────────
  content: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
  },

  // ── Auth loading state ────────────────────────────────────────────────────
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },

  // ── Chat tab wrappers ─────────────────────────────────────────────────────
  chatWrapper: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
    height: "100%",
  },
  welcomeWrapper: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
    height: "100%",
  },
});

// ---------------------------------------------------------------------------
// ConversationPane
// ---------------------------------------------------------------------------

/**
 * ConversationPane — left slot of ThreePaneLayout for the SpaarkeAi Code Page (R2).
 *
 * Renders a tab bar with "Chat" and "History" tabs, delegating to WelcomePanel
 * or SprkChat (chat tab) and ChatHistoryPanel (history tab). All session and
 * streaming state is consumed from useAiSession() — this component contains
 * no auth or SSE logic of its own.
 *
 * Welcome → ActiveChat transition:
 *   1. User clicks a prompt button → pendingMessage is set → SprkChat mounts
 *      with predefinedPrompts → toLoading() advances the shell stage.
 *   2. SprkChat sends the first message → onSessionCreated fires → chatSessionId
 *      becomes non-null → WelcomePanel is permanently hidden.
 *   3. User resumes a session from WelcomePanel → handleResumeSession calls
 *      setChatSessionId → chatSessionId becomes non-null → SprkChat loads history.
 */
export function ConversationPane(): React.JSX.Element {
  const styles = useStyles();

  // ── R2 session state — from AiSessionProvider ──────────────────────────
  const {
    token,
    isAuthenticated,
    bffBaseUrl,
    chatSessionId,
    setChatSessionId,
    playbookId,
    setPlaybookId,
    entityContext,
    streaming,
  } = useAiSession();

  // ── Shell stage transitions ─────────────────────────────────────────────
  const { toLoading } = useShellStage();

  // ── Tab state ───────────────────────────────────────────────────────────
  const [activeView, setActiveView] = React.useState<LeftPaneView>("chat");

  // ── Welcome → Chat transition state ────────────────────────────────────
  //
  // pendingMessage: set when the user clicks a prompt button in WelcomePanel.
  // Triggers the switch from WelcomePanel to SprkChat with the message pre-set
  // as a predefined prompt. Cleared once onSessionCreated fires.
  const [pendingMessage, setPendingMessage] = React.useState<string | null>(null);

  // ── SprkChat callbacks ──────────────────────────────────────────────────

  /**
   * onSessionCreated — fires when SprkChat creates a new chat session.
   *
   * Persists the session ID to AiSessionProvider (and sessionStorage).
   * Clears pendingMessage since the welcome flow is now complete.
   */
  const handleSessionCreated = React.useCallback(
    (session: IChatSession) => {
      if (session?.sessionId) {
        setChatSessionId(session.sessionId);
        setPendingMessage(null);
      }
    },
    [setChatSessionId]
  );

  /**
   * onPlaybookChange — fires when the user switches playbooks in SprkChat.
   *
   * Persists the new playbook ID to AiSessionProvider (and sessionStorage).
   */
  const handlePlaybookChange = React.useCallback(
    (newPlaybookId: string) => {
      setPlaybookId(newPlaybookId);
    },
    [setPlaybookId]
  );

  // ── WelcomePanel callbacks ──────────────────────────────────────────────

  /**
   * handlePromptSelected — called when the user clicks a prompt button in WelcomePanel.
   *
   * Sets pendingMessage to trigger the WelcomePanel → SprkChat transition,
   * and advances the shell stage to 'loading' so the workspace pane begins
   * its loading state in parallel.
   */
  const handlePromptSelected = React.useCallback(
    (message: string) => {
      setPendingMessage(message);
      toLoading();
    },
    [toLoading]
  );

  /**
   * handleResumeSession — called when the user clicks a recent session card.
   *
   * Directly sets chatSessionId, which collapses the WelcomePanel and
   * causes SprkChat to mount with sessionId set (resume existing session).
   */
  const handleResumeSession = React.useCallback(
    (sessionId: string) => {
      setChatSessionId(sessionId);
    },
    [setChatSessionId]
  );

  // ── Auth loading guard ──────────────────────────────────────────────────
  //
  // Show a loading spinner while auth is resolving. This mirrors R1 ChatPanel.tsx
  // behaviour (spinner with "Initializing AI Chat..." label).
  if (!isAuthenticated || !token) {
    return (
      <div className={styles.root}>
        <div className={styles.loadingContainer}>
          <Spinner size="medium" label="Initializing AI Chat..." labelPosition="below" />
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            Connecting to Dataverse...
          </Text>
        </div>
      </div>
    );
  }

  // ── Welcome vs SprkChat decision ────────────────────────────────────────
  //
  // Show WelcomePanel when ALL of the following are true:
  //   1. No active chat session (chatSessionId is null)
  //   2. No entity context (entityless / no-context launch from main nav)
  //   3. No pending message selected from WelcomePanel
  //
  // This is the same three-condition gate as R1 ChatPanel.tsx.
  const showWelcomePanel =
    chatSessionId === null && entityContext === null && pendingMessage === null;

  // Build predefinedPrompts for SprkChat from pendingMessage (welcome flow).
  // SprkChat shows these as clickable suggestion chips when the message list is empty.
  const predefinedPrompts = pendingMessage
    ? [{ key: "welcome-prompt", label: pendingMessage, prompt: pendingMessage }]
    : undefined;

  // Build SprkChat hostContext from entityContext (same mapping as R1 ChatPanel.tsx).
  const hostContext = entityContext
    ? {
        entityType: entityContext.entityType as string,
        entityId: entityContext.entityId,
        workspaceType: "spaarke-ai",
      }
    : undefined;

  // ── Render ──────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Tab bar — Chat / History toggle (mirrors R1 LeftPane.tsx) ───── */}
      <div className={styles.tabBar} role="tablist" aria-label="AI Chat navigation">
        <Button
          appearance="subtle"
          role="tab"
          aria-selected={activeView === "chat"}
          icon={<ChatRegular />}
          className={
            activeView === "chat"
              ? `${styles.tabButton} ${styles.tabButtonActive}`
              : styles.tabButton
          }
          onClick={() => setActiveView("chat")}
          size="small"
        >
          <Text
            size={200}
            weight={activeView === "chat" ? "semibold" : "regular"}
          >
            Chat
          </Text>
        </Button>

        <Button
          appearance="subtle"
          role="tab"
          aria-selected={activeView === "history"}
          icon={<HistoryRegular />}
          className={
            activeView === "history"
              ? `${styles.tabButton} ${styles.tabButtonActive}`
              : styles.tabButton
          }
          onClick={() => setActiveView("history")}
          size="small"
        >
          <Text
            size={200}
            weight={activeView === "history" ? "semibold" : "regular"}
          >
            History
          </Text>
        </Button>
      </div>

      {/* ── Active panel content ─────────────────────────────────────────── */}
      <div
        className={styles.content}
        role="tabpanel"
        aria-label={activeView === "chat" ? "AI Chat" : "Chat History"}
      >
        {activeView === "history" ? (
          // History tab — ChatHistoryPanel is now wired to useAiSession
          // via its own internal context consumption (R2 updated version).
          <ChatHistoryPanel />
        ) : showWelcomePanel ? (
          // Chat tab — Welcome state: no session, no entity, no pending message
          <div className={styles.welcomeWrapper}>
            <WelcomePanel
              onPromptSelected={handlePromptSelected}
              onResumeSession={handleResumeSession}
              bffBaseUrl={bffBaseUrl}
              token={token}
              isAuthenticated={isAuthenticated}
            />
          </div>
        ) : (
          // Chat tab — Active state: session exists, entity context present, or prompt selected
          //
          // onPaneEvent is wired to streaming.onPaneEvent from AiSessionProvider.
          // This is the R1 → R2 migration point:
          //   R1: streaming?.onPaneEvent (single-subscriber ref in StandaloneAiProvider)
          //   R2: streaming.onPaneEvent (routes to PaneEventBus channels — multi-subscriber)
          //
          // All other SprkChat props are mapped identically to R1 ChatPanel.tsx.
          <div className={styles.chatWrapper}>
            <SprkChat
              apiBaseUrl={bffBaseUrl}
              accessToken={token}
              sessionId={chatSessionId ?? undefined}
              playbookId={playbookId}
              onSessionCreated={handleSessionCreated}
              onPlaybookChange={handlePlaybookChange}
              predefinedPrompts={predefinedPrompts}
              hostContext={hostContext}
              onPaneEvent={streaming.onPaneEvent ?? null}
            />
          </div>
        )}
      </div>
    </div>
  );
}
