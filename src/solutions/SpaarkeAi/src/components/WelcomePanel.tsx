/**
 * WelcomePanel.tsx — Welcome experience for Spaarke AI (no-context launch)
 *
 * Shown in the left pane when Spaarke AI opens from main navigation with no
 * entity context and no active chat session. Provides:
 *   1. Branded header: "Welcome to Spaarke AI" with sparkle icon
 *   2. Subtext: "How can I help you today?"
 *   3. 2×2 grid of guided prompt buttons (Fluent v9 Card components)
 *   4. "Recent Conversations" section showing last 5 sessions
 *
 * When a prompt button is clicked, onPromptSelected is called with the
 * injected message text. ChatPanel uses this to transition to SprkChat
 * with the message pre-populated as a predefined prompt.
 *
 * When a recent session card is clicked, onResumeSession is called with
 * the session ID — ChatPanel resumes that session via setChatSessionId().
 *
 * Design constraints:
 * - ADR-021: Fluent v9 semantic tokens only — no hardcoded colors
 * - ADR-021: Dark mode must work without additional CSS (tokens adapt automatically)
 * - Responsive grid: 2×2 on desktop, single-column on narrow panes
 *
 * @see ADR-021 — Fluent v9 design system, dark mode, semantic tokens
 * @see ChatPanel.tsx — renders this component when no session and no entity context
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Card,
  CardHeader,
  Badge,
  Spinner,
} from "@fluentui/react-components";
import {
  SparkleRegular,
  DocumentTextRegular,
  SearchRegular,
  CalculatorRegular,
  FolderSearchRegular,
  HistoryRegular,
  ArrowRightRegular,
} from "@fluentui/react-icons";
import { buildBffApiUrl } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A single guided prompt button configuration */
export interface PromptButtonConfig {
  /** Unique key */
  key: string;
  /** Display label shown on the card */
  label: string;
  /** Short description shown below the label */
  description: string;
  /** Icon element rendered in the card */
  icon: React.ReactElement;
  /** Message text injected into SprkChat when clicked */
  message: string;
}

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
  /** Called when the user clicks a prompt button — provides the injected message text */
  onPromptSelected: (message: string) => void;
  /** Called when the user clicks a recent session card — provides the session ID to resume */
  onResumeSession: (sessionId: string) => void;
  /** BFF API base URL (for fetching recent sessions) */
  bffBaseUrl: string;
  /** Bearer token for BFF API auth (null when not authenticated yet) */
  token: string | null;
  /** Whether the user is authenticated */
  isAuthenticated: boolean;
}

// ---------------------------------------------------------------------------
// Default prompt button configuration (configurable array — ADR constraint)
// ---------------------------------------------------------------------------

const DEFAULT_PROMPT_BUTTONS: PromptButtonConfig[] = [
  {
    key: "analyze-document",
    label: "Analyze a Document",
    description: "Review, summarize, or extract key information from a document",
    icon: <DocumentTextRegular />,
    message: "I want to analyze a document",
  },
  {
    key: "research-topic",
    label: "Research a Topic",
    description: "Research legal topics, case law, or regulations",
    icon: <SearchRegular />,
    message: "I want to research a legal topic",
  },
  {
    key: "financial-analysis",
    label: "Financial Analysis",
    description: "Analyze financial statements, budgets, or transaction data",
    icon: <CalculatorRegular />,
    message: "I want to analyze financial data",
  },
  {
    key: "find-documents",
    label: "Find Documents",
    description: "Search for documents across your matters and projects",
    icon: <FolderSearchRegular />,
    message: "Search for documents in my matters",
  },
];

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

  // ── Header ────────────────────────────────────────────────────────────────
  header: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    gap: tokens.spacingVerticalXS,
  },
  headerIconWrapper: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "48px",
    height: "48px",
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    marginBottom: tokens.spacingVerticalS,
    flexShrink: 0,
  },
  headerIcon: {
    fontSize: "24px",
    color: tokens.colorBrandForeground1,
  },
  headerTitle: {
    color: tokens.colorNeutralForeground1,
    textAlign: "center",
  },
  headerSubtitle: {
    color: tokens.colorNeutralForeground2,
    textAlign: "center",
    marginTop: tokens.spacingVerticalXXS,
  },

  // ── Prompt button grid ────────────────────────────────────────────────────
  promptSection: {
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalL,
  },
  promptGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: tokens.spacingVerticalS,
    // Collapse to single column when pane is narrow (< 280px content width)
    "@media (max-width: 280px)": {
      gridTemplateColumns: "1fr",
    },
  },
  promptCard: {
    cursor: "pointer",
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    transition: "background-color 0.1s ease, border-color 0.1s ease",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    // Hover state — handled via CSS class composition
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
      borderColor: tokens.colorNeutralStroke1,
    },
    ":active": {
      backgroundColor: tokens.colorNeutralBackground2Pressed,
    },
    minHeight: "80px",
  },
  promptCardIcon: {
    fontSize: "20px",
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  promptCardLabel: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  promptCardDescription: {
    color: tokens.colorNeutralForeground3,
    lineHeight: tokens.lineHeightBase200,
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
  token: string | null
): UseRecentSessionsResult {
  const [sessions, setSessions] = React.useState<RecentSession[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);

  React.useEffect(() => {
    if (!token || !bffBaseUrl) {
      setSessions([]);
      return;
    }

    let cancelled = false;
    setIsLoading(true);

    const fetchSessions = async (): Promise<void> => {
      try {
        // All BFF URL construction MUST use buildBffApiUrl() per auth.md constraint
        const url = buildBffApiUrl(bffBaseUrl, "/ai/chat/sessions?limit=5");
        const response = await fetch(url, {
          headers: {
            Authorization: `Bearer ${token}`,
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
  }, [bffBaseUrl, token]);

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
 * Displays a branded welcome header, a 2×2 grid of guided prompt buttons,
 * and a "Recent Conversations" section with the user's last 5 sessions.
 *
 * Prompt buttons call onPromptSelected with the configured message text.
 * Recent session cards call onResumeSession with the session ID.
 *
 * This component disappears once ChatPanel detects chatSessionId !== null
 * (i.e., once the user starts a session or resumes one).
 */
export function WelcomePanel({
  onPromptSelected,
  onResumeSession,
  bffBaseUrl,
  token,
  isAuthenticated,
}: WelcomePanelProps): React.JSX.Element {
  const styles = useStyles();

  const { sessions, isLoading } = useRecentSessions(
    bffBaseUrl,
    isAuthenticated ? token : null
  );

  return (
    <div className={styles.root} role="region" aria-label="Welcome to Spaarke AI">
      {/* ── Branded header ────────────────────────────────────────────────── */}
      <div className={styles.header}>
        <div className={styles.headerIconWrapper} aria-hidden="true">
          <SparkleRegular className={styles.headerIcon} />
        </div>
        <Text
          as="h2"
          size={500}
          weight="semibold"
          className={styles.headerTitle}
        >
          Welcome to Spaarke AI
        </Text>
        <Text size={300} className={styles.headerSubtitle}>
          How can I help you today?
        </Text>
      </div>

      {/* ── Guided prompt buttons grid ────────────────────────────────────── */}
      <div className={styles.promptSection}>
        <div className={styles.promptGrid} role="list" aria-label="Suggested actions">
          {DEFAULT_PROMPT_BUTTONS.map((btn) => (
            <PromptButton
              key={btn.key}
              config={btn}
              onSelect={onPromptSelected}
              styles={styles}
            />
          ))}
        </div>
      </div>

      {/* ── Recent conversations ──────────────────────────────────────────── */}
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
// PromptButton — a single guided action card
// ---------------------------------------------------------------------------

interface PromptButtonProps {
  config: PromptButtonConfig;
  onSelect: (message: string) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  styles: Record<string, any>;
}

function PromptButton({ config, onSelect, styles }: PromptButtonProps): React.JSX.Element {
  const handleClick = React.useCallback(() => {
    onSelect(config.message);
  }, [config.message, onSelect]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        onSelect(config.message);
      }
    },
    [config.message, onSelect]
  );

  return (
    <div
      className={styles.promptCard}
      role="listitem"
      tabIndex={0}
      aria-label={`${config.label}: ${config.description}`}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
    >
      <div className={styles.promptCardIcon} aria-hidden="true">
        {config.icon}
      </div>
      <Text size={200} weight="semibold" className={styles.promptCardLabel}>
        {config.label}
      </Text>
      <Text size={100} className={styles.promptCardDescription}>
        {config.description}
      </Text>
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
