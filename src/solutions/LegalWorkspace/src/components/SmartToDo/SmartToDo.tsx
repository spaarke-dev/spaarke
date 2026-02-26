/**
 * SmartToDo — Smart To Do list container (Block 4).
 *
 * Renders a card with:
 *   - Section header: "Smart To Do" title + Badge with live item count
 *   - Refresh button to re-fetch from Dataverse
 *   - AddTodoBar (Task 015): input row for manual to-do creation
 *   - Loading state (Spinner)
 *   - Error state (MessageBar with retry)
 *   - Empty state (no to-do items)
 *   - Scrollable list of TodoItem rows
 *   - DismissedSection (Task 015): collapsible section for dismissed items
 *
 * Data:
 *   - Fetches active to-do items via useTodoItems hook
 *     (sprk_event where sprk_todoflag=true AND sprk_todostatus != Dismissed)
 *   - Items sorted FR-07: priorityscore DESC, then duedate ASC
 *
 * Cross-block integration:
 *   - useTodoItems internally consumes FeedTodoSyncContext.subscribe() so that
 *     flag toggles in the Updates Feed (Block 3) are immediately reflected here:
 *       * Newly flagged event → inserted and re-sorted
 *       * Unflagged event → removed from list
 *   - FeedTodoSyncProvider must wrap both ActivityFeed and SmartToDo at app level
 *     (done in LegalWorkspaceApp.tsx as of task 012).
 *
 * Task 015 additions:
 *   - AddTodoBar at top of scrollable area
 *   - Checkbox toggle: optimistic Open ↔ Completed, rollback on failure
 *   - Dismiss button: optimistic move to dismissed list, rollback on failure
 *   - DismissedSection at bottom of card: collapsible, with restore button
 *   - Restore: optimistic move back to active list, rollback on failure
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) for all custom styles
 *   - Support light, dark, and high-contrast modes (automatic via token system)
 *   - Fixed height card with overflow-y scroll, matching ActivityFeed pattern
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Badge,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
} from "@fluentui/react-components";
import { ArrowClockwiseRegular } from "@fluentui/react-icons";
import { TodoItem } from "./TodoItem";
import { AddTodoBar } from "./AddTodoBar";
import { DismissedSection } from "./DismissedSection";
import { useTodoItems } from "../../hooks/useTodoItems";
import { DataverseService } from "../../services/DataverseService";
import { IEvent } from "../../types/entities";
import { computeTodoScore } from "../../utils/todoScoreUtils";
import type { IWebApi } from "../../types/xrm";

// ---------------------------------------------------------------------------
// Lazy-loaded AI Summary dialog (bundle-size optimization — Task 033)
//
// TodoAISummaryDialog contains PriorityScoreCard and EffortScoreCard with
// factor breakdown tables and multiplier checklists. Lazy-loading defers
// this complex sub-tree from the initial bundle until first user click.
// ---------------------------------------------------------------------------

const LazyTodoAISummaryDialog = React.lazy(
  () => import("./TodoAISummaryDialog")
);

/** Suspense fallback shown while the TodoAISummaryDialog chunk loads. */
const TodoAISummaryFallback: React.FC = () => (
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

// Expose as named exports so TodoItem or future consumers can mount the dialog.
export { LazyTodoAISummaryDialog, TodoAISummaryFallback };

// ---------------------------------------------------------------------------
// Sort helper (mirrors useTodoItems.ts — used when inserting new items)
// ---------------------------------------------------------------------------

function sortTodoItems(items: IEvent[]): IEvent[] {
  return [...items].sort((a, b) => {
    // Primary: To Do Score DESC (higher is more important)
    const scoreA = computeTodoScore(a).todoScore;
    const scoreB = computeTodoScore(b).todoScore;
    const scoreDiff = scoreB - scoreA;
    if (scoreDiff !== 0) return scoreDiff;

    // Tiebreaker: duedate ASC (earlier is more urgent)
    const dueDateA = a.sprk_duedate ? new Date(a.sprk_duedate).getTime() : Infinity;
    const dueDateB = b.sprk_duedate ? new Date(b.sprk_duedate).getTime() : Infinity;
    return dueDateA - dueDateB;
  });
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

/** Matches the ActivityFeed card height for visual balance in the 2-column grid */
const TODO_CARD_HEIGHT = "520px";

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
    height: TODO_CARD_HEIGHT,
    minHeight: TODO_CARD_HEIGHT,
  },
  /** Borderless, height-flexible root for use inside a tabbed container. */
  embeddedRoot: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 0",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // ── Header ───────────────────────────────────────────────────────────────
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
  headerLeft: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
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

  // ── Loading state ─────────────────────────────────────────────────────────
  loadingContainer: {
    flex: "1 1 0",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },

  // ── Error state ───────────────────────────────────────────────────────────
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

  // ── Add-error banner ─────────────────────────────────────────────────────
  addErrorContainer: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    flexShrink: 0,
  },

  // ── Empty state ───────────────────────────────────────────────────────────
  emptyContainer: {
    flex: "1 1 0",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },

  // ── Scrollable list ───────────────────────────────────────────────────────
  listContainer: {
    flex: "1 1 0",
    overflowY: "auto",
    // Custom scrollbar styling — subtle to match Fluent aesthetic
    scrollbarWidth: "thin",
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
  },
});

// ---------------------------------------------------------------------------
// Empty state sub-component
// ---------------------------------------------------------------------------

const TodoEmptyState: React.FC = () => {
  const styles = useStyles();
  return (
    <div className={styles.emptyContainer} role="status" aria-live="polite">
      <Text size={300} weight="semibold">
        All caught up
      </Text>
      <Text size={200}>
        No to-do items at the moment. Items flagged from the Updates Feed or
        system-generated tasks will appear here.
      </Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISmartToDoProps {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /**
   * Optional mock items for local development / testing.
   * When provided, bypasses Xrm.WebApi.
   */
  mockItems?: IEvent[];
  /**
   * When true, hides the card wrapper (border, fixed height) and header
   * so the component can be embedded inside a tabbed container.
   */
  embedded?: boolean;
  /** Report the active item count to the parent (for tab badge display). */
  onCountChange?: (count: number) => void;
  /** Expose the refetch function to the parent (for refresh button in tab header). */
  onRefetchReady?: (refetch: () => void) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SmartToDo: React.FC<ISmartToDoProps> = ({
  webApi,
  userId,
  mockItems,
  embedded = false,
  onCountChange,
  onRefetchReady,
}) => {
  const styles = useStyles();

  // Stable DataverseService reference
  const serviceRef = React.useRef<DataverseService>(new DataverseService(webApi));
  React.useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  const { items, isLoading, error, refetch } = useTodoItems({
    webApi,
    userId,
    mockItems,
  });

  // Expose refetch to parent for refresh button routing (embedded mode)
  React.useEffect(() => {
    onRefetchReady?.(refetch);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refetch]);

  // Local overrides on top of the hook's items list.
  // We layer optimistic changes over the hook's items so the hook's
  // cross-block sync (FeedTodoSync) continues to work.

  /** Status overrides keyed by eventId: 0=Open, 1=Completed */
  const [statusOverrides, setStatusOverrides] = React.useState<Map<string, number>>(
    new Map()
  );

  /** Set of eventIds that are currently being dismissed (disable dismiss button) */
  const [dismissingIds, setDismissingIds] = React.useState<Set<string>>(new Set());

  /** Dismissed items managed locally — populated optimistically and persisted in Dataverse */
  const [dismissedItems, setDismissedItems] = React.useState<IEvent[]>([]);

  /** Set of eventIds currently being restored from the dismissed list */
  const [restoringIds, setRestoringIds] = React.useState<Set<string>>(new Set());

  /** Whether a manual add operation is in-flight */
  const [isAdding, setIsAdding] = React.useState<boolean>(false);

  /** Error from a failed add operation */
  const [addError, setAddError] = React.useState<string | null>(null);

  // -------------------------------------------------------------------------
  // Derived active items: hook items minus dismissed ones, with status overlays
  // -------------------------------------------------------------------------

  const activeItems = React.useMemo(() => {
    const dismissedSet = new Set(dismissedItems.map((d) => d.sprk_eventid));
    return items
      .filter((item) => !dismissedSet.has(item.sprk_eventid))
      .map((item) => {
        const overrideStatus = statusOverrides.get(item.sprk_eventid);
        if (overrideStatus === undefined) return item;
        return { ...item, sprk_todostatus: overrideStatus };
      });
  }, [items, dismissedItems, statusOverrides]);

  const itemCount = activeItems.length;
  const isEmpty = !isLoading && !error && itemCount === 0 && dismissedItems.length === 0;

  // -------------------------------------------------------------------------
  // Manual add handler
  // -------------------------------------------------------------------------

  const handleAdd = React.useCallback(
    async (title: string) => {
      setIsAdding(true);
      setAddError(null);

      // Optimistic: create a temporary item with a fake ID
      const tempId = `temp-${Date.now()}`;
      const optimisticItem: IEvent = {
        sprk_eventid: tempId,
        sprk_eventname: title,
        sprk_todoflag: true,
        sprk_todostatus: 100000000, // Open
        sprk_todosource: 100000001, // User
        sprk_priority: 0, // Low
        sprk_effortscore: 10, // Low
        createdon: new Date().toISOString(),
        modifiedon: new Date().toISOString(),
      };

      // We can't mutate the hook's items directly, so we track manually-added
      // items separately and include them via useTodoItems refetch.
      // Optimistic approach: add to the hook state via refetch + temp tracking
      // is complex. Instead use a simpler pattern: add temp item to a local list,
      // then remove it and trigger refetch once Dataverse confirms creation.
      const addTempItem = (item: IEvent) => {
        // We surface temp items by adding them into the dismissed-items-exclusion
        // mechanism via a separate local addedItems state (see below).
        setAddedItems((prev) => sortTodoItems([...prev, item]));
      };
      addTempItem(optimisticItem);

      try {
        const result = await serviceRef.current.createTodo(title, userId);

        if (!result.success) {
          // Rollback optimistic item
          setAddedItems((prev) =>
            prev.filter((i) => i.sprk_eventid !== tempId)
          );
          setAddError(
            result.error?.message ?? "Failed to create to-do item. Please try again."
          );
        } else {
          // Remove temp item and trigger a refetch to get the real record
          setAddedItems((prev) =>
            prev.filter((i) => i.sprk_eventid !== tempId)
          );
          refetch();
        }
      } catch (err: unknown) {
        // Rollback
        setAddedItems((prev) =>
          prev.filter((i) => i.sprk_eventid !== tempId)
        );
        setAddError("Failed to create to-do item. Please try again.");
      } finally {
        setIsAdding(false);
      }
    },
    [userId, refetch]
  );

  /** Locally-added items (optimistic, replaced by refetch on Dataverse success) */
  const [addedItems, setAddedItems] = React.useState<IEvent[]>([]);

  // Merge addedItems into the display list (they are already sorted)
  const displayItems = React.useMemo(() => {
    if (addedItems.length === 0) return activeItems;
    const addedIds = new Set(addedItems.map((a) => a.sprk_eventid));
    // Remove any hook items that match added IDs (avoids duplicates after refetch)
    const dedupedActive = activeItems.filter((i) => !addedIds.has(i.sprk_eventid));
    return sortTodoItems([...dedupedActive, ...addedItems]);
  }, [activeItems, addedItems]);

  // -------------------------------------------------------------------------
  // Checkbox toggle handler
  // -------------------------------------------------------------------------

  const handleToggleComplete = React.useCallback(
    async (eventId: string, completed: boolean) => {
      const newStatus = completed ? 1 : 0; // 1=Completed, 0=Open
      const prevStatus = statusOverrides.get(eventId);

      // Optimistic: apply immediately
      setStatusOverrides((prev) => new Map(prev).set(eventId, newStatus));

      try {
        const result = await serviceRef.current.updateTodoStatus(
          eventId,
          completed ? "Completed" : "Open"
        );

        if (!result.success) {
          // Rollback
          setStatusOverrides((prev) => {
            const next = new Map(prev);
            if (prevStatus === undefined) {
              next.delete(eventId);
            } else {
              next.set(eventId, prevStatus);
            }
            return next;
          });
        }
      } catch {
        // Rollback
        setStatusOverrides((prev) => {
          const next = new Map(prev);
          if (prevStatus === undefined) {
            next.delete(eventId);
          } else {
            next.set(eventId, prevStatus);
          }
          return next;
        });
      }
    },
    [statusOverrides]
  );

  // -------------------------------------------------------------------------
  // Dismiss handler
  // -------------------------------------------------------------------------

  const handleDismiss = React.useCallback(
    async (eventId: string) => {
      // Find the item to move
      const item = displayItems.find((i) => i.sprk_eventid === eventId);
      if (!item) return;

      // Optimistic: mark as dismissing + add to dismissed list
      setDismissingIds((prev) => new Set(prev).add(eventId));
      setDismissedItems((prev) => [item, ...prev]);

      try {
        const result = await serviceRef.current.dismissTodo(eventId);

        if (!result.success) {
          // Rollback
          setDismissedItems((prev) =>
            prev.filter((i) => i.sprk_eventid !== eventId)
          );
        }
      } catch {
        // Rollback
        setDismissedItems((prev) =>
          prev.filter((i) => i.sprk_eventid !== eventId)
        );
      } finally {
        setDismissingIds((prev) => {
          const next = new Set(prev);
          next.delete(eventId);
          return next;
        });
      }
    },
    [displayItems]
  );

  // -------------------------------------------------------------------------
  // Restore dismissed handler
  // -------------------------------------------------------------------------

  const handleRestore = React.useCallback(
    async (eventId: string) => {
      const item = dismissedItems.find((i) => i.sprk_eventid === eventId);
      if (!item) return;

      // Optimistic: remove from dismissed, add back to active via statusOverrides=Open
      setRestoringIds((prev) => new Set(prev).add(eventId));
      setDismissedItems((prev) => prev.filter((i) => i.sprk_eventid !== eventId));
      // Item is still in the hook's items list with Dismissed status — override to Open
      setStatusOverrides((prev) => new Map(prev).set(eventId, 0));

      try {
        const result = await serviceRef.current.updateTodoStatus(eventId, "Open");

        if (!result.success) {
          // Rollback: put back in dismissed, remove override
          setDismissedItems((prev) => [item, ...prev]);
          setStatusOverrides((prev) => {
            const next = new Map(prev);
            next.delete(eventId);
            return next;
          });
        } else {
          // Success: trigger a refetch so the item appears with proper Dataverse data
          refetch();
        }
      } catch {
        // Rollback
        setDismissedItems((prev) => [item, ...prev]);
        setStatusOverrides((prev) => {
          const next = new Map(prev);
          next.delete(eventId);
          return next;
        });
      } finally {
        setRestoringIds((prev) => {
          const next = new Set(prev);
          next.delete(eventId);
          return next;
        });
      }
    },
    [dismissedItems, refetch]
  );

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  const totalCount = displayItems.length;

  // Report active item count to parent for tab badge display (embedded mode)
  React.useEffect(() => {
    onCountChange?.(totalCount);
  }, [totalCount, onCountChange]);

  return (
    <div
      className={embedded ? styles.embeddedRoot : styles.card}
      role="region"
      aria-label={`Smart To Do list, ${totalCount} item${totalCount === 1 ? "" : "s"}`}
    >
      {/* ── Header — hidden when embedded inside a tabbed container ──── */}
      {!embedded && (
        <div className={styles.header}>
          <div className={styles.headerLeft}>
            <Text className={styles.headerTitle} size={400}>
              Smart To Do
            </Text>
            {/* Item count badge — updates reactively as items change */}
            {!isLoading && !error && (
              <Badge
                appearance="filled"
                color="brand"
                size="small"
                aria-label={`${totalCount} to-do item${totalCount === 1 ? "" : "s"}`}
                aria-live="polite"
              >
                {totalCount}
              </Badge>
            )}
          </div>

          <div className={styles.headerActions}>
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              onClick={refetch}
              aria-label="Refresh to-do list"
              disabled={isLoading}
            />
          </div>
        </div>
      )}

      {/* ── Add-error banner ──────────────────────────────────────────── */}
      {addError && (
        <div className={styles.addErrorContainer}>
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>
              {addError}
              <Button
                appearance="transparent"
                size="small"
                onClick={() => setAddError(null)}
                className={styles.retryButton}
              >
                Dismiss
              </Button>
            </MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* ── Loading state ─────────────────────────────────────────────── */}
      {isLoading && (
        <div className={styles.loadingContainer}>
          <Spinner
            size="medium"
            label="Loading to-do items..."
            labelPosition="below"
          />
        </div>
      )}

      {/* ── Error state ───────────────────────────────────────────────── */}
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

      {/* ── Main content area (AddTodoBar + list + dismissed) ─────────── */}
      {!isLoading && !error && (
        <>
          {/* Add input bar — always visible */}
          <AddTodoBar onAdd={handleAdd} isAdding={isAdding} />

          {/* Empty state */}
          {isEmpty && <TodoEmptyState />}

          {/* Scrollable todo list */}
          {!isEmpty && (
            <div
              className={styles.listContainer}
              role="list"
              aria-label="To-do items"
            >
              {displayItems.map((item) => (
                <TodoItem
                  key={item.sprk_eventid}
                  event={item}
                  onToggleComplete={handleToggleComplete}
                  onDismiss={handleDismiss}
                  isDismissing={dismissingIds.has(item.sprk_eventid)}
                />
              ))}
            </div>
          )}

          {/* Dismissed section — collapsible, at bottom of card */}
          <DismissedSection
            items={dismissedItems}
            onRestore={handleRestore}
            restoringIds={restoringIds}
          />
        </>
      )}
    </div>
  );
};
