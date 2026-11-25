/**
 * SmartToDo — Smart To Do Kanban board container (rich subtree).
 *
 * R5 FR-01 / task 002 — hoisted host-agnostic from
 * `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` into
 * `@spaarke/smart-todo-components`.
 *
 * #2907 (DEF-007) — this is now the ONE Smart To Do board. The SmartTodo code
 * page used to keep a divergent fork of it; the fork's features are optional
 * props here, and both hosts are thin:
 *   - search            → `searchQuery`
 *   - card affordances  → `cardAppearance="compact"` + `selectedIds` /
 *                         `onToggleSelect` (multi-select), `selectedTodoId` /
 *                         `onSelectTodo` (single-select highlight), `onOpenTodo`
 *                         (Open icon + double-click)
 *   - orientation       → `orientation` (the host owns the toggle)
 *   - host-owned chrome → `hideHeader`, `onSettingsOpenerReady`
 *   - column layout     → `defaultCollapsedColumns`, `persistColumnOrder`,
 *                         `fillContainer`
 * Every new prop's default keeps the LegalWorkspace behaviour it had before.
 *
 * HOST-AGNOSTIC (ADR-012 / NFR-05 — zero `src/solutions/...` reach-in): the
 * host runs the data hooks (`useTodoItems` / `useTodoKanbanPreferences` from
 * this package, over its `TodoDataverseService`) and passes the results +
 * mutation callbacks in as props:
 *   - `useTodoItems`             → `items` / `isLoading` / `error` / `onRefetch`
 *   - `useTodoKanbanPreferences` → `preferences` / `onUpdatePreferences` / `prefsLoading`
 *   - `TodoDataverseService`     → `onCreateTodo` / `onDismissTodo` / `onRestoreTodo`
 *                                  and `dataverseService` (column / pin persistence)
 *   - feed sync                  → optional `feedSync: IFeedSyncBridge`
 *   - `Xrm.Navigation`           → optional `onOpenTodo` (host owns navigation)
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
  mergeClasses,
  shorthands,
  tokens,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import {
  KanbanBoard,
  EmptyState,
  // smart-todo-r5 task 011 (FR-02/FR-03): quick-add has no Priority/Effort
  // input, so its optimistic card uses the shared mapping's null-defaults —
  // the same table CreateTodoWizard and the sprk_todo form resolve through.
  NULL_DEFAULT_PRIORITY_SCORE,
  NULL_DEFAULT_EFFORT_SCORE,
} from '@spaarke/ui-components';
import type { DropResult } from '@hello-pangea/dnd';
import { KanbanCard } from './KanbanCard';
// The compact card (checkbox / Open icon / double-click) — what the SmartTodo
// code page renders (`cardAppearance="compact"`).
import { KanbanCard as CompactKanbanCard } from '../KanbanCard';
import { KanbanHeader } from './KanbanHeader';
import { ThresholdSettingsPopover } from './ThresholdSettings';
import { DismissedSection } from './DismissedSection';
import { useKanbanColumns } from '../../hooks/useKanbanColumns';
import { sortTodoItems } from '../../hooks/useTodoItems';
import { matchesTodoSearchQuery } from '../../utils/todoSearchUtils';
import type { ITodo, ITodoKanbanPreferences, ITodoMutationResult } from '../../types/entities';
import type { TodoColumn, IKanbanDataverseService, KanbanOrientation } from '../../types/kanban';
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
  /** `fillContainer`: the card takes its parent's full height (the code page's page panel). */
  cardFill: {
    height: '100%',
    boxSizing: 'border-box',
    minHeight: 'auto',
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

  // ── Empty state (pre-081 look on top of the shared compact EmptyState) ──
  emptyStateContainer: {
    flex: '1 1 0',
    paddingTop: 0,
    paddingBottom: 0,
  },
  emptyStateHeading: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground3,
  },
  emptyStateDescription: {
    maxWidth: 'none',
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
//
// Hoisted to the shared `EmptyState` in `@spaarke/ui-components` (task 081 /
// C-11) — this was one of three hand-rolled copies of the same icon+heading+
// description shape.
// ---------------------------------------------------------------------------

const TodoEmptyState: React.FC = () => {
  const styles = useStyles();
  return (
    <EmptyState
      size="compact"
      className={styles.emptyStateContainer}
      headingClassName={styles.emptyStateHeading}
      descriptionClassName={styles.emptyStateDescription}
      heading="All caught up"
      description="No to-do items at the moment. Items flagged from the Updates Feed or system-generated tasks will appear here."
    />
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

/** The workspace's long-standing default: the Future column starts collapsed. */
const DEFAULT_COLLAPSED_COLUMNS: readonly string[] = ['Future'];

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISmartToDoProps {
  // ── Data (host runs useTodoItems and passes results in) ──────────────────
  /** Sorted active to-do items (host sorts by score desc + duedate asc). */
  items: ITodo[];
  /** True while the host's initial fetch is in progress. */
  isLoading: boolean;
  /** User-friendly error message, null when healthy. */
  error: string | null;
  /** Trigger a fresh fetch from the host's data source. */
  onRefetch: () => void;

  // ── Kanban preferences (host runs useTodoKanbanPreferences) ──────────────
  /** Current preferences (thresholds; column order when `persistColumnOrder`). */
  preferences: ITodoKanbanPreferences;
  /** Persist updated preferences (host writes to Dataverse). */
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
   * `TodoDataverseService` satisfies this interface.
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
   * Opens a to-do (the host wires `Xrm.Navigation` / its surface-launch
   * registry). With `cardAppearance="record"` a card click opens it, and
   * "Show more" calls it with `undefined` (open the full view). With
   * `cardAppearance="compact"` the card's Open icon and a double-click open
   * it, and the icon is shown only when this prop is set. Omitted: open
   * actions are inert.
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

  // ── #2907 — features of the former SmartTodo code-page fork ──────────────
  /**
   * Free-text filter applied client-side before the items are bucketed
   * (title, description, regarding record name / number, assignee, due date —
   * `matchesTodoSearchQuery`). Omitted or blank: no filter.
   */
  searchQuery?: string;
  /**
   * Which card to render. `record` (default) is the workspace card
   * (RecordCardShell). `compact` is the code page's card, which adds the
   * multi-select checkbox, the single-select highlight and the Open icon.
   */
  cardAppearance?: 'record' | 'compact';
  /**
   * Multi-select set (R4 task 060 / FR-27). With `onToggleSelect`, each
   * compact card renders a selection checkbox bound to it. Compact cards only.
   */
  selectedIds?: ReadonlySet<string>;
  /** Called when a compact card's selection checkbox is toggled. */
  onToggleSelect?: (todoId: string) => void;
  /** The highlighted (single-selected) to-do. Compact cards only. */
  selectedTodoId?: string | null;
  /** Called on a compact card body click (not on its pin / Open / checkbox). */
  onSelectTodo?: (todoId: string) => void;
  /**
   * When true, the inner header (title, add bar, recalculate, settings) is
   * not rendered — for a host that renders its own chrome. The settings
   * popover stays mounted; open it through `onSettingsOpenerReady`.
   */
  hideHeader?: boolean;
  /** Receives a function that opens the threshold-settings popover (for host chrome). */
  onSettingsOpenerReady?: (open: () => void) => void;
  /** Board orientation (the host owns the toggle and its persistence). Omitted: the board's default (horizontal). */
  orientation?: KanbanOrientation;
  /** Column ids collapsed on first render. Default: `['Future']`. */
  defaultCollapsedColumns?: readonly string[];
  /**
   * When true, the manual card order within each column is read from
   * `preferences.columnOrders` and saved through `onUpdatePreferences`, so it
   * survives a reload. Default false: the order is kept for the session only.
   */
  persistColumnOrder?: boolean;
  /**
   * When true (and not `embedded`), the bordered card takes its parent's full
   * height instead of growing as a flex child with a 400px minimum.
   */
  fillContainer?: boolean;
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
  searchQuery,
  cardAppearance = 'record',
  selectedIds,
  onToggleSelect,
  selectedTodoId = null,
  onSelectTodo,
  hideHeader = false,
  onSettingsOpenerReady,
  orientation,
  defaultCollapsedColumns = DEFAULT_COLLAPSED_COLUMNS,
  persistColumnOrder = false,
  fillContainer = false,
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

  // Hand the settings opener to host chrome (R4-104). The popover is anchored
  // to a hidden trigger inside this component, so opening it from outside is safe.
  React.useEffect(() => {
    onSettingsOpenerReady?.(() => setSettingsOpen(true));
  }, [onSettingsOpenerReady]);

  /** Collapsed Kanban columns — `defaultCollapsedColumns` on first render. */
  const [collapsedColumns, setCollapsedColumns] = React.useState<ReadonlySet<string>>(
    () => new Set(defaultCollapsedColumns)
  );

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
  const mergedItems = React.useMemo(() => {
    if (addedItems.length === 0) return activeItems;
    const addedIds = new Set(addedItems.map(a => a.sprk_todoid));
    const dedupedActive = activeItems.filter(i => !addedIds.has(i.sprk_todoid));
    return sortTodoItems([...dedupedActive, ...addedItems]);
  }, [activeItems, addedItems]);

  // `searchQuery` — the shared predicate the widget uses too (§11).
  const displayItems = React.useMemo(() => {
    const q = searchQuery ?? '';
    if (!q.trim()) return mergedItems;
    return mergedItems.filter(item => matchesTodoSearchQuery(item, q));
  }, [mergedItems, searchQuery]);

  const totalCount = displayItems.length;
  const isEmpty = !isLoading && !error && totalCount === 0 && dismissedItems.length === 0;

  // -------------------------------------------------------------------------
  // Kanban columns hook (host-agnostic package hook — column/pin persistence
  // flows through the optional injected `dataverseService`)
  // -------------------------------------------------------------------------

  const handleColumnOrdersChange = React.useCallback(
    (next: Record<string, string[]>) => {
      onUpdatePreferences({ columnOrders: next });
    },
    [onUpdatePreferences]
  );

  const { columns, moveItem, reorderInColumn, togglePin, recalculate, isRecalculating } = useKanbanColumns<ITodo>({
    items: displayItems,
    todayThreshold: preferences.todayThreshold,
    tomorrowThreshold: preferences.tomorrowThreshold,
    dataverseService,
    initialColumnOrders: persistColumnOrder ? preferences.columnOrders : undefined,
    onColumnOrdersChange: persistColumnOrder ? handleColumnOrdersChange : undefined,
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
        sprk_priorityscore: NULL_DEFAULT_PRIORITY_SCORE,
        sprk_effortscore: NULL_DEFAULT_EFFORT_SCORE,
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

  /** Record-card click handler — opens the host surface with the clicked item's ID. */
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
      if (cardAppearance === 'compact') {
        return (
          <CompactKanbanCard
            todo={item}
            onPinToggle={handlePinToggle}
            onClick={!disableSidePane ? onSelectTodo : undefined}
            accentColor={col?.accentColor}
            isSelected={item.sprk_todoid === selectedTodoId}
            onOpen={!disableSidePane && onOpenTodo ? (todoId: string) => onOpenTodo(todoId) : undefined}
            isMultiSelected={selectedIds?.has(item.sprk_todoid) ?? false}
            onToggleSelect={onToggleSelect}
          />
        );
      }
      return (
        <KanbanCard
          todo={item}
          onPinToggle={handlePinToggle}
          onClick={!disableSidePane ? handleCardClick : undefined}
          accentColor={col?.accentColor}
        />
      );
    },
    [
      columns,
      cardAppearance,
      handlePinToggle,
      disableSidePane,
      handleCardClick,
      onSelectTodo,
      selectedTodoId,
      onOpenTodo,
      selectedIds,
      onToggleSelect,
    ]
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

  const rootClassName = embedded ? styles.embeddedRoot : mergeClasses(styles.card, fillContainer && styles.cardFill);

  return (
    <div
      className={rootClassName}
      role="region"
      aria-label={`Smart To Do Kanban, ${totalCount} item${totalCount === 1 ? '' : 's'}`}
    >
      {/* ── KanbanHeader — hidden in workspace preview (disableSidePane) and
            when the host renders its own chrome (hideHeader) ── */}
      {!disableSidePane && !hideHeader && (
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
                orientation={orientation}
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
