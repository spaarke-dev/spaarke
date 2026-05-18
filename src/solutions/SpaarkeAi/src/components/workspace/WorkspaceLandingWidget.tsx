/**
 * WorkspaceLandingWidget.tsx — Stage 1 landing content for the Workspace center pane.
 *
 * Shown when the shell is in Stage 1 (welcome) — no session, no playbook selected.
 * Displays:
 *   - "What would you like to work on?" heading
 *   - Recent work cards (reusing the R1 WelcomePanel session data API)
 *   - "Start new work" CTA button
 *
 * Clicking a recent work card triggers session restore (AIPU2-106) by dispatching
 * a session_restore event on the workspace bus channel.
 *
 * This component extracts the "recent sessions" functionality from WelcomePanel.tsx
 * and surfaces it in the center pane as part of the R2 three-pane Stage 1 redesign.
 *
 * @see WelcomePanel.tsx — R1 component; recent sessions API reused here
 * @see AIPU2-107 — welcome panel redesign task
 * @see ADR-021 — Fluent v9 tokens only, dark mode, no hardcoded colors
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Card,
  CardHeader,
  Badge,
  Spinner,
} from "@fluentui/react-components";
import {
  AppsListRegular,
  AddRegular,
  ArrowRightRegular,
  HistoryRegular,
  SparkleRegular,
} from "@fluentui/react-icons";
import { buildBffApiUrl } from "@spaarke/auth";
import { useAiSession, useDispatchPaneEvent } from "@spaarke/ai-widgets";

// ---------------------------------------------------------------------------
// Recent session type (mirrors WelcomePanel.tsx RecentSession)
// ---------------------------------------------------------------------------

interface RecentWorkItem {
  id: string;
  title: string;
  entityType?: string;
  entityName?: string;
  playbookName?: string;
  updatedAt: string;
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
    backgroundColor: tokens.colorNeutralBackground2,
  },

  header: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    gap: tokens.spacingVerticalS,
    textAlign: "center",
  },

  headerIcon: {
    fontSize: "40px",
    color: tokens.colorBrandForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },

  ctaButton: {
    marginTop: tokens.spacingVerticalS,
  },

  recentSection: {
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
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

  workCard: {
    cursor: "pointer",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      borderColor: tokens.colorNeutralStroke1,
    },
    ":active": {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },

  workCardInfo: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    flex: 1,
    minWidth: 0,
  },

  workCardTitle: {
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },

  workCardMeta: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexWrap: "wrap",
  },

  workCardTimestamp: {
    color: tokens.colorNeutralForeground3,
  },

  workCardArrow: {
    fontSize: "14px",
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },

  loadingContainer: {
    display: "flex",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalM,
  },

  emptyText: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    paddingTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Hook: fetch recent sessions (same API as WelcomePanel)
// ---------------------------------------------------------------------------

function useRecentWork(
  bffBaseUrl: string,
  token: string | null
): { items: RecentWorkItem[]; isLoading: boolean } {
  const [items, setItems] = React.useState<RecentWorkItem[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);

  React.useEffect(() => {
    if (!token || !bffBaseUrl) return;
    let cancelled = false;
    setIsLoading(true);

    const fetchRecent = async (): Promise<void> => {
      try {
        const url = buildBffApiUrl(bffBaseUrl, "/ai/chat/sessions?limit=5");
        const response = await fetch(url, {
          headers: {
            Authorization: `Bearer ${token}`,
            Accept: "application/json",
          },
        });

        if (!response.ok || cancelled) {
          if (!cancelled) setItems([]);
          return;
        }

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const data = (await response.json()) as any[];
        const mapped: RecentWorkItem[] = Array.isArray(data)
          ? data.slice(0, 5).map((item) => ({
              id: String(item.id ?? item.sessionId ?? ""),
              title: String(item.title ?? item.playbookName ?? "Untitled Conversation"),
              entityType: item.entityType ? String(item.entityType) : undefined,
              entityName: item.entityName ? String(item.entityName) : undefined,
              playbookName: item.playbookName ? String(item.playbookName) : undefined,
              updatedAt: String(item.updatedAt ?? item.lastMessageAt ?? new Date().toISOString()),
            }))
          : [];

        if (!cancelled) setItems(mapped);
      } catch {
        if (!cancelled) setItems([]);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };

    void fetchRecent();
    return () => { cancelled = true; };
  }, [bffBaseUrl, token]);

  return { items, isLoading };
}

// ---------------------------------------------------------------------------
// Utility: relative time formatting (reused from WelcomePanel)
// ---------------------------------------------------------------------------

function formatRelativeTime(isoString: string): string {
  try {
    const date = new Date(isoString);
    const diffMs = Date.now() - date.getTime();
    const diffMinutes = Math.floor(diffMs / 60000);
    if (diffMinutes < 1) return "just now";
    if (diffMinutes < 60) return `${diffMinutes}m ago`;
    const diffHours = Math.floor(diffMinutes / 60);
    if (diffHours < 24) return `${diffHours}h ago`;
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays}d ago`;
    return date.toLocaleDateString(undefined, { month: "short", day: "numeric" });
  } catch {
    return "";
  }
}

function formatEntityType(entityType: string): string {
  const map: Record<string, string> = {
    sprk_matter: "Matter",
    sprk_project: "Project",
    sprk_document: "Document",
    account: "Company",
    contact: "Contact",
  };
  return map[entityType.toLowerCase()] ?? entityType;
}

// ---------------------------------------------------------------------------
// WorkspaceLandingWidget
// ---------------------------------------------------------------------------

export function WorkspaceLandingWidget(): React.JSX.Element {
  const styles = useStyles();
  const { bffBaseUrl, token, setChatSessionId } = useAiSession();
  const dispatch = useDispatchPaneEvent();

  const { items, isLoading } = useRecentWork(bffBaseUrl, token);

  const handleResumeWork = React.useCallback(
    (sessionId: string): void => {
      // Set session ID — this triggers SprkChat to load the session history
      setChatSessionId(sessionId);
    },
    [setChatSessionId]
  );

  const handleStartNew = React.useCallback((): void => {
    // Dispatch first_message to advance stage from welcome to loading
    dispatch("conversation", {
      type: "first_message",
      suggestionText: "I want to start a new task",
    } as Parameters<typeof dispatch>[1]);
  }, [dispatch]);

  return (
    <div className={styles.root} data-testid="workspace-landing-widget">
      {/* ── Header ── */}
      <div className={styles.header}>
        <AppsListRegular className={styles.headerIcon} />
        <Text as="h2" size={500} weight="semibold">
          What would you like to work on?
        </Text>
        <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
          Select a playbook from the right panel, resume recent work below,
          or type a question in the chat.
        </Text>
        <Button
          appearance="primary"
          icon={<AddRegular />}
          className={styles.ctaButton}
          onClick={handleStartNew}
        >
          Start new work
        </Button>
      </div>

      {/* ── Recent work ── */}
      <div className={styles.recentSection}>
        <div className={styles.recentHeader}>
          <HistoryRegular style={{ fontSize: "14px", flexShrink: 0 }} />
          <Text size={200} weight="semibold">
            Recent Work
          </Text>
        </div>

        {isLoading ? (
          <div className={styles.loadingContainer}>
            <Spinner size="tiny" label="Loading recent work..." labelPosition="after" />
          </div>
        ) : items.length === 0 ? (
          <Text size={200} className={styles.emptyText}>
            No recent work
          </Text>
        ) : (
          items.map((item) => (
            <div
              key={item.id}
              className={styles.workCard}
              role="button"
              tabIndex={0}
              aria-label={`Resume: ${item.title}`}
              onClick={() => handleResumeWork(item.id)}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  handleResumeWork(item.id);
                }
              }}
            >
              <div className={styles.workCardInfo}>
                <Text size={200} weight="semibold" className={styles.workCardTitle}>
                  {item.title}
                </Text>
                <div className={styles.workCardMeta}>
                  {item.playbookName && (
                    <Badge size="small" appearance="outline" color="brand">
                      {item.playbookName}
                    </Badge>
                  )}
                  {item.entityType && (
                    <Badge size="small" appearance="outline" color="informative">
                      {formatEntityType(item.entityType)}
                    </Badge>
                  )}
                  <Text size={100} className={styles.workCardTimestamp}>
                    {formatRelativeTime(item.updatedAt)}
                  </Text>
                </div>
              </div>
              <ArrowRightRegular className={styles.workCardArrow} aria-hidden="true" />
            </div>
          ))
        )}
      </div>
    </div>
  );
}
