/**
 * useKanbanColumns — Hoisted Kanban bucketing + mutation hook for sprk_todo.
 *
 * R4 task 101 (W-3, 2026-06-18) — hoisted from
 * `src/solutions/SmartTodo/src/hooks/useKanbanColumns.ts` into the
 * `@spaarke/smart-todo-components` peer package. Closes the deferred
 * "13-file rich-feature subtree" follow-up from R4-020 at HOOK SCOPE only
 * (full Kanban drag-drop is intentionally out of scope per task 101 POML).
 *
 * Why a single hoisted hook instead of two (rich vs lean):
 *   - The Code Page consumes the FULL surface (columns + moveItem + togglePin
 *     + reorderInColumn + recalculate). Splitting would force two import
 *     paths and risk drift between the bucketing logic in each.
 *   - The widget consumes ONLY `columns`. The mutation methods are inert when
 *     no `dataverseService` is provided — defensive no-ops with a console
 *     warning, mirroring the established pattern of host-injected services
 *     (cf. `IFeedSyncBridge` in `../types/todo.ts`).
 *   - One source of truth means the widget's grouping is guaranteed to match
 *     the Code Page's, satisfying the W-3 audit's core requirement (UAT
 *     issue 6: "items should be organized by Today/Tomorrow/Future").
 *
 * Generic on `T extends IKanbanTodoLike` — both the Code Page's `ITodo` and
 * the widget's `ITodoRecord` are structural supertypes, so both call sites
 * stay strongly typed without forcing a single concrete shape upstream.
 *
 * Mutation contract:
 *   - `dataverseService` is OPTIONAL. When omitted, the widget pattern,
 *     `moveItem`/`togglePin`/`reorderInColumn`/`recalculate` are inert —
 *     they update local state optimistically but skip persistence and emit
 *     `[useKanbanColumns]` console.warn so accidental misuse is visible.
 *   - When supplied, behavior is identical to the original Code Page hook:
 *     optimistic UI + fire-and-forget persistence with rollback on
 *     recalculate batch failure.
 *
 * Standards:
 *   - ADR-012 — peer-package hoist; structural typing keeps it generic
 *   - ADR-021 — Fluent v9 token usage (column accent colors)
 *
 * See also:
 *   - `projects/smart-todo-r4/notes/d-widget-parity-audit-2026-06-18.md` §6
 *   - `src/solutions/SmartTodo/src/components/SmartToDo.tsx` — Code Page consumer
 *   - `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx`
 *     — widget consumer (rendering only, no mutations)
 */

import * as React from 'react';
import { tokens } from '@fluentui/react-components';
import type { IKanbanColumn, IKanbanDataverseService, IKanbanTodoLike, TodoColumn } from '../types/kanban';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Default threshold: items scoring at or above 60 land in "Today". */
export const DEFAULT_TODAY_THRESHOLD = 60;
/** Default threshold: items scoring at or above 30 (but below today) land in "Tomorrow". */
export const DEFAULT_TOMORROW_THRESHOLD = 30;

/** Map TodoColumn string to Dataverse choice value. */
const COLUMN_TO_CHOICE: Record<TodoColumn, number> = {
  Today: 100000000,
  Tomorrow: 100000001,
  Future: 100000002,
};

/** Map Dataverse choice value to TodoColumn string. */
const CHOICE_TO_COLUMN: Record<number, TodoColumn> = {
  100000000: 'Today',
  100000001: 'Tomorrow',
  100000002: 'Future',
};

// ---------------------------------------------------------------------------
// Pure scoring helpers — local copies kept inside the peer package to keep
// the hook self-contained (no cross-package dependency on the Code Page's
// `todoScoreUtils.ts`). The math + weights match the Code Page hook bit-for-bit
// — verified against `src/solutions/SmartTodo/src/utils/todoScoreUtils.ts`.
// ---------------------------------------------------------------------------

const W_PRIORITY = 0.5;
const W_EFFORT = 0.2;
const W_URGENCY = 0.3;

/** Parse an ISO date string defensively. */
function parseDueDate(isoString: string | undefined | null): Date | null {
  if (!isoString) return null;
  const d = new Date(isoString);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** Convert days-until-due into a 0-100 urgency raw score. */
function computeDueDateUrgencyRaw(dueDate: Date | null): number {
  if (!dueDate) return 0;
  const now = new Date();
  const diffMs = dueDate.getTime() - now.getTime();
  const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
  if (diffDays < 0) return 100;
  if (diffDays <= 3) return 80;
  if (diffDays <= 7) return 50;
  if (diffDays <= 10) return 25;
  return 0;
}

/** Composite To Do Score for an item — 0-100, clamped. */
function computeTodoScore(todo: IKanbanTodoLike): number {
  const rawPriority = todo.sprk_priorityscore ?? 50;
  const rawEffort = todo.sprk_effortscore ?? 50;
  const dueDate = parseDueDate(todo.sprk_duedate);
  const rawUrgency = computeDueDateUrgencyRaw(dueDate);

  const raw = rawPriority * W_PRIORITY + (100 - rawEffort) * W_EFFORT + rawUrgency * W_URGENCY;
  return Math.max(0, Math.min(100, Math.round(raw)));
}

// ---------------------------------------------------------------------------
// Pure bucketing helpers (also exported as `bucketTodoItems` for the widget's
// no-mutation render path that doesn't need stateful overrides).
// ---------------------------------------------------------------------------

/** Determine which column an unpinned item belongs to based on its To Do Score. */
function assignColumnByScore(todo: IKanbanTodoLike, todayThreshold: number, tomorrowThreshold: number): TodoColumn {
  const score = computeTodoScore(todo);
  if (score >= todayThreshold) return 'Today';
  if (score >= tomorrowThreshold) return 'Tomorrow';
  return 'Future';
}

/**
 * Read the pinned column's choice value as a number, regardless of whether
 * Dataverse delivered it as a number, a string-of-a-number, or null.
 * Defensive against retrieveMultipleRecords sometimes returning string OData
 * primitives for choice columns.
 */
function pinnedColumnAsNumber(value: number | string | null | undefined): number | null {
  if (value == null) return null;
  if (typeof value === 'number') return value;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

/**
 * Resolve the effective column for an item, respecting pin state.
 * - Pinned items: use their stored sprk_todocolumn (fallback to score-based).
 * - Unpinned items: compute from To Do Score + thresholds.
 */
function resolveColumn(todo: IKanbanTodoLike, todayThreshold: number, tomorrowThreshold: number): TodoColumn {
  const pinnedChoice = pinnedColumnAsNumber(todo.sprk_todocolumn);
  if (todo.sprk_todopinned && pinnedChoice != null) {
    return CHOICE_TO_COLUMN[pinnedChoice] ?? assignColumnByScore(todo, todayThreshold, tomorrowThreshold);
  }
  return assignColumnByScore(todo, todayThreshold, tomorrowThreshold);
}

/**
 * Build the three-column structure from a flat item list. Pure function —
 * safe for the widget's render path. Preserves item order (callers pre-sort).
 */
export function bucketTodoItems<T extends IKanbanTodoLike>(
  items: ReadonlyArray<T>,
  todayThreshold = DEFAULT_TODAY_THRESHOLD,
  tomorrowThreshold = DEFAULT_TOMORROW_THRESHOLD
): IKanbanColumn<T>[] {
  const today: T[] = [];
  const tomorrow: T[] = [];
  const future: T[] = [];

  for (const item of items) {
    const col = resolveColumn(item, todayThreshold, tomorrowThreshold);
    if (col === 'Today') today.push(item);
    else if (col === 'Tomorrow') tomorrow.push(item);
    else future.push(item);
  }

  // R4 task 103 (E-2, 2026-06-18) — column tints (UAT 5):
  //   Today    → light red tint   (tokens.colorPaletteRedBackground1)
  //   Tomorrow → light yellow tint (tokens.colorPaletteYellowBackground1)
  //   Future   → light green tint  (tokens.colorPaletteGreenBackground1)
  //
  // ADR-021 binding: semantic tokens only (no hex). The `Background1` variants
  // are the LIGHTEST in the Fluent v9 palette and read as gentle wash backgrounds
  // — not competing with card content while still cueing column identity.
  // The existing top-border `accentColor` (Border2 variants) stays in place as a
  // sharper accent rail; the tint sits behind it for visual reinforcement.
  //
  // Capital Case labels ('Today'/'Tomorrow'/'Future') are already passed as
  // column.title strings; the `KanbanBoard` columnTitle style does NOT apply
  // text-transform, so they render verbatim. (The widget's legacy `groupTitle`
  // class — which DID have text-transform: uppercase — is no longer referenced
  // by the post-102 render path; remains in the style sheet for back-compat
  // but is dead code from R4-101's grouped-list rendering.)
  return [
    {
      id: 'Today',
      title: 'Today',
      subtitle: `Score ≥ ${todayThreshold}`,
      items: today,
      accentColor: tokens.colorPaletteRedBorder2,
      tintColor: tokens.colorPaletteRedBackground1,
    },
    {
      id: 'Tomorrow',
      title: 'Tomorrow',
      subtitle: `Score ${tomorrowThreshold}–${todayThreshold - 1}`,
      items: tomorrow,
      // 2026-06-19 UAT: yellow accent (not orange/red) per user feedback —
      // matches the yellow tint background; cards inherit this for left-border.
      accentColor: tokens.colorPaletteYellowBorder2,
      tintColor: tokens.colorPaletteYellowBackground1,
      // 2026-06-19 UAT: yellow pill needs DARK text (WCAG contrast).
      // Red + Green keep the default white-on-brand.
      countTextColor: tokens.colorNeutralForeground1,
    },
    {
      id: 'Future',
      title: 'Future',
      subtitle: `Score < ${tomorrowThreshold}`,
      items: future,
      accentColor: tokens.colorPaletteGreenBorder2,
      tintColor: tokens.colorPaletteGreenBackground1,
    },
  ];
}

// ---------------------------------------------------------------------------
// Hook public types
// ---------------------------------------------------------------------------

export interface IUseKanbanColumnsOptions<T extends IKanbanTodoLike> {
  /** Sorted todo items (callers pre-sort by score desc + duedate asc). */
  items: ReadonlyArray<T>;
  /** Score threshold for the "Today" bucket. */
  todayThreshold: number;
  /** Score threshold for the "Tomorrow" bucket. */
  tomorrowThreshold: number;
  /**
   * Optional Dataverse service for persistence. When omitted, mutation
   * callbacks are inert (local optimistic state only). The widget call site
   * omits this — it renders grouped lists with no drag-drop.
   */
  dataverseService?: IKanbanDataverseService;
  /**
   * UAT 2026-06-19 — initial column orders (from user preferences,
   * cross-device persisted). Each key = column id, each value = ordered
   * list of sprk_todoids. Cards present in the array render in that order;
   * cards NOT in the array (newly created since last reorder) get appended.
   * Pass `undefined` (default) for legacy default-sort behavior.
   */
  initialColumnOrders?: Record<string, string[]>;
  /**
   * UAT 2026-06-19 — called when the user reorders cards within a column.
   * Caller persists the new map to user preferences (sprk_userpreference)
   * so the order survives refresh + cross-device. Receives the FULL current
   * columnOrders map after the reorder; caller usually just spread-merges
   * into the prefs.
   */
  onColumnOrdersChange?: (next: Record<string, string[]>) => void;
}

export interface IUseKanbanColumnsResult<T extends IKanbanTodoLike> {
  /** Three columns (Today, Tomorrow, Future) with items assigned. */
  columns: IKanbanColumn<T>[];
  /** Move an item to a target column. Auto-pins and persists when a service is wired. */
  moveItem: (todoId: string, targetColumn: TodoColumn) => void;
  /** Reorder an item within the same column (drag-drop reorder). */
  reorderInColumn: (columnId: string, fromIndex: number, toIndex: number) => void;
  /** Toggle pin state for an item. Persists when a service is wired. */
  togglePin: (todoId: string) => void;
  /** Reassign all unpinned items by current scores. Batch-writes when a service is wired. */
  recalculate: () => void;
  /** True while a recalculate batch write is in progress. */
  isRecalculating: boolean;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useKanbanColumns<T extends IKanbanTodoLike>(
  options: IUseKanbanColumnsOptions<T>
): IUseKanbanColumnsResult<T> {
  const { items, todayThreshold, tomorrowThreshold, dataverseService, initialColumnOrders, onColumnOrdersChange } = options;

  const [isRecalculating, setIsRecalculating] = React.useState(false);

  // UAT 2026-06-19 — Seed from user-preference's columnOrders (cross-device
  // persisted). Subsequent reorderInColumn calls fire onColumnOrdersChange
  // so the caller can persist back to sprk_userpreference. When pref later
  // refetches with a newer order, the seed still wins (we don't merge mid-session
  // to avoid clobbering the user's in-progress drag work).
  const [columnOrders, setColumnOrders] = React.useState<Record<string, string[]>>(
    () => initialColumnOrders ?? {}
  );

  // Hydrate columnOrders ONCE when initialColumnOrders arrives non-empty
  // (the prefs hook starts with {} then resolves after the Dataverse fetch).
  const initialHydratedRef = React.useRef(false);
  React.useEffect(() => {
    if (initialHydratedRef.current) return;
    if (initialColumnOrders && Object.keys(initialColumnOrders).length > 0) {
      setColumnOrders(initialColumnOrders);
      initialHydratedRef.current = true;
    }
  }, [initialColumnOrders]);

  // Local overrides for optimistic column/pin mutations.
  // Key: todoId, Value: { column, pinned } overrides.
  const [overrides, setOverrides] = React.useState<Record<string, { column?: number; pinned?: boolean }>>({});

  // Reconcile overrides when items change (fresh fetch).
  const prevItemsRef = React.useRef(items);
  React.useEffect(() => {
    if (prevItemsRef.current !== items) {
      prevItemsRef.current = items;
      setOverrides(prev => {
        if (Object.keys(prev).length === 0) return prev;
        const remaining: typeof prev = {};
        for (const [todoId, ov] of Object.entries(prev)) {
          const freshItem = items.find(i => i.sprk_todoid === todoId);
          if (!freshItem) continue;
          const freshColumn = pinnedColumnAsNumber(freshItem.sprk_todocolumn);
          const columnMatch = ov.column == null || freshColumn === ov.column;
          const pinnedMatch = ov.pinned == null || freshItem.sprk_todopinned === ov.pinned;
          if (!columnMatch || !pinnedMatch) {
            remaining[todoId] = ov;
          }
        }
        return Object.keys(remaining).length > 0 ? remaining : {};
      });
    }
  }, [items]);

  // Apply overrides to items for column computation.
  const effectiveItems = React.useMemo(() => {
    if (Object.keys(overrides).length === 0) return items;
    return items.map(item => {
      const ov = overrides[item.sprk_todoid];
      if (!ov) return item;
      return {
        ...item,
        ...(ov.column != null ? { sprk_todocolumn: ov.column } : {}),
        ...(ov.pinned != null ? { sprk_todopinned: ov.pinned } : {}),
      } as T;
    });
  }, [items, overrides]);

  // Derive columns (score-based), then apply manual intra-column ordering.
  const columns = React.useMemo<IKanbanColumn<T>[]>(() => {
    const base = bucketTodoItems(effectiveItems, todayThreshold, tomorrowThreshold);
    if (Object.keys(columnOrders).length === 0) return base;

    return base.map(col => {
      const order = columnOrders[col.id];
      if (!order || order.length === 0) return col;
      const itemMap = new Map(col.items.map(i => [i.sprk_todoid, i]));
      const ordered: T[] = [];
      for (const id of order) {
        const item = itemMap.get(id);
        if (item) {
          ordered.push(item);
          itemMap.delete(id);
        }
      }
      // Append items not in the custom order (new items since the reorder).
      for (const item of col.items) {
        if (itemMap.has(item.sprk_todoid)) ordered.push(item);
      }
      return { ...col, items: ordered };
    });
  }, [effectiveItems, todayThreshold, tomorrowThreshold, columnOrders]);

  // ---- reorderInColumn ----
  const reorderInColumn = React.useCallback(
    (columnId: string, fromIndex: number, toIndex: number) => {
      const col = columns.find(c => c.id === columnId);
      if (!col) return;
      const ids = col.items.map(i => i.sprk_todoid);
      const [moved] = ids.splice(fromIndex, 1);
      ids.splice(toIndex, 0, moved);
      setColumnOrders(prev => {
        const next = { ...prev, [columnId]: ids };
        // UAT 2026-06-19 — persist back so order survives refresh + cross-device.
        // Defer the callback so we don't fire during setState (React warns).
        if (onColumnOrdersChange) {
          queueMicrotask(() => onColumnOrdersChange(next));
        }
        return next;
      });
    },
    [columns, onColumnOrdersChange]
  );

  // ---- moveItem ----
  const moveItem = React.useCallback(
    (todoId: string, targetColumn: TodoColumn) => {
      const choiceValue = COLUMN_TO_CHOICE[targetColumn];

      // Optimistic: set column + pin.
      setOverrides(prev => ({
        ...prev,
        [todoId]: { column: choiceValue, pinned: true },
      }));

      if (!dataverseService) {
        // eslint-disable-next-line no-console
        console.warn('[useKanbanColumns] moveItem called without a dataverseService — change is local-only.');
        return;
      }

      // Fire-and-forget persistence (parallel column + pin writes).
      Promise.all([
        dataverseService.updateEventColumn(todoId, choiceValue),
        dataverseService.updateEventPinned(todoId, true),
      ]).then(([colResult, pinResult]) => {
        if (!colResult.success || !pinResult.success) {
          // eslint-disable-next-line no-console
          console.warn('[useKanbanColumns] moveItem write failed', {
            colResult,
            pinResult,
          });
        }
      });
    },
    [dataverseService]
  );

  // ---- togglePin ----
  const togglePin = React.useCallback(
    (todoId: string) => {
      const item = effectiveItems.find(i => i.sprk_todoid === todoId);
      if (!item) return;

      const currentPinned = item.sprk_todopinned ?? false;
      const newPinned = !currentPinned;

      setOverrides(prev => {
        const existing = prev[todoId] ?? {};
        return {
          ...prev,
          [todoId]: { ...existing, pinned: newPinned },
        };
      });

      if (!dataverseService) {
        // eslint-disable-next-line no-console
        console.warn('[useKanbanColumns] togglePin called without a dataverseService — change is local-only.');
        return;
      }

      dataverseService.updateEventPinned(todoId, newPinned).then(result => {
        if (!result.success) {
          // eslint-disable-next-line no-console
          console.warn('[useKanbanColumns] togglePin write failed', result);
        }
      });
    },
    [effectiveItems, dataverseService]
  );

  // ---- recalculate ----
  const recalculate = React.useCallback(() => {
    setIsRecalculating(true);

    const updates: Array<{ eventId: string; column: number }> = [];

    for (const item of effectiveItems) {
      if (item.sprk_todopinned) continue;

      const computedColumn = assignColumnByScore(item, todayThreshold, tomorrowThreshold);
      const computedChoice = COLUMN_TO_CHOICE[computedColumn];
      const currentChoice = pinnedColumnAsNumber(item.sprk_todocolumn);

      if (currentChoice !== computedChoice) {
        updates.push({ eventId: item.sprk_todoid, column: computedChoice });
      }
    }

    if (updates.length === 0) {
      setIsRecalculating(false);
      return;
    }

    // Optimistic: apply all column changes locally.
    setOverrides(prev => {
      const next = { ...prev };
      for (const u of updates) {
        const existing = next[u.eventId] ?? {};
        next[u.eventId] = { ...existing, column: u.column };
      }
      return next;
    });

    if (!dataverseService) {
      // eslint-disable-next-line no-console
      console.warn('[useKanbanColumns] recalculate called without a dataverseService — changes are local-only.');
      setIsRecalculating(false);
      return;
    }

    dataverseService.batchUpdateEventColumns(updates).then(result => {
      if (!result.success) {
        // eslint-disable-next-line no-console
        console.error('[useKanbanColumns] recalculate batch write failed, rolling back', result);
        setOverrides(prev => {
          const next = { ...prev };
          for (const u of updates) {
            const existing = next[u.eventId];
            if (!existing) continue;
            const { column: _removed, ...rest } = existing;
            if (Object.keys(rest).length === 0) {
              delete next[u.eventId];
            } else {
              next[u.eventId] = rest;
            }
          }
          return next;
        });
      }
      setIsRecalculating(false);
    });
  }, [effectiveItems, todayThreshold, tomorrowThreshold, dataverseService]);

  return { columns, moveItem, reorderInColumn, togglePin, recalculate, isRecalculating };
}
