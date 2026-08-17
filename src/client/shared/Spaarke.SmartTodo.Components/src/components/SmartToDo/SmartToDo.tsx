/**
 * SmartToDo — Smart To Do Kanban board container (rich subtree).
 *
 * R5 FR-01 / task 002 — hoisted host-agnostic from
 * `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` into
 * `@spaarke/smart-todo-components`.
 *
 * HOST-AGNOSTIC REDESIGN (ADR-012 / NFR-05 — zero `src/solutions/...` reach-in):
 * the LegalWorkspace-local data layer is no longer imported here. Instead the
 * host shim (task 003) runs its own `useTodoItems` / `useUserPreferences`
 * (Dataverse-bound) and passes the results + mutation callbacks in as props —
 * mirroring the established `SmartTodoWidget` "host brokers coupling" pattern
 * (`IFeedSyncBridge`, `IKanbanDataverseService`). Specifically:
 *   - `useTodoItems`         → `items` / `isLoading` / `error` / `onRefetch` props
 *   - `useUserPreferences`   → `preferences` / `onUpdatePreferences` / `prefsLoading` props
 *   - `DataverseService`     → `onCreateTodo` / `onDismissTodo` / `onRestoreTodo` callbacks
 *   - `useFeedTodoSync`      → optional `feedSync: IFeedSyncBridge` prop
 *   - LW `useKanbanColumns`  → the package's host-agnostic `useKanbanColumns`
 *                              (column/pin persistence via optional `dataverseService`)
 *   - `Xrm.Navigation`       → optional `onOpenTodo` callback (host owns navigation)
 *
 * Layout:
 *   - KanbanHeader: title, AddTodoBar, recalculate button, settings gear
 *   - KanbanBoard: drag-and-drop columns with KanbanCard items
 *   - DismissedSection: collapsible section for dismissed items
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) for all custom styles
 *   - Support light, dark, and high-contrast modes (automatic via token system)
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import { KanbanBoard } from '@spaarke/ui-components';
import type { DropResult } from '@hello-pangea/dnd';
import { KanbanCard } from './KanbanCard';
import { KanbanHeader } from './KanbanHeader';
import { ThresholdSettingsPopover } from './ThresholdSettings';
import { DismissedSection } from './DismissedSection';
import { useKanbanColumns } from '../../hooks/useKanbanColumns';
import { computeTodoScore } from '../../utils/todoScoring';
import type { ITodo, ITodoKanbanPreferences, ITodoMutationResult } from '../../types/entities';
import type { TodoColumn, IKanbanDataverseService } from '../../types/kanban';
import type { IFeedSyncBridge } from '../../types/todo';

// ---------------------------------------------------------------------------
// Lazy-loaded AI Summary dialog (bundle-size optimization)
//
// TodoAISummaryDialog contains PriorityScoreCard and EffortScoreCard with
// factor breakdown tables and multiplier checklists. Lazy-loading defers
// this complex sub-tree from the initial bundle until first user click.
// ---------------------------------------------------------------------------

const LazyTodoAISummaryDialog = React.lazy(() => import('./TodoAISummaryDialog'));

/** Suspense fallback shown while the TodoAISummaryDialog chunk loads. */
const TodoAISummaryFallback: React.FC = () => (
  <div
    style={{
      position: 'fixed',
      inset: 0,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: tokens.colorNeutralStroke1,
      zIndex: 1000,
    }}
    aria-live="polite"
    aria-label="Loading AI summary"
  >
    <Spinner size="medium" label="Loading AI summary..." labelPosition="below" />
  </div>
);

// Expose as named exports so future consumers can mount the dialog.
export { LazyTodoAISummaryDialog, TodoAISummaryFallback };

// ---------------------------------------------------------------------------
// Sort helper (mirrors host useTodoItems.ts — used when inserting new items)
// ---------------------------------------------------------------------------

function sortTodoItems(items: ITodo[]): ITodo[] {
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
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    flex: '1 1 0',
    minHeight: '400px',
  },
  /** Borderless, height-flexible root for use inside a tabbed container. */
  embeddedRoot: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 0',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // ── Loading state ─────────────────────────────────────────────────────────
  loadingContainer: {
    flex: '1 1 0',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
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
    flex: '1 1 0',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },

  // ── Kanban board area ─────────────────────────────────────────────────────
  boardContainer: {
    flex: '1 1 0',
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    overflow: 'hidden',
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
        No to-do items at the moment. Items flagged from the Updates Feed or system-generated tasks will appear here.
      </Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Column ID to TodoColumn mapping
// ---------------------------------------------------------------------------

const COLUMN_ID_MAP: Record<string, TodoColumn> = {
  Today: 'Today',
  Tomorrow: 'Tomorrow',
  Future: 'Future',
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISmartToDoProps {
  // ── Data (host runs useTodoItems in the shim and passes results in) ──────
  /** Sorted active to-do items (host sorts by score desc + duedate asc). */
  items: ITodo[];
  /** True while the host's initial fetch is in progress. */
  isLoading: boolean;
  /** User-friendly error message, null when healthy. */
  error: string | null;
  /** Trigger a fresh fetch from the host's data source. */
  onRefetch: () => void;

  // ── Threshold preferences (host runs useUserPreferences in the shim) ─────
  /** Current threshold preferences. */
  preferences: ITodoKanbanPreferences;
  /** Persist new threshold preferences (host writes to Dataverse). */
  onUpdatePreferences: (prefs: Partial<ITodoKanbanPreferences>) => void;
  /** True while the host's preferences fetch is in progress. */
  prefsLoading?: boolean;

  // ── Mutations (host provides Dataverse-backed implementations) ───────────
  /** Create a new manual to-do; resolves with success + new id. */
  onCreateTodo: (title: string) => Promise<ITodoMutationResult>;
  /** Dismiss a to-do; resolves with success. */
  onDismissTodo: (todoId: string) => Promise<ITodoMutationResult>;
  /** Restore a dismissed to-do to Open; resolves with success. */
  onRestoreTodo: (todoId: string) => Promise<ITodoMutationResult>;

  // ── Kanban column/pin persistence ───────────────────────────────────────
  /**
   * Optional Dataverse service for column-move / pin persistence. When
   * omitted, drag-drop + pin toggles are local-only (the hook logs a warning).
   * A host-side `Xrm.WebApi` adapter satisfies this interface.
   */
  dataverseService?: IKanbanDataverseService;

  // ── Cross-block lifecycle sync ───────────────────────────────────────────
  /**
   * Optional feed-sync bridge. The host shim wires it to its
   * FeedTodoSyncContext; the container calls `notifyChange` after local
   * add / dismiss / restore mutations.
   */
  feedSync?: IFeedSyncBridge;

  // ── Navigation (host owns Xrm.Navigation) ────────────────────────────────
  /**
   * Called when the user opens a card (or "show more"). Passes the clicked
   * `sprk_todoid` (or `undefined` for the general "open full view"). The host
   * wires this to `Xrm.Navigation` / its surface-launch registry. When
   * omitted, open actions are inert.
   */
  onOpenTodo?: (todoId?: string) => void;

  // ── Presentation ─────────────────────────────────────────────────────────
  /**
   * When true, hides the card wrapper (border, fixed height) and header
   * so the component can be embedded inside a tabbed container.
   */
  embedded?: boolean;
  /** Report the active item count to the parent (for tab badge display). */
  onCountChange?: (count: number) => void;
  /** Expose the refetch function to the parent (for refresh button in tab header). */
  onRefetchReady?: (refetch: () => void) => void;
  /** Called when "Show more" is clicked. */
  onShowMore?: () => void;
  /** When true, disables card click behavior (used for workspace glance mode). */
  disableSidePane?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SmartToDo: React.FC<ISmartToDoProps> = ({
  items,
  isLoading,
  error,
  onRefetch,
  preferences,
  onUpdatePreferences,
  prefsLoading = false,
  onCreateTodo,
  onDismissTodo,
  onRestoreTodo,
  dataverseService,
  feedSync,
  onOpenTodo,
  embedded = false,
  onCountChange,
  onRefetchReady,
  onShowMore,
  disableSidePane = false,
}) => {
  const styles = useStyles();

  // Expose refetch to parent for refresh button routing (embedded mode)
  React.useEffect(() => {
    onRefetchReady?.(onRefetch);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [onRefetch]);

  // -------------------------------------------------------------------------
  // Local optimistic state (preserved from pre-hoist version)
  // -------------------------------------------------------------------------

  /**
   * Status overrides keyed by sprk_todoid. Stored as a Dataverse statuscode
   * (1=Open, 659490001=In Progress, 2=Completed, 659490002=Dismissed).
   */
  const [statusOverrides, setStatusOverrides] = React.useState<Map<string, number>>(new Map());

  /** Set of todoIds that are currently being dismissed (disable dismiss button) */
  const [dismissingIds, setDismissingIds] = React.useState<Set<string>>(new Set());

  /** Dismissed items managed locally — populated optimistically and persisted in Dataverse */
  const [dismissedItems, setDismissedItems] = React.useState<ITodo[]>([]);

  /** Set of todoIds currently being restored from the dismissed list */
  const [restoringIds, setRestoringIds] = React.useState<Set<string>>(new Set());

  /** Whether a manual add operation is in-flight */
  const [isAdding, setIsAdding] = React.useState<boolean>(false);

  /** Error from a failed add operation */
  const [addError, setAddError] = React.useState<string | null>(null);

  /** Locally-added items (optimistic, replaced by refetch on Dataverse success) */
  const [addedItems, setAddedItems] = React.useState<ITodo[]>([]);

  /** Settings popover state */
  const [settingsOpen, setSettingsOpen] = React.useState(false);

  /** Collapsed Kanban columns — Future is collapsed by default */
  const [collapsedColumns, setCollapsedColumns] = React.useState<ReadonlySet<string>>(new Set(['Future']));

  const handleToggleCollapse = React.useCallback((columnId: string) => {
    setCollapsedColumns(prev => {
      const next = new Set(prev);
      if (next.has(columnId)) {
        next.delete(columnId);
      } else {
        next.add(columnId);
      }
      return next;
    });
  }, []);

  // -------------------------------------------------------------------------
  // Derived active items: prop items minus dismissed ones, with status overlays
  // -------------------------------------------------------------------------

  const activeItems = React.useMemo(() => {
    const dismissedSet = new Set(dismissedItems.map(d => d.sprk_todoid));
    return items
      .filter(item => !dismissedSet.has(item.sprk_todoid))
      .map(item => {
        const overrideStatuscode = statusOverrides.get(item.sprk_todoid);
        if (overrideStatuscode === undefined) return item;
        // statecode follows statuscode (per task 009): Open/InProgress => Active, else Inactive.
        const isActive = overrideStatuscode === 1 || overrideStatuscode === 659490001;
        return {
          ...item,
          statuscode: overrideStatuscode,
          statecode: isActive ? 0 : 1,
        };
      });
  }, [items, dismissedItems, statusOverrides]);

  // Merge addedItems into the display list
  const displayItems = React.useMemo(() => {
    if (addedItems.length === 0) return activeItems;
    const addedIds = new Set(addedItems.map(a => a.sprk_todoid));
    const dedupedActive = activeItems.filter(i => !addedIds.has(i.sprk_todoid));
    return sortTodoItems([...dedupedActive, ...addedItems]);
  }, [activeItems, addedItems]);

  const totalCount = displayItems.length;
  const isEmpty = !isLoading && !error && totalCount === 0 && dismissedItems.length === 0;

  // -------------------------------------------------------------------------
  // Kanban columns hook (host-agnostic package hook — column/pin persistence
  // flows through the optional injected `dataverseService`)
  // -------------------------------------------------------------------------

  const { columns, moveItem, reorderInColumn, togglePin, recalculate, isRecalculating } = useKanbanColumns<ITodo>({
    items: displayItems,
    todayThreshold: preferences.todayThreshold,
    tomorrowThreshold: preferences.tomorrowThreshold,
    dataverseService,
  });

  // -------------------------------------------------------------------------
  // Manual add handler
  // -------------------------------------------------------------------------

  const handleAdd = React.useCallback(
    async (title: string) => {
      setIsAdding(true);
      setAddError(null);

      const tempId = `temp-${Date.now()}`;
      const optimisticItem: ITodo = {
        sprk_todoid: tempId,
        sprk_name: title,
        statecode: 0, // Active
        statuscode: 1, // Open (per task 009)
        sprk_priorityscore: 50,
        sprk_effortscore: 10,
        createdon: new Date().toISOString(),
        modifiedon: new Date().toISOString(),
      };

      setAddedItems(prev => sortTodoItems([...prev, optimisticItem]));

      try {
        const result = await onCreateTodo(title);

        if (!result.success) {
          setAddedItems(prev => prev.filter(i => i.sprk_todoid !== tempId));
          setAddError(result.error?.message ?? 'Failed to create to-do item. Please try again.');
        } else {
          setAddedItems(prev => prev.filter(i => i.sprk_todoid !== tempId));
          onRefetch();
          // FR-14: cross-block notification — the new todo is active.
          const newId = result.id ?? tempId;
          feedSync?.notifyChange(newId, true);
        }
      } catch {
        setAddedItems(prev => prev.filter(i => i.sprk_todoid !== tempId));
        setAddError('Failed to create to-do item. Please try again.');
      } finally {
        setIsAdding(false);
      }
    },
    [onCreateTodo, onRefetch, feedSync]
  );

  // -------------------------------------------------------------------------
  // Dismiss handler
  // -------------------------------------------------------------------------

  const handleDismiss = React.useCallback(
    async (todoId: string) => {
      const item = displayItems.find(i => i.sprk_todoid === todoId);
      if (!item) return;

      setDismissingIds(prev => new Set(prev).add(todoId));
      setDismissedItems(prev => [item, ...prev]);

      try {
        const result = await onDismissTodo(todoId);
        if (!result.success) {
          setDismissedItems(prev => prev.filter(i => i.sprk_todoid !== todoId));
        } else {
          // FR-14: cross-block notification — todo became inactive.
          feedSync?.notifyChange(todoId, false);
        }
      } catch {
        setDismissedItems(prev => prev.filter(i => i.sprk_todoid !== todoId));
      } finally {
        setDismissingIds(prev => {
          const next = new Set(prev);
          next.delete(todoId);
          return next;
        });
      }
    },
    [displayItems, onDismissTodo, feedSync]
  );

  // -------------------------------------------------------------------------
  // Restore dismissed handler
  // -------------------------------------------------------------------------

  const handleRestore = React.useCallback(
    async (todoId: string) => {
      const item = dismissedItems.find(i => i.sprk_todoid === todoId);
      if (!item) return;

      setRestoringIds(prev => new Set(prev).add(todoId));
      setDismissedItems(prev => prev.filter(i => i.sprk_todoid !== todoId));
      // Override to statuscode=1 (Open) optimistically.
      setStatusOverrides(prev => new Map(prev).set(todoId, 1));

      try {
        const result = await onRestoreTodo(todoId);
        if (!result.success) {
          setDismissedItems(prev => [item, ...prev]);
          setStatusOverrides(prev => {
            const next = new Map(prev);
            next.delete(todoId);
            return next;
          });
        } else {
          onRefetch();
          // FR-14: cross-block notification — restored todo is active again.
          feedSync?.notifyChange(todoId, true);
        }
      } catch {
        setDismissedItems(prev => [item, ...prev]);
        setStatusOverrides(prev => {
          const next = new Map(prev);
          next.delete(todoId);
          return next;
        });
      } finally {
        setRestoringIds(prev => {
          const next = new Set(prev);
          next.delete(todoId);
          return next;
        });
      }
    },
    [dismissedItems, onRestoreTodo, onRefetch, feedSync]
  );

  // -------------------------------------------------------------------------
  // Drag-end handler: move item between Kanban columns
  // -------------------------------------------------------------------------

  const handleDragEnd = React.useCallback(
    (result: DropResult) => {
      const { destination, source } = result;

      // Dropped outside any column or back to the same position
      if (!destination) return;
      if (destination.droppableId === source.droppableId && destination.index === source.index) {
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
  // Settings: save thresholds
  // -------------------------------------------------------------------------

  const handleSettingsSave = React.useCallback(
    (prefs: { todayThreshold: number; tomorrowThreshold: number }) => {
      onUpdatePreferences(prefs);
    },
    [onUpdatePreferences]
  );

  // -------------------------------------------------------------------------
  // Pin toggle handler
  // -------------------------------------------------------------------------

  const handlePinToggle = React.useCallback(
    (todoId: string) => {
      togglePin(todoId);
    },
    [togglePin]
  );

  // -------------------------------------------------------------------------
  // Open handlers — delegate to the host-provided `onOpenTodo` (host owns
  // Xrm.Navigation / surface-launch). Inert when the prop is omitted.
  // -------------------------------------------------------------------------

  /** Card click handler — opens the host surface with the clicked item's ID. */
  const handleCardClick = React.useCallback(
    (todoId: string) => {
      if (!disableSidePane) {
        onOpenTodo?.(todoId);
      }
    },
    [disableSidePane, onOpenTodo]
  );

  /** "Show more" handler — opens the host surface without a specific item selected. */
  const handleShowMore = React.useCallback(() => {
    if (onShowMore) {
      onShowMore();
    } else {
      onOpenTodo?.();
    }
  }, [onShowMore, onOpenTodo]);

  // -------------------------------------------------------------------------
  // renderCard for KanbanBoard
  // -------------------------------------------------------------------------

  const renderCard = React.useCallback(
    (item: ITodo, _index: number, columnId: string) => {
      // Get column accent colour from the columns array
      const col = columns.find(c => c.id === columnId);
      return (
        <KanbanCard
          todo={item}
          onPinToggle={handlePinToggle}
          onClick={!disableSidePane ? handleCardClick : undefined}
          accentColor={col?.accentColor}
        />
      );
    },
    [columns, handlePinToggle, disableSidePane, handleCardClick]
  );

  const getItemId = React.useCallback((item: ITodo) => item.sprk_todoid, []);

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
      aria-label={`Smart To Do Kanban, ${totalCount} item${totalCount === 1 ? '' : 's'}`}
    >
      {/* ── KanbanHeader — hidden in workspace preview (disableSidePane) ── */}
      {!disableSidePane && (
        <KanbanHeader
          totalCount={totalCount}
          onRecalculate={recalculate}
          isRecalculating={isRecalculating}
          onAdd={handleAdd}
          isAdding={isAdding}
          onSettingsOpen={() => setSettingsOpen(true)}
          embedded={embedded}
        />
      )}

      {/* ── Settings popover — anchor to a hidden trigger ──────────────── */}
      <ThresholdSettingsPopover
        open={settingsOpen}
        onOpenChange={setSettingsOpen}
        preferences={preferences}
        onSave={handleSettingsSave}
      >
        <span style={{ display: 'none' }} />
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
          <Spinner size="medium" label="Loading to-do items..." labelPosition="below" />
        </div>
      )}

      {/* ── Error state ───────────────────────────────────────────────── */}
      {!isLoading && error && (
        <div className={styles.errorContainer}>
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>
              {error}
              <Button appearance="transparent" size="small" onClick={onRefetch} className={styles.retryButton}>
                Try again
              </Button>
            </MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* ── Main content area (Kanban board) ── */}
      {!isLoading && !prefsLoading && !error && (
        <>
          {/* Empty state */}
          {isEmpty && <TodoEmptyState />}

          {/* Kanban board */}
          {!isEmpty && (
            <div className={styles.boardContainer}>
              <KanbanBoard<ITodo>
                columns={columns}
                onDragEnd={handleDragEnd}
                renderCard={renderCard}
                getItemId={getItemId}
                ariaLabel="Smart To Do Kanban board"
                collapsedColumns={collapsedColumns}
                onToggleCollapse={handleToggleCollapse}
              />
            </div>
          )}

          {/* Show more / Open full view button for workspace preview mode */}
          {(onShowMore || disableSidePane) && (
            <div style={{ display: 'flex', justifyContent: 'center', padding: '8px' }}>
              <Button appearance="subtle" size="small" onClick={handleShowMore}>
                {disableSidePane ? 'Open full view' : 'Show more'}
              </Button>
            </div>
          )}

          {/* Dismissed section — collapsible, at bottom of card */}
          <DismissedSection items={dismissedItems} onRestore={handleRestore} restoringIds={restoringIds} />
        </>
      )}
    </div>
  );
};
