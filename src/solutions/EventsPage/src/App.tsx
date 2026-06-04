/**
 * EventsPage — thin Custom Page host built on the Spaarke DataGrid framework.
 *
 * **Task 035 hardening (2026-06-04)**: migrated to `<DataGridPageShell>` +
 * the generalized `DataGridSidePaneOrchestrator` from `@spaarke/ui-components`.
 * The shell handles FluentProvider + theme + box-sizing + side-pane filter
 * subscription. The orchestrator handles the Calendar pane lifecycle (register
 * on mount, close on form-tab navigation away, mutual exclusivity with the
 * Event Detail pane). The host owns ONLY: custom command handler registration,
 * custom row-open behavior (Event Detail side pane), URL parsing for embedded
 * parent context, and the Event Detail pane lifecycle.
 *
 * See `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md` for the contract.
 *
 * **Config record**: `e15c2b93-a05f-f111-a825-70a8a59455f4` (sprk_event default,
 * authored in task 030). The configjson's `commandBar.primary` references the
 * `CompleteEvents/CloseEvents/CancelEvents/OnHoldEvents/ArchiveEvents` custom
 * handlers — those are registered below BEFORE the React mount so they're
 * present when the DataGrid first renders.
 */

import * as React from 'react';
import {
  DataGridPageShell,
  DataGridSidePaneOrchestrator,
  type DataGridParentContext,
  type HostFilterCondition,
} from '@spaarke/ui-components';

import { registerEventHandlers } from './registerEventHandlers';
import {
  CALENDAR_PANE_ID,
  CALENDAR_PANE_WIDTH,
  CALENDAR_WEB_RESOURCE_NAME,
} from './config';
import { getXrm } from './xrmHelpers';

// ─────────────────────────────────────────────────────────────────────────────
// Config record id (authored in task 030)
// ─────────────────────────────────────────────────────────────────────────────

const EVENT_CONFIG_ID = 'e15c2b93-a05f-f111-a825-70a8a59455f4';

// ─────────────────────────────────────────────────────────────────────────────
// URL parsing — drill-through (dialog) / embedded / standalone modes
// ─────────────────────────────────────────────────────────────────────────────

interface DrillThroughParams {
  mode: string | null;
  entityName: string | null;
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
      recordId: stdId || (dataParams?.get('recordId') ?? null),
    };
  } catch {
    return { mode: null, entityName: null, recordId: null };
  }
}

const DRILL_THROUGH_PARAMS = parseDrillThroughParams();
const IS_DIALOG_MODE = DRILL_THROUGH_PARAMS.mode === 'dialog';

// ─────────────────────────────────────────────────────────────────────────────
// Custom command handlers — must be registered BEFORE first DataGrid render
// ─────────────────────────────────────────────────────────────────────────────

registerEventHandlers();

// ─────────────────────────────────────────────────────────────────────────────
// Calendar side pane filter — translator from CalendarSidePane payload to
// HostFilterCondition[]. The pane's payload shape:
//   { type: 'single' | 'range' | 'clear', date?, start?, end?, dateFields?: string[] }
// (See src/solutions/CalendarSidePane/src/utils/parseParams.ts and the
//  sendSidePaneFilter call in App.tsx.)
//
// Multi-field OR is intentionally NOT expressed — flat HostFilterCondition[]
// is AND-only; nested OR groups are a deferred Option 2 follow-up. The
// translator picks the first dateField (or sprk_duedate as default) and
// lowercases the attribute name for FetchXML conventions.
// ─────────────────────────────────────────────────────────────────────────────

interface CalendarPaneInnerPayload {
  type: 'single' | 'range' | 'clear';
  date?: string;
  start?: string;
  end?: string;
  dateFields?: string[];
}

function calendarPaneTranslator(
  payload: CalendarPaneInnerPayload | null | undefined
): HostFilterCondition[] {
  if (!payload || payload.type === 'clear') return [];
  const fields = (payload.dateFields ?? []).filter(f => f && f !== '__all__');
  const field = (fields[0] ?? 'sprk_duedate').toLowerCase();
  if (payload.type === 'single' && payload.date) {
    return [{ attribute: field, operator: 'on', value: payload.date }];
  }
  if (payload.type === 'range' && payload.start && payload.end) {
    return [
      { attribute: field, operator: 'between', value: [payload.start, payload.end] },
    ];
  }
  return [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Standalone row-open: open Event record in a centered Dataverse dialog.
//
// UAT-2 (2026-06-04): drill-through hosts (Matter card → Events grid) use the
// framework's default `rowOpen.type=navigateToForm` from the configjson and
// open the record in a new tab. The standalone EventsPage (the one with the
// Calendar side pane) wants a different UX. Long-term: open in the Event
// detail side pane. Short-term (for easier testing): open in a centered
// dialog. This handler is only wired in standalone mode (no parentContext);
// drill-through hosts skip it and get the framework default.
// ─────────────────────────────────────────────────────────────────────────────

async function openEventInDialog(recordId: string): Promise<void> {
  /* eslint-disable @typescript-eslint/no-explicit-any */
  const xrm: any = getXrm();
  if (!xrm?.Navigation?.navigateTo) {
    // eslint-disable-next-line no-console
    console.warn('[EventsPage] Xrm.Navigation.navigateTo unavailable; cannot open dialog');
    return;
  }
  const cleanId = recordId.replace(/[{}]/g, '');
  try {
    await xrm.Navigation.navigateTo(
      { pageType: 'entityrecord', entityName: 'sprk_event', entityId: cleanId },
      // target 2 = inline dialog; position 1 = center.
      { target: 2, position: 1, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
    );
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('[EventsPage] Failed to open Event in dialog:', error);
  }
  /* eslint-enable @typescript-eslint/no-explicit-any */
}

// ─────────────────────────────────────────────────────────────────────────────
// App component
// ─────────────────────────────────────────────────────────────────────────────

export const App: React.FC = () => {
  // Lifecycle: register the Calendar pane (skipped in dialog mode — the
  // drill-through host doesn't include the Calendar UX). The orchestrator
  // handles the IntersectionObserver visibility detection + browser-cleanup +
  // mutual exclusivity automatically.
  const orchestrator = React.useMemo(() => new DataGridSidePaneOrchestrator(), []);

  React.useEffect(() => {
    if (IS_DIALOG_MODE) return undefined;

    const timer = window.setTimeout(() => {
      void orchestrator.registerPane({
        paneId: CALENDAR_PANE_ID,
        title: 'Date Filter: Event',
        webResourceName: CALENDAR_WEB_RESOURCE_NAME,
        width: CALENDAR_PANE_WIDTH,
        iconName: 'WebResources/sprk_calendarline_24',
      });
    }, 500);

    const root = document.getElementById('root');
    const detach = root ? orchestrator.attachVisibilityLifecycle(root) : () => undefined;

    return () => {
      window.clearTimeout(timer);
      detach();
    };
  }, [orchestrator]);

  const parentContext = React.useMemo<DataGridParentContext | undefined>(() => {
    if (!DRILL_THROUGH_PARAMS.recordId) return undefined;
    const cleanId = DRILL_THROUGH_PARAMS.recordId.replace(/[{}]/g, '');
    const ctx: DataGridParentContext = {
      entityType: DRILL_THROUGH_PARAMS.entityName ?? '',
      id: cleanId,
      name: '',
    };
    // When the parent is a Matter, ALSO expose the id under the configjson's
    // parentContextKey ('matterId'). The framework reads
    // `parentContext[parentContextFilter.parentContextKey]` to overlay the
    // FetchXML — without this line, the configjson's
    // `behavior.parentContextFilter` (added 2026-06-04) would never find a value.
    // Same pattern as sprk_invoicespage / sprk_kpiassessmentspage.
    if (DRILL_THROUGH_PARAMS.entityName === 'sprk_matter') {
      ctx.matterId = cleanId;
    }
    return ctx;
  }, []);

  // Drill-through hosts (Matter card → Events grid) have a parentContext.
  // Standalone EventsPage does not. Only the standalone variant overrides
  // row-open to use the dialog; drill-through stays on the framework default
  // (configjson `rowOpen.type=navigateToForm` → new tab).
  const isDrillThrough = parentContext !== undefined;

  return (
    <DataGridPageShell
      configId={EVENT_CONFIG_ID}
      parentContext={parentContext}
      sidePaneFilter={IS_DIALOG_MODE ? undefined : {
        paneId: CALENDAR_PANE_ID,
        translator: calendarPaneTranslator,
      }}
      onRecordOpen={isDrillThrough ? undefined : (recordId) => {
        if (recordId) void openEventInDialog(recordId);
      }}
    />
  );
};

export default App;
