/**
 * ActivityFeed — Updates Feed main container (Block 3).
 *
 * Orchestrates:
 *   1. useEvents    — fetches all events (top 500) from Dataverse via Xrm.WebApi
 *   2. useActivityFeedFilters — manages active pill, derives per-category counts
 *   3. FilterBar    — renders the 8 pill filter buttons with live count badges
 *   4. ActivityFeedList — virtualized scrolling list of feed items
 *   5. ActivityFeedEmptyState — shown when filter yields zero results
 *   6. Error / loading states using Fluent UI v9 components
 *
 * Data flow:
 *   - All 500 events are fetched once (EventFilterCategory.All).
 *   - Client-side filtering applies the active category predicate to the cached list.
 *   - Category counts are derived from the full All list (no extra round-trips).
 *   - Sort order: priority rank descending, then modifiedon descending.
 *
 * Layout:
 *   - Card with header + filter bar + scrollable feed list
 *   - Fixed height to enable virtualized overflow scroll
 *   - Responsive: fills allocated width from WorkspaceGrid's left column
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  Button,
} from "@fluentui/react-components";
import { ArrowClockwiseRegular } from "@fluentui/react-icons";
import { FilterBar } from "./FilterBar";
import { ActivityFeedList } from "./ActivityFeedList";
import { ActivityFeedEmptyState } from "./EmptyState";
import { useEvents, sortEvents } from "../../hooks/useEvents";
import { useActivityFeedFilters } from "../../hooks/useActivityFeedFilters";
import { EventFilterCategory } from "../../types/enums";
import { IEvent } from "../../types/entities";
import { useFeedTodoSync } from "../../hooks/useFeedTodoSync";
import { navigateToEntity } from "../../utils/navigation";
import type { IWebApi } from "../../types/xrm";

// ---------------------------------------------------------------------------
// Lazy-loaded AI Summary dialog (bundle-size optimization — Task 033)
//
// AISummaryDialog is a large modal with AI result cards and icon resolution
// logic. Loading it lazily means users who never open AI summaries pay no
// bundle cost for this code at initial page load.
// ---------------------------------------------------------------------------

const LazyAISummaryDialog = React.lazy(() => import("./AISummaryDialog"));

/** Suspense fallback shown while the AISummaryDialog chunk loads. */
const AISummaryFallback: React.FC = () => (
  <div
    style={{
      position: "fixed",
      inset: 0,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      backgroundColor: "rgba(0,0,0,0.12)",
      zIndex: 1000,
    }}
    aria-live="polite"
    aria-label="Loading AI summary"
  >
    <Spinner size="medium" label="Loading AI summary..." labelPosition="below" />
  </div>
);

// Expose lazy dialog + fallback as named exports so ActivityFeedList or other
// consumers can mount them when they wire up the AI Summary dialog (task 013).
export { LazyAISummaryDialog, AISummaryFallback };

// ---------------------------------------------------------------------------
// Client-side filter predicates
// These mirror queryHelpers.buildEventCategoryFilter for offline filtering
// against the locally cached All-filter event list.
// ---------------------------------------------------------------------------

function applyClientFilter(
  events: IEvent[],
  filter: EventFilterCategory
): IEvent[] {
  if (filter === EventFilterCategory.All) return events;

  const today = new Date();
  today.setHours(0, 0, 0, 0);

  return events.filter((event) => {
    const type = (event.eventTypeName ?? "").toLowerCase();
    const priorityScore = event.sprk_priorityscore ?? 0;

    switch (filter) {
      case EventFilterCategory.HighPriority:
        return priorityScore > 70;

      case EventFilterCategory.Overdue: {
        if (!event.sprk_duedate) return false;
        return new Date(event.sprk_duedate) < today;
      }

      case EventFilterCategory.Alerts:
        return type === "notification" || type === "status change" || type === "reminder";

      case EventFilterCategory.Emails:
        return type === "communication";

      case EventFilterCategory.Documents:
        return type === "filing";

      case EventFilterCategory.Invoices:
        return type === "approval";

      case EventFilterCategory.Tasks:
        return type === "task" || type === "to do" || type === "action" || type === "deadline";

      default:
        return true;
    }
  });
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const FEED_HEIGHT = "520px";

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
    // Fixed height enables the inner virtualized scroll
    height: FEED_HEIGHT,
    minHeight: FEED_HEIGHT,
  },
  /** Borderless, height-flexible root for use inside a tabbed container. */
  embeddedRoot: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 0",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  headerActions: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  loadingContainer: {
    flex: "1 1 0",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  errorContainer: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  retryButton: {
    marginLeft: tokens.spacingHorizontalS,
  },
  feedContent: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 0",
    overflow: "hidden",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IActivityFeedProps {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /**
   * Optional mock events for local development / testing.
   * When provided, bypasses Xrm.WebApi.
   */
  mockEvents?: IEvent[];
  /**
   * When true, hides the card wrapper (border, fixed height) and header
   * so the component can be embedded inside a tabbed container.
   */
  embedded?: boolean;
  /** Report the total event count to the parent (for tab badge display). */
  onCountChange?: (count: number) => void;
  /** Expose the refetch function to the parent (for refresh button in tab header). */
  onRefetchReady?: (refetch: () => void) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ActivityFeed: React.FC<IActivityFeedProps> = ({
  webApi,
  userId,
  mockEvents,
  embedded = false,
  onCountChange,
  onRefetchReady,
}) => {
  const styles = useStyles();

  // Scroll container ref — passed to ActivityFeedList so FilterBar can
  // scroll-to-top when the active filter changes.
  const scrollContainerRef = React.useRef<HTMLDivElement>(null);

  // Fetch all events (top 500) — single query, client-side filtering
  const {
    events: allEvents,
    isLoading,
    error,
    refetch,
  } = useEvents({
    webApi,
    userId,
    filter: EventFilterCategory.All,
    top: 500,
    mockEvents,
  });

  // Seed FeedTodoSyncContext with initial todoflag states from the fetched events.
  // This ensures the flag toggle UI reflects persisted Dataverse state on first
  // render, before the user has interacted with any flags.
  const { initFlags } = useFeedTodoSync();
  React.useEffect(() => {
    if (allEvents.length > 0) {
      initFlags(allEvents);
    }
  }, [allEvents, initFlags]);

  // Report event count to parent for tab badge display (embedded mode)
  React.useEffect(() => {
    onCountChange?.(allEvents.length);
  }, [allEvents.length, onCountChange]);

  // Expose refetch to parent for refresh button routing (embedded mode)
  React.useEffect(() => {
    onRefetchReady?.(refetch);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refetch]);

  // Filter state + category counts
  const { activeFilter, setFilter, categoryCounts } = useActivityFeedFilters({
    allEvents,
  });

  // Apply the active filter client-side and re-sort
  const filteredEvents = React.useMemo(() => {
    const filtered = applyClientFilter(allEvents, activeFilter);
    // Re-sort after filtering to preserve priority-then-timestamp order
    return sortEvents(filtered);
  }, [allEvents, activeFilter]);

  // Determine empty state reason
  const isEmpty = !isLoading && !error && filteredEvents.length === 0;
  const emptyReason =
    activeFilter === EventFilterCategory.All ? "no-events" : "no-match";

  // Screen reader announcement for filter result counts
  const filterResultAnnouncement = !isLoading && !error
    ? `${filteredEvents.length} ${filteredEvents.length === 1 ? "update" : "updates"} shown`
    : "";

  // useCallback: stable handler reference prevents FilterBar from re-rendering
  // every time the parent re-renders (FilterBar is passed this as a prop).
  // Scroll-to-top is included so the ref stays captured in the callback.
  const handleFilterChange = React.useCallback(
    (filter: EventFilterCategory) => {
      setFilter(filter);
      if (scrollContainerRef.current) {
        scrollContainerRef.current.scrollTop = 0;
      }
    },
    [setFilter]
  );

  // ── Action handlers for FeedItemCard ─────────────────────────────────
  const handleEmail = React.useCallback((eventId: string) => {
    console.info(`[ActivityFeed] Email action for event ${eventId} — will connect to Communication Service`);
  }, []);

  const handleTeams = React.useCallback((eventId: string) => {
    console.info(`[ActivityFeed] Teams action for event ${eventId} — will connect to Teams deep link`);
  }, []);

  const handleEdit = React.useCallback((eventId: string) => {
    navigateToEntity({
      action: "openRecord",
      entityName: "sprk_event",
      entityId: eventId,
    });
  }, []);

  return (
    <div className={embedded ? styles.embeddedRoot : styles.card} aria-label="Updates Feed" role="region">
      {/* Header — hidden when embedded inside a tabbed container */}
      {!embedded && (
        <div className={styles.header}>
          <Text className={styles.headerTitle} size={400}>
            Updates
          </Text>
          <div className={styles.headerActions}>
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              onClick={refetch}
              aria-label="Refresh updates feed"
              disabled={isLoading}
            />
          </div>
        </div>
      )}

      {/* Screen reader live region: announces filter result count on filter change */}
      <span
        role="status"
        aria-live="polite"
        aria-atomic="true"
        style={{ position: "absolute", width: "1px", height: "1px", overflow: "hidden", clip: "rect(0,0,0,0)", whiteSpace: "nowrap" }}
      >
        {filterResultAnnouncement}
      </span>

      {/* Filter Bar — always visible so users can see count badges */}
      <FilterBar
        activeFilter={activeFilter}
        categoryCounts={categoryCounts}
        onFilterChange={handleFilterChange}
      />

      {/* Feed content area */}
      <div className={styles.feedContent}>
        {/* Loading state */}
        {isLoading && (
          <div className={styles.loadingContainer}>
            <Spinner
              size="medium"
              label="Loading updates..."
              labelPosition="below"
            />
          </div>
        )}

        {/* Error state */}
        {!isLoading && error && (
          <div className={styles.errorContainer}>
            <MessageBar intent="error" layout="multiline">
              <MessageBarBody>
                {error}
                <Button
                  appearance="transparent"
                  size="small"
                  onClick={refetch}
                  className={styles.retryButton}
                >
                  Try again
                </Button>
              </MessageBarBody>
            </MessageBar>
          </div>
        )}

        {/* Empty state */}
        {isEmpty && (
          <ActivityFeedEmptyState
            reason={emptyReason}
            activeFilter={activeFilter}
            onClearFilter={
              activeFilter !== EventFilterCategory.All
                ? () => setFilter(EventFilterCategory.All)
                : undefined
            }
          />
        )}

        {/* Feed list — virtualized */}
        {!isLoading && !error && !isEmpty && (
          <ActivityFeedList
            events={filteredEvents}
            scrollContainerRef={scrollContainerRef}
            onEmail={handleEmail}
            onTeams={handleTeams}
            onEdit={handleEdit}
          />
        )}
      </div>
    </div>
  );
};
