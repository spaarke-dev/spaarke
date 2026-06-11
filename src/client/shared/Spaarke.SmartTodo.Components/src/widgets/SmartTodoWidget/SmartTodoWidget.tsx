/**
 * SmartTodoWidget — host-agnostic SpaarkeAi workspace widget for sprk_todo.
 *
 * R4 task 020 (Pattern D dual-use rebuild — 2026-06-10):
 *   - Replaces the stale deployed bundle that queried `sprk_event.sprk_todoflag`
 *     (retired in R3 FR-29 / OS-1). This rebuild queries `sprk_todo` directly.
 *   - Host-agnostic: ZERO subscription to LegalWorkspace-internal contexts
 *     (e.g., FeedTodoSyncContext). Cross-block sync is wired by the host shim
 *     via the `feedSync` prop (see types/todo.ts IFeedSyncBridge).
 *   - Mirrors the proven `CalendarWorkspaceWidget` Pattern D structure from
 *     `@spaarke/events-components` (R3 task 115).
 *
 * Query (spec.md FR-02):
 *   - Entity: `sprk_todo`
 *   - Filter: statecode eq 0 AND (statuscode eq 1 or statuscode eq 659490001)
 *     — Open (1) + In Progress (659490001) per R3 task 009.
 *   - Optional regarding filter: `_sprk_regarding<X>_value eq <recordId>` when
 *     `regardingContext` is supplied (LW host injects current workspace's lookup).
 *   - Optional owner clause: when `userId` is supplied AND no regardingContext,
 *     falls back to `_ownerid_value eq <userId>` so the widget renders the
 *     current user's active todos. Mirrors LW `buildOwnerFilter` shape.
 *
 * 6-layout compatibility (spec.md FR-04):
 *   - Uses `PaneHeader` from @spaarke/ui-components for consistent header.
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
 *   - `projects/smart-todo-r4/notes/widget-surface-audit.md` — R4-001 audit
 *   - `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`
 *     — canonical Pattern D worked example (R3 task 115 / R4 task 033b).
 */

import * as React from 'react';
import { Body1, Button, MessageBar, MessageBarBody, Spinner, Text } from '@fluentui/react-components';
import { ArrowClockwiseRegular, TaskListAdd24Regular } from '@fluentui/react-icons';

// Cross-package source import — same pattern as Calendar widget importing
// DataGrid from Spaarke.UI.Components source. PaneHeader is a shared primitive
// hoisted under ADR-012; importing the source path (rather than the package
// root) keeps this peer package's tsc check from pulling the PCF-framework
// surface (DatasetGrid, UniversalDatasetGrid, etc.) into compilation.
import { PaneHeader } from '../../../../Spaarke.UI.Components/src/components/PaneHeader/PaneHeader';

import { useSmartTodoWidgetStyles } from './SmartTodoWidget.styles';
import type { IFeedSyncBridge, IRegardingContext, ITodoRecord, IWebApi } from '../../types/todo';

// ---------------------------------------------------------------------------
// Public statuscode constants (R3 task 009 / OS-1)
// ---------------------------------------------------------------------------

export const TODO_STATUSCODE_OPEN = 1 as const;
export const TODO_STATUSCODE_IN_PROGRESS = 659490001 as const;
export const TODO_STATUSCODE_COMPLETED = 2 as const;
export const TODO_STATUSCODE_DISMISSED = 659490002 as const;

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
  /** Title shown in the widget header. Default: "My To Do List". */
  title?: string;
  /** Notify the host of the active count (for badge / tab counter). */
  onBadgeCountChange?: (count: number) => void;
  /** Expose the refetch trigger to the host (for header refresh button). */
  onRefetchReady?: (refetch: () => void) => void;
  /**
   * Open handler for a clicked todo. Hosts wire to their navigation surface
   * (e.g., `Xrm.Navigation.navigateTo({pageType: 'webresource', webresourceName: 'sprk_smarttodo', data: 'eventId=<id>'})`
   * or, in R4 C work, the new `<RecordNavigationModalShell>` per FR-16).
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
  title = 'My To Do List',
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

  // Stable refetch — bumps the fetchKey so the effect re-runs.
  const refetch = React.useCallback(() => {
    setFetchKey(k => k + 1);
  }, []);

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
  // Render
  // -------------------------------------------------------------------------

  const handleCardClick = React.useCallback(
    (todoId: string) => {
      onOpenTodo?.(todoId);
    },
    [onOpenTodo]
  );

  const renderItem = (item: ITodoRecord) => {
    const due = formatDue(item.sprk_duedate);
    const status = item.statuscode === TODO_STATUSCODE_IN_PROGRESS ? 'In progress' : 'Open';
    return (
      <div
        key={item.sprk_todoid}
        className={styles.todoCard}
        role="button"
        tabIndex={0}
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

  return (
    <div
      className={styles.root}
      role="region"
      aria-label={`${title}, ${items.length} item${items.length === 1 ? '' : 's'}`}
    >
      <PaneHeader
        title={items.length > 0 ? `${title} (${items.length})` : title}
        rightSlot={
          <>
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              onClick={refetch}
              aria-label="Refresh to-do list"
            />
            {onAddTodo && (
              <Button
                appearance="subtle"
                size="small"
                icon={<TaskListAdd24Regular />}
                onClick={onAddTodo}
                aria-label="Add new to-do"
              />
            )}
          </>
        }
      />

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

        {!isLoading && !error && items.length === 0 && (
          <div className={styles.emptyContainer} role="status" aria-live="polite">
            <Body1>All caught up</Body1>
            <Text size={200}>No active to-do items for this context.</Text>
          </div>
        )}

        {!isLoading && !error && items.length > 0 && <div className={styles.cardList}>{items.map(renderItem)}</div>}
      </div>
    </div>
  );
};

export default SmartTodoWidget;
