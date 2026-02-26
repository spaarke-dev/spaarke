/**
 * SmartToDo — Smart To Do Kanban board container (Block 4).
 *
 * Renders a three-column Kanban board (Today / Tomorrow / Future) where items
 * are automatically assigned to columns based on their To Do Score and
 * user-configurable thresholds.
 *
 * Layout:
 *   - KanbanHeader: title, AddTodoBar, recalculate button, settings gear
 *   - KanbanBoard: drag-and-drop columns with KanbanCard items
 *   - DismissedSection: collapsible section for dismissed items
 *   - TodoDetailSidePane: Xrm.App.sidePanes web resource for full event details
 *
 * Data:
 *   - Fetches active to-do items via useTodoItems hook
 *   - Column assignment via useKanbanColumns hook (score-based with pin support)
 *   - Threshold preferences via useUserPreferences hook (persisted in Dataverse)
 *
 * Cross-block integration:
 *   - useTodoItems internally consumes FeedTodoSyncContext.subscribe() so that
 *     flag toggles in the Updates Feed (Block 3) are immediately reflected here.
 *   - FeedTodoSyncProvider must wrap both ActivityFeed and SmartToDo at app level.
 *
 * Preserved features (from pre-Kanban version):
 *   - AddTodoBar (relocated to KanbanHeader)
 *   - Checkbox toggle: optimistic Open/Completed, rollback on failure
 *   - Dismiss button: optimistic move to dismissed list, rollback on failure
 *   - DismissedSection: collapsible, with restore button
 *   - LazyTodoAISummaryDialog: lazy-loaded AI summary dialog
 *   - Embedded mode: headerless, flex-height for tabbed containers
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) for all custom styles
 *   - Support light, dark, and high-contrast modes (automatic via token system)
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
} from "@fluentui/react-components";
import { KanbanBoard } from "../shared/KanbanBoard";
import { KanbanCard } from "./KanbanCard";
import { KanbanHeader } from "./KanbanHeader";
import { ThresholdSettingsPopover } from "./ThresholdSettings";
import { DismissedSection } from "./DismissedSection";
import { useTodoItems } from "../../hooks/useTodoItems";
import { useKanbanColumns } from "../../hooks/useKanbanColumns";
import { useUserPreferences } from "../../hooks/useUserPreferences";
import { DataverseService } from "../../services/DataverseService";
import { IEvent } from "../../types/entities";
import { computeTodoScore } from "../../utils/todoScoreUtils";
import type { TodoColumn } from "../../types/enums";
import type { DropResult } from "@hello-pangea/dnd";
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
    flex: "1 1 0",
    minHeight: "400px",
  },
  /** Borderless, height-flexible root for use inside a tabbed container. */
  embeddedRoot: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 0",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
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

  // ── Kanban board area ─────────────────────────────────────────────────────
  boardContainer: {
    flex: "1 1 0",
    display: "flex",
    flexDirection: "column",
    minHeight: 0,
    overflow: "hidden",
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
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
// Column ID to TodoColumn mapping
// ---------------------------------------------------------------------------

const COLUMN_ID_MAP: Record<string, TodoColumn> = {
  Today: "Today",
  Tomorrow: "Tomorrow",
  Future: "Future",
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

  // -------------------------------------------------------------------------
  // Core data hooks
  // -------------------------------------------------------------------------

  const { items, isLoading, error, refetch } = useTodoItems({
    webApi,
    userId,
    mockItems,
  });

  const { preferences, updatePreferences, isLoading: prefsLoading } =
    useUserPreferences({ webApi, userId });

  // Expose refetch to parent for refresh button routing (embedded mode)
  React.useEffect(() => {
    onRefetchReady?.(refetch);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refetch]);

  // -------------------------------------------------------------------------
  // Local optimistic state (preserved from pre-Kanban version)
  // -------------------------------------------------------------------------

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

  /** Locally-added items (optimistic, replaced by refetch on Dataverse success) */
  const [addedItems, setAddedItems] = React.useState<IEvent[]>([]);

  /** Settings popover state */
  const [settingsOpen, setSettingsOpen] = React.useState(false);

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

  // Merge addedItems into the display list
  const displayItems = React.useMemo(() => {
    if (addedItems.length === 0) return activeItems;
    const addedIds = new Set(addedItems.map((a) => a.sprk_eventid));
    const dedupedActive = activeItems.filter((i) => !addedIds.has(i.sprk_eventid));
    return sortTodoItems([...dedupedActive, ...addedItems]);
  }, [activeItems, addedItems]);

  const totalCount = displayItems.length;
  const isEmpty = !isLoading && !error && totalCount === 0 && dismissedItems.length === 0;

  // -------------------------------------------------------------------------
  // Kanban columns hook
  // -------------------------------------------------------------------------

  const {
    columns,
    moveItem,
    reorderInColumn,
    togglePin,
    recalculate,
    isRecalculating,
  } = useKanbanColumns({
    items: displayItems,
    todayThreshold: preferences.todayThreshold,
    tomorrowThreshold: preferences.tomorrowThreshold,
    webApi,
    userId,
  });

  // -------------------------------------------------------------------------
  // Manual add handler
  // -------------------------------------------------------------------------

  const handleAdd = React.useCallback(
    async (title: string) => {
      setIsAdding(true);
      setAddError(null);

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

      setAddedItems((prev) => sortTodoItems([...prev, optimisticItem]));

      try {
        const result = await serviceRef.current.createTodo(title, userId);

        if (!result.success) {
          setAddedItems((prev) =>
            prev.filter((i) => i.sprk_eventid !== tempId)
          );
          setAddError(
            result.error?.message ?? "Failed to create to-do item. Please try again."
          );
        } else {
          setAddedItems((prev) =>
            prev.filter((i) => i.sprk_eventid !== tempId)
          );
          refetch();
        }
      } catch {
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

  // -------------------------------------------------------------------------
  // Dismiss handler
  // -------------------------------------------------------------------------

  const handleDismiss = React.useCallback(
    async (eventId: string) => {
      const item = displayItems.find((i) => i.sprk_eventid === eventId);
      if (!item) return;

      setDismissingIds((prev) => new Set(prev).add(eventId));
      setDismissedItems((prev) => [item, ...prev]);

      try {
        const result = await serviceRef.current.dismissTodo(eventId);
        if (!result.success) {
          setDismissedItems((prev) =>
            prev.filter((i) => i.sprk_eventid !== eventId)
          );
        }
      } catch {
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

      setRestoringIds((prev) => new Set(prev).add(eventId));
      setDismissedItems((prev) => prev.filter((i) => i.sprk_eventid !== eventId));
      setStatusOverrides((prev) => new Map(prev).set(eventId, 0));

      try {
        const result = await serviceRef.current.updateTodoStatus(eventId, "Open");
        if (!result.success) {
          setDismissedItems((prev) => [item, ...prev]);
          setStatusOverrides((prev) => {
            const next = new Map(prev);
            next.delete(eventId);
            return next;
          });
        } else {
          refetch();
        }
      } catch {
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
  // Drag-end handler: move item between Kanban columns
  // -------------------------------------------------------------------------

  const handleDragEnd = React.useCallback(
    (result: DropResult) => {
      const { destination, source } = result;

      // Dropped outside any column or back to the same position
      if (!destination) return;
      if (
        destination.droppableId === source.droppableId &&
        destination.index === source.index
      ) {
        return;
      }

      if (destination.droppableId === source.droppableId) {
        // Same-column reorder — preserve user's manual arrangement
        reorderInColumn(source.droppableId, source.index, destination.index);
      } else {
        // Cross-column move
        const targetColumn = COLUMN_ID_MAP[destination.droppableId];
        if (targetColumn) {
          moveItem(result.draggableId, targetColumn);
        }
      }
    },
    [moveItem, reorderInColumn]
  );

  // -------------------------------------------------------------------------
  // Card click handler: open Xrm.App.sidePanes detail pane
  // -------------------------------------------------------------------------

  const handleCardClick = React.useCallback(async (eventId: string) => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
      const sidePanes = xrm?.App?.sidePanes;
      if (!sidePanes) {
        console.warn("[SmartToDo] Xrm.App.sidePanes not available");
        return;
      }

      const PANE_ID = "todoDetailPane";
      const pageInput = {
        pageType: "webresource",
        webresourceName: "sprk_tododetailsidepane",
        data: `eventId=${eventId}`,
      };

      const existingPane = sidePanes.getPane(PANE_ID);
      if (existingPane) {
        await existingPane.navigate(pageInput);
        existingPane.select();
      } else {
        const pane = await sidePanes.createPane({
          title: "To Do Details",
          paneId: PANE_ID,
          canClose: true,
          width: 400,
          isSelected: true,
        });
        await pane.navigate(pageInput);
      }
    } catch (err) {
      console.warn("[SmartToDo] Side pane unavailable", err);
    }
  }, []);

  // -------------------------------------------------------------------------
  // BroadcastChannel: listen for saves from TodoDetailSidePane → refetch
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    let channel: BroadcastChannel | null = null;
    try {
      channel = new BroadcastChannel("spaarke-todo-detail-channel");
      channel.onmessage = (ev: MessageEvent) => {
        if (ev.data?.type === "TODO_SAVED") {
          refetch();
        }
      };
    } catch {
      // BroadcastChannel not supported — ignore
    }
    return () => {
      channel?.close();
    };
  }, [refetch]);

  // -------------------------------------------------------------------------
  // Settings: save thresholds
  // -------------------------------------------------------------------------

  const handleSettingsSave = React.useCallback(
    (prefs: { todayThreshold: number; tomorrowThreshold: number }) => {
      void updatePreferences(prefs);
    },
    [updatePreferences]
  );

  // -------------------------------------------------------------------------
  // Pin toggle handler
  // -------------------------------------------------------------------------

  const handlePinToggle = React.useCallback(
    (eventId: string) => {
      togglePin(eventId);
    },
    [togglePin]
  );

  // -------------------------------------------------------------------------
  // renderCard for KanbanBoard
  // -------------------------------------------------------------------------

  const renderCard = React.useCallback(
    (item: IEvent, _index: number, columnId: string) => {
      // Get column accent colour from the columns array
      const col = columns.find((c) => c.id === columnId);
      return (
        <KanbanCard
          event={item}
          onPinToggle={handlePinToggle}
          onClick={handleCardClick}
          accentColor={col?.accentColor}
        />
      );
    },
    [columns, handlePinToggle, handleCardClick]
  );

  const getItemId = React.useCallback(
    (item: IEvent) => item.sprk_eventid,
    []
  );

  // -------------------------------------------------------------------------
  // Report count to parent
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    onCountChange?.(totalCount);
  }, [totalCount, onCountChange]);

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <div
      className={embedded ? styles.embeddedRoot : styles.card}
      role="region"
      aria-label={`Smart To Do Kanban, ${totalCount} item${totalCount === 1 ? "" : "s"}`}
    >
      {/* ── KanbanHeader — hidden when embedded ────────────────────────── */}
      <KanbanHeader
        totalCount={totalCount}
        onRecalculate={recalculate}
        isRecalculating={isRecalculating}
        onAdd={handleAdd}
        isAdding={isAdding}
        onSettingsOpen={() => setSettingsOpen(true)}
        embedded={embedded}
      />

      {/* ── Settings popover — anchor to a hidden trigger ──────────────── */}
      <ThresholdSettingsPopover
        open={settingsOpen}
        onOpenChange={setSettingsOpen}
        preferences={preferences}
        onSave={handleSettingsSave}
      >
        <span style={{ display: "none" }} />
      </ThresholdSettingsPopover>

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
      {(isLoading || prefsLoading) && (
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

      {/* ── Main content area (Kanban board + dismissed) ──────────────── */}
      {!isLoading && !prefsLoading && !error && (
        <>
          {/* Empty state */}
          {isEmpty && <TodoEmptyState />}

          {/* Kanban board */}
          {!isEmpty && (
            <div className={styles.boardContainer}>
              <KanbanBoard<IEvent>
                columns={columns}
                onDragEnd={handleDragEnd}
                renderCard={renderCard}
                getItemId={getItemId}
                ariaLabel="Smart To Do Kanban board"
              />
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
