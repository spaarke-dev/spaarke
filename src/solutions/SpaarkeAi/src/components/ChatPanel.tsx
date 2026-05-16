/**
 * ChatPanel.tsx — Left pane chat component for the SpaarkeAi Code Page.
 *
 * Renders SprkChat from @spaarke/ui-components, wired to the StandaloneAiContext
 * via useStandaloneAi(). SprkChat drives the chat interaction and connects to the
 * BFF streaming endpoint for AI responses.
 *
 * Auth is consumed from context (token, bffBaseUrl) — no prop drilling from App.
 * Session lifecycle is managed by StandaloneAiProvider (chatSessionId, playbookId).
 *
 * @see ADR-021 — Fluent v9, dark mode via FluentProvider (no hardcoded colors)
 * @see ADR-022 — React 19 Code Pages (hooks, functional components)
 * @see StandaloneAiContext — context that provides all chat state
 */

import * as React from "react";
import { makeStyles, tokens, Spinner, Text } from "@fluentui/react-components";
import { SprkChat } from "@spaarke/ui-components";
import { useStandaloneAi } from "@spaarke/ai-context";
import type { IChatSession } from "@spaarke/ai-context";

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
});

// ---------------------------------------------------------------------------
// ChatPanel
// ---------------------------------------------------------------------------

/**
 * ChatPanel renders SprkChat wired to StandaloneAiContext.
 *
 * Key wiring:
 * - apiBaseUrl + accessToken: from useStandaloneAi() context
 * - sessionId / playbookId: from context (sessionStorage-persisted)
 * - onSessionCreated: updates chatSessionId in context
 * - onPlaybookChange: updates playbookId in context
 *
 * When isAuthenticated is false (auth still in progress), renders a
 * loading spinner rather than SprkChat to avoid unauthenticated API calls.
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
  } = useStandaloneAi();

  // Handle session creation — persist new sessionId to context + sessionStorage
  const handleSessionCreated = React.useCallback(
    (session: IChatSession) => {
      if (session?.sessionId) {
        setChatSessionId(session.sessionId);
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

  // Show spinner while auth is in progress
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
          hostContext={
            entityContext
              ? {
                  entityType: entityContext.entityType as string,
                  entityId: entityContext.entityId,
                  workspaceType: "spaarke-ai",
                }
              : undefined
          }
        />
      </div>
    </div>
  );
}
