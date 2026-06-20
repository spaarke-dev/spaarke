/**
 * SmartTodoWidget — host-agnostic SpaarkeAi workspace widget for sprk_todo.
 *
 * R4 task 103 (E-2 — toolbar polish, 2026-06-18):
 *   - SEARCH AS ICON (UAT 1): The toolbar SearchBox is replaced with a
 *     `<ToggleButton>` (Search20Regular icon, right-aligned with the other
 *     action icons). When the user toggles ON, a horizontal SearchBox row
 *     renders BELOW the toolbar; toggling OFF collapses it AND clears the
 *     query so the next open starts fresh.
 *   - OPEN ALWAYS-ENABLED (UAT 2): The Open button is no longer disabled by
 *     selection count. Behaviour:
 *       * 0 selected → `onOpenTodo()` called with NO todoId → host shim
 *         opens the SmartTodo Code Page at its DEFAULT view (3-col Kanban,
 *         no auto-modal). `useLaunchContext` returns `undefined` for the
 *         landing URL since no recognised launch action is present.
 *       * 1+ selected → `onOpenTodo(firstSelectedId)` → host shim dispatches
 *         the existing `openTodo` launch context, app auto-mounts the modal.
 *   - COLUMN TINTS + CAPITAL CASE LABELS (UAT 5): Today/Tomorrow/Future
 *     columns now carry a light Fluent v9 background tint
 *     (`colorPaletteRedBackground1` / `YellowBackground1` / `GreenBackground1`)
 *     in addition to their existing top-border accent. Labels are already
 *     Capital Case in `bucketTodoItems()`; the `<KanbanBoard>` columnTitle
 *     style does NOT apply text-transform, so they render verbatim.
 *   - INLINE QUICK-ADD (UAT 7): The toolbar's left slot now contains
 *     `[+ wizard] [QuickAdd Input] [Add btn]`. Typing a title + clicking Add
 *     (or pressing Enter) calls `webApi.createRecord('sprk_todo', { sprk_name })`
 *     directly — no wizard, no modal, no roundtrip to a separate Code Page.
 *     On success: clear input + refetch. On error: surface a MessageBar with
 *     a "Open full wizard" link that delegates to the existing `onAddTodo`
 *     callback (the full wizard handles required fields the bare title can't).
 *
 * R4 task 099 (W-1 — widget chrome consolidation + Pattern D alignment, 2026-06-18):
 *   - REMOVED `<PaneHeader>` entirely. Per the 2026-06-18 widget-parity audit,
 *     the widget's PaneHeader was rendering a SECOND title bar on top of the
 *     SectionPanel title that the LegalWorkspace shim already provides (mirrors
 *     Calendar's canonical Pattern D — Calendar widget has no header of its own).
 *   - ADDED a single `<Toolbar>` row containing `[SearchBox, +, Open, refresh]`
 *     (audit issue 5 + 3). This is the widget's SOLE chrome row. (R4-103
 *     later reorganised this to `[+ wizard] [QuickAdd] [Add] | (spacer) |
 *     [Open] [Refresh] [Orient] [Search icon]` per UAT round 2.)
 *   - ADDED SearchBox with local debounced state — filters items in-memory by
 *     case-insensitive substring match against `sprk_name` and `sprk_description`.
 *     No OData round-trip (sufficient for the widget's bounded result set).
 *   - ADDED single-card selection state so the Open button is selection-aware
 *     (disabled when 0 selected; enabled when 1 selected — mirrors the Code Page
 *     `SelectionAwareToolbar` pattern at a smaller scope). (R4-103 made Open
 *     always-enabled per UAT 2.)
 *   - RENAMED default `title` prop from "My To Do List" to "Smart To Do".
 *     Title is now used only for `aria-label` on the root region — the visible
 *     title comes from the host's SectionPanel (LegalWorkspace shim) or, in
 *     Direct widget mounts without a SectionPanel, the consumer must provide
 *     their own title chrome around the widget.
 *
 * R4 task 020 (Pattern D dual-use rebuild — 2026-06-10):
 *   - Initial host-agnostic widget. ZERO subscription to LegalWorkspace-internal
 *     contexts (e.g., FeedTodoSyncContext). Cross-block sync is wired by the host
 *     shim via the `feedSync` prop (see types/todo.ts IFeedSyncBridge).
 *   - Mirrors the proven `CalendarWorkspaceWidget` Pattern D structure from
 *     `@spaarke/events-components` (R3 task 115).
 *
 * Query (spec.md FR-02):
 *   - Entity: `sprk_todo`
 *   - Filter: statecode eq 0 AND (statuscode eq 1 or statuscode eq 659490001)
 *   - Optional regarding filter; owner fallback when no regarding context.
 *
 * 6-layout compatibility (spec.md FR-04):
 *   - Body flexes with no fixed widths; cards stack vertically; truncation
 *     handles narrow panes.
 *   - All colors via Fluent v9 semantic tokens (light / dark / high-contrast
 *     all derived automatically per ADR-021).
 *
 * NO PaneEventBus dispatch (ADR-030):
 *   - This is a data-only widget; it does not own workspace-level lifecycle
 *     events. If the host needs widget-load telemetry, the shim wires that
 *     around the widget via the host's existing bus.
 *
 * Standards:
 *   - ADR-021 (Fluent v9 + Griffel + semantic tokens — no v8, no inline styles)
 *   - ADR-012 (peer package mirrors `@spaarke/events-components`)
 *   - spec.md FR-02 (query targets sprk_todo + regarding context + statuscode)
 *   - spec.md FR-04 (mounts cleanly under all 6 workspace layouts)
 *
 * See also:
 *   - `projects/smart-todo-r4/notes/d-widget-parity-audit-2026-06-18.md` — W-1 audit
 *   - `projects/smart-todo-r4/notes/widget-surface-audit.md` — R4-001 audit
 *   - `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`
 *     — canonical Pattern D worked example (R3 task 115 / R4 task 033b).
 */

import * as React from 'react';
import {
  Body1,
  Button,
  Input,
  Link,
  MessageBar,
  MessageBarBody,
  SearchBox,
  Spinner,
  Text,
  ToggleButton,
  Toolbar,
  Tooltip,
  type SearchBoxChangeEvent,
  type InputOnChangeData,
} from '@fluentui/react-components';
import { ArrowClockwiseRegular, Add20Regular, Open20Regular, Search20Regular } from '@fluentui/react-icons';
import {
  OrientationToggle,
  type Orientation,
} from '../../../../Spaarke.UI.Components/src/components/OrientationToggle';
import { MicrosoftToDoIcon } from '../../../../Spaarke.UI.Components/src/icons/MicrosoftToDoIcon';

import { useSmartTodoWidgetStyles } from './SmartTodoWidget.styles';
import type { IFeedSyncBridge, IRegardingContext, ITodoRecord, IWebApi } from '../../types/todo';
import type { IKanbanDataverseService } from '../../types/kanban';
import { SmartTodoKanban } from '../../components/SmartTodoKanban';

// ---------------------------------------------------------------------------
// Public statuscode constants (R3 task 009 / OS-1)
// ---------------------------------------------------------------------------

export const TODO_STATUSCODE_OPEN = 1 as const;
export const TODO_STATUSCODE_IN_PROGRESS = 659490001 as const;
export const TODO_STATUSCODE_COMPLETED = 2 as const;
export const TODO_STATUSCODE_DISMISSED = 659490002 as const;

// ---------------------------------------------------------------------------
// Search debounce — local SearchBox text is held by `searchQuery` state and
// flushed to the filter applied to the rendered list after this delay. 150ms
// is short enough that the user perceives results as live but long enough to
// skip per-keystroke filter recomputes on long lists.
// ---------------------------------------------------------------------------

const SEARCH_DEBOUNCE_MS = 150;

// ---------------------------------------------------------------------------
// OData query builder — host-agnostic; takes the inputs the host injects.
// ---------------------------------------------------------------------------

/**
 * Build the active-todo $select+$filter query targeting `sprk_todo`.
 *
 * Per R4 spec FR-02 + ownership-alignment fix 2026-06-19:
 *   - statecode eq 0
 *   - statuscode in (Open, In Progress)
 *   - regarding context filter (when supplied)
 *   - assignee filter (when supplied and no regarding context) —
 *     aligned to the SmartTodoApp Code Page (R4-031 / FR-07 / OD-2). Previously
 *     used `_ownerid_value` which mismatched the Code Page and caused records
 *     created on one surface to be invisible on the other (caught by the
 *     spaarke-prototype/smart-todo-r4-uat harness 2026-06-19). `ownerid` is
 *     BU-default and not user-meaningful; `sprk_assignedto` is the canonical
 *     "this is YOUR to-do" lookup across the Spaarke surfaces.
 *
 * Zero `sprk_event` references. Zero `sprk_todoflag` references.
 */
export function buildSmartTodoQuery(opts: {
  regardingContext?: IRegardingContext | null;
  userId?: string;
  businessUnitId?: string;
  scope?: 'my' | 'all';
  top?: number;
}): string {
  const top = opts.top ?? 100;

  // Select only what the widget renders + a couple of sort keys.
  const select = [
    'sprk_todoid',
    'sprk_name',
    'sprk_description',
    'sprk_duedate',
    'sprk_priorityscore',
    'sprk_effortscore',
    'sprk_todocolumn',
    'sprk_todopinned',
    'statecode',
    'statuscode',
    '_sprk_assignedto_value',
    'createdon',
    'modifiedon',
  ].join(',');

  // Active clause is invariant — Open + In Progress.
  const activeClause = `statecode eq 0 and (statuscode eq ${TODO_STATUSCODE_OPEN} or statuscode eq ${TODO_STATUSCODE_IN_PROGRESS})`;

  // Build context clause — regarding takes precedence; falls back to assignee.
  // `scope='all'` retains the BU-OR-assignee expansion (rare workspace mode).
  let contextClause = '';
  if (opts.regardingContext) {
    const lookupField = entityLogicalNameToLookup(opts.regardingContext.entityLogicalName);
    if (lookupField) {
      contextClause = `${lookupField} eq ${opts.regardingContext.recordId}`;
    }
  } else if (opts.scope === 'all' && opts.businessUnitId) {
    contextClause = `(_sprk_assignedto_value eq ${opts.userId ?? '00000000-0000-0000-0000-000000000000'} or _owningbusinessunit_value eq ${opts.businessUnitId})`;
  } else if (opts.userId) {
    contextClause = `_sprk_assignedto_value eq ${opts.userId}`;
  }

  const filter = contextClause ? `${contextClause} and ${activeClause}` : activeClause;
  const orderby = 'sprk_priorityscore desc,sprk_duedate asc';

  return `?$select=${select}&$filter=${encodeURIComponent(filter)}&$orderby=${encodeURIComponent(orderby)}&$top=${top}`;
}

/**
 * Map a regarding entity's logical name to its corresponding `_value` lookup
 * column on `sprk_todo`. Mirrors the R3 polymorphic-resolver field naming.
 *
 * Returns null if the entity isn't a known regarding target — the caller
 * should treat that as "no regarding filter applied".
 */
function entityLogicalNameToLookup(entityLogicalName: string): string | null {
  switch (entityLogicalName) {
    case 'sprk_matter':
      return '_sprk_regardingmatter_value';
    case 'sprk_project':
      return '_sprk_regardingproject_value';
    case 'sprk_event':
      return '_sprk_regardingevent_value';
    case 'sprk_communication':
      return '_sprk_regardingcommunication_value';
    case 'sprk_workassignment':
      return '_sprk_regardingworkassignment_value';
    case 'sprk_invoice':
      return '_sprk_regardinginvoice_value';
    case 'sprk_budget':
      return '_sprk_regardingbudget_value';
    case 'sprk_analysis':
      return '_sprk_regardinganalysis_value';
    case 'organization':
      return '_sprk_regardingorganization_value';
    case 'contact':
      return '_sprk_regardingcontact_value';
    case 'sprk_document':
      return '_sprk_regardingdocument_value';
    default:
      return null;
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SmartTodoWidgetProps {
  /** Xrm.WebApi from the host (passed through from PCF / Code Page context). */
  webApi: IWebApi;
  /** Current user's systemuserid GUID — used for owner fallback filter. */
  userId?: string;
  /** Workspace's regarding context (what record drives the filter). */
  regardingContext?: IRegardingContext | null;
  /** Workspace scope ('my' | 'all'). Default 'my'. */
  scope?: 'my' | 'all';
  /** Business unit ID — used when scope === 'all'. */
  businessUnitId?: string;
  /**
   * Optional cross-block sync bridge. The LW shim wires this to its local
   * FeedTodoSyncContext; SpaarkeAi Direct-widget consumers may omit it.
   */
  feedSync?: IFeedSyncBridge;
  /**
   * Title text. Used for the root region's `aria-label` AND (when
   * `showTitle` is true) for the visible title row above the toolbar.
   * Defaults to "Smart To Do".
   */
  title?: string;
  /**
   * UAT 2026-06-19: show a visible title row above the toolbar with the
   * brand icon + title text. Defaults to `true` for standalone / harness
   * consumption. Hosts that render their own section title (e.g.,
   * LegalWorkspace SectionPanel) should pass `false` to avoid duplication.
   */
  showTitle?: boolean;
  /** Notify the host of the active count (for badge / tab counter). */
  onBadgeCountChange?: (count: number) => void;
  /** Expose the refetch trigger to the host (for header refresh button). */
  onRefetchReady?: (refetch: () => void) => void;
  /**
   * Open handler — called when the user clicks the Open button OR a card's
   * Open icon. Hosts wire to their navigation surface.
   *
   * R4 task 103 (E-2, 2026-06-18) — UAT 2 made Open always-enabled. When
   * called with NO `todoId` (toolbar Open with 0 cards selected), the host
   * shim should open the SmartTodo Code Page at its DEFAULT view (no
   * `openTodo` launch discriminator → `useLaunchContext` returns undefined
   * → app renders the Kanban). When called WITH a `todoId` (1+ selected, or
   * per-card Open icon), the shim dispatches the existing `openTodo` launch
   * context so the app auto-mounts `<SmartTodoModal>` on that record.
   *
   * Hosts must accept the no-`todoId` case; passing the param through `undefined`
   * to the existing `openTodo`-launch URL construction is the correct behavior
   * (the Code Page's `useLaunchContext` returns `undefined` for missing keys
   * — see `parseLaunchContextFromSearch` in `useLaunchContext.ts`).
   */
  onOpenTodo?: (todoId?: string) => void;
  /** Optional "+ New" handler — opens the host's CreateTodoWizard. */
  onAddTodo?: () => void;
  /**
   * Optional placeholder for the inline QuickAdd Input. Defaults to
   * "Quick add a to-do…". Hosts can override for localised UX or to
   * emphasise the quick-add affordance differently in standalone vs
   * embedded mounts.
   */
  quickAddPlaceholder?: string;
}

// ---------------------------------------------------------------------------
// Widget
// ---------------------------------------------------------------------------

export const SmartTodoWidget: React.FC<SmartTodoWidgetProps> = ({
  webApi,
  userId,
  regardingContext,
  scope,
  businessUnitId,
  feedSync,
  title = 'Smart To Do',
  showTitle = true,
  onBadgeCountChange,
  onRefetchReady,
  onOpenTodo,
  onAddTodo,
  quickAddPlaceholder = 'Quick add a to-do…',
}) => {
  const styles = useSmartTodoWidgetStyles();

  const [items, setItems] = React.useState<ITodoRecord[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(true);
  const [error, setError] = React.useState<string | null>(null);
  const [fetchKey, setFetchKey] = React.useState(0);

  // R4 task 102 (E-1, 2026-06-18) — selection model upgraded from single
  // (`string | null`) to MULTI (`Set<string>`) per UAT issue 6 (cards need
  // multi-select parity with the app's `<KanbanCard>`). The hoisted
  // `<KanbanCard>` (via `<SmartTodoKanban>`) renders a per-card checkbox
  // bound to this Set + the `toggleSelect` callback. The Open button is
  // enabled when at least one card is selected (any of N opens the modal on
  // the FIRST id — matches app's selection-aware toolbar pattern).
  const [selectedIds, setSelectedIds] = React.useState<ReadonlySet<string>>(() => new Set());

  // UAT 2026-06-19 — collapsible columns in both orientations.
  // Default: all columns expanded (matches Code Page default after the same
  // UAT round). User collapses via column-header click.
  const [collapsedColumns, setCollapsedColumns] = React.useState<ReadonlySet<string>>(() => new Set());
  const handleToggleCollapse = React.useCallback((columnId: string) => {
    setCollapsedColumns((prev) => {
      const next = new Set(prev);
      if (next.has(columnId)) next.delete(columnId);
      else next.add(columnId);
      return next;
    });
  }, []);

  // R4 task 102 (E-1, 2026-06-18) — local orientation state. Kept WIDGET-LOCAL
  // (not persisted via `useUserPreferences`) because the widget mounts in
  // multiple workspace contexts and forcing per-context Dataverse round-trips
  // would inflate cold-start time. The Code Page (which is the user's "Smart
  // To Do home") persists orientation via `useUserPreferences` (R4-071) —
  // that's the canonical persistence point. Default 'horizontal' matches the
  // Code Page default + the original app behaviour.
  const [orientation, setOrientation] = React.useState<Orientation>('horizontal');

  // SearchBox — local controlled value debounced into `appliedQuery` which
  // drives the in-memory filter.
  const [searchQuery, setSearchQuery] = React.useState<string>('');
  const [appliedQuery, setAppliedQuery] = React.useState<string>('');

  // R4 task 103 (E-2, 2026-06-18) — search expand/collapse state (UAT 1).
  // The search icon toggles this; when true, the SearchBox renders in a row
  // BELOW the toolbar (not in the toolbar itself). Toggling OFF clears the
  // query so the next open starts fresh — keeps "search" and "filter is
  // active" perceptually coupled. Default closed for low visual weight.
  const [isSearchExpanded, setIsSearchExpanded] = React.useState<boolean>(false);

  // UAT 2026-06-19 — three-field inline quick-add: Title + Due Date + Assigned To + Add.
  // Replaces the prior single-field title-only quick-add. Each field is
  // independently controlled; submission sends all three to webApi.createRecord.
  // Defaults: due date = today (end-of-day local), assigned to = widget's userId prop.
  const todayISODate = React.useMemo(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }, []);
  const [quickAddTitle, setQuickAddTitle] = React.useState<string>('');
  const [quickAddDueDate, setQuickAddDueDate] = React.useState<string>(todayISODate);
  const [quickAddAssignedTo, setQuickAddAssignedTo] = React.useState<string>(userId ?? '');
  const [quickAddError, setQuickAddError] = React.useState<string | null>(null);
  const [isQuickAdding, setIsQuickAdding] = React.useState<boolean>(false);

  // Stable refetch — bumps the fetchKey so the effect re-runs.
  const refetch = React.useCallback(() => {
    setFetchKey(k => k + 1);
  }, []);

  // -------------------------------------------------------------------------
  // Search debounce — flush `searchQuery` to `appliedQuery` after a short
  // delay so we don't re-filter on every keystroke. 150ms feels live but
  // skips redundant work for fast typers.
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    if (searchQuery === appliedQuery) return;
    const handle = window.setTimeout(() => {
      setAppliedQuery(searchQuery);
    }, SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [searchQuery, appliedQuery]);

  // -------------------------------------------------------------------------
  // Query effect
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setError(null);

    const query = buildSmartTodoQuery({
      regardingContext,
      userId,
      businessUnitId,
      scope,
    });

    webApi
      .retrieveMultipleRecords('sprk_todo', query)
      .then(result => {
        if (cancelled) return;
        const entities = (result.entities ?? []) as ITodoRecord[];
        setItems(entities);
        setIsLoading(false);
        setError(null);
      })
      .catch((err: Error) => {
        if (cancelled) return;
        // eslint-disable-next-line no-console
        console.warn('[SmartTodoWidget] sprk_todo fetch failed:', err);
        setError(err?.message ?? 'Unable to load to-do items. Please try again.');
        setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [
    webApi,
    userId,
    businessUnitId,
    scope,
    regardingContext?.entityLogicalName,
    regardingContext?.recordId,
    fetchKey,
  ]);

  // -------------------------------------------------------------------------
  // FeedSync bridge — host-owned subscription forwarded as props.
  //
  // Per R4 task 020 user decision: the widget does NOT subscribe to
  // FeedTodoSyncContext directly. The LW shim subscribes and passes a bridge
  // here so external lifecycle events (e.g., todo dismissed from ActivityFeed)
  // trigger a refetch.
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    if (!feedSync) return;
    const unsubscribe = feedSync.subscribe((todoId, isActive) => {
      // Naive policy: when ANY lifecycle event fires for a todo this widget
      // is showing (or one that just became active), refetch. The widget
      // doesn't try surgical state edits — refetch is fast (single OData) and
      // keeps the local state aligned with Dataverse truth.
      const inList = items.some(t => t.sprk_todoid === todoId);
      if (!isActive && inList) {
        refetch();
      } else if (isActive) {
        refetch();
      }
    });
    return unsubscribe;
  }, [feedSync, items, refetch]);

  // -------------------------------------------------------------------------
  // Expose refetch + count to host
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    onRefetchReady?.(refetch);
  }, [onRefetchReady, refetch]);

  React.useEffect(() => {
    onBadgeCountChange?.(items.length);
  }, [items.length, onBadgeCountChange]);

  // -------------------------------------------------------------------------
  // In-memory search filter — case-insensitive substring match across
  // `sprk_name` (subject) and `sprk_description`. Cheap to include both
  // since `description` is already in the $select.
  // -------------------------------------------------------------------------

  const filteredItems = React.useMemo(() => {
    const q = appliedQuery.trim().toLowerCase();
    if (!q) return items;
    return items.filter(item => {
      const name = (item.sprk_name ?? '').toLowerCase();
      const desc = (item.sprk_description ?? '').toLowerCase();
      return name.includes(q) || desc.includes(q);
    });
  }, [items, appliedQuery]);

  // Prune stale selections when the visible list changes (search filter,
  // refetch removed an item) so the multi-select Set never holds ids that
  // aren't currently rendered.
  React.useEffect(() => {
    setSelectedIds(prev => {
      if (prev.size === 0) return prev;
      const visibleIds = new Set(filteredItems.map(t => t.sprk_todoid));
      let changed = false;
      const next = new Set<string>();
      for (const id of prev) {
        if (visibleIds.has(id)) {
          next.add(id);
        } else {
          changed = true;
        }
      }
      return changed ? next : prev;
    });
  }, [filteredItems]);

  // -------------------------------------------------------------------------
  // Handlers
  // -------------------------------------------------------------------------

  const handleSearchChange = React.useCallback((_e: SearchBoxChangeEvent, data: InputOnChangeData) => {
    setSearchQuery(data.value);
  }, []);

  // R4 task 102 (E-1, 2026-06-18) — multi-select toggle. The hoisted
  // `<KanbanCard>` checkbox dispatches this for every check/uncheck. The
  // resulting Set drives both the card's selection state AND the Open button's
  // enabled state.
  const handleToggleSelect = React.useCallback((todoId: string) => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(todoId)) {
        next.delete(todoId);
      } else {
        next.add(todoId);
      }
      return next;
    });
  }, []);

  const handleOpenSelected = React.useCallback(() => {
    if (!onOpenTodo) return;
    // R4 task 103 (E-2, 2026-06-18) — UAT 2 made Open always-enabled:
    //   - 0 selected → call onOpenTodo() with NO todoId — host shim opens
    //     the SmartTodo Code Page at its default Kanban view (no openTodo
    //     launch discriminator → useLaunchContext returns undefined).
    //   - 1+ selected → call onOpenTodo(first) — host shim dispatches the
    //     existing openTodo launch context (R4-100), app auto-mounts modal.
    if (selectedIds.size === 0) {
      onOpenTodo();
      return;
    }
    const first = selectedIds.values().next().value;
    if (first) {
      onOpenTodo(first);
    }
  }, [selectedIds, onOpenTodo]);

  // R4 task 103 (E-2, 2026-06-18) — search expand toggle handler (UAT 1).
  // Toggling OFF clears both the live and applied query so the filter resets
  // — visible "search is closed" === "no filter active".
  const handleToggleSearch = React.useCallback(() => {
    setIsSearchExpanded(prev => {
      const next = !prev;
      if (!next) {
        setSearchQuery('');
        setAppliedQuery('');
      }
      return next;
    });
  }, []);

  // UAT 2026-06-19 — three-field quick-add handlers.
  const handleQuickAddTitleChange = React.useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setQuickAddTitle(data.value);
      if (quickAddError) setQuickAddError(null);
    },
    [quickAddError]
  );

  const handleQuickAddDueDateChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setQuickAddDueDate(e.target.value);
    },
    []
  );

  const handleQuickAddAssignedToChange = React.useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setQuickAddAssignedTo(data.value);
    },
    []
  );

  const submitQuickAdd = React.useCallback(async () => {
    const title = quickAddTitle.trim();
    if (!title) return;
    if (!webApi.createRecord) {
      // eslint-disable-next-line no-console
      console.warn('[SmartTodoWidget] quickAdd invoked without webApi.createRecord — input ignored.');
      return;
    }
    setIsQuickAdding(true);
    setQuickAddError(null);

    // Build payload from the three-field quick-add. Per ownership-alignment
    // (2026-06-19), `sprk_assignedto@odata.bind` is required for the record
    // to appear in both surfaces (widget + Code Page filter by assignedto).
    const payload: Record<string, unknown> = { sprk_name: title };
    const assignedToId = quickAddAssignedTo.trim() || userId || '';
    if (assignedToId) {
      payload['sprk_assignedto@odata.bind'] = `/systemusers(${assignedToId})`;
    }
    if (quickAddDueDate) {
      // Date input gives YYYY-MM-DD; treat as end-of-day local.
      const [y, m, d] = quickAddDueDate.split('-').map(Number);
      const dt = new Date(y, m - 1, d, 23, 59, 0);
      payload['sprk_duedate'] = dt.toISOString();
    }

    try {
      await webApi.createRecord('sprk_todo', payload);
      setQuickAddTitle('');
      // Keep due date + assigned-to defaults for the next entry (faster repeat).
      refetch();
    } catch (err) {
      const message = err instanceof Error && err.message ? err.message : 'Could not create the to-do.';
      // eslint-disable-next-line no-console
      console.warn('[SmartTodoWidget] quickAdd create failed:', err);
      setQuickAddError(message);
    } finally {
      setIsQuickAdding(false);
    }
  }, [quickAddTitle, quickAddDueDate, quickAddAssignedTo, webApi, userId, refetch]);

  const handleQuickAddClick = React.useCallback(() => {
    void submitQuickAdd();
  }, [submitQuickAdd]);

  const handleQuickAddKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === 'Enter') {
        ev.preventDefault();
        void submitQuickAdd();
      }
    },
    [submitQuickAdd]
  );

  // "Open full wizard" recovery link in the quick-add error MessageBar
  // delegates to the existing wizard launcher. Closes the error UX loop —
  // user is never stuck if Dataverse rejects the bare-title create.
  const handleOpenWizardFromError = React.useCallback(() => {
    setQuickAddError(null);
    setQuickAddTitle('');
    onAddTodo?.();
  }, [onAddTodo]);

  // -------------------------------------------------------------------------
  // R4 task 102 (E-1, 2026-06-18) — Dataverse service adapter for the hoisted
  // Kanban hook's column / pin persistence path. Wraps the host-injected
  // `webApi.updateRecord` (when available) so drag-drop and pin toggles
  // persist to `sprk_todo`. When `updateRecord` isn't provided (legacy host),
  // the hook falls back to local-only mutations + a console warning — drag is
  // still visually responsive, just non-durable.
  //
  // The adapter is memoised on `webApi` so the hook's persistence callbacks
  // keep stable identity across renders.
  // -------------------------------------------------------------------------

  const dataverseService = React.useMemo<IKanbanDataverseService | undefined>(() => {
    if (!webApi.updateRecord) return undefined;
    const update = webApi.updateRecord.bind(webApi);
    return {
      async updateEventColumn(todoId, column) {
        try {
          await update('sprk_todo', todoId, { sprk_todocolumn: column });
          return { success: true };
        } catch (err) {
          // eslint-disable-next-line no-console
          console.warn('[SmartTodoWidget] updateEventColumn failed', err);
          return { success: false };
        }
      },
      async updateEventPinned(todoId, pinned) {
        try {
          await update('sprk_todo', todoId, { sprk_todopinned: pinned });
          return { success: true };
        } catch (err) {
          // eslint-disable-next-line no-console
          console.warn('[SmartTodoWidget] updateEventPinned failed', err);
          return { success: false };
        }
      },
      async batchUpdateEventColumns(updates) {
        let allOk = true;
        for (const u of updates) {
          try {
            await update('sprk_todo', u.eventId, { sprk_todocolumn: u.column });
          } catch (err) {
            // eslint-disable-next-line no-console
            console.warn('[SmartTodoWidget] batchUpdateEventColumns row failed', err);
            allOk = false;
          }
        }
        return { success: allOk };
      },
    };
  }, [webApi]);

  // -------------------------------------------------------------------------
  // R4 task 103 (E-2, 2026-06-18) — Open is ALWAYS-ENABLED (UAT 2). Wave D
  // (R4-099) made it selection-aware (disabled when 0 selected); R4-102 made
  // it permissive (1..N); R4-103 removes the disable entirely so users can
  // always "just open the app" — with no selection, the app lands on its
  // default Kanban view; with selection, on the first selected record's
  // modal. See `handleOpenSelected` for the branching.
  // -------------------------------------------------------------------------

  // Tooltip text reflects which mode Open will use, so the affordance is
  // never opaque even when the button is always live.
  const openTooltip =
    selectedIds.size === 0
      ? 'Open Smart To Do'
      : selectedIds.size === 1
        ? 'Open selected to-do'
        : `Open first selected to-do (${selectedIds.size} selected)`;

  // QuickAdd is only available when the host's webApi exposes createRecord.
  // When absent (legacy hosts, read-only mounts), the QuickAdd Input + Add
  // button are suppressed; the `+` wizard button remains as the only create
  // affordance.
  const quickAddAvailable = typeof webApi.createRecord === 'function';
  const quickAddDisabled = quickAddTitle.trim().length === 0 || isQuickAdding;

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <div
      className={styles.root}
      role="region"
      aria-label={`${title}, ${filteredItems.length} item${filteredItems.length === 1 ? '' : 's'}`}
    >
      {/*
        R4 task 103 (E-2, 2026-06-18) — toolbar layout reorganised to:
          LEFT:  [+ wizard] [QuickAdd Input] [Add btn]
          (spacer)
          RIGHT: [Open] [Refresh] [Orient] [Search icon toggle]

        Tab order matches visual order. All icon-only buttons carry both an
        `aria-label` AND a `<Tooltip relationship="label">` per Fluent v9
        accessibility conventions.
      */}
      {/* ── Title row (UAT 2026-06-19): brand icon + "Smart To Do" text in
            its own row above the toolbar. Mirrors the Code Page Header's
            title row for chrome uniformity. Suppressed via `showTitle={false}`
            when host (e.g., LegalWorkspace SectionPanel) already renders a
            section title. */}
      {showTitle && (
        <div className={styles.titleRow}>
          <MicrosoftToDoIcon size={20} active />
          <Text size={400} weight="semibold" as="h1" className={styles.titleText}>
            {title}
          </Text>
        </div>
      )}

      <Toolbar aria-label="Smart To Do toolbar" size="small" className={styles.toolbar}>
        {/* ── LEFT: QuickAdd only (UAT 2026-06-19: '+ wizard' button removed
             per user feedback — quick-add is the sole create affordance in
             the widget toolbar. The full-form wizard remains reachable from
             the quick-add error MessageBar's 'Open full wizard' link, AND
             from the parent-form ribbon / Outlook ribbon entry points.) ──── */}
        <div className={styles.toolbarLeft}>
          {quickAddAvailable && (
            <>
              {/* UAT 2026-06-19: three-field quick-add — Title + Due Date + Assigned To + Add. */}
              <Input
                size="small"
                value={quickAddTitle}
                placeholder={quickAddPlaceholder}
                onChange={handleQuickAddTitleChange}
                onKeyDown={handleQuickAddKeyDown}
                aria-label="To-do name"
                disabled={isQuickAdding}
                className={styles.quickAddInput}
              />
              <input
                type="date"
                value={quickAddDueDate}
                onChange={handleQuickAddDueDateChange}
                disabled={isQuickAdding}
                aria-label="Due date"
                className={styles.quickAddDateInput}
              />
              <Input
                size="small"
                value={quickAddAssignedTo}
                onChange={handleQuickAddAssignedToChange}
                placeholder="Assigned to (GUID or name)"
                disabled={isQuickAdding}
                aria-label="Assigned to"
                className={styles.quickAddAssignedInput}
              />
              <Tooltip content="Add to-do (Enter)" relationship="label">
                <Button
                  appearance="primary"
                  size="small"
                  onClick={handleQuickAddClick}
                  disabled={quickAddDisabled}
                  aria-label="Add to-do"
                >
                  Add
                </Button>
              </Tooltip>
            </>
          )}
        </div>

        {/* ── SPACER ──────────────────────────────────────────────────── */}
        <div className={styles.toolbarSpacer} />

        {/* ── RIGHT: actions OR inline filter input (UAT 2026-06-19) ────
              When Filter is toggled OFF, show the action cluster (Open /
              Refresh / Orient / Filter icon).
              When Filter is toggled ON, the action cluster slides out (hidden)
              and a SearchBox slides in to its place — filter field is INLINE
              in the toolbar, not in a separate row below. Click the Filter
              icon again to slide back to actions. */}
        <div className={styles.toolbarActions}>
          {isSearchExpanded ? (
            <>
              <SearchBox
                value={searchQuery}
                placeholder="Filter to-dos…"
                onChange={handleSearchChange}
                aria-label="Filter to-dos"
                size="small"
                className={styles.inlineFilterBox}
                autoFocus
              />
              <Tooltip content="Close filter" relationship="label">
                <ToggleButton
                  appearance="subtle"
                  size="small"
                  icon={<Search20Regular />}
                  checked
                  onClick={handleToggleSearch}
                  aria-label="Close filter"
                  aria-expanded
                />
              </Tooltip>
            </>
          ) : (
            <>
              <Tooltip content={openTooltip} relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<Open20Regular />}
                  onClick={handleOpenSelected}
                  aria-label={openTooltip}
                />
              </Tooltip>
              <Tooltip content="Refresh to-do list" relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<ArrowClockwiseRegular />}
                  onClick={refetch}
                  aria-label="Refresh to-do list"
                />
              </Tooltip>
              <OrientationToggle orientation={orientation} onChange={setOrientation} />
              <Tooltip content="Filter to-dos" relationship="label">
                <ToggleButton
                  appearance="subtle"
                  size="small"
                  icon={<Search20Regular />}
                  checked={false}
                  onClick={handleToggleSearch}
                  aria-label="Open filter"
                  aria-expanded={false}
                />
              </Tooltip>
            </>
          )}
        </div>
      </Toolbar>

      {/* UAT 2026-06-19: the prior expanded-search row BELOW the toolbar
          is removed. Filter input lives INLINE in the toolbar (above).
          Kept this conditional false-fallthrough so any legacy `isSearchExpanded`
          consumers don't break. */}
      {false && isSearchExpanded && (
        <div className={styles.searchRow}>
          <div className={styles.searchWrap}>
            <SearchBox
              value={searchQuery}
              placeholder="Search to-dos…"
              onChange={handleSearchChange}
              aria-label="Search to-dos"
              size="small"
              autoFocus
            />
          </div>
        </div>
      )}

      {/* ── Quick-add error (R4-103, UAT 7) ───────────────────────────── */}
      {quickAddError && (
        <div className={styles.errorWithLink}>
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>
              {quickAddError}{' '}
              {onAddTodo && (
                <Link as="button" onClick={handleOpenWizardFromError} inline>
                  Open full wizard
                </Link>
              )}
            </MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* Error banner */}
      {error && (
        <div className={styles.errorContainer}>
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>
              {error}{' '}
              <Button appearance="transparent" size="small" onClick={refetch}>
                Try again
              </Button>
            </MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* Body — owns scroll */}
      <div className={styles.body}>
        {isLoading && (
          <div className={styles.loadingContainer}>
            <Spinner size="medium" label="Loading to-do items..." labelPosition="below" />
          </div>
        )}

        {!isLoading && !error && filteredItems.length === 0 && items.length > 0 && (
          <div className={styles.emptyContainer} role="status" aria-live="polite">
            <Body1>No matches</Body1>
            <Text size={200}>No to-dos match &quot;{appliedQuery}&quot;.</Text>
          </div>
        )}

        {!isLoading && !error && items.length === 0 && (
          <div className={styles.emptyContainer} role="status" aria-live="polite">
            <Body1>All caught up</Body1>
            <Text size={200}>No active to-do items for this context.</Text>
          </div>
        )}

        {/* R4 task 102 (E-1, 2026-06-18) — full Kanban replaces the R4-101
            grouped lists. `<SmartTodoKanban>` consumes the same hoisted
            `useKanbanColumns` hook internally + renders cards via the hoisted
            `<KanbanCard>` — ONE source of truth shared with the Code Page.
            Drag-drop column changes + pin toggles persist through the
            `dataverseService` adapter built above (when the host's `webApi`
            exposes `updateRecord`). Multi-select state lives in this widget
            and threads through to per-card checkboxes. */}
        {!isLoading && !error && filteredItems.length > 0 && (
          <div className={styles.kanbanContainer}>
            <SmartTodoKanban<ITodoRecord>
              items={filteredItems}
              dataverseService={dataverseService}
              selectedIds={selectedIds}
              onToggleSelect={handleToggleSelect}
              onOpenTodo={onOpenTodo}
              orientation={orientation}
              collapsedColumns={collapsedColumns}
              onToggleCollapse={handleToggleCollapse}
              ariaLabel={`${title} Kanban board`}
            />
          </div>
        )}
      </div>
    </div>
  );
};

export default SmartTodoWidget;
