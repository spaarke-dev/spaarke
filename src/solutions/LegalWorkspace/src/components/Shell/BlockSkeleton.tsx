/**
 * BlockSkeleton — Fluent UI v9 Skeleton placeholder for workspace blocks.
 *
 * Performance rationale (NFR-01: page load < 3s):
 *   - The Shell renders immediately; each block shows its own skeleton while
 *     its data fetch is in flight.
 *   - Blocks load independently — a slow BFF call for Portfolio Health does
 *     not block the Updates Feed skeleton from rendering (or vice versa).
 *   - When a block's hook reports isLoading=false the skeleton is replaced
 *     with real content without any full-page re-paint.
 *
 * Skeleton variants (via the `variant` prop):
 *
 *   "feed"         — Mimics the Updates Feed card (Block 3):
 *                    header bar + 6 stacked item rows (icon + text + badge)
 *
 *   "todo"         — Mimics the Smart To Do card (Block 4):
 *                    header bar + 5 stacked item rows (checkbox + text + badges)
 *
 *   "portfolio"    — Mimics the Portfolio Health strip (Block 2):
 *                    3 metric tiles side by side
 *
 *   "widget"       — Mimics the My Portfolio sidebar widget (Block 5):
 *                    tab bar + 4 item rows
 *
 *   "generic"      — Fallback: N configurable rows of varying widths
 *
 * Usage:
 *   // In an ActivityFeed while events are loading:
 *   {isLoading && <BlockSkeleton variant="feed" />}
 *   {!isLoading && <ActivityFeedList events={filteredEvents} ... />}
 *
 *   // Generic with custom row count:
 *   <BlockSkeleton variant="generic" rows={4} />
 *
 * Design constraints:
 *   - ALL colours delegated to Fluent UI v9 Skeleton — zero hardcoded hex
 *   - makeStyles (Griffel) for layout only
 *   - Accessible: aria-busy + aria-label on the container
 *   - Supports light, dark, and high-contrast modes automatically
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Skeleton,
  SkeletonItem,
} from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Outer card — matches the card dimensions used by ActivityFeed / SmartToDo
  card: {
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
  },

  // Header skeleton bar (mimics the card header)
  headerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },

  // Body area containing item rows
  body: {
    display: 'flex',
    flexDirection: 'column',
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalS,
    flex: '1 1 auto',
  },

  // Single item row skeleton
  itemRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke3,
  },

  // Left section of an item row (icon placeholder)
  itemIcon: {
    flexShrink: 0,
  },

  // Centre column: title + subtitle lines
  itemContent: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },

  // Right section: badge / meta
  itemMeta: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-end',
    gap: tokens.spacingVerticalXXS,
  },

  // Portfolio Health: 3 metric tiles side by side
  healthTiles: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },

  healthTile: {
    flex: '1 1 0',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
  },

  // Tab bar skeleton for widget variant
  tabBar: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

interface ISkeletonHeaderProps {
  /** Width of the title skeleton (default "40%") */
  titleWidth?: string;
  /** Whether to show a small action button skeleton on the right */
  showAction?: boolean;
}

/** Skeleton header bar matching the card header pattern */
const SkeletonHeader: React.FC<ISkeletonHeaderProps> = ({
  titleWidth = '40%',
  showAction = true,
}) => {
  const styles = useStyles();
  return (
    <div className={styles.headerRow}>
      <Skeleton>
        <SkeletonItem size={16} style={{ width: titleWidth }} />
      </Skeleton>
      {showAction && (
        <Skeleton>
          <SkeletonItem shape="circle" size={24} />
        </Skeleton>
      )}
    </div>
  );
};

interface ISkeletonFeedItemProps {
  /** Width of the title line (for visual variation) */
  titleWidth?: string;
}

/** Single feed item skeleton row (icon + text + badge + timestamp) */
const SkeletonFeedItem: React.FC<ISkeletonFeedItemProps> = ({
  titleWidth = '60%',
}) => {
  const styles = useStyles();
  return (
    <div className={styles.itemRow}>
      {/* Type icon placeholder */}
      <div className={styles.itemIcon}>
        <Skeleton>
          <SkeletonItem shape="circle" size={16} />
        </Skeleton>
      </div>
      {/* Title + description */}
      <div className={styles.itemContent}>
        <Skeleton>
          <SkeletonItem size={16} style={{ width: titleWidth }} />
          <SkeletonItem size={12} style={{ width: '80%', marginTop: tokens.spacingVerticalXXS }} />
        </Skeleton>
      </div>
      {/* Badge + timestamp */}
      <div className={styles.itemMeta}>
        <Skeleton>
          <SkeletonItem size={12} style={{ width: '48px' }} />
          <SkeletonItem size={12} style={{ width: '36px', marginTop: tokens.spacingVerticalXXS }} />
        </Skeleton>
      </div>
    </div>
  );
};

interface ISkeletonTodoItemProps {
  titleWidth?: string;
}

/** Single to-do item skeleton row (checkbox + icon + text + badges) */
const SkeletonTodoItem: React.FC<ISkeletonTodoItemProps> = ({
  titleWidth = '55%',
}) => {
  const styles = useStyles();
  return (
    <div className={styles.itemRow}>
      {/* Drag handle placeholder */}
      <div className={styles.itemIcon}>
        <Skeleton>
          <SkeletonItem size={16} style={{ width: '8px' }} />
        </Skeleton>
      </div>
      {/* Checkbox placeholder */}
      <div className={styles.itemIcon}>
        <Skeleton>
          <SkeletonItem shape="square" size={16} />
        </Skeleton>
      </div>
      {/* Title + context */}
      <div className={styles.itemContent}>
        <Skeleton>
          <SkeletonItem size={16} style={{ width: titleWidth }} />
          <SkeletonItem size={12} style={{ width: '75%', marginTop: tokens.spacingVerticalXXS }} />
        </Skeleton>
      </div>
      {/* Priority + due badges */}
      <div className={styles.itemMeta}>
        <Skeleton>
          <SkeletonItem size={12} style={{ width: '40px' }} />
          <SkeletonItem size={12} style={{ width: '32px', marginTop: tokens.spacingVerticalXXS }} />
        </Skeleton>
      </div>
    </div>
  );
};

interface ISkeletonGenericItemProps {
  titleWidth?: string;
}

/** Generic skeleton item row for flexible use */
const SkeletonGenericItem: React.FC<ISkeletonGenericItemProps> = ({
  titleWidth = '70%',
}) => {
  const styles = useStyles();
  return (
    <div className={styles.itemRow}>
      <div className={styles.itemContent}>
        <Skeleton>
          <SkeletonItem size={16} style={{ width: titleWidth }} />
          <SkeletonItem size={12} style={{ width: '50%', marginTop: tokens.spacingVerticalXXS }} />
        </Skeleton>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Variant-specific skeletons
// ---------------------------------------------------------------------------

/** Feed variant — 6 stacked item rows with varied title widths */
const FeedSkeleton: React.FC = () => {
  const styles = useStyles();
  const titleWidths = ['65%', '50%', '72%', '58%', '45%', '68%'];
  return (
    <div
      className={styles.card}
      role="status"
      aria-busy="true"
      aria-label="Updates feed loading"
      style={{ height: '520px', minHeight: '520px' }}
    >
      <SkeletonHeader titleWidth="30%" />
      {/* Filter bar skeleton */}
      <div className={styles.tabBar}>
        {[56, 72, 64, 48, 64, 56, 52, 48].map((w, i) => (
          <Skeleton key={i}>
            <SkeletonItem size={24} style={{ width: `${w}px`, borderRadius: tokens.borderRadiusCircular }} />
          </Skeleton>
        ))}
      </div>
      <div className={styles.body}>
        {titleWidths.map((w, i) => (
          <SkeletonFeedItem key={i} titleWidth={w} />
        ))}
      </div>
    </div>
  );
};

/** To-do variant — 5 stacked item rows */
const TodoSkeleton: React.FC = () => {
  const styles = useStyles();
  const titleWidths = ['60%', '48%', '70%', '55%', '42%'];
  return (
    <div
      className={styles.card}
      role="status"
      aria-busy="true"
      aria-label="To-do list loading"
      style={{ height: '520px', minHeight: '520px' }}
    >
      <SkeletonHeader titleWidth="35%" />
      <div className={styles.body}>
        {titleWidths.map((w, i) => (
          <SkeletonTodoItem key={i} titleWidth={w} />
        ))}
      </div>
    </div>
  );
};

/** Portfolio Health strip variant — 3 metric tiles */
const PortfolioSkeleton: React.FC = () => {
  const styles = useStyles();
  return (
    <div
      className={styles.card}
      role="status"
      aria-busy="true"
      aria-label="Portfolio health loading"
    >
      <SkeletonHeader titleWidth="45%" showAction={false} />
      <div className={styles.healthTiles}>
        {[1, 2, 3].map((i) => (
          <div key={i} className={styles.healthTile}>
            <Skeleton>
              <SkeletonItem size={12} style={{ width: '70%' }} />
              <SkeletonItem size={28} style={{ width: '50%', marginTop: tokens.spacingVerticalXS }} />
              <SkeletonItem size={8} style={{ width: '90%', marginTop: tokens.spacingVerticalXS }} />
            </Skeleton>
          </div>
        ))}
      </div>
    </div>
  );
};

/** My Portfolio widget variant — tab bar + 4 item rows */
const WidgetSkeleton: React.FC = () => {
  const styles = useStyles();
  const titleWidths = ['65%', '50%', '72%', '55%'];
  return (
    <div
      className={styles.card}
      role="status"
      aria-busy="true"
      aria-label="Portfolio widget loading"
      style={{ minHeight: '280px' }}
    >
      <SkeletonHeader titleWidth="40%" />
      {/* Tab bar */}
      <div className={styles.tabBar}>
        {[56, 56, 72].map((w, i) => (
          <Skeleton key={i}>
            <SkeletonItem size={20} style={{ width: `${w}px` }} />
          </Skeleton>
        ))}
      </div>
      <div className={styles.body}>
        {titleWidths.map((w, i) => (
          <SkeletonGenericItem key={i} titleWidth={w} />
        ))}
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export type BlockSkeletonVariant =
  | 'feed'
  | 'todo'
  | 'portfolio'
  | 'widget'
  | 'generic';

export interface IBlockSkeletonProps {
  /**
   * Which block to mimic with the skeleton layout.
   *   "feed"      → Updates Feed card (Block 3)
   *   "todo"      → Smart To Do card (Block 4)
   *   "portfolio" → Portfolio Health strip (Block 2)
   *   "widget"    → My Portfolio sidebar widget (Block 5)
   *   "generic"   → Configurable generic rows
   */
  variant: BlockSkeletonVariant;
  /**
   * Number of rows to render when variant="generic" (default: 3).
   * Ignored for named variants (they have fixed row counts).
   */
  rows?: number;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * BlockSkeleton — renders a Fluent UI v9 Skeleton placeholder for a workspace
 * block while its data is loading. Each variant mimics the shape and height of
 * the target block so the page layout does not shift when content loads.
 *
 * Blocks load independently — pass each block's own `isLoading` flag:
 *
 *   {isLoading && <BlockSkeleton variant="feed" />}
 *   {!isLoading && <ActivityFeed ... />}
 */
export const BlockSkeleton: React.FC<IBlockSkeletonProps> = React.memo(({ variant, rows = 3 }) => {
  const styles = useStyles();

  switch (variant) {
    case 'feed':
      return <FeedSkeleton />;

    case 'todo':
      return <TodoSkeleton />;

    case 'portfolio':
      return <PortfolioSkeleton />;

    case 'widget':
      return <WidgetSkeleton />;

    case 'generic':
    default: {
      const titleWidths = ['70%', '55%', '65%', '50%', '60%', '48%'];
      return (
        <div
          className={styles.card}
          role="status"
          aria-busy="true"
          aria-label="Content loading"
        >
          <SkeletonHeader />
          <div className={styles.body}>
            {Array.from({ length: rows }).map((_, i) => (
              <SkeletonGenericItem
                key={i}
                titleWidth={titleWidths[i % titleWidths.length]}
              />
            ))}
          </div>
        </div>
      );
    }
  }
});

BlockSkeleton.displayName = 'BlockSkeleton';
