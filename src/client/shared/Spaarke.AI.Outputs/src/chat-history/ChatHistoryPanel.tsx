/**
 * ChatHistoryPanel
 *
 * Displays a searchable list of prior AI chat sessions. Intended for use in the
 * left pane of the three-pane AI layout or as a slide-out drawer.
 *
 * Data responsibilities:
 *  - Receives sessions as a prop (no BFF API calls inside this component).
 *  - The calling code page is responsible for fetching sessions via
 *    useChatSession from @spaarke/ai-context and passing them down.
 *
 * Features:
 *  - SearchBox at the top (Fluent v9) bound to local search state.
 *  - Client-side 200ms-debounced filtering via useChatHistoryFilter.
 *  - Spinner shown when isLoading is true.
 *  - Empty state (no sessions or all filtered out) with a centered message.
 *  - Scrollable ChatSessionCard list otherwise.
 *
 * All colors come from Fluent v9 design tokens only — dark mode is automatic
 * via FluentProvider theme switching (ADR-021).
 *
 * NOT PCF-safe — requires React 19.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  SearchBox,
  Spinner,
  Text,
} from "@fluentui/react-components";
import { HistoryRegular } from "@fluentui/react-icons";
import type { InputOnChangeData } from "@fluentui/react-components";
import type { SearchBoxChangeEvent } from "@fluentui/react-components";
import type { ChatHistoryPanelProps } from "./ChatHistoryPanel.types";
import { ChatSessionCard } from "./ChatSessionCard";
import { useChatHistoryFilter } from "./useChatHistoryFilter";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground2,
    overflow: "hidden",
  },
  searchArea: {
    flexShrink: 0,
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  searchBox: {
    width: "100%",
  },
  listArea: {
    flexGrow: 1,
    overflowY: "auto",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  centeredState: {
    flexGrow: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  emptyIcon: {
    fontSize: "32px",
    color: tokens.colorNeutralForeground4,
  },
  emptyTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  emptySubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Renders the chat history panel: a search field at the top, a loading spinner
 * while sessions are being fetched, an empty state when there's nothing to show,
 * or a scrollable list of ChatSessionCard components.
 *
 * @example
 * ```tsx
 * // In a code page that owns data fetching:
 * const { sessions, isLoading } = useSessionHistory({ bffBaseUrl });
 *
 * <ChatHistoryPanel
 *   sessions={sessions}
 *   isLoading={isLoading}
 *   onResume={(id) => resumeSession(id)}
 *   onDelete={(id) => deleteSession(id)}
 * />
 * ```
 */
export function ChatHistoryPanel({
  sessions,
  isLoading = false,
  onResume,
  onDelete,
  className,
}: ChatHistoryPanelProps): React.ReactElement {
  const styles = useStyles();

  const [searchQuery, setSearchQuery] = React.useState<string>("");

  const filteredSessions = useChatHistoryFilter(sessions, searchQuery);

  const handleSearchChange = React.useCallback(
    (_event: SearchBoxChangeEvent, data: InputOnChangeData): void => {
      setSearchQuery(data.value ?? "");
    },
    []
  );

  // Note: SearchBox dismiss button fires onChange with an empty value — no
  // separate onClear handler required. The onChange handler above covers it.

  // ── Render: loading state ─────────────────────────────────────────────────

  const renderBody = (): React.ReactElement => {
    if (isLoading) {
      return (
        <div className={styles.centeredState}>
          <Spinner
            size="medium"
            label="Loading conversations..."
            labelPosition="below"
          />
        </div>
      );
    }

    // ── Render: empty state ─────────────────────────────────────────────────

    if (filteredSessions.length === 0) {
      const hasQuery = searchQuery.trim().length > 0;
      return (
        <div className={styles.centeredState}>
          <HistoryRegular className={styles.emptyIcon} />
          <Text className={styles.emptyTitle}>
            {hasQuery ? "No matching conversations" : "No conversations yet"}
          </Text>
          <Text className={styles.emptySubtitle}>
            {hasQuery
              ? "Try a different search term."
              : "Start a new AI conversation to see your history here."}
          </Text>
        </div>
      );
    }

    // ── Render: session list ────────────────────────────────────────────────

    return (
      <div className={styles.listArea} role="list">
        {filteredSessions.map((session) => (
          <div key={session.id} role="listitem">
            <ChatSessionCard
              session={session}
              onResume={onResume}
              onDelete={onDelete}
            />
          </div>
        ))}
      </div>
    );
  };

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Search area — always visible */}
      <div className={styles.searchArea}>
        <SearchBox
          className={styles.searchBox}
          placeholder="Search conversations..."
          value={searchQuery}
          onChange={handleSearchChange}
          aria-label="Search chat history"
          size="medium"
        />
      </div>

      {/* Body — loading, empty state, or session list */}
      {renderBody()}
    </div>
  );
}

export default ChatHistoryPanel;
