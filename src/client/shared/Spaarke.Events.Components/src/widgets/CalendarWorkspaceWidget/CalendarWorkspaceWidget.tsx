/**
 * CalendarWorkspaceWidget — the 5th SpaarkeAi system workspace widget.
 *
 * Task 033b (spaarke-datagrid-framework-r1, 2026-06-03) — DataGrid framework migration:
 *
 *   Per the task 033 sign-off (notes/drafts/033-widget-owner-signoff.md):
 *     Q1 — drop widget toolbar; rely on DataGrid command bar.
 *     Q2 — preserve filter row AS-IS; build hostFilters framework extension
 *          (task 033a) so its state pipes through <DataGrid hostFilters={…}/>
 *          instead of dispatching through EventsPageContext.
 *     Q3 — calendar event-date counting unchanged (handleRecordsLoaded plugs
 *          into the new <DataGrid onRecordsLoaded={…}/> callback).
 *     Q4 — keep EventsPageProvider so eventDates flow continues to drive the
 *          calendar strip's dot indicators through context.
 *
 * What changed vs. task 130:
 *  - <GridSection .../> swap → <DataGrid configId="e15c2b93-…" hostFilters
 *    onRecordsLoaded onRecordOpen dataverseClient/>. Same sprk_event Dataverse
 *    grid configuration record as the standalone EventsPage (task 030).
 *  - Filter row + calendar strip + EventsPageProvider preserved unchanged.
 *  - Toolbar removed (Q1). The DataGrid's own command bar (configjson
 *    commandBar.primary = +New / Delete / Refresh) handles those actions
 *    natively. Complete/Close/Cancel/OnHold/Archive are NOT registered for
 *    this widget surface (matches Q1 acceptance); when the configjson is
 *    extended in a follow-up, they will appear automatically without code
 *    changes here.
 *  - Bulk-status callbacks + helpers (bulkStatusUpdate, bulkArchive,
 *    confirmDialog, selectedIds state, hasSelection) removed — dead code
 *    after toolbar removal.
 *  - applied/pending → HostFilterCondition[] via overlayHostFilters
 *    (task 033a). selectedDate (day-click) and the filter row's
 *    dateField/from/to/eventTypeId/eventStatusValue all map to flat
 *    HostFilterCondition entries.
 *  - The original "filter-divergence" effect against context.filters.calendarFilter
 *    is removed: with the dispatch loop gone, selectedDate is the only source
 *    of truth for day-click filtering.
 *
 * Task 130 baseline behavior preserved (operator directive):
 *
 *   "If the grid has a filter applied by virtue of the drop down, then
 *    the Events shown will be different. This is confusing to the users."
 *
 * Filter UX (unchanged from task 130):
 *  - No view-selector dropdown. Grid drives off the sprk_gridconfiguration
 *    record's `source.savedquery-set { entityLogicalName: "sprk_event" }`.
 *  - Filter row: Event Type / Event Status / Filter by Date Field / From / To.
 *  - "Filter by Date Field" has a "(none)" sentinel; null dateField = no
 *    date filter applied.
 *  - Pending-vs-applied pattern. Apply button when pending != applied. Clear
 *    button when any filter or selectedDate is active.
 *  - Calendar day click bypasses the pending mechanism (day-click applies
 *    immediately via the hostFilters useMemo).
 *
 * Prior task history (kept for archaeological context):
 *  - Task 115 — initial 5th workspace widget (vertical 3-month strip)
 *  - Task 116 — horizontal strip + responsive month count + collapse
 *  - Task 118 — day-cell click → grid filter; chevron moved to filter row
 *  - Task 120 — onRecordsLoaded → eventDates dispatch; Open icon → toolbar
 *  - Task 121 — calendar event-date fallback (sprk_duedate || createdon)
 *  - Task 122 — event-day highlight in active range; Clear button on filter row
 *  - Task 124 — text Clear button; calendar→grid spacing; auto-anchor-to-earliest
 *  - Task 125/126/127 — event-day hover lock + filteredEvents → calendar pipeline
 *  - Task 128 — Clear visible on selectedDate; selected vs event-day visual distinction
 *  - Task 129 — grid buildDateFilter applies sprk_duedate→createdon fallback OR clause
 *  - Task 130 — view-selector removed; pending/applied filter pattern; Type+Status filters
 *  - Task 033b — DataGrid framework migration (THIS REWRITE)
 *
 * Integration seam (preserved): Event detail opens as a MODAL via
 * `Xrm.Navigation.navigateTo({pageType: "entityrecord", entityName, entityId},
 * { target: 2, width/height: 80% })`. Mirrors the Documents Expand affordance
 * from task 111.
 *
 * CLAUDE.md §10 (BFF Hygiene): ZERO new BFF endpoints. All Dataverse access
 * flows through the DataGrid's XrmDataverseClient (which uses Xrm.WebApi per
 * ADR-028). Event-type options still fetched via Xrm.WebApi.retrieveMultipleRecords
 * for the filter row dropdown.
 *
 * ADR-021 (Fluent v9 tokens), ADR-022 (React-16-safe consumption — the widget
 * runs in React 18 via the legal-workspace shell but uses no React-18-only APIs),
 * ADR-028 (Xrm.WebApi for metadata).
 *
 * @see src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx
 * @see src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts (overlayHostFilters)
 * @see src/client/shared/Spaarke.Events.Components/src/context/EventsPageContext.tsx
 * @see src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarSection.tsx
 * @see src/solutions/EventsPage/src/App.tsx (the standalone EventsPage rewrite — task 031)
 * @see projects/spaarke-datagrid-framework-r1/notes/drafts/033-widget-owner-signoff.md
 * @see projects/spaarke-datagrid-framework-r1/notes/drafts/033a-deviations.md
 * @see projects/spaarke-datagrid-framework-r1/notes/drafts/033b-deviations.md
 */

import * as React from 'react';
import { makeStyles, tokens, shorthands, Dropdown, Option, Label, Button, Tooltip } from '@fluentui/react-components';
import {
  ChevronLeft20Regular,
  ChevronRight20Regular,
  CaretUp24Regular,
  CaretDown24Regular,
} from '@fluentui/react-icons';

import { EventsPageProvider, useEventsPageContext } from '../../context/EventsPageContext';
import { CalendarSection } from '../../components/CalendarSection/CalendarSection';
import type { IEventDateInfo, CalendarFilterOutput } from '../../components/CalendarSection/CalendarSection';
import { addMonths, startOfMonth } from '../../utils/dateMath';

// Cross-package SOURCE imports. Deep paths (not the @spaarke/ui-components
// package root) so we don't pull the PCF-framework-dependent surface
// (UniversalDatasetGrid, useDatasetMode, SprkChat, etc.) into events-components's
// tsc check — events-components has no ComponentFramework types installed.
// See task 033b deviations doc for rationale.
import { DataGrid, type HostFilterCondition } from '../../../../Spaarke.UI.Components/src/components/DataGrid';
import { XrmDataverseClient } from '../../../../Spaarke.UI.Components/src/services/XrmDataverseClient';

// ─────────────────────────────────────────────────────────────────────────────
// Configuration — the sprk_gridconfiguration record id that drives the grid.
// Authored in DEV by task 030 (Phase D anchor). Same record drives EventsPage.
// ─────────────────────────────────────────────────────────────────────────────

const EVENT_CONFIG_ID = 'e15c2b93-a05f-f111-a825-70a8a59455f4';
const EVENT_ENTITY_NAME = 'sprk_event';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm typing — host-provided global (kept for the event-type options fetch
// and the event-detail modal navigation in the outer widget).
// ─────────────────────────────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Event status values (mirror sprk_event_ribbon_commands.js — task 115)
// ─────────────────────────────────────────────────────────────────────────────

const EventStatus = {
  DRAFT: 0,
  OPEN: 1,
  COMPLETED: 2,
  CLOSED: 3,
  ON_HOLD: 4,
  CANCELLED: 5,
  REASSIGNED: 6,
  ARCHIVED: 7,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Event Status options (Task 130) — hard-coded because sprk_eventstatus is an
// integer field with no Dataverse choice metadata attached.
// ─────────────────────────────────────────────────────────────────────────────

interface IStatusOption {
  value: number;
  label: string;
}

const STATUS_OPTIONS: IStatusOption[] = [
  { value: EventStatus.DRAFT, label: 'Draft' },
  { value: EventStatus.OPEN, label: 'Open' },
  { value: EventStatus.COMPLETED, label: 'Completed' },
  { value: EventStatus.CLOSED, label: 'Closed' },
  { value: EventStatus.ON_HOLD, label: 'On Hold' },
  { value: EventStatus.CANCELLED, label: 'Cancelled' },
  { value: EventStatus.REASSIGNED, label: 'Reassigned' },
  { value: EventStatus.ARCHIVED, label: 'Archived' },
];

// ─────────────────────────────────────────────────────────────────────────────
// Date-range filter options (Task 130 — adds "(none)" sentinel)
// ─────────────────────────────────────────────────────────────────────────────

const DATE_FIELD_NONE = '' as const;

const DATE_FIELDS = [
  { value: DATE_FIELD_NONE, label: '(none)' },
  { value: 'sprk_duedate', label: 'Due Date' },
  { value: 'sprk_startdate', label: 'Start Date' },
  { value: 'createdon', label: 'Created On' },
  { value: 'modifiedon', label: 'Modified On' },
] as const;

// ─────────────────────────────────────────────────────────────────────────────
// Collapse persistence (task 116) — mirrors `pinnedWorkspaces.ts` pattern
// ─────────────────────────────────────────────────────────────────────────────

const CALENDAR_COLLAPSED_KEY = 'spaarke:calendar:collapsed';

function readCollapsedPref(): boolean {
  try {
    return window.localStorage?.getItem(CALENDAR_COLLAPSED_KEY) === '1';
  } catch {
    return false;
  }
}

function writeCollapsedPref(collapsed: boolean): void {
  try {
    if (collapsed) {
      window.localStorage?.setItem(CALENDAR_COLLAPSED_KEY, '1');
    } else {
      window.localStorage?.removeItem(CALENDAR_COLLAPSED_KEY);
    }
  } catch {
    /* see readCollapsedPref */
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Responsive month-count breakpoints (task 116)
// ─────────────────────────────────────────────────────────────────────────────

function computeMonthsForWidth(widthPx: number): number {
  if (widthPx < 380) return 1;
  if (widthPx < 720) return 2;
  if (widthPx < 1060) return 3;
  if (widthPx < 1400) return 4;
  return 5;
}

// ─────────────────────────────────────────────────────────────────────────────
// Pending-vs-applied state model (Task 130 — preserved as-is per Q2 sign-off)
// ─────────────────────────────────────────────────────────────────────────────

interface ICalendarFilterSet {
  dateField: string;
  fromDate: string;
  toDate: string;
  eventTypeId: string | null;
  eventStatusValue: number | null;
}

const EMPTY_FILTER_SET: ICalendarFilterSet = {
  dateField: DATE_FIELD_NONE,
  fromDate: '',
  toDate: '',
  eventTypeId: null,
  eventStatusValue: null,
};

function filterSetsEqual(a: ICalendarFilterSet, b: ICalendarFilterSet): boolean {
  return (
    a.dateField === b.dateField &&
    a.fromDate === b.fromDate &&
    a.toDate === b.toDate &&
    a.eventTypeId === b.eventTypeId &&
    a.eventStatusValue === b.eventStatusValue
  );
}

function filterSetIsEmpty(s: ICalendarFilterSet): boolean {
  return filterSetsEqual(s, EMPTY_FILTER_SET);
}

// ─────────────────────────────────────────────────────────────────────────────
// Event-type option fetched from sprk_eventtype_ref records (task 131)
// ─────────────────────────────────────────────────────────────────────────────

interface IEventTypeOption {
  id: string;
  name: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface CalendarWorkspaceWidgetProps {
  /**
   * Optional override for the default date field. Default: `""` (no date
   * filter — the operator's task-130 directive: "the Filter by Date Field
   * needs to have a blank value").
   */
  initialDateField?: string;
  /**
   * Deprecated by task 130 (view-selector removed). The prop remains in
   * the signature for backward compatibility but is no longer consumed.
   */
  initialViewId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles — preserved from task 130; toolbar styles removed (task 033b Q1).
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    ...shorthands.padding('8px'),
    ...shorthands.gap('8px'),
    // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): root no longer scrolls.
    // The grid container owns its own scroll so the filter row + calendar
    // strip stay anchored at the top while only the event grid scrolls.
    overflow: 'hidden',
    boxSizing: 'border-box',
  },
  dateRangeRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-end',
    flexWrap: 'wrap',
    rowGap: '12px',
    // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): restore Griffel atomic-
    // CSS spacing between filter fields. Task 033b regressed this to inline
    // `marginRight: 28` per-field; this columnGap replaces those overrides
    // and is the canonical spacing source for all fields in the row.
    columnGap: tokens.spacingHorizontalXXL,
    flexShrink: 0,
    ...shorthands.padding('4px', '4px', '8px', '4px'),
    // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): subtle brand-accent
    // border instead of neutral; provides the "color title line" the user
    // requested without disrupting the dark-mode palette.
    ...shorthands.borderBottom('1px', 'solid', tokens.colorBrandStroke2),
  },
  dateRangeField: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 0 140px',
    minWidth: '140px',
  },
  dateRangeLabel: {
    fontSize: tokens.fontSizeBase200,
    // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): brand-tinted label color
    // (subtle), keeping ADR-021 compliance (semantic token, dark-mode safe).
    color: tokens.colorBrandForeground2,
    fontWeight: tokens.fontWeightSemibold,
    marginBottom: '4px',
  },
  filterActions: {
    display: 'flex',
    alignItems: 'flex-end',
    flexShrink: 0,
    ...shorthands.gap('8px'),
  },
  calendarRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'stretch',
    flexShrink: 0,
    ...shorthands.gap('4px'),
    ...shorthands.padding('0', '4px'),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    minHeight: '280px',
  },
  calendarStrip: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    minWidth: 0,
  },
  navButton: {
    alignSelf: 'center',
    flexShrink: 0,
  },
  gridContainer: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): fit the event grid to
    // the widget container. `minHeight: 0` lets the flex child shrink below
    // its intrinsic content height so the DataGrid can size to the
    // remaining space and scroll internally instead of pushing the root
    // beyond its bounds. `overflow: hidden` clips any DataGrid bleed
    // (header sticky / footer pagination) to the container edge.
    minHeight: 0,
    overflow: 'hidden',
    marginTop: tokens.spacingVerticalL,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Xrm context resolution — kept for the event-type options fetch + the
// event-detail modal navigation in the outer widget shell.
// ─────────────────────────────────────────────────────────────────────────────

function getXrm(): typeof Xrm | null {
  try {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const w: any = window;
    if (w?.Xrm?.WebApi) return w.Xrm;
    if (w?.parent?.Xrm?.WebApi) return w.parent.Xrm;
    if (w?.top?.Xrm?.WebApi) return w.top.Xrm;
    /* eslint-enable @typescript-eslint/no-explicit-any */
  } catch {
    /* cross-origin frame access */
  }
  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Date helpers
// ─────────────────────────────────────────────────────────────────────────────

function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Inner layout — consumes EventsPageContext (Q4 — provider kept so eventDates
// flow continues to drive the calendar strip's dot indicators).
// ─────────────────────────────────────────────────────────────────────────────

interface ICalendarWorkspaceLayoutProps {
  initialDateField: string;
}

const CalendarWorkspaceLayout: React.FC<ICalendarWorkspaceLayoutProps> = ({ initialDateField }) => {
  const styles = useStyles();
  // eventDates: derived from records via handleRecordsLoaded → setEventDates.
  // refreshTrigger: bumped externally to remount the grid (manual refresh).
  // setCalendarFilter/setRecordTypeFilter/setStatusFilter are NO LONGER USED
  //   by the grid pipeline (task 033b Q2 — hostFilters replaces the dispatch
  //   loop). The methods remain on context for any external integrations.
  const { eventDates, refreshTrigger, setEventDates } = useEventsPageContext();

  // ── Calendar strip state (task 116) ──────────────────────────────────────
  const [viewDate, setViewDate] = React.useState<Date>(() => startOfMonth(new Date()));

  const stripRef = React.useRef<HTMLDivElement | null>(null);
  const [monthsToShow, setMonthsToShow] = React.useState<number>(2);

  React.useEffect(() => {
    if (typeof ResizeObserver === 'undefined') return;
    const node = stripRef.current;
    if (!node) return;
    const ro = new ResizeObserver(entries => {
      for (const entry of entries) {
        const w = entry.contentRect.width;
        if (w > 0) {
          const next = computeMonthsForWidth(w);
          setMonthsToShow(prev => (prev === next ? prev : next));
        }
      }
    });
    ro.observe(node);
    return () => ro.disconnect();
  }, []);

  // Collapse state (task 116) — persisted in localStorage.
  const [calendarCollapsed, setCalendarCollapsed] = React.useState<boolean>(() => readCollapsedPref());
  const toggleCollapsed = React.useCallback(() => {
    setCalendarCollapsed(prev => {
      const next = !prev;
      writeCollapsedPref(next);
      return next;
    });
  }, []);

  // ── Day-cell selection ───────────────────────────────────────────────────
  // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): the previous `selectedDate`
  // single-day state has been removed. Day clicks now drive the range via
  // `pending.fromDate` / `pending.toDate` exclusively (CalendarSection runs in
  // `clickMode='range'`, emits `single` / `range` / `clear` via
  // `onFilterChange`, and `onCalendarFilter` below mirrors that into pending
  // state). One source of truth for date filtering.

  // ── Event-day derivation (task 120) ──────────────────────────────────────
  const lastAutoAnchorSignatureRef = React.useRef<string | null>(null);

  const handleRecordsLoaded = React.useCallback(
    (records: ReadonlyArray<Record<string, unknown>>) => {
      const counts = new Map<string, number>();
      const eventDateObjects: Date[] = [];
      for (const r of records) {
        const dateStr = (r.sprk_duedate as string | undefined) || (r.createdon as string | undefined);
        if (!dateStr) continue;
        const d = new Date(dateStr);
        if (Number.isNaN(d.getTime())) continue;
        const key = toIsoDate(d);
        counts.set(key, (counts.get(key) ?? 0) + 1);
        eventDateObjects.push(d);
      }
      const next: IEventDateInfo[] = Array.from(counts.entries()).map(([date, count]) => ({ date, count }));
      setEventDates(next);

      // Task 124: auto-anchor to the earliest event's month if none are visible.
      if (eventDateObjects.length === 0) return;
      const sortedKeys = Array.from(counts.keys()).sort();
      const signature = sortedKeys.join('|');
      if (lastAutoAnchorSignatureRef.current === signature) return;

      const visibleStart = new Date(viewDate.getFullYear(), viewDate.getMonth(), 1);
      const visibleEnd = new Date(viewDate.getFullYear(), viewDate.getMonth() + monthsToShow, 0);
      const anyInVisible = eventDateObjects.some(d => d >= visibleStart && d <= visibleEnd);
      if (anyInVisible) {
        lastAutoAnchorSignatureRef.current = signature;
        return;
      }

      const earliest = eventDateObjects.reduce((acc, d) => (d < acc ? d : acc), eventDateObjects[0]);
      setViewDate(new Date(earliest.getFullYear(), earliest.getMonth(), 1));
      lastAutoAnchorSignatureRef.current = signature;
    },
    [setEventDates, viewDate, monthsToShow]
  );

  // ── Event-type options (Task 130) ────────────────────────────────────────
  const [eventTypeOptions, setEventTypeOptions] = React.useState<IEventTypeOption[]>([]);

  React.useEffect(() => {
    let cancelled = false;

    async function fetchEventTypeOptions() {
      const xrm = getXrm();
      if (!xrm?.WebApi) {
        console.warn('[CalendarWorkspaceWidget] Xrm.WebApi unavailable; Event Type dropdown will be empty.');
        return;
      }
      try {
        // Task 131 (R13 follow-up #10, 2026-05-23): operator confirmed the
        // correct schema. Event Type LOOKUP TABLE = sprk_eventtype_ref; the
        // GUID column = sprk_eventtype_refid; the lookup FIELD on sprk_event
        // is also sprk_eventtype_ref (resolved via the OData annotated value
        // `_sprk_eventtype_ref_value`).
        const result = await xrm.WebApi.retrieveMultipleRecords(
          'sprk_eventtype_ref',
          '?$select=sprk_eventtype_refid,sprk_name' +
            '&$filter=statecode eq 0' +
            '&$orderby=sprk_name asc' +
            '&$top=200'
        );
        if (cancelled) return;
        /* eslint-disable @typescript-eslint/no-explicit-any */
        const types: IEventTypeOption[] = (result.entities || [])
          .map((t: any) => ({
            id: t.sprk_eventtype_refid,
            name: t.sprk_name || 'Unnamed Type',
          }))
          .filter((t: IEventTypeOption) => !!t.id);
        /* eslint-enable @typescript-eslint/no-explicit-any */
        setEventTypeOptions(types);
      } catch (err) {
        console.warn('[CalendarWorkspaceWidget] Failed to fetch sprk_eventtype options:', err);
      }
    }

    fetchEventTypeOptions();
    return () => {
      cancelled = true;
    };
  }, []);

  // ── Pending vs applied filter state (Task 130 — preserved per Q2) ────────
  const initialFilterSet: ICalendarFilterSet = React.useMemo(
    () => ({
      ...EMPTY_FILTER_SET,
      dateField: initialDateField,
    }),
    [initialDateField]
  );

  const [pending, setPending] = React.useState<ICalendarFilterSet>(initialFilterSet);
  const [applied, setApplied] = React.useState<ICalendarFilterSet>(initialFilterSet);

  const hasUnapplied = !filterSetsEqual(pending, applied);
  const hasAnyApplied = !filterSetIsEmpty(applied);

  // ── hostFilters useMemo ──────────────────────────────────────────────────
  // Map applied → flat HostFilterCondition[] (task 033a API).
  //
  // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): single source of truth for
  // date filtering — `applied.fromDate` / `applied.toDate` (the range set by
  // CalendarSection day clicks in `clickMode='range'`). When the user has
  // not chosen a date-field, default to `sprk_duedate` so the range still
  // filters something meaningful. When both ends of the range equal the same
  // day (the "first-click-only" interim state), this emits a single-day
  // `on` filter — exactly what the user expects to see after the first
  // click before they pick the second.
  const hostFilters = React.useMemo<HostFilterCondition[]>(() => {
    const conditions: HostFilterCondition[] = [];

    if (applied.eventTypeId) {
      conditions.push({
        attribute: 'sprk_eventtype_ref',
        operator: 'eq',
        value: applied.eventTypeId,
      });
    }

    if (applied.eventStatusValue !== null) {
      conditions.push({
        attribute: 'sprk_eventstatus',
        operator: 'eq',
        value: applied.eventStatusValue,
      });
    }

    if (applied.fromDate || applied.toDate) {
      const effectiveDateField =
        applied.dateField && applied.dateField !== DATE_FIELD_NONE ? applied.dateField : 'sprk_duedate';
      if (applied.fromDate && applied.toDate) {
        if (applied.fromDate === applied.toDate) {
          conditions.push({
            attribute: effectiveDateField,
            operator: 'on',
            value: applied.fromDate,
          });
        } else {
          conditions.push({
            attribute: effectiveDateField,
            operator: 'between',
            value: [applied.fromDate, applied.toDate],
          });
        }
      } else if (applied.fromDate) {
        conditions.push({
          attribute: effectiveDateField,
          operator: 'on-or-after',
          value: applied.fromDate,
        });
      } else if (applied.toDate) {
        conditions.push({
          attribute: effectiveDateField,
          operator: 'on-or-before',
          value: applied.toDate,
        });
      }
    }

    return conditions;
  }, [applied]);

  // ── Apply + Clear handlers ───────────────────────────────────────────────
  const onApply = React.useCallback(() => {
    setApplied(pending);
  }, [pending]);

  const onClear = React.useCallback(() => {
    setPending(EMPTY_FILTER_SET);
    setApplied(EMPTY_FILTER_SET);
  }, []);

  // ── Calendar range click handler ─────────────────────────────────────────
  // ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): CalendarSection runs in
  // `clickMode='range'` and emits three filter shapes:
  //   • `{ type: 'single', date }` after the first click — range start picked,
  //     range end still pending. We mirror this as `fromDate=toDate=date` so
  //     the grid filters to the chosen day immediately.
  //   • `{ type: 'range', start, end }` after the second click — finalize.
  //   • `{ type: 'clear' }` when the calendar's third-click reset occurs (or
  //     the host's Clear button — but we drive that via setPending directly).
  // The Apply button stays the gate to push `pending` → `applied` so all
  // filter changes (date + type + status) feel coherent.
  const onCalendarFilter = React.useCallback((filter: CalendarFilterOutput | null) => {
    if (!filter || filter.type === 'clear') {
      setPending(prev => ({ ...prev, fromDate: '', toDate: '' }));
      return;
    }
    if (filter.type === 'single') {
      setPending(prev => ({ ...prev, fromDate: filter.date, toDate: filter.date }));
      return;
    }
    // 'range'
    setPending(prev => ({ ...prev, fromDate: filter.start, toDate: filter.end }));
  }, []);

  // ── Month navigation handlers (task 116) ─────────────────────────────────
  const onPrevMonth = React.useCallback(
    (e: React.MouseEvent<HTMLButtonElement>) => {
      const step = e.shiftKey ? Math.max(1, monthsToShow) : 1;
      setViewDate(prev => addMonths(prev, -step));
    },
    [monthsToShow]
  );
  const onNextMonth = React.useCallback(
    (e: React.MouseEvent<HTMLButtonElement>) => {
      const step = e.shiftKey ? Math.max(1, monthsToShow) : 1;
      setViewDate(prev => addMonths(prev, step));
    },
    [monthsToShow]
  );

  // ── Dropdown display helpers ─────────────────────────────────────────────
  const dateFieldDisplay = React.useMemo(() => {
    const f = DATE_FIELDS.find(x => x.value === pending.dateField);
    return f ? f.label : '(none)';
  }, [pending.dateField]);

  const eventTypeDisplay = React.useMemo(() => {
    if (!pending.eventTypeId) return 'All';
    return eventTypeOptions.find(t => t.id === pending.eventTypeId)?.name ?? 'All';
  }, [pending.eventTypeId, eventTypeOptions]);

  const eventStatusDisplay = React.useMemo(() => {
    if (pending.eventStatusValue === null) return 'All';
    return STATUS_OPTIONS.find(s => s.value === pending.eventStatusValue)?.label ?? 'All';
  }, [pending.eventStatusValue]);

  // ── DataGrid wiring ──────────────────────────────────────────────────────
  // Stable XrmDataverseClient instance for the lifetime of the layout.
  const dataverseClientRef = React.useRef<XrmDataverseClient | null>(null);
  if (!dataverseClientRef.current) {
    dataverseClientRef.current = new XrmDataverseClient();
  }

  // onRecordOpen — bridges DataGrid row-click to the outer widget's
  // event-modal navigation. Routed through context for parity with the
  // existing `openEvent` consumer hook signature (1-arg).
  const { openEvent } = useEventsPageContext();
  const onRecordOpen = React.useCallback(
    (recordId: string) => {
      openEvent(recordId);
    },
    [openEvent]
  );

  return (
    <div className={styles.root}>
      {/* (1) Filter row — preserved AS-IS from task 130/136 per Q2 sign-off. */}
      <div className={styles.dateRangeRow}>
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>Event Type</Label>
          <Dropdown
            value={eventTypeDisplay}
            selectedOptions={pending.eventTypeId ? [pending.eventTypeId] : ['']}
            onOptionSelect={(_e, data) => {
              const v = data.optionValue ?? '';
              setPending(prev => ({
                ...prev,
                eventTypeId: v === '' ? null : v,
              }));
            }}
          >
            <Option value="" text="All">
              All
            </Option>
            {eventTypeOptions.map(t => (
              <Option key={t.id} value={t.id} text={t.name}>
                {t.name}
              </Option>
            ))}
          </Dropdown>
        </div>
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>Event Status</Label>
          <Dropdown
            value={eventStatusDisplay}
            selectedOptions={pending.eventStatusValue !== null ? [String(pending.eventStatusValue)] : ['']}
            onOptionSelect={(_e, data) => {
              const v = data.optionValue ?? '';
              setPending(prev => ({
                ...prev,
                eventStatusValue: v === '' ? null : Number(v),
              }));
            }}
          >
            <Option value="" text="All">
              All
            </Option>
            {STATUS_OPTIONS.map(s => (
              <Option key={s.value} value={String(s.value)} text={s.label}>
                {s.label}
              </Option>
            ))}
          </Dropdown>
        </div>
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>Filter by Date Field</Label>
          <Dropdown
            value={dateFieldDisplay}
            selectedOptions={[pending.dateField]}
            onOptionSelect={(_e, data) => {
              const next = data.optionValue ?? DATE_FIELD_NONE;
              setPending(prev => ({ ...prev, dateField: next }));
            }}
          >
            {DATE_FIELDS.map(f => (
              <Option key={f.value || 'none'} value={f.value} text={f.label}>
                {f.label}
              </Option>
            ))}
          </Dropdown>
        </div>
        {/* ai-spaarke-ai-workspace-UI-r1 #1 (2026-06-08): explicit From / To
            date inputs removed. The range is now picked entirely by clicking
            days on the calendar strip (first click = From, second click = To,
            third click resets). See `onCalendarFilter` for the wiring. */}
        <div className={styles.filterActions}>
          {hasUnapplied && (
            <Button appearance="primary" size="small" onClick={onApply} aria-label="Apply filters">
              Apply
            </Button>
          )}
          {hasAnyApplied && (
            <Button appearance="subtle" size="small" onClick={onClear} aria-label="Clear filters">
              Clear
            </Button>
          )}
          <Tooltip content={calendarCollapsed ? 'Expand calendar' : 'Collapse calendar'} relationship="label">
            <Button
              appearance="subtle"
              icon={calendarCollapsed ? <CaretDown24Regular /> : <CaretUp24Regular />}
              onClick={toggleCollapsed}
              aria-expanded={!calendarCollapsed}
              aria-label={calendarCollapsed ? 'Expand calendar' : 'Collapse calendar'}
            />
          </Tooltip>
        </div>
      </div>

      {/* (2) Calendar strip — preserved unchanged. eventDates flow comes from
              handleRecordsLoaded → setEventDates → context.eventDates. */}
      {!calendarCollapsed && (
        <div className={styles.calendarRow}>
          <Tooltip content="Previous month (Shift+click: jump by window)" relationship="label">
            <Button
              className={styles.navButton}
              appearance="subtle"
              icon={<ChevronLeft20Regular />}
              onClick={onPrevMonth}
              aria-label="Previous month"
            />
          </Tooltip>
          <div ref={stripRef} className={styles.calendarStrip}>
            <CalendarSection
              eventDates={eventDates as IEventDateInfo[]}
              onFilterChange={onCalendarFilter}
              viewDate={viewDate}
              monthsToShow={monthsToShow}
              layout="horizontal"
              clickMode="range"
            />
          </div>
          <Tooltip content="Next month (Shift+click: jump by window)" relationship="label">
            <Button
              className={styles.navButton}
              appearance="subtle"
              icon={<ChevronRight20Regular />}
              onClick={onNextMonth}
              aria-label="Next month"
            />
          </Tooltip>
        </div>
      )}

      {/* (3) Grid — task 033b: <DataGrid configId hostFilters
              onRecordsLoaded onRecordOpen/>. The DataGrid's own command bar
              (configjson commandBar.primary = +New / Delete / Refresh) handles
              what the old widget toolbar did per Q1 sign-off. */}
      <div className={styles.gridContainer}>
        <DataGrid
          key={refreshTrigger}
          configId={EVENT_CONFIG_ID}
          dataverseClient={dataverseClientRef.current}
          hostFilters={hostFilters}
          onRecordsLoaded={handleRecordsLoaded}
          onRecordOpen={onRecordOpen}
        />
      </div>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Top-level widget component — provides EventsPageProvider (Q4 sign-off)
// ─────────────────────────────────────────────────────────────────────────────

export const CalendarWorkspaceWidget: React.FC<CalendarWorkspaceWidgetProps> = ({ initialDateField = '' }) => {
  const handleOpenEvent = React.useCallback((eventId: string, _eventTypeId?: string) => {
    const xrm = getXrm();
    if (!xrm?.Navigation?.navigateTo) {
      console.warn('[CalendarWidget] Xrm.Navigation.navigateTo unavailable; cannot open event modal.');
      return;
    }
    const cleanId = eventId.replace(/[{}]/g, '');
    try {
      xrm.Navigation.navigateTo(
        {
          pageType: 'entityrecord',
          entityName: EVENT_ENTITY_NAME,
          entityId: cleanId,
        },
        {
          target: 2,
          width: { value: 80, unit: '%' },
          height: { value: 80, unit: '%' },
          position: 1,
        }
      );
    } catch (e) {
      console.error('[CalendarWidget] Failed to open event modal:', e);
    }
  }, []);

  return (
    <EventsPageProvider onOpenEvent={handleOpenEvent}>
      <CalendarWorkspaceLayout initialDateField={initialDateField} />
    </EventsPageProvider>
  );
};

export default CalendarWorkspaceWidget;
