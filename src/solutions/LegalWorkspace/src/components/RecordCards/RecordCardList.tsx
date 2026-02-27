/**
 * RecordCardList — virtualized scrolling list container for record cards.
 *
 * Follows the same IntersectionObserver-based windowing pattern as ActivityFeedList:
 *   - Fixed-height container with overflow-y: scroll
 *   - Sentinel div at bottom triggers loading more items
 *   - INITIAL_WINDOW_SIZE=25, LOAD_BATCH_SIZE=20
 *
 * Supports loading/empty/error states.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const INITIAL_WINDOW_SIZE = 25;
const LOAD_BATCH_SIZE = 20;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  scrollContainer: {
    flex: "1 1 0",
    overflowY: "scroll",
    overflowX: "hidden",
    scrollBehavior: "smooth",
    contain: "strict",
  },
  innerList: {
    display: "flex",
    flexDirection: "column",
  },
  sentinelContainer: {
    display: "flex",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    minHeight: "32px",
  },
  totalCountText: {
    color: tokens.colorNeutralForeground4,
  },
  centeredMessage: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 0",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground3,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IRecordCardListProps {
  /** Total number of items */
  totalCount: number;
  /** Whether data is currently loading */
  isLoading: boolean;
  /** Error message (shown instead of list when non-null) */
  error: string | null;
  /** Accessible label for the list container */
  ariaLabel: string;
  /** The rendered card elements */
  children: React.ReactNode;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RecordCardList: React.FC<IRecordCardListProps> = ({
  totalCount,
  isLoading,
  error,
  ariaLabel,
  children,
}) => {
  const styles = useStyles();
  const scrollRef = React.useRef<HTMLDivElement>(null);
  const sentinelRef = React.useRef<HTMLDivElement>(null);
  const [visibleCount, setVisibleCount] = React.useState(INITIAL_WINDOW_SIZE);

  // Reset window when children change
  React.useEffect(() => {
    setVisibleCount(INITIAL_WINDOW_SIZE);
    if (scrollRef.current) {
      scrollRef.current.scrollTop = 0;
    }
  }, [totalCount]);

  // IntersectionObserver for progressive rendering
  React.useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting) {
          setVisibleCount((prev) => Math.min(prev + LOAD_BATCH_SIZE, totalCount));
        }
      },
      {
        root: scrollRef.current,
        rootMargin: "200px",
        threshold: 0,
      }
    );

    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [totalCount]);

  // Loading state
  if (isLoading) {
    return (
      <div className={styles.centeredMessage}>
        <Spinner size="small" label="Loading..." labelPosition="below" />
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className={styles.centeredMessage}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  // Empty state
  if (totalCount === 0) {
    return (
      <div className={styles.centeredMessage}>
        <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
          No records found.
        </Text>
      </div>
    );
  }

  // Slice visible children
  const childArray = React.Children.toArray(children);
  const visibleChildren = childArray.slice(0, visibleCount);
  const hasMore = visibleCount < totalCount;

  return (
    <div
      className={styles.scrollContainer}
      ref={scrollRef}
      role="list"
      aria-label={ariaLabel}
    >
      <div className={styles.innerList}>
        {visibleChildren}

        <div
          ref={sentinelRef}
          className={styles.sentinelContainer}
          aria-hidden="true"
        >
          {!hasMore && totalCount > 0 && (
            <Text size={100} className={styles.totalCountText}>
              {totalCount} {totalCount === 1 ? "record" : "records"} total
            </Text>
          )}
        </div>
      </div>
    </div>
  );
};
