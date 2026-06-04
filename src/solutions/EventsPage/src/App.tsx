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
  EVENT_DETAIL_PANE_ID,
  EVENT_DETAIL_WEB_RESOURCE_NAME,
  PANE_WIDTH,
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
// Event Detail side pane — opens via Xrm.Navigation; mutually exclusive with
// the Calendar pane via the orchestrator's mutuallyExclusiveWith mechanism.
// Preserved verbatim from the prior iteration's calendarPaneOrchestrator.
// ─────────────────────────────────────────────────────────────────────────────

async function openEventDetailPane(eventId: string, eventTypeId?: string): Promise<void> {
  /* eslint-disable @typescript-eslint/no-explicit-any */
  const xrm: any = getXrm();
  if (!xrm?.App?.sidePanes) return;
  try {
    const sidePanes = xrm.App.sidePanes;
    // Mutual exclusivity preserve: re-register Calendar pane if user closed it.
    const calendarPane = sidePanes.getPane(CALENDAR_PANE_ID);
    if (!calendarPane) {
      await sidePanes.createPane({
        title: 'Date Filter: Event',
        paneId: CALENDAR_PANE_ID,
        canClose: true,
        width: CALENDAR_PANE_WIDTH,
        isSelected: false,
        imageSrc: 'WebResources/sprk_calendarline_24',
      });
      const recreated = sidePanes.getPane(CALENDAR_PANE_ID);
      if (recreated) {
        await recreated.navigate({
          pageType: 'webresource',
          webresourceName: CALENDAR_WEB_RESOURCE_NAME,
        });
      }
    }

    const cleanId = eventId.replace(/[{}]/g, '');
    const navigationOptions = {
      pageType: 'webresource',
      webresourceName: EVENT_DETAIL_WEB_RESOURCE_NAME,
      data: `eventId=${cleanId}&eventType=${eventTypeId ?? ''}`,
    };

    const existing = sidePanes.getPane(EVENT_DETAIL_PANE_ID);
    if (existing) {
      await existing.navigate(navigationOptions);
      existing.select?.();
      return;
    }

    const newPane = await sidePanes.createPane({
      title: 'Event',
      paneId: EVENT_DETAIL_PANE_ID,
      canClose: true,
      width: PANE_WIDTH,
      isSelected: true,
      imageSrc: 'WebResources/sprk_tabaddline_24',
    });
    await newPane.navigate(navigationOptions);
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('[EventsPage] Failed to open Event detail pane:', error);
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
        mutuallyExclusiveWith: [EVENT_DETAIL_PANE_ID],
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

  return (
    <DataGridPageShell
      configId={EVENT_CONFIG_ID}
      parentContext={parentContext}
      sidePaneFilter={IS_DIALOG_MODE ? undefined : {
        paneId: CALENDAR_PANE_ID,
        translator: calendarPaneTranslator,
      }}
      onRecordOpen={(recordId, record) => {
        // Override the framework's default record-open (which uses configjson
        // rowOpen.type=webResource) so EventsPage opens the Event detail
        // side pane instead — matches the legacy in-app UX. Drill-through
        // and embedded modes still get the configjson behavior because
        // those hosts skip this prop.
        const eventTypeId = String(
          (record as Record<string, unknown>)._sprk_eventtype_ref_value ?? ''
        );
        if (recordId) void openEventDetailPane(recordId, eventTypeId || undefined);
      }}
    />
  );
};

export default App;
