/**
 * ChatHistoryPanel.tsx — Chat session history panel for SpaarkeAi.
 *
 * Wraps the ChatHistoryPanel component from @spaarke/ai-outputs, wiring it to
 * AiSessionContext session state. Handles session data fetching from the BFF
 * API and passes sessions as props to the presentational panel component.
 *
 * Data flow:
 *   useAiSession() → bffBaseUrl + authenticatedFetch + isAuthenticated
 *     → BFF /ai/chat/sessions list
 *     → ChatHistoryPanel (from @spaarke/ai-outputs) — purely presentational
 *
 * R2 migration: switched from useStandaloneAi() (R1) to useAiSession() (R2)
 * so this component works inside the AiSessionProvider tree in ConversationPane.
 *
 * Auth v2 migration (task 022): the panel no longer destructures `token` from
 * useAiSession(); instead it uses `authenticatedFetch` (preferred) so the Bearer
 * token never crosses a component boundary. See AUDIT-FINDINGS-AUTH-SYSTEM §H-4.
 *
 * Resume behavior: clicking "Resume" on a session card calls setChatSessionId()
 * in context, which updates the chatSessionId in AiSessionProvider + sessionStorage,
 * causing SprkChat (in ConversationPane) to resume that session on next render.
 *
 * Collapsibility: this panel is toggled by the left pane collapse mechanism in
 * ThreePaneLayout. When the left pane is collapsed, this component unmounts.
 * The ThreePaneLayout renders a collapsed strip indicator instead.
 *
 * @see ADR-021 — Fluent v9 semantic tokens only (no hardcoded colors)
 * @see ADR-022 — React 19, functional components
 * @see ChatHistoryPanel from @spaarke/ai-outputs — presentational component
 * @see AUDIT-FINDINGS-AUTH-SYSTEM §H-4 — function-based auth contract
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { ChatHistoryPanel as LibChatHistoryPanel } from "@spaarke/ai-outputs";
import { useAiSession } from "@spaarke/ai-widgets";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";
import type { ChatSession } from "@spaarke/ai-outputs";

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
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

// ---------------------------------------------------------------------------
// Session fetch hook (local to this module — avoids a new shared hook file)
// ---------------------------------------------------------------------------

interface UseSessionHistoryResult {
  sessions: ChatSession[];
  isLoading: boolean;
  reload: () => void;
}

function useSessionHistory(
  bffBaseUrl: string,
  authenticatedFetch: AuthenticatedFetchFn,
  isAuthenticated: boolean
): UseSessionHistoryResult {
  const [sessions, setSessions] = React.useState<ChatSession[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);
  const [reloadKey, setReloadKey] = React.useState<number>(0);

  React.useEffect(() => {
    if (!isAuthenticated || !bffBaseUrl) {
      setSessions([]);
      return;
    }

    let cancelled = false;
    setIsLoading(true);

    const fetchSessions = async (): Promise<void> => {
      try {
        // All BFF URL construction MUST use buildBffApiUrl() per auth.md constraint.
        // authenticatedFetch attaches Bearer header automatically — the token never
        // crosses a component boundary (Spaarke Auth v2 §H-4).
        const url = buildBffApiUrl(bffBaseUrl, "/ai/chat/sessions");
        const response = await authenticatedFetch(url, {
          headers: {
            "Content-Type": "application/json",
          },
        });

        if (!response.ok) {
          console.warn(`[ChatHistoryPanel] Sessions fetch returned ${response.status}`);
          if (!cancelled) setSessions([]);
          return;
        }

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const data = (await response.json()) as any[];

        // Map BFF response to ChatSession shape expected by @spaarke/ai-outputs
        const mapped: ChatSession[] = Array.isArray(data)
          ? data.map((item) => ({
              id: String(item.id ?? item.sessionId ?? ""),
              title: String(item.title ?? item.playbookName ?? "Untitled Conversation"),
              lastMessagePreview: item.lastMessagePreview
                ? String(item.lastMessagePreview)
                : undefined,
              updatedAt: String(item.updatedAt ?? item.lastMessageAt ?? new Date().toISOString()),
              entityType: item.entityType ? String(item.entityType) : undefined,
              entityName: item.entityName ? String(item.entityName) : undefined,
              entityId: item.entityId ? String(item.entityId) : undefined,
            }))
          : [];

        if (!cancelled) {
          setSessions(mapped);
        }
      } catch (err) {
        if (!cancelled) {
          console.warn("[ChatHistoryPanel] Failed to fetch sessions:", err);
          setSessions([]);
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    void fetchSessions();

    return () => {
      cancelled = true;
    };
    // authenticatedFetch is a stable module-level function in @spaarke/auth and
    // does not need to be a dep — including it would re-fire on every render
    // because useAiSession() returns a new object each call.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bffBaseUrl, isAuthenticated, reloadKey]);

  const reload = React.useCallback(() => {
    setReloadKey((k) => k + 1);
  }, []);

  return { sessions, isLoading, reload };
}

// ---------------------------------------------------------------------------
// ChatHistoryPanel
// ---------------------------------------------------------------------------

/**
 * ChatHistoryPanel — collapsible session history for SpaarkeAi.
 *
 * Fetches prior chat sessions from the BFF API and renders them using the
 * shared ChatHistoryPanel component from @spaarke/ai-outputs. The panel is
 * collapsible via ThreePaneLayout's left pane toggle mechanism.
 *
 * Resuming a session updates chatSessionId in StandaloneAiContext, which
 * causes SprkChat (rendered in ChatPanel) to resume the selected session.
 */
export function ChatHistoryPanel(): React.JSX.Element {
  const styles = useStyles();

  const { bffBaseUrl, authenticatedFetch, isAuthenticated, setChatSessionId } = useAiSession();

  const { sessions, isLoading, reload } = useSessionHistory(
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated
  );

  const handleResume = React.useCallback(
    (sessionId: string) => {
      setChatSessionId(sessionId);
      // The context update propagates to ConversationPane → SprkChat via re-render
      console.info("[ChatHistoryPanel] Resuming session:", sessionId);
    },
    [setChatSessionId]
  );

  const handleDelete = React.useCallback(
    async (sessionId: string) => {
      if (!isAuthenticated || !bffBaseUrl) return;

      try {
        // authenticatedFetch attaches Bearer header automatically (Spaarke Auth v2).
        const url = buildBffApiUrl(bffBaseUrl, `/ai/chat/sessions/${sessionId}`);
        await authenticatedFetch(url, { method: "DELETE" });
        reload();
      } catch (err) {
        console.warn("[ChatHistoryPanel] Delete session failed:", err);
      }
    },
    [bffBaseUrl, authenticatedFetch, isAuthenticated, reload]
  );

  return (
    <div className={styles.root}>
      <LibChatHistoryPanel
        sessions={sessions}
        isLoading={isLoading}
        onResume={handleResume}
        onDelete={handleDelete}
      />
    </div>
  );
}
