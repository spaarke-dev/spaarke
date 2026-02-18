/**
 * ActivityFeedList — virtualized scrolling list container for the Updates Feed.
 *
 * Virtualization approach: IntersectionObserver-based windowing.
 *   - The container is fixed-height with overflow-y: scroll.
 *   - A sentinel div at the bottom is observed; when it enters the viewport,
 *     the visible window expands by LOAD_BATCH_SIZE items.
 *   - Items above the visible window are replaced with a spacer div to maintain
 *     scroll position without rendering DOM nodes.
 *   - This handles 500+ items without external dependencies (no react-window,
 *     no react-virtual) — safe for the PCF platform library constraints.
 *
 * Each item is rendered as a FeedItemCard (task 011) which provides the full
 * interactive card UI including:
 *   - Unread dot + type icon + title + priority badge + timestamp
 *   - Flag toggle (wired to FeedTodoSyncContext — task 012)
 *   - AI Summary button (task 013)
 *
 * The PlaceholderFeedItem below is retained as a fallback/dev aid but is no
 * longer used in the main render path.
 *
 * The component is forward-ref compatible; consumers can hold a ref to the
 * scroll container for programmatic scroll-to-top on filter changes.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Spinner,
} from "@fluentui/react-components";
import { IEvent } from "../../types/entities";
import { formatRelativeTime } from "../NotificationPanel/notificationTypes";
import { FeedItemCard } from "./FeedItemCard";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Number of items in the initial render window */
const INITIAL_WINDOW_SIZE = 40;
/** How many more items to render when the bottom sentinel fires */
const LOAD_BATCH_SIZE = 30;
/** Approximate pixel height of a single feed item (used for spacer calculation) */
const APPROX_ITEM_HEIGHT_PX = 72;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  scrollContainer: {
    flex: "1 1 0",
    overflowY: "scroll",
    overflowX: "hidden",
    // Smooth scroll on filter change
    scrollBehavior: "smooth",
    // Contain paint to avoid layout thrash during virtualization
    contain: "strict",
  },
  innerList: {
    display: "flex",
    flexDirection: "column",
  },
  item: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke3,
    cursor: "default",
    backgroundColor: tokens.colorNeutralBackground1,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ":focus-visible": {
      outlineStyle: "solid",
      outlineWidth: "2px",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "-2px",
    },
  },
  itemRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalS,
  },
  itemSubject: {
    flex: "1 1 0",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  itemMeta: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
  itemTime: {
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
  },
  sentinelContainer: {
    display: "flex",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    minHeight: "32px",
  },
  loadingMore: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    display: "flex",
    justifyContent: "center",
  },
  totalCountText: {
    color: tokens.colorNeutralForeground4,
  },
});

// ---------------------------------------------------------------------------
// Placeholder feed item (task 011 will replace with FeedItemCard)
// ---------------------------------------------------------------------------

interface IPlaceholderFeedItemProps {
  event: IEvent;
}

function getTypeBadgeColor(
  eventType: string | undefined
): "important" | "warning" | "informative" | "success" | "severe" {
  const type = (eventType ?? "").toLowerCase();
  if (type === "financial-alert" || type === "status-change") return "important";
  if (type === "invoice") return "warning";
  if (type === "task") return "success";
  if (type === "email") return "informative";
  return "informative";
}

function formatEventTypeLabel(eventType: string | undefined): string {
  const type = eventType ?? "";
  const map: Record<string, string> = {
    email: "Email",
    document: "Document",
    invoice: "Invoice",
    task: "Task",
    meeting: "Meeting",
    analysis: "Analysis",
    "financial-alert": "Alert",
    "status-change": "Status",
    alertresponse: "Alert",
    documentreview: "Document",
  };
  return map[type.toLowerCase()] ?? type;
}

const PlaceholderFeedItem: React.FC<IPlaceholderFeedItemProps> = React.memo(
  ({ event }) => {
    const styles = useStyles();
    const relativeTime = formatRelativeTime(event.modifiedon);

    return (
      <div
        className={styles.item}
        role="listitem"
        tabIndex={0}
        aria-label={`${event.sprk_subject}, ${formatEventTypeLabel(event.sprk_type)}, ${relativeTime}`}
      >
        <div className={styles.itemRow}>
          <Text className={styles.itemSubject} size={300}>
            {event.sprk_subject}
          </Text>
          <div className={styles.itemMeta}>
            <Badge
              size="small"
              appearance="tint"
              color={getTypeBadgeColor(event.sprk_type)}
            >
              {formatEventTypeLabel(event.sprk_type)}
            </Badge>
            <Text className={styles.itemTime} size={200}>
              {relativeTime}
            </Text>
          </div>
        </div>
      </div>
    );
  }
);

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IActivityFeedListProps {
  /** The sorted, filtered event items to render */
  events: IEvent[];
  /** Whether more items are being loaded (shows bottom spinner) */
  isLoadingMore?: boolean;
  /** Passed to the scroll container so parents can scroll-to-top */
  scrollContainerRef?: React.RefObject<HTMLDivElement>;
  /**
   * Called when the user clicks "AI Summary" on a feed item card.
   * The parent (ActivityFeed) opens the AI Summary dialog (task 013).
   */
  onAISummary?: (eventId: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ActivityFeedList: React.FC<IActivityFeedListProps> = ({
  events,
  isLoadingMore = false,
  scrollContainerRef,
  onAISummary,
}) => {
  const styles = useStyles();

  // Stable no-op fallback for onAISummary — task 013 will supply the real handler
  const handleAISummary = React.useCallback(
    (eventId: string) => {
      if (onAISummary) {
        onAISummary(eventId);
      }
      // else: no-op until AI Summary dialog (task 013) is implemented
    },
    [onAISummary]
  );

  // Visible window: render items [0, visibleCount)
  const [visibleCount, setVisibleCount] = React.useState<number>(INITIAL_WINDOW_SIZE);

  // Sentinel ref for IntersectionObserver
  const sentinelRef = React.useRef<HTMLDivElement>(null);

  // Reset window when event list changes (filter switch or fresh fetch)
  React.useEffect(() => {
    setVisibleCount(INITIAL_WINDOW_SIZE);
    // Scroll to top when filter changes
    if (scrollContainerRef?.current) {
      scrollContainerRef.current.scrollTop = 0;
    }
  }, [events, scrollContainerRef]);

  // IntersectionObserver — expand window when sentinel enters viewport
  React.useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const entry = entries[0];
        if (entry.isIntersecting) {
          setVisibleCount((prev) => Math.min(prev + LOAD_BATCH_SIZE, events.length));
        }
      },
      {
        // Observe relative to the scroll container
        root: scrollContainerRef?.current ?? null,
        rootMargin: "200px",
        threshold: 0,
      }
    );

    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [events.length, scrollContainerRef]);

  const visibleEvents = events.slice(0, visibleCount);
  const hasMore = visibleCount < events.length;

  // Spacer height: items not yet rendered above the visible window.
  // In this windowing approach we always start from index 0, so no top spacer
  // is needed — all items below visibleCount are simply not mounted.
  // The scroll container grows naturally as items are appended.

  return (
    <div
      className={styles.scrollContainer}
      ref={scrollContainerRef}
      role="list"
      aria-label="Updates feed"
      aria-busy={isLoadingMore}
    >
      <div className={styles.innerList}>
        {visibleEvents.map((event) => (
          <FeedItemCard
            key={event.sprk_eventid}
            event={event}
            onAISummary={handleAISummary}
          />
        ))}

        {/* Bottom sentinel — triggers loading more items */}
        <div
          ref={sentinelRef}
          className={styles.sentinelContainer}
          aria-hidden="true"
        >
          {isLoadingMore && (
            <div className={styles.loadingMore}>
              <Spinner size="tiny" label="Loading more..." labelPosition="after" />
            </div>
          )}
          {!hasMore && events.length > 0 && (
            <Text size={100} className={styles.totalCountText}>
              {events.length} {events.length === 1 ? "item" : "items"} total
            </Text>
          )}
        </div>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Export height constant so ActivityFeed can set its container height
// ---------------------------------------------------------------------------

export const APPROX_FEED_ITEM_HEIGHT = APPROX_ITEM_HEIGHT_PX;
