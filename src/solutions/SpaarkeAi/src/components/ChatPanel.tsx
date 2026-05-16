/**
 * ChatPanel.tsx — Left pane chat component for the SpaarkeAi Code Page.
 *
 * Renders either:
 *   a) WelcomePanel — when the user opens Spaarke AI with no entity context
 *      and no active chat session (no-context launch from main nav).
 *   b) SprkChat — when there is an active session OR entity context.
 *
 * Welcome → Chat transition:
 *   - Prompt button click: sets pendingMessage → hides WelcomePanel, shows SprkChat
 *     with predefinedPrompts containing the selected message
 *   - Recent session click: calls setChatSessionId() directly → chatSessionId becomes
 *     non-null → WelcomePanel disappears per the acceptance criteria
 *
 * Auth is consumed from context (token, bffBaseUrl) — no prop drilling from App.
 * Session lifecycle is managed by StandaloneAiProvider (chatSessionId, playbookId).
 *
 * @see WelcomePanel.tsx — welcome experience component (no-context launch)
 * @see ADR-021 — Fluent v9, dark mode via FluentProvider (no hardcoded colors)
 * @see ADR-022 — React 19 Code Pages (hooks, functional components)
 * @see StandaloneAiContext — context that provides all chat state
 */

import * as React from "react";
import { makeStyles, tokens, Spinner, Text } from "@fluentui/react-components";
import { SprkChat } from "@spaarke/ui-components";
import { useStandaloneAi } from "@spaarke/ai-context";
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "./WelcomePanel";

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
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  chatWrapper: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
  },
  welcomeWrapper: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
  },
});

// ---------------------------------------------------------------------------
// ChatPanel
// ---------------------------------------------------------------------------

/**
 * ChatPanel renders either WelcomePanel or SprkChat based on session state.
 *
 * WelcomePanel is shown when:
 *   - User is authenticated
 *   - There is no active chatSessionId (no session in progress or resumed)
 *   - There is no entity context (no entity URL params — no-context launch)
 *   - No pending message has been selected from WelcomePanel
 *
 * SprkChat is shown when:
 *   - User is authenticated AND (chatSessionId is set OR entityContext exists OR pendingMessage is set)
 *
 * When a welcome prompt button is clicked:
 *   1. pendingMessage is set with the configured message text
 *   2. SprkChat mounts with predefinedPrompts containing the message
 *   3. The user sees the prompt suggestion and clicks to send it
 *   4. onSessionCreated fires → chatSessionId is set → WelcomePanel is permanently hidden
 *
 * When a recent session is clicked:
 *   1. setChatSessionId() is called with the session ID
 *   2. chatSessionId becomes non-null → WelcomePanel disappears
 *   3. SprkChat loads with sessionId → resumes the session history
 */
export function ChatPanel(): React.JSX.Element {
  const styles = useStyles();

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
  } = useStandaloneAi();

  // ── Welcome → Chat transition state ───────────────────────────────────────
  //
  // pendingMessage: set when the user clicks a prompt button in WelcomePanel.
  // Triggers the switch from WelcomePanel to SprkChat view. Cleared once the
  // chat session is created (onSessionCreated fires) to avoid stale prompts.
  const [pendingMessage, setPendingMessage] = React.useState<string | null>(null);

  // Handle session creation — persist new sessionId to context + sessionStorage
  // Also clears pendingMessage since the session is now active.
  const handleSessionCreated = React.useCallback(
    (session: IChatSession) => {
      if (session?.sessionId) {
        setChatSessionId(session.sessionId);
        // Clear pending message once session is created (welcome flow complete)
        setPendingMessage(null);
      }
    },
    [setChatSessionId]
  );

  // Handle playbook switch — persist new playbookId to context + sessionStorage
  const handlePlaybookChange = React.useCallback(
    (newPlaybookId: string) => {
      setPlaybookId(newPlaybookId);
    },
    [setPlaybookId]
  );

  // Handle welcome prompt button click — transitions from WelcomePanel to SprkChat
  const handlePromptSelected = React.useCallback((message: string) => {
    setPendingMessage(message);
  }, []);

  // Handle recent session card click — resumes the selected session directly
  const handleResumeSession = React.useCallback(
    (sessionId: string) => {
      setChatSessionId(sessionId);
    },
    [setChatSessionId]
  );

  // ── Show spinner while auth is in progress ────────────────────────────────
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

  // ── Determine which view to show ──────────────────────────────────────────
  //
  // Show WelcomePanel when all of these are true:
  //   1. No active chat session (chatSessionId is null)
  //   2. No entity context (this is a no-context launch from main nav)
  //   3. No pending message selected from WelcomePanel
  //
  // Show SprkChat otherwise (session active, entity context present, or prompt selected).
  const showWelcomePanel =
    chatSessionId === null && entityContext === null && pendingMessage === null;

  if (showWelcomePanel) {
    return (
      <div className={styles.root}>
        <div className={styles.welcomeWrapper}>
          <WelcomePanel
            onPromptSelected={handlePromptSelected}
            onResumeSession={handleResumeSession}
            bffBaseUrl={bffBaseUrl}
            token={token}
            isAuthenticated={isAuthenticated}
          />
        </div>
      </div>
    );
  }

  // ── SprkChat view ─────────────────────────────────────────────────────────
  //
  // Build predefinedPrompts from pendingMessage (if set).
  // SprkChat shows predefinedPrompts as clickable suggestions when the message
  // list is empty, giving the user a one-click send for their chosen workflow.
  const predefinedPrompts = pendingMessage
    ? [{ key: "welcome-prompt", label: pendingMessage, prompt: pendingMessage }]
    : undefined;

  return (
    <div className={styles.root}>
      <div className={styles.chatWrapper}>
        <SprkChat
          apiBaseUrl={bffBaseUrl}
          accessToken={token}
          sessionId={chatSessionId ?? undefined}
          playbookId={playbookId}
          onSessionCreated={handleSessionCreated}
          onPlaybookChange={handlePlaybookChange}
          predefinedPrompts={predefinedPrompts}
          hostContext={
            entityContext
              ? {
                  entityType: entityContext.entityType as string,
                  entityId: entityContext.entityId,
                  workspaceType: "spaarke-ai",
                }
              : undefined
          }
          onPaneEvent={streaming.onPaneEvent}
        />
      </div>
    </div>
  );
}
