/**
 * EventsPage — thin Custom Page host built on the Spaarke DataGrid Framework.
 *
 * Replaces the legacy 1868-line `App.tsx` with a ~150-line shell per design.md
 * §11.5.3 + task 031. The grid + filter chips + command bar + row open
 * behavior all flow from the `sprk_event` `sprk_gridconfiguration` record
 * (id `e15c2b93-a05f-f111-a825-70a8a59455f4`, authored in task 030).
 *
 * What stays in this host (not in the framework):
 * - URL `data=` envelope parsing (drill-through dialog / embedded modes)
 * - Calendar side-pane registration + Event detail side-pane lifecycle +
 *   mutual exclusivity (see `calendarPaneOrchestrator.ts`)
 * - Event-specific custom command handlers (see `registerEventHandlers.ts`)
 * - Cleanup of side-pane state on navigation away
 *
 * Everything else — column rendering, sort, filter chips, lazy-load paging,
 * empty state, command bar UI — is owned by `<DataGrid />` from
 * `@spaarke/ui-components`.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/tasks/031-eventspage-app-tsx-rewrite.poml
 * **Config record**: `e15c2b93-a05f-f111-a825-70a8a59455f4`
 */

import * as React from 'react';
import { FluentProvider } from '@fluentui/react-components';
import {
  DataGrid,
  XrmDataverseClient,
  resolveCodePageTheme,
  setupCodePageThemeListener,
  type HostFilterCondition,
} from '@spaarke/ui-components';

import { registerEventHandlers } from './registerEventHandlers';
import {
  closeAllEventsPanes,
  openEventDetailPane,
  registerCalendarPane,
  subscribeToCalendarFilter,
} from './calendarPaneOrchestrator';

// ─────────────────────────────────────────────────────────────────────────────
// Calendar filter payload → HostFilterCondition[] translation (task 035 UAT
// iteration 3 — corrected payload shape after iteration 2 missed the
// wrapping `{ filter: ... }` and the filter never reached the DataGrid.)
//
// The CalendarSidePane web resource (`sprk_calendarsidepane.html`) emits
// CALENDAR_FILTER_CHANGED on the `spaarke-events-page-channel` BroadcastChannel
// with this shape (see `src/solutions/CalendarSidePane/src/utils/postMessage.ts`):
//
//   { type: 'CALENDAR_FILTER_CHANGED', payload: { filter: <CalendarFilterOutput> } }
//
// `subscribeToCalendarFilter` (calendarPaneOrchestrator) passes `e.data.payload`
// to the callback — so the value our callback receives is `{ filter: ... }`,
// NOT the filter directly. Iteration 2 forgot to unwrap; this version does.
//
// The inner filter shape:
//   { type: 'single', date: '2026-06-15', dateFields?: ['sprk_DueDate'] }
//   { type: 'range',  start: '2026-06-01', end: '2026-06-30', dateFields?: [...] }
//   { type: 'clear' }
//
// `dateFields` in the side pane uses PascalCase per its own field metadata
// (`sprk_DueDate`, `CreatedOn`, etc.). Dataverse FetchXML attribute names are
// case-insensitive but Spaarke convention is all-lowercase — so we lowercase
// the chosen field defensively.
//
// Multi-field OR (when dateFields has multiple entries) intentionally NOT
// expressed — flat HostFilterCondition API is AND-only; nested OR groups are
// the deferred Option-2 follow-up.
// ─────────────────────────────────────────────────────────────────────────────

interface CalendarFilterInner {
  type: 'single' | 'range' | 'clear';
  date?: string;
  start?: string;
  end?: string;
  dateFields?: string[];
}

interface CalendarFilterPayload {
  filter: CalendarFilterInner | null;
}

function calendarFilterToHostFilters(filter: CalendarFilterInner | null | undefined): HostFilterCondition[] {
  if (!filter || filter.type === 'clear') return [];
  // Strip the "All Dates" sentinel (`__all__`) if it slipped through; if that's
  // the only entry, fall back to sprk_duedate so we still produce a useful filter.
  const fields = (filter.dateFields ?? []).filter(f => f && f !== '__all__');
  const field = (fields[0] ?? 'sprk_duedate').toLowerCase();
  if (filter.type === 'single' && filter.date) {
    return [{ attribute: field, operator: 'on', value: filter.date }];
  }
  if (filter.type === 'range' && filter.start && filter.end) {
    return [{ attribute: field, operator: 'between', value: [filter.start, filter.end] }];
  }
  return [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Config record id authored in task 030 (sprk_event default grid configuration)
// ─────────────────────────────────────────────────────────────────────────────

const EVENT_CONFIG_ID = 'e15c2b93-a05f-f111-a825-70a8a59455f4';

// ─────────────────────────────────────────────────────────────────────────────
// URL param parsing — preserved verbatim from legacy App.tsx L120-189
// Supports three host modes:
//   • dialog: drill-through from VisualHost PCF (entityName + filterValue)
//   • embedded: context-aware tab on an entity form (id + typename)
//   • (none): system-level EventsPage launched from the app shell
// ─────────────────────────────────────────────────────────────────────────────

interface DrillThroughParams {
  mode: string | null;
  entityName: string | null;
  filterField: string | null;
  filterValue: string | null;
  viewId: string | null;
  recordId: string | null;
}

function parseDrillThroughParams(): DrillThroughParams {
  try {
    const urlParams = new URLSearchParams(window.location.search);
    const data = urlParams.get('data');
    const dataParams = data ? new URLSearchParams(data) : null;
    const stdId = urlParams.get('id');
    const stdTypename = urlParams.get('typename');

    let mode = dataParams?.get('mode') ?? null;
    if (!mode && data && data.includes('embedded')) mode = 'embedded';

    return {
      mode,
      entityName: stdTypename || (dataParams?.get('entityName') ?? null),
      filterField: dataParams?.get('filterField') ?? null,
      filterValue: dataParams?.get('filterValue') ?? null,
      viewId: dataParams?.get('viewId') ?? null,
      recordId: stdId || (dataParams?.get('recordId') ?? null),
    };
  } catch {
    return { mode: null, entityName: null, filterField: null, filterValue: null, viewId: null, recordId: null };
  }
}

const DRILL_THROUGH_PARAMS = parseDrillThroughParams();
const IS_DIALOG_MODE = DRILL_THROUGH_PARAMS.mode === 'dialog';

// ─────────────────────────────────────────────────────────────────────────────
// Host-level command handler registration — runs ONCE at module load so the
// registry is populated before the first `<DataGrid />` render. See
// `registerEventHandlers.ts` for the full handler set.
// ─────────────────────────────────────────────────────────────────────────────

registerEventHandlers();

// ─────────────────────────────────────────────────────────────────────────────
// App component
// ─────────────────────────────────────────────────────────────────────────────

const dataverseClient = new XrmDataverseClient();

export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  // hostFilters from the Calendar side pane (task 035-fix-2). Updated each
  // time the pane emits a CALENDAR_FILTER_CHANGED message; flows directly
  // into <DataGrid hostFilters={...} /> via the task 033a composition layer.
  // Stable identity matters — useState provides referential stability between
  // unrelated re-renders. Memoized in the JSX consumer below.
  const [calendarHostFilters, setCalendarHostFilters] = React.useState<HostFilterCondition[]>([]);

  React.useEffect(() => setupCodePageThemeListener(() => setTheme(resolveCodePageTheme())), []);

  // Calendar side pane: register once on mount (skipped in dialog mode
  // because the drill-through host doesn't include the Calendar UX). See
  // calendarPaneOrchestrator for the load-bearing mutual-exclusivity rules.
  //
  // Lifecycle (task 035-fix-2):
  //  - On mount: register the pane + subscribe to filter messages.
  //  - On filter message: translate to HostFilterCondition[] and update
  //    calendarHostFilters state → DataGrid re-fetches with the new overlay.
  //  - On IntersectionObserver "iframe became hidden" event: close panes.
  //    This handles MDA form-tab switching (which only flips CSS display,
  //    doesn't unmount). When the iframe becomes visible again, re-register.
  //  - On beforeunload / pagehide: close panes (browser navigation).
  React.useEffect(() => {
    if (IS_DIALOG_MODE) return undefined;
    let paneRegistered = false;
    const registerTimer = window.setTimeout(() => {
      void registerCalendarPane();
      paneRegistered = true;
    }, 500);

    const unsubscribe = subscribeToCalendarFilter(payload => {
      // The CalendarSidePane wraps its CalendarFilterOutput in { filter: ... };
      // unwrap before translation so the operator selection actually reaches
      // the grid's FetchXML composition.
      const wrapped = payload as CalendarFilterPayload | null | undefined;
      const inner = wrapped?.filter ?? null;
      setCalendarHostFilters(calendarFilterToHostFilters(inner));
    });

    const cleanup = () => closeAllEventsPanes();
    window.addEventListener('beforeunload', cleanup);
    window.addEventListener('pagehide', cleanup);

    // IntersectionObserver lifecycle: detect form-tab show/hide on the
    // #root mount element. When the iframe scrolls or its containing tab
    // becomes hidden, close the side panes; when it returns to visible,
    // re-register the Calendar pane. Observing #root directly (instead of a
    // wrapper div) keeps the DOM structurally identical to the InvoicesPage
    // shell — task 035-fix-iteration-3 reverted the wrapper div that was
    // introduced earlier for the ref attach point.
    let observer: IntersectionObserver | null = null;
    const node = document.getElementById('root');
    if (node && typeof IntersectionObserver !== 'undefined') {
      let wasVisible = true;
      observer = new IntersectionObserver(
        entries => {
          for (const entry of entries) {
            const visible = entry.isIntersecting && entry.intersectionRatio > 0;
            if (wasVisible && !visible) {
              // Just became hidden — tear down side panes so they don't
              // outlive the visible grid (issue 035-4 from UAT).
              closeAllEventsPanes();
            } else if (!wasVisible && visible) {
              // Returning from hidden — re-register the Calendar pane if it
              // was the user's prior state. Idempotent per registerCalendarPane.
              if (paneRegistered) void registerCalendarPane();
            }
            wasVisible = visible;
          }
        },
        { threshold: 0 }
      );
      observer.observe(node);
    }

    return () => {
      window.clearTimeout(registerTimer);
      unsubscribe();
      window.removeEventListener('beforeunload', cleanup);
      window.removeEventListener('pagehide', cleanup);
      observer?.disconnect();
      // Best-effort: close panes on unmount as well (covers React 18 strict-
      // mode double-mount + any cases where the component unmounts but the
      // iframe stays alive).
      closeAllEventsPanes();
    };
  }, []);

  const parentContext = React.useMemo(() => {
    if (DRILL_THROUGH_PARAMS.recordId) {
      return {
        entityType: DRILL_THROUGH_PARAMS.entityName ?? '',
        id: DRILL_THROUGH_PARAMS.recordId,
        name: '',
      };
    }
    return undefined;
  }, []);

  return (
    <FluentProvider theme={theme} applyStylesToPortals={true} style={{ height: '100%' }}>
      <DataGrid
        configId={EVENT_CONFIG_ID}
        parentContext={parentContext}
        dataverseClient={dataverseClient}
        theme={theme}
        hostFilters={calendarHostFilters}
        onRecordOpen={(recordId, record) => {
          // Override the framework's default record-open (which uses configjson
          // rowOpen.type=webResource) so EventsPage opens the Event detail
          // side pane instead — matches the legacy in-app UX. Drill-through
          // and embedded modes still get the configjson behavior because
          // those hosts don't reach this code path.
          const eventTypeId = String((record as Record<string, unknown>)._sprk_eventtype_ref_value ?? '');
          if (recordId) void openEventDetailPane(recordId, eventTypeId || undefined);
        }}
      />
    </FluentProvider>
  );
};

export default App;
