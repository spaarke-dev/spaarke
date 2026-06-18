/**
 * SmartTodoWidget — host-agnostic SpaarkeAi workspace widget for sprk_todo.
 *
 * R4 task 099 (W-1 — widget chrome consolidation + Pattern D alignment, 2026-06-18):
 *   - REMOVED `<PaneHeader>` entirely. Per the 2026-06-18 widget-parity audit,
 *     the widget's PaneHeader was rendering a SECOND title bar on top of the
 *     SectionPanel title that the LegalWorkspace shim already provides (mirrors
 *     Calendar's canonical Pattern D — Calendar widget has no header of its own).
 *   - ADDED a single `<Toolbar>` row containing `[SearchBox, +, Open, refresh]`
 *     (audit issue 5 + 3). This is the widget's SOLE chrome row.
 *   - ADDED SearchBox with local debounced state — filters items in-memory by
 *     case-insensitive substring match against `sprk_name` and `sprk_description`.
 *     No OData round-trip (sufficient for the widget's bounded result set).
 *   - ADDED single-card selection state so the Open button is selection-aware
 *     (disabled when 0 selected; enabled when 1 selected — mirrors the Code Page
 *     `SelectionAwareToolbar` pattern at a smaller scope).
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
  MessageBar,
  MessageBarBody,
  SearchBox,
  Spinner,
  Text,
  Toolbar,
  Tooltip,
  type SearchBoxChangeEvent,
  type InputOnChangeData,
} from '@fluentui/react-components';
import { ArrowClockwiseRegular, Add20Regular, Open20Regular } from '@fluentui/react-icons';

import { useSmartTodoWidgetStyles } from './SmartTodoWidget.styles';
import type { IFeedSyncBridge, IRegardingContext, ITodoRecord, IWebApi } from '../../types/todo';
import { bucketTodoItems, DEFAULT_TODAY_THRESHOLD, DEFAULT_TOMORROW_THRESHOLD } from '../../hooks/useKanbanColumns';

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
 * Per R4 spec FR-02:
 *   - statecode eq 0
 *   - statuscode in (Open, In Progress)
 *   - regarding context filter (when supplied)
 *   - owner filter (when supplied and no regarding context)
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
    'createdon',
    'modifiedon',
  ].join(',');

  // Active clause is invariant — Open + In Progress.
  const activeClause = `statecode eq 0 and (statuscode eq ${TODO_STATUSCODE_OPEN} or statuscode eq ${TODO_STATUSCODE_IN_PROGRESS})`;

  // Build context clause — regarding takes precedence; falls back to owner.
  let contextClause = '';
  if (opts.regardingContext) {
    const lookupField = entityLogicalNameToLookup(opts.regardingContext.entityLogicalName);
    if (lookupField) {
      contextClause = `${lookupField} eq ${opts.regardingContext.recordId}`;
    }
  } else if (opts.scope === 'all' && opts.businessUnitId) {
    contextClause = `(_ownerid_value eq ${opts.userId ?? '00000000-0000-0000-0000-000000000000'} or _owningbusinessunit_value eq ${opts.businessUnitId})`;
  } else if (opts.userId) {
    contextClause = `_ownerid_value eq ${opts.userId}`;
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
// Due-date formatter (lightweight; full LW formatter stays in LW utils for now)
// ---------------------------------------------------------------------------

function formatDue(iso?: string): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
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
   * Title used for the root region's `aria-label`. Defaults to "Smart To Do".
   *
   * Post-099 (Pattern D consolidation): the widget no longer renders a visible
   * title bar — the host (LegalWorkspace SectionPanel or Direct-widget caller)
   * owns the visible title. This prop survives for accessibility only.
   */
  title?: string;
  /** Notify the host of the active count (for badge / tab counter). */
  onBadgeCountChange?: (count: number) => void;
  /** Expose the refetch trigger to the host (for header refresh button). */
  onRefetchReady?: (refetch: () => void) => void;
  /**
   * Open handler for a clicked todo. Hosts wire to their navigation surface
   * (e.g., `Xrm.Navigation.navigateTo({pageType: 'webresource', webresourceName: 'sprk_smarttodo', data: 'eventId=<id>'})`
   * or, in R4 W-2 work, the new `openTodo` discriminator that auto-mounts
   * `<SmartTodoModal>` on the clicked record).
   */
  onOpenTodo?: (todoId: string) => void;
  /** Optional "+ New" handler — opens the host's CreateTodoWizard. */
  onAddTodo?: () => void;
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
  onBadgeCountChange,
  onRefetchReady,
  onOpenTodo,
  onAddTodo,
}) => {
  const styles = useSmartTodoWidgetStyles();

  const [items, setItems] = React.useState<ITodoRecord[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(true);
  const [error, setError] = React.useState<string | null>(null);
  const [fetchKey, setFetchKey] = React.useState(0);

  // Selection model — single-select (0..1). The Open button is enabled only
  // when one card is selected; clicking a card toggles its selection.
  const [selectedId, setSelectedId] = React.useState<string | null>(null);

  // SearchBox — local controlled value debounced into `appliedQuery` which
  // drives the in-memory filter.
  const [searchQuery, setSearchQuery] = React.useState<string>('');
  const [appliedQuery, setAppliedQuery] = React.useState<string>('');

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

  // Clear stale selection when the visible list changes such that the
  // currently-selected id is no longer rendered (search filter, refetch).
  React.useEffect(() => {
    if (selectedId && !filteredItems.some(t => t.sprk_todoid === selectedId)) {
      setSelectedId(null);
    }
  }, [filteredItems, selectedId]);

  // -------------------------------------------------------------------------
  // Handlers
  // -------------------------------------------------------------------------

  const handleSearchChange = React.useCallback((_e: SearchBoxChangeEvent, data: InputOnChangeData) => {
    setSearchQuery(data.value);
  }, []);

  const handleCardClick = React.useCallback((todoId: string) => {
    setSelectedId(prev => (prev === todoId ? null : todoId));
  }, []);

  const handleOpenSelected = React.useCallback(() => {
    if (selectedId && onOpenTodo) {
      onOpenTodo(selectedId);
    }
  }, [selectedId, onOpenTodo]);

  // -------------------------------------------------------------------------
  // Render helpers
  // -------------------------------------------------------------------------

  const renderItem = (item: ITodoRecord) => {
    const due = formatDue(item.sprk_duedate);
    const status = item.statuscode === TODO_STATUSCODE_IN_PROGRESS ? 'In progress' : 'Open';
    const isSelected = selectedId === item.sprk_todoid;
    return (
      <div
        key={item.sprk_todoid}
        className={isSelected ? styles.todoCardSelected : styles.todoCard}
        role="button"
        tabIndex={0}
        aria-pressed={isSelected}
        onClick={() => handleCardClick(item.sprk_todoid)}
        onKeyDown={e => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            handleCardClick(item.sprk_todoid);
          }
        }}
        aria-label={`${item.sprk_name}${due ? `, due ${due}` : ''}, ${status}`}
      >
        <span className={styles.cardTitle}>{item.sprk_name || 'Untitled to-do'}</span>
        <span className={styles.cardMeta}>
          {due && <Text size={200}>{due}</Text>}
          <span className={styles.statusBadge}>{status}</span>
        </span>
      </div>
    );
  };

  // Open is enabled only when a single card is selected.
  const openDisabled = selectedId === null;

  // -------------------------------------------------------------------------
  // Today / Tomorrow / Future grouped sections (R4 task 101 / W-3, 2026-06-18).
  // Uses the hoisted `bucketTodoItems` pure helper from the peer package's
  // hooks barrel — the same logic that drives the Code Page's full-Kanban
  // `useKanbanColumns` (one source of truth, satisfying UAT issue 6).
  //
  // Thresholds: widget uses the package defaults (60 / 30 — matching
  // `useUserPreferences`'s `DEFAULT_TODAY_THRESHOLD` / `_TOMORROW_THRESHOLD`).
  // Per-user threshold prefs are intentionally NOT pulled into the widget
  // here (would require a Dataverse round-trip on every widget mount across
  // every workspace tab — disproportionate to the visual benefit). If a
  // future task surfaces a clear need, the widget can accept a
  // `thresholds?: { today: number; tomorrow: number }` prop and the host
  // shim can fetch + pass them.
  // -------------------------------------------------------------------------

  const groupedColumns = React.useMemo(
    () => bucketTodoItems(filteredItems, DEFAULT_TODAY_THRESHOLD, DEFAULT_TOMORROW_THRESHOLD),
    [filteredItems]
  );

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <div
      className={styles.root}
      role="region"
      aria-label={`${title}, ${filteredItems.length} item${filteredItems.length === 1 ? '' : 's'}`}
    >
      {/* ── Sole chrome row — Toolbar: [SearchBox, +, Open, refresh] ──── */}
      <Toolbar aria-label="Smart To Do toolbar" size="small" className={styles.toolbar}>
        <div className={styles.searchWrap}>
          <SearchBox
            value={searchQuery}
            placeholder="Search to-dos…"
            onChange={handleSearchChange}
            aria-label="Search to-dos"
            size="small"
          />
        </div>

        <div className={styles.toolbarActions}>
          {onAddTodo && (
            <Tooltip content="Add new to-do" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<Add20Regular />}
                onClick={onAddTodo}
                aria-label="Add new to-do"
              />
            </Tooltip>
          )}
          <Tooltip content={openDisabled ? 'Select a to-do to open' : 'Open selected to-do'} relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<Open20Regular />}
              onClick={handleOpenSelected}
              disabled={openDisabled}
              aria-label="Open selected to-do"
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
        </div>
      </Toolbar>

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

        {!isLoading && !error && filteredItems.length > 0 && (
          <div className={styles.groupList}>
            {groupedColumns.map(col => (
              <section key={col.id} className={styles.groupSection} aria-label={`${col.title} (${col.items.length})`}>
                <header
                  className={styles.groupHeader}
                  style={col.accentColor ? { borderLeftColor: col.accentColor } : undefined}
                >
                  <span className={styles.groupTitle}>{col.title}</span>
                  <span className={styles.groupCount}>{col.items.length}</span>
                </header>
                {col.items.length === 0 ? (
                  <div className={styles.groupEmpty} role="status">
                    <Text size={200}>No items</Text>
                  </div>
                ) : (
                  <div className={styles.cardList}>{col.items.map(renderItem)}</div>
                )}
              </section>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

export default SmartTodoWidget;
