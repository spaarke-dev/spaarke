/**
 * SmartToDo — Smart To Do Kanban board container (standalone Code Page version).
 *
 * Renders a three-column Kanban board (Today / Tomorrow / Future) where items
 * are automatically assigned to columns based on their To Do Score and
 * user-configurable thresholds.
 *
 * Layout:
 *   - KanbanHeader: title, AddTodoBar, recalculate button, settings gear
 *   - KanbanBoard: drag-and-drop columns with KanbanCard items
 *   - DismissedSection: collapsible section for dismissed items
 *
 * Data:
 *   - Fetches active to-do items via useTodoItems hook
 *   - Column assignment via useKanbanColumns hook (score-based with pin support)
 *   - Threshold preferences via useUserPreferences hook (persisted in Dataverse)
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
 *
 * Standalone differences (vs LegalWorkspace version):
 *   - No BroadcastChannel listener (no cross-page sync)
 *   - No Xrm.App.sidePanes integration (no side pane detail view)
 *   - No 6-layer navigation detection / side pane cleanup
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
import { KanbanBoard, OrientationToggle, type Orientation } from "@spaarke/ui-components";
import { useCurrentContactId } from "@spaarke/smart-todo-components";
// R4 task 102 (E-1, 2026-06-18) — `KanbanCard` hoisted from this folder into
// the `@spaarke/smart-todo-components` peer package so the workspace widget
// can render the IDENTICAL card surface. The Code Page swap is an
// import-source change only — same visual + interaction behaviour.
import { KanbanCard } from "@spaarke/smart-todo-components";
import { KanbanHeader } from "./KanbanHeader";
// R4-104 (Wave E-3, 2026-06-18) — the consolidated SmartTodoApp Header now
// owns the QuickAdd input. It dispatches `QUICK_ADD_TODO_EVENT` window events
// which this component subscribes to and routes through its existing
// `handleAdd` (single-source optimistic add + Dataverse create logic).
import { QUICK_ADD_TODO_EVENT } from "./Header";
import type { QuickAddTodoEventDetail } from "./Header";
import { ThresholdSettingsPopover } from "./ThresholdSettings";
import { DismissedSection } from "./DismissedSection";
import { useTodoItems } from "../hooks/useTodoItems";
// R4 task 101 (W-3, 2026-06-18) — `useKanbanColumns` was hoisted into the
// `@spaarke/smart-todo-components` peer package so the workspace widget can
// reuse the same Today/Tomorrow/Future bucketing. The Code Page now imports
// the hoisted hook and supplies its concrete `DataverseService` via the
// optional `dataverseService` prop (kept structurally compatible — the
// service's three Kanban methods already return `IResult<...>` with
// `{ success: boolean }` matching `IKanbanDataverseService`).
import { useKanbanColumns } from "@spaarke/smart-todo-components";
import { useUserPreferences } from "../hooks/useUserPreferences";
import { DataverseService } from "../services/DataverseService";
import { ITodo } from "../types/entities";
import { computeTodoScore } from "../utils/todoScoreUtils";
import { useOptionalTodoContext } from "../context/TodoContext";
import type { TodoColumn } from "../types/enums";
import type { DropResult } from "@hello-pangea/dnd";
import type { IWebApi } from "../types/xrm";

// ---------------------------------------------------------------------------
// Lazy-loaded AI Summary dialog (bundle-size optimization)
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
      backgroundColor: tokens.colorNeutralStroke1,
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
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
    height: "100%",
    boxSizing: "border-box",
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
  mockItems?: ITodo[];
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
  /**
   * Multi-select set lifted to the host (R4 task 060 / spec FR-27).
   * When provided, each KanbanCard renders a selection checkbox bound to this
   * Set + the `onToggleSelect` callback. The same Set drives the
   * selection-aware toolbar in `<Header>` (FR-08).
   *
   * Omit (both `selectedIds` and `onToggleSelect`) for embedded surfaces that
   * don't yet plumb multi-select — checkboxes are hidden + the toolbar Row 4
   * is not affected.
   */
  selectedIds?: ReadonlySet<string>;
  /** Called when the user toggles a card's selection checkbox. */
  onToggleSelect?: (todoId: string) => void;
  /**
   * Called when the user requests to OPEN a card (per-card Open icon or
   * double-click — R4 task 060 / spec FR-25 + FR-26). The callback is expected
   * to dispatch the canonical `OPEN_TODOS_EVENT` so the existing modal
   * subscriber (in `<SmartTodoLayout>`, Wave A task 040) handles routing.
   *
   * When omitted, the Open icon button is not rendered and double-click is a
   * no-op — back-compat for embedded surfaces (LegalWorkspace dashboard).
   */
  onOpenTodo?: (todoId: string) => void;
  /**
   * R4-104 (Wave E-3, 2026-06-18) — when true, the inner `<KanbanHeader>`
   * is fully suppressed. The consolidated SmartTodoApp Header (R4-104)
   * relocates the title + QuickAdd + Refresh + Settings + OrientationToggle
   * into a single Toolbar landmark above the Kanban; rendering KanbanHeader
   * would create the duplicate-chrome UAT 8 + 11 issues.
   *
   * Settings + OrientationToggle stay functional via the callback props
   * below; QuickAdd routes through the QUICK_ADD_TODO_EVENT listener mounted
   * in this component. The settings popover stays mounted (anchored to a
   * hidden trigger) so the consolidated Header can open it via callback.
   *
   * Defaults to `false` for back-compat with any embedded consumer that
   * doesn't yet route its own chrome.
   */
  hideHeader?: boolean;
  /**
   * R4-104 — Optional callback that exposes the Settings open trigger to a
   * parent (the consolidated Header). When provided, the parent calls this
   * to open the threshold-settings popover that is still anchored inside
   * SmartToDo. Implemented as a callback ref bound on mount.
   */
  onSettingsOpenerReady?: (open: () => void) => void;
  /**
   * UAT 2026-06-19 — Optional orientation override.
   * When provided, this prop wins over SmartToDo's internal
   * `useUserPreferences().orientation`. This is the cross-instance fix:
   * if the parent (SmartTodoApp) also calls useUserPreferences, both
   * instances would hold independent local state and the user's Header
   * toggle wouldn't reach SmartToDo's KanbanBoard. With this prop, the
   * parent is the single source of truth.
   *
   * Back-compat: when omitted, SmartToDo uses its own preference instance.
   */
  orientation?: Orientation;
  /**
   * Code-review hotfix 2026-06-27 — filter predicate for the consolidated
   * Header's SearchBox. When provided, displayItems are filtered (case-
   * insensitive substring on sprk_name + sprk_description) BEFORE the
   * kanban-columns hook buckets them. Mirrors the widget pattern at
   * `Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget.tsx:552-560`.
   *
   * Back-compat: when omitted or empty string, no filter is applied.
   */
  searchQuery?: string;
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
  onShowMore,
  selectedIds,
  onToggleSelect,
  onOpenTodo,
  hideHeader = false,
  onSettingsOpenerReady,
  orientation: orientationProp,
  searchQuery,
}) => {
  const styles = useStyles();

  // -------------------------------------------------------------------------
  // TodoContext integration (optional — only available inside TodoProvider)
  // When embedded mode is active (disableSidePane), card clicks do NOT
  // open the detail panel.
  // -------------------------------------------------------------------------

  const todoCtx = useOptionalTodoContext();

  const handleCardClick = React.useCallback(
    (todoId: string) => {
      // In embedded mode, card clicks should NOT open the detail panel
      if (embedded) return;
      todoCtx?.selectItem(todoId);
    },
    [embedded, todoCtx],
  );

  // Stable DataverseService reference
  const serviceRef = React.useRef<DataverseService>(new DataverseService(webApi));
  React.useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  // -------------------------------------------------------------------------
  // Core data hooks
  // -------------------------------------------------------------------------

  const { preferences, updatePreferences, isLoading: prefsLoading } =
    useUserPreferences({ webApi, userId });

  // UAT 2026-06-19 — resolve current systemuser → sprk_contact for the
  // migrated sprk_assignedto Contact lookup. useTodoItems gets the
  // contactId (passed via the legacy-named `userId` arg per the boundary
  // comment on useTodoItems options).
  const { contactId } = useCurrentContactId({ webApi, userId });

  const { items, isLoading, error, refetch } = useTodoItems({
    webApi,
    userId: contactId ?? '00000000-0000-0000-0000-000000000000',
    mockItems,
  });

  // R4 task 031 / FR-07 / OD-2 — "Assigned to Me" is the sole filter mode for
  // the SmartTodo Code Page. The R3 user-controllable `myTasksFilterMode` was
  // removed (legacy "My Tasks" + "All" modes dropped because `ownerid` is
  // BU-owned and UAT users couldn't distinguish the parallel modes). The
  // single mode is now baked into the OData predicate in
  // `services/queryHelpers.ts buildTodoItemsQuery`.

  // Expose refetch to parent for refresh button routing (embedded mode)
  React.useEffect(() => {
    onRefetchReady?.(refetch);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refetch]);

  // -------------------------------------------------------------------------
  // Local optimistic state (preserved from pre-Kanban version)
  // -------------------------------------------------------------------------

  /**
   * Status overrides keyed by sprk_todoid. Stored as a Dataverse statuscode
   * (1=Open, 659490001=In Progress, 2=Completed, 659490002=Dismissed).
   */
  const [statusOverrides, setStatusOverrides] = React.useState<Map<string, number>>(
    new Map()
  );

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

  // R4-104 (Wave E-3) — expose the Settings opener to a parent (the
  // consolidated SmartTodoApp Header). Runs once on mount. The popover is
  // anchored to a hidden trigger inside SmartToDo, so opening from outside is
  // safe.
  React.useEffect(() => {
    onSettingsOpenerReady?.(() => setSettingsOpen(true));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [onSettingsOpenerReady]);

  /**
   * Collapsed Kanban columns — UAT 2026-06-19: ALL columns expanded by default
   * per user feedback (previously Future was collapsed). User explicitly
   * collapses via column-header click.
   */
  const [collapsedColumns, setCollapsedColumns] = React.useState<ReadonlySet<string>>(
    new Set()
  );

  /**
   * Board layout orientation (R4 task 070 / 071 / FR-28 / FR-29 / FR-30).
   *
   * Toggled via `<OrientationToggle>` in the KanbanHeader. The swap is a
   * pure CSS-class change on the shared `<KanbanBoard>` — no React
   * re-mount, so cards keep their drag-drop + selection state across an
   * orientation flip (NFR-08).
   *
   * Persisted via `useUserPreferences` (task 071) — the user's choice
   * round-trips through `sprk_userpreference` (the SAME kanban-prefs JSON
   * envelope that already carries thresholds + viewMode). On first visit
   * the hook returns `DEFAULT_SMART_TODO_ORIENTATION` ("horizontal").
   *
   * `setOrientation` writes the new value optimistically AND persists via
   * the hook — `updatePreferences` already does an optimistic local
   * update, so a single call drives both state + persistence.
   */
  // UAT 2026-06-19 — if parent passes orientation prop, that wins over
  // SmartToDo's internal preference state. Otherwise fall back to the
  // hook's value. Fixes the cross-instance state-sync bug: when both
  // SmartTodoApp and SmartToDo call useUserPreferences independently,
  // toggling in the Header updates the app's instance only — SmartToDo's
  // KanbanBoard stays stuck on the original orientation without this
  // prop override.
  const orientation = orientationProp ?? preferences.orientation;
  const setOrientation = React.useCallback(
    (next: Orientation) => {
      void updatePreferences({ orientation: next });
    },
    [updatePreferences],
  );

  const handleToggleCollapse = React.useCallback((columnId: string) => {
    setCollapsedColumns((prev) => {
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
  // Derived active items: hook items minus dismissed ones, with status overlays
  // -------------------------------------------------------------------------

  const activeItems = React.useMemo(() => {
    const dismissedSet = new Set(dismissedItems.map((d) => d.sprk_todoid));
    return items
      .filter((item) => !dismissedSet.has(item.sprk_todoid))
      .map((item) => {
        const overrideStatuscode = statusOverrides.get(item.sprk_todoid);
        if (overrideStatuscode === undefined) return item;
        // statecode follows statuscode (per task 009 mapping): Open/InProgress => Active, else Inactive.
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
    const addedIds = new Set(addedItems.map((a) => a.sprk_todoid));
    const dedupedActive = activeItems.filter((i) => !addedIds.has(i.sprk_todoid));
    return sortTodoItems([...dedupedActive, ...addedItems]);
  }, [activeItems, addedItems]);

  // Code-review hotfix 2026-06-27 — apply Header SearchBox filter (when set).
  // Mirrors `SmartTodoWidget.tsx:552-560` substring-match-on-name+description.
  const displayItems = React.useMemo(() => {
    const q = (searchQuery ?? "").trim().toLowerCase();
    if (!q) return mergedItems;
    return mergedItems.filter((item) => {
      const name = (item.sprk_name ?? "").toLowerCase();
      const desc = (item.sprk_description ?? "").toLowerCase();
      return name.includes(q) || desc.includes(q);
    });
  }, [mergedItems, searchQuery]);

  const totalCount = displayItems.length;
  const isEmpty = !isLoading && !error && totalCount === 0 && dismissedItems.length === 0;

  // -------------------------------------------------------------------------
  // Kanban columns hook
  // -------------------------------------------------------------------------

  // UAT 2026-06-19 — persist manual intra-column order to user prefs
  // (cross-device via sprk_userpreference). Reads `columnOrders` from prefs
  // on mount; writes back on each reorder.
  const handleColumnOrdersChange = React.useCallback(
    (next: Record<string, string[]>) => {
      void updatePreferences({ columnOrders: next });
    },
    [updatePreferences],
  );

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
    // R4 task 101 — hoisted hook accepts `dataverseService` (interface) instead
    // of the prior `webApi` + `userId` pair. Our `DataverseService` is
    // structurally compatible because its 3 Kanban methods return
    // `IResult<...>` which carries `success: boolean`.
    dataverseService: serviceRef.current,
    initialColumnOrders: preferences.columnOrders,
    onColumnOrdersChange: handleColumnOrdersChange,
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
        statecode: 0,        // Active
        statuscode: 1,       // Open (per task 009)
        sprk_priorityscore: 50,
        sprk_effortscore: 10,
        createdon: new Date().toISOString(),
        modifiedon: new Date().toISOString(),
      };

      setAddedItems((prev) => sortTodoItems([...prev, optimisticItem]));

      try {
        const result = await serviceRef.current.createTodo(title, userId);

        if (!result.success) {
          setAddedItems((prev) =>
            prev.filter((i) => i.sprk_todoid !== tempId)
          );
          setAddError(
            result.error?.message ?? "Failed to create to-do item. Please try again."
          );
        } else {
          setAddedItems((prev) =>
            prev.filter((i) => i.sprk_todoid !== tempId)
          );
          refetch();
        }
      } catch {
        setAddedItems((prev) =>
          prev.filter((i) => i.sprk_todoid !== tempId)
        );
        setAddError("Failed to create to-do item. Please try again.");
      } finally {
        setIsAdding(false);
      }
    },
    [userId, refetch]
  );

  // -------------------------------------------------------------------------
  // R4-104 (Wave E-3) — QUICK_ADD_TODO_EVENT subscription
  // -------------------------------------------------------------------------
  // The consolidated SmartTodoApp Header (R4-104) owns the QuickAdd input but
  // delegates the actual create to this component (which holds the optimistic
  // state + Dataverse service). We subscribe at window scope so any future
  // launcher (keyboard shortcut, Outlook ribbon QuickAdd, etc.) can dispatch
  // the same event without coupling to React props.
  //
  // The handler reads from a ref so the listener identity stays stable across
  // renders (no thrash on every state change).
  const handleAddRef = React.useRef(handleAdd);
  handleAddRef.current = handleAdd;

  React.useEffect(() => {
    const listener = (ev: Event): void => {
      const detail = (ev as CustomEvent<QuickAddTodoEventDetail>).detail;
      if (!detail?.title) return;
      // UAT 2026-06-19 — three-field payload. When dueDate / assignedToId are
      // present, bypass the basic handleAdd and call webApi.createRecord
      // directly so the additional fields (`sprk_duedate`,
      // `sprk_assignedto@odata.bind`) land on the create. Falls back to the
      // simple title-only path when extra fields aren't provided (back-compat
      // for old launchers).
      const hasExtras = !!detail.dueDate || !!detail.assignedToId;
      if (!hasExtras) {
        void handleAddRef.current(detail.title);
        return;
      }
      const payload: Record<string, unknown> = { sprk_name: detail.title };
      // UAT 2026-06-20 — sprk_assignedto binds to the OOB `contact` entity.
      // detail.assignedToId is a contact GUID (the Header's quick-add
      // Assigned To field). When unset, fall back to current user's contactId.
      // Bind set name is `contacts` (plural of the OOB contact table).
      const assignedToContactId = detail.assignedToId || contactId || '';
      if (assignedToContactId) {
        // UAT 2026-06-22 round 13: bind key MUST be PascalCase nav-prop
        // name `sprk_AssignedTo` (verified via EntityDefinitions metadata),
        // not the lookup column logical name `sprk_assignedto`. The
        // lowercase form fails with "An undeclared property
        // 'sprk_assignedto' which only has property annotations..."
        payload['sprk_AssignedTo@odata.bind'] = `/contacts(${assignedToContactId})`;
      }
      if (detail.dueDate) {
        const [y, m, d] = detail.dueDate.split('-').map(Number);
        const dt = new Date(y, m - 1, d, 23, 59, 0);
        payload['sprk_duedate'] = dt.toISOString();
      }
      void (async () => {
        try {
          await webApi.createRecord('sprk_todo', payload);
          refetch();
        } catch (err) {
          // eslint-disable-next-line no-console
          console.warn('[SmartToDo] three-field quickAdd create failed:', err);
        }
      })();
    };
    window.addEventListener(QUICK_ADD_TODO_EVENT, listener);
    return () => {
      window.removeEventListener(QUICK_ADD_TODO_EVENT, listener);
    };
  }, [webApi, contactId, refetch]);

  // -------------------------------------------------------------------------
  // Dismiss handler
  // -------------------------------------------------------------------------

  const handleDismiss = React.useCallback(
    async (todoId: string) => {
      const item = displayItems.find((i) => i.sprk_todoid === todoId);
      if (!item) return;

      setDismissingIds((prev) => new Set(prev).add(todoId));
      setDismissedItems((prev) => [item, ...prev]);

      try {
        const result = await serviceRef.current.dismissTodo(todoId);
        if (!result.success) {
          setDismissedItems((prev) =>
            prev.filter((i) => i.sprk_todoid !== todoId)
          );
        }
      } catch {
        setDismissedItems((prev) =>
          prev.filter((i) => i.sprk_todoid !== todoId)
        );
      } finally {
        setDismissingIds((prev) => {
          const next = new Set(prev);
          next.delete(todoId);
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
    async (todoId: string) => {
      const item = dismissedItems.find((i) => i.sprk_todoid === todoId);
      if (!item) return;

      setRestoringIds((prev) => new Set(prev).add(todoId));
      setDismissedItems((prev) => prev.filter((i) => i.sprk_todoid !== todoId));
      // Override to statuscode=1 (Open) optimistically.
      setStatusOverrides((prev) => new Map(prev).set(todoId, 1));

      try {
        const result = await serviceRef.current.updateTodoStatus(todoId, "Open");
        if (!result.success) {
          setDismissedItems((prev) => [item, ...prev]);
          setStatusOverrides((prev) => {
            const next = new Map(prev);
            next.delete(todoId);
            return next;
          });
        } else {
          refetch();
        }
      } catch {
        setDismissedItems((prev) => [item, ...prev]);
        setStatusOverrides((prev) => {
          const next = new Map(prev);
          next.delete(todoId);
          return next;
        });
      } finally {
        setRestoringIds((prev) => {
          const next = new Set(prev);
          next.delete(todoId);
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
    (todoId: string) => {
      togglePin(todoId);
    },
    [togglePin]
  );

  // -------------------------------------------------------------------------
  // renderCard for KanbanBoard
  // -------------------------------------------------------------------------

  const selectedEventId = todoCtx?.selectedEventId ?? null;

  const renderCard = React.useCallback(
    (item: ITodo, _index: number, columnId: string) => {
      // Get column accent colour from the columns array
      const col = columns.find((c) => c.id === columnId);
      return (
        <KanbanCard
          todo={item}
          onPinToggle={handlePinToggle}
          onClick={handleCardClick}
          accentColor={col?.accentColor}
          isSelected={item.sprk_todoid === selectedEventId}
          // R4 task 060 — Card affordances (FR-25 / FR-26 / FR-27).
          // Per-card props plumbed only when the host provides the wiring;
          // omitting `onOpenTodo` / `onToggleSelect` keeps the card
          // backwards-compatible for embedded LW surfaces.
          onOpen={onOpenTodo}
          isMultiSelected={selectedIds?.has(item.sprk_todoid) ?? false}
          onToggleSelect={onToggleSelect}
        />
      );
    },
    [
      columns,
      handlePinToggle,
      handleCardClick,
      selectedEventId,
      onOpenTodo,
      selectedIds,
      onToggleSelect,
    ]
  );

  const getItemId = React.useCallback(
    (item: ITodo) => item.sprk_todoid,
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
      {/* ── KanbanHeader — suppressed when the consolidated SmartTodoApp
            Header (R4-104) owns the title + QuickAdd + Settings + Orientation
            chrome. The Settings popover below stays mounted (hidden trigger)
            so the consolidated Header can open it via the
            `onSettingsOpenerReady` callback. */}
      {!hideHeader && (
        <KanbanHeader
          totalCount={totalCount}
          onRecalculate={recalculate}
          isRecalculating={isRecalculating}
          onAdd={handleAdd}
          isAdding={isAdding}
          onSettingsOpen={() => setSettingsOpen(true)}
          embedded={embedded}
          orientationSlot={
            <OrientationToggle
              orientation={orientation}
              onChange={setOrientation}
            />
          }
        />
      )}

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

          {/* Show more button for workspace preview mode */}
          {onShowMore && (
            <div style={{ display: "flex", justifyContent: "center", padding: "8px" }}>
              <Button appearance="subtle" size="small" onClick={onShowMore}>
                Show more
              </Button>
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
