/**
 * WelcomePanel.tsx — Welcome experience for Spaarke AI (no-context launch)
 *
 * **R3 Moment 1: Arrival redesign (FR-04, FR-05)**: Chrome trimmed to the
 * essential. The previous sparkle icon, branded welcome heading, and 2x2
 * prompt card grid have been removed. The welcome state now shows only:
 *   1. The central prompt "How can I help you today?" (Fluent v9 size 400, no icon)
 *   2. The "Recent Conversations" section (unchanged) — last 5 sessions
 *
 * The cold-load chat input is editable directly (task 023 / FR-06), so the
 * scaffold prompt cards are no longer needed for discoverability. The recent
 * session click-to-resume contract is preserved.
 *
 * Shown in the left pane when Spaarke AI opens from main navigation with no
 * entity context and no active chat session. When a recent session card is
 * clicked, onResumeSession is called with the session ID — ChatPanel resumes
 * that session via setChatSessionId().
 *
 * Design constraints:
 * - ADR-012: No new shared components introduced here (in-solution component).
 * - ADR-021: Fluent v9 semantic tokens only — no hardcoded colors / no rgba literals.
 * - ADR-021: Dark mode must work without additional CSS (tokens adapt automatically).
 * - ADR-028: Auth surface comes via useAiSession() — no token snapshots in props/state.
 *
 * @see ADR-021 — Fluent v9 design system, dark mode, semantic tokens
 * @see ConversationPane.tsx — renders this component when no session and no entity context
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Spinner,
} from "@fluentui/react-components";
import {
  HistoryRegular,
  ArrowRightRegular,
} from "@fluentui/react-icons";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";
import { useAiSession } from "@spaarke/ai-widgets";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A recent chat session shown in the "Recent Conversations" section */
export interface RecentSession {
  id: string;
  title: string;
  entityType?: string;
  entityName?: string;
  updatedAt: string;
}

/** Props for WelcomePanel */
export interface WelcomePanelProps {
  /**
   * Legacy prop retained for parent-component contract compatibility (R3 task 020
   * trimmed the prompt cards — this callback is no longer invoked from within
   * WelcomePanel). Parent components MAY pass a no-op; the prop will be removed
   * once ConversationPane is updated in task 021.
   */
  onPromptSelected?: (message: string) => void;
  /** Called when the user clicks a recent session card — provides the session ID to resume */
  onResumeSession: (sessionId: string) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflowY: "auto",
    overflowX: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: "border-box",
  },

  // ── Central prompt (Moment 1: Arrival — FR-04) ───────────────────────────
  // R3: Trimmed to a single centered prompt at Fluent v9 size 400, no icon,
  // no surrounding heading. The cold-load chat input handles discoverability.
  header: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
  },
  headerSubtitle: {
    color: tokens.colorNeutralForeground1,
    textAlign: "center",
  },

  // ── Recent sessions ───────────────────────────────────────────────────────
  recentSection: {
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalXXL,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  recentHeader: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
    paddingBottom: tokens.spacingVerticalXXS,
  },
  recentHeaderIcon: {
    fontSize: "14px",
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  recentSessionCard: {
    cursor: "pointer",
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
      borderColor: tokens.colorNeutralStroke1,
    },
    ":active": {
      backgroundColor: tokens.colorNeutralBackground2Pressed,
    },
  },
  recentSessionInfo: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    flex: 1,
    minWidth: 0,
  },
  recentSessionTitle: {
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  recentSessionMeta: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexWrap: "wrap",
  },
  recentSessionTimestamp: {
    color: tokens.colorNeutralForeground3,
  },
  recentArrow: {
    fontSize: "14px",
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  loadingContainer: {
    display: "flex",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalM,
  },
  emptyRecentText: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    paddingTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Recent session data hook (local — mirrors ChatHistoryPanel.tsx pattern)
// ---------------------------------------------------------------------------

interface UseRecentSessionsResult {
  sessions: RecentSession[];
  isLoading: boolean;
}

function useRecentSessions(
  bffBaseUrl: string,
  authenticatedFetch: AuthenticatedFetchFn,
  isAuthenticated: boolean
): UseRecentSessionsResult {
  const [sessions, setSessions] = React.useState<RecentSession[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);

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
        const url = buildBffApiUrl(bffBaseUrl, "/ai/chat/sessions?limit=5");
        const response = await authenticatedFetch(url, {
          headers: {
            "Content-Type": "application/json",
          },
        });

        if (!response.ok) {
          console.warn(`[WelcomePanel] Sessions fetch returned ${response.status}`);
          if (!cancelled) setSessions([]);
          return;
        }

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const data = (await response.json()) as any[];

        const mapped: RecentSession[] = Array.isArray(data)
          ? data.slice(0, 5).map((item) => ({
              id: String(item.id ?? item.sessionId ?? ""),
              title: String(item.title ?? item.playbookName ?? "Untitled Conversation"),
              entityType: item.entityType ? String(item.entityType) : undefined,
              entityName: item.entityName ? String(item.entityName) : undefined,
              updatedAt: String(item.updatedAt ?? item.lastMessageAt ?? new Date().toISOString()),
            }))
          : [];

        if (!cancelled) {
          setSessions(mapped);
        }
      } catch (err) {
        if (!cancelled) {
          console.warn("[WelcomePanel] Failed to fetch recent sessions:", err);
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
    // does not need to be a dep — including it would re-fire on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bffBaseUrl, isAuthenticated]);

  return { sessions, isLoading };
}

// ---------------------------------------------------------------------------
// Utility: format relative timestamp
// ---------------------------------------------------------------------------

function formatRelativeTime(isoString: string): string {
  try {
    const date = new Date(isoString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMinutes = Math.floor(diffMs / 60000);

    if (diffMinutes < 1) return "just now";
    if (diffMinutes < 60) return `${diffMinutes}m ago`;

    const diffHours = Math.floor(diffMinutes / 60);
    if (diffHours < 24) return `${diffHours}h ago`;

    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays}d ago`;

    // Older than a week — show date
    return date.toLocaleDateString(undefined, { month: "short", day: "numeric" });
  } catch {
    return "";
  }
}

// ---------------------------------------------------------------------------
// Utility: friendly entity type display name
// ---------------------------------------------------------------------------

function formatEntityTypeBadge(entityType: string): string {
  // Map known entity logical names to friendly labels
  const knownTypes: Record<string, string> = {
    sprk_matter: "Matter",
    sprk_project: "Project",
    sprk_document: "Document",
    sprk_analysisoutput: "Analysis",
    account: "Company",
    contact: "Contact",
    incident: "Case",
  };
  return knownTypes[entityType.toLowerCase()] ?? entityType;
}

// ---------------------------------------------------------------------------
// WelcomePanel
// ---------------------------------------------------------------------------

/**
 * WelcomePanel — shown when Spaarke AI opens with no entity context and no session.
 *
 * R3 (FR-04, FR-05): Displays only the central prompt "How can I help you today?"
 * at Fluent v9 size 400 (no leading icon, no heading), followed by the "Recent
 * Conversations" section with the user's last 5 sessions.
 *
 * Recent session cards call onResumeSession with the session ID.
 *
 * This component disappears once ChatPanel detects chatSessionId !== null
 * (i.e., once the user starts a session or resumes one).
 */
export function WelcomePanel({
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  onPromptSelected: _onPromptSelected,
  onResumeSession,
}: WelcomePanelProps): React.JSX.Element {
  const styles = useStyles();

  // Auth surface comes from AiSessionProvider via useAiSession() — no token
  // is ever materialised in props or React state (Spaarke Auth v2 §H-4).
  const { bffBaseUrl, authenticatedFetch, isAuthenticated } = useAiSession();

  const { sessions, isLoading } = useRecentSessions(
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated
  );

  return (
    <div className={styles.root} role="region" aria-label="Spaarke AI welcome">
      {/* ── Central prompt (FR-04) ────────────────────────────────────────── */}
      <div className={styles.header}>
        <Text as="h2" size={400} weight="semibold" className={styles.headerSubtitle}>
          How can I help you today?
        </Text>
      </div>

      {/* ── Recent conversations (FR-05) ──────────────────────────────────── */}
      <RecentConversationsSection
        sessions={sessions}
        isLoading={isLoading}
        onResumeSession={onResumeSession}
        styles={styles}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// RecentConversationsSection — last 3–5 sessions
// ---------------------------------------------------------------------------

interface RecentConversationsSectionProps {
  sessions: RecentSession[];
  isLoading: boolean;
  onResumeSession: (sessionId: string) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  styles: Record<string, any>;
}

function RecentConversationsSection({
  sessions,
  isLoading,
  onResumeSession,
  styles,
}: RecentConversationsSectionProps): React.JSX.Element {
  return (
    <div className={styles.recentSection}>
      {/* Section header */}
      <div className={styles.recentHeader}>
        <HistoryRegular className={styles.recentHeaderIcon} />
        <Text size={200} weight="semibold">
          Recent Conversations
        </Text>
      </div>

      {/* Content */}
      {isLoading ? (
        <div className={styles.loadingContainer}>
          <Spinner size="tiny" label="Loading recent conversations..." labelPosition="after" />
        </div>
      ) : sessions.length === 0 ? (
        <Text size={200} className={styles.emptyRecentText}>
          No recent conversations
        </Text>
      ) : (
        sessions.map((session) => (
          <RecentSessionCard
            key={session.id}
            session={session}
            onResume={onResumeSession}
            styles={styles}
          />
        ))
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// RecentSessionCard — individual session card in the recents section
// ---------------------------------------------------------------------------

interface RecentSessionCardProps {
  session: RecentSession;
  onResume: (sessionId: string) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  styles: Record<string, any>;
}

function RecentSessionCard({
  session,
  onResume,
  styles,
}: RecentSessionCardProps): React.JSX.Element {
  const handleClick = React.useCallback(() => {
    onResume(session.id);
  }, [session.id, onResume]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        onResume(session.id);
      }
    },
    [session.id, onResume]
  );

  const relativeTime = React.useMemo(
    () => formatRelativeTime(session.updatedAt),
    [session.updatedAt]
  );

  const entityBadgeLabel = React.useMemo(
    () => (session.entityType ? formatEntityTypeBadge(session.entityType) : null),
    [session.entityType]
  );

  return (
    <div
      className={styles.recentSessionCard}
      role="button"
      tabIndex={0}
      aria-label={`Resume conversation: ${session.title}`}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
    >
      <div className={styles.recentSessionInfo}>
        <Text size={200} weight="semibold" className={styles.recentSessionTitle}>
          {session.title}
        </Text>
        <div className={styles.recentSessionMeta}>
          {entityBadgeLabel && (
            <Badge size="small" appearance="outline" color="informative">
              {entityBadgeLabel}
            </Badge>
          )}
          <Text size={100} className={styles.recentSessionTimestamp}>
            {relativeTime}
          </Text>
        </div>
      </div>
      <ArrowRightRegular className={styles.recentArrow} aria-hidden="true" />
    </div>
  );
}
