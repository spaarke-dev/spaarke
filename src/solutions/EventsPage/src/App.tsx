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
import { DataGrid, XrmDataverseClient, resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components';

import { registerEventHandlers } from './registerEventHandlers';
import {
  closeAllEventsPanes,
  openEventDetailPane,
  registerCalendarPane,
  subscribeToCalendarFilter,
} from './calendarPaneOrchestrator';

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

  React.useEffect(() => setupCodePageThemeListener(() => setTheme(resolveCodePageTheme())), []);

  // Calendar side pane: register once on mount (skipped in dialog mode
  // because the drill-through host doesn't include the Calendar UX). See
  // calendarPaneOrchestrator for the load-bearing mutual-exclusivity rules.
  React.useEffect(() => {
    if (IS_DIALOG_MODE) return undefined;
    const timer = window.setTimeout(() => {
      void registerCalendarPane();
    }, 500);
    const unsubscribe = subscribeToCalendarFilter(_payload => {
      // R1: Calendar filter messages reach the host but do NOT yet feed into
      // the framework grid's filter state. The piped overlay is task 033
      // (Calendar widget migration). See current-task.md handoff notes.
    });
    const cleanup = () => closeAllEventsPanes();
    window.addEventListener('beforeunload', cleanup);
    window.addEventListener('pagehide', cleanup);
    return () => {
      window.clearTimeout(timer);
      unsubscribe();
      window.removeEventListener('beforeunload', cleanup);
      window.removeEventListener('pagehide', cleanup);
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
