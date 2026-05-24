/**
 * CalendarWorkspaceWidget — the 5th SpaarkeAi system workspace widget.
 *
 * Task 130 (Round 13 follow-up #9, 2026-05-23) — operator design directive:
 *
 *   "If the grid has a filter applied by virtue of the drop down, then
 *    the Events shown will be different. This is confusing to the users."
 *
 * Refactor consequences:
 *
 *  1. The view-selector dropdown is REMOVED from the widget. The grid now
 *     queries via GridSection's OData fallback path (no view-based
 *     FetchXML). The standalone EventsPage code page keeps its own view
 *     selector — that flow is untouched.
 *  2. Two NEW filter dropdowns on the filter row: Event Type (lookup-
 *     based, fetched from `sprk_eventtype` records) and Event Status
 *     (numeric choice, values 0-7 enumerated below).
 *  3. The "Filter by Date Field" dropdown gains a "(none)" option →
 *     dateField = null means no date filter is applied. Default is null
 *     so the widget initially loads ALL events with no date narrowing.
 *  4. Pending-vs-applied state pattern. Filter-row changes update
 *     `pending`. An Apply button copies pending → applied; the grid +
 *     calendar consume `applied`. Apply button visibility: only when
 *     pending differs from applied. Clear button visibility: whenever
 *     any applied filter is non-default OR a calendar day is selected.
 *  5. Calendar day click BYPASSES the pending mechanism (operator:
 *     "day click applies immediately") — directly dispatches the
 *     calendar filter through context. Clear still resets it.
 *  6. Calendar highlights derive from `filteredEvents` (task 127) — the
 *     applied Type/Status/date filters narrow what the grid shows, and
 *     the calendar mirrors that visualization. When dateField is null,
 *     buildDateFilter is a no-op so the grid returns the full set, and
 *     CalendarSection highlights all of them via the task-121 fallback
 *     chain (sprk_duedate || createdon).
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
 *  - Task 130 — THIS TASK
 *
 * Integration seam (operator decision, Round 9 Q&A 2026-05-22):
 *   Event detail opens as a MODAL via `Xrm.Navigation.navigateTo(
 *     { pageType: "entityrecord", entityName, entityId },
 *     { target: 2, width/height: 80% }
 *   )` — NOT via `Xrm.App.sidePanes`. Mirrors the Documents Expand
 *   affordance from task 111 (consistent SpaarkeAi modal-detail UX).
 *
 * CLAUDE.md §10 (BFF Hygiene): ZERO new BFF endpoints. All Dataverse
 * access flows through `Xrm.WebApi.retrieveMultipleRecords` (ADR-028)
 * and GridSection's existing OData fallback path.
 *
 * ADR-021 (Fluent v9 tokens), ADR-022 (React 19), ADR-028 (Xrm.WebApi
 * for metadata).
 *
 * @see src/client/shared/Spaarke.Events.Components/src/context/EventsPageContext.tsx
 * @see src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarSection.tsx
 * @see src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx
 * @see src/client/shared/Spaarke.Events.Components/src/components/RecordTypeFilter/RecordTypeFilter.tsx (sprk_eventtype fetch pattern)
 * @see src/solutions/EventsPage/src/App.tsx (the standalone composition this widget mirrors — keeps its own view dropdown)
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Dropdown,
  Option,
  Input,
  Label,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import {
  Add24Regular,
  Delete24Regular,
  ArrowClockwise24Regular,
  CalendarLtr24Regular,
  CheckmarkCircle24Regular,
  Dismiss24Regular,
  DismissCircle24Regular,
  Pause24Regular,
  Archive24Regular,
  ChevronLeft20Regular,
  ChevronRight20Regular,
  CaretUp24Regular,
  CaretDown24Regular,
  Open24Regular,
} from "@fluentui/react-icons";

import {
  EventsPageProvider,
  useEventsPageContext,
} from "../../context/EventsPageContext";
import { CalendarSection } from "../../components/CalendarSection/CalendarSection";
import type { IEventDateInfo } from "../../components/CalendarSection/CalendarSection";
import { GridSection } from "../../components/GridSection/GridSection";
import type { IEventRecord } from "../../components/GridSection/GridSection";
import { addMonths, startOfMonth } from "../../utils/dateMath";

// ─────────────────────────────────────────────────────────────────────────────
// Xrm typing — host-provided global
// ─────────────────────────────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

const EVENT_ENTITY_NAME = "sprk_event";

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

const StateCode = {
  ACTIVE: 0,
  INACTIVE: 1,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Event Status options (Task 130) — mirrors STATUS_FILTER_OPTIONS in
// GridSection. Hard-coded because sprk_eventstatus is an integer field with
// no Dataverse choice metadata attached (the formatted-value labels come
// from the per-row OData annotation).
// ─────────────────────────────────────────────────────────────────────────────

interface IStatusOption {
  value: number;
  label: string;
}

const STATUS_OPTIONS: IStatusOption[] = [
  { value: EventStatus.DRAFT, label: "Draft" },
  { value: EventStatus.OPEN, label: "Open" },
  { value: EventStatus.COMPLETED, label: "Completed" },
  { value: EventStatus.CLOSED, label: "Closed" },
  { value: EventStatus.ON_HOLD, label: "On Hold" },
  { value: EventStatus.CANCELLED, label: "Cancelled" },
  { value: EventStatus.REASSIGNED, label: "Reassigned" },
  { value: EventStatus.ARCHIVED, label: "Archived" },
];

// ─────────────────────────────────────────────────────────────────────────────
// Date-range filter options (Task 130 — adds "(none)" sentinel)
// ─────────────────────────────────────────────────────────────────────────────

const DATE_FIELD_NONE = "" as const; // sentinel for "no date filter"

const DATE_FIELDS = [
  { value: DATE_FIELD_NONE, label: "(none)" },
  { value: "sprk_duedate", label: "Due Date" },
  { value: "sprk_startdate", label: "Start Date" },
  { value: "createdon", label: "Created On" },
  { value: "modifiedon", label: "Modified On" },
] as const;

// ─────────────────────────────────────────────────────────────────────────────
// Collapse persistence (task 116) — mirrors `pinnedWorkspaces.ts` pattern
// ─────────────────────────────────────────────────────────────────────────────

const CALENDAR_COLLAPSED_KEY = "spaarke:calendar:collapsed";

function readCollapsedPref(): boolean {
  try {
    return window.localStorage?.getItem(CALENDAR_COLLAPSED_KEY) === "1";
  } catch {
    return false;
  }
}

function writeCollapsedPref(collapsed: boolean): void {
  try {
    if (collapsed) {
      window.localStorage?.setItem(CALENDAR_COLLAPSED_KEY, "1");
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
// Pending-vs-applied state model (Task 130)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Filter row state — both the in-flight "pending" set (what the user is
 * editing) and the "applied" set (what the grid + calendar consume).
 *
 * Day-cell clicks BYPASS this state machine and dispatch directly through
 * EventsPageContext.setCalendarFilter (operator: "day click applies
 * immediately"). The selectedDate state in the layout is independent of
 * `applied` so a user can click a day with no filter row activity, and
 * still see the Clear button.
 */
interface ICalendarFilterSet {
  /** Date-field selector. Empty string ("(none)") = no date filter applied. */
  dateField: string;
  /** Lower bound for the date filter (YYYY-MM-DD). Empty = no lower bound. */
  fromDate: string;
  /** Upper bound for the date filter (YYYY-MM-DD). Empty = no upper bound. */
  toDate: string;
  /** sprk_eventtype lookup GUID. null = all event types. */
  eventTypeId: string | null;
  /** sprk_eventstatus numeric value (0-7). null = all statuses. */
  eventStatusValue: number | null;
}

const EMPTY_FILTER_SET: ICalendarFilterSet = {
  dateField: DATE_FIELD_NONE,
  fromDate: "",
  toDate: "",
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
// Event-type option fetched from sprk_eventtype records
// ─────────────────────────────────────────────────────────────────────────────

interface IEventTypeOption {
  /** sprk_eventtypeid GUID (used as OData filter value via _sprk_eventtype_ref_value). */
  id: string;
  /** sprk_name (display label). */
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
   *
   * Backward compatibility: callers that passed `"sprk_duedate"` (the
   * pre-task-130 default) will still get that behavior on initial mount.
   */
  initialDateField?: string;
  /**
   * Deprecated by task 130 (view-selector removed). The prop remains in
   * the signature for backward compatibility but is no longer consumed.
   */
  initialViewId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    ...shorthands.padding("8px"),
    ...shorthands.gap("8px"),
    overflowY: "auto",
    boxSizing: "border-box",
    scrollbarWidth: "none",
    "::-webkit-scrollbar": {
      display: "none",
    },
  },
  // Task 138 (R13 follow-up #17, 2026-05-24): operator confirmed via
  // DevTools that the gap CSS is being emitted but is much narrower
  // than 28px configured. Griffel's `gap` shorthand isn't emitting the
  // column-gap value reliably in this stack across tasks 134-137.
  // Falling back to explicit `marginRight` on each field — old-school
  // CSS that works regardless of any Griffel atomic-CSS quirks.
  //
  // The row gap (row-gap when fields wrap) still uses Griffel's
  // shorthands.gap single-arg form (vertical-only, which Griffel
  // handles reliably).
  dateRangeRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-end",
    flexWrap: "wrap",
    rowGap: "12px",
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  // Task 137: action group uses `margin-left: auto` to right-align
  // without disrupting flex-wrap.
  filterActions: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-end",
    ...shorthands.gap("8px"),
    flex: "0 0 auto",
    marginLeft: "auto",
  },
  dateRangeField: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
    flex: "0 0 140px",
    minWidth: "0",
    // Task 138: explicit horizontal spacing between fields via
    // marginRight. This is the reliable old-school approach since
    // Griffel's gap shorthand wasn't emitting the column-gap value
    // (operator confirmed visually + via DevTools). Every field gets
    // 28px right-margin; the last field's trailing margin is consumed
    // by the action group's marginLeft:auto, so it's invisible to
    // the user.
    marginRight: "28px",
  },
  dateRangeLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  filterRowSpacer: {
    flex: "1 1 auto",
  },
  collapseToggleSlot: {
    display: "flex",
    alignItems: "flex-end",
    flexShrink: 0,
  },
  // Task 122/130: container for the Apply + Clear buttons. Bottom-aligned
  // so they sit flush with the filter inputs.
  filterButtonsSlot: {
    display: "flex",
    alignItems: "flex-end",
    flexShrink: 0,
    ...shorthands.gap("8px"),
  },
  calendarRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "stretch",
    flexShrink: 0,
    ...shorthands.gap("4px"),
    ...shorthands.padding("0", "4px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
    minHeight: "280px",
  },
  calendarStrip: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    minWidth: 0,
  },
  navButton: {
    alignSelf: "center",
    flexShrink: 0,
  },
  toolbarRow: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    marginTop: tokens.spacingVerticalL,
    ...shorthands.padding("0", "4px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  toolbarRowSpacer: {
    flex: "1 1 auto",
  },
  // Task 130: view-selector row removed entirely. The widget no longer
  // hosts a ViewSelectorDropdown — the grid queries via GridSection's
  // OData fallback path. EventsPage standalone keeps its own dropdown.
  gridContainer: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    minHeight: "320px",
    marginTop: tokens.spacingVerticalL,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Xrm context resolution + bulk-action helpers (mirror EventsPage App.tsx)
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

async function bulkStatusUpdate(
  ids: string[],
  newStatus: number,
  label: string,
  extra?: Record<string, unknown>,
): Promise<boolean> {
  const xrm = getXrm();
  if (!xrm?.WebApi) return false;
  const clean = ids.map((id) => id.replace(/[{}]/g, ""));
  const payload: Record<string, unknown> = { sprk_eventstatus: newStatus, ...extra };
  try {
    await Promise.all(
      clean.map((id) => xrm.WebApi.updateRecord(EVENT_ENTITY_NAME, id, payload)),
    );
    xrm.App?.addGlobalNotification?.({
      type: 2,
      level: 1,
      message: `${ids.length} event(s) set to ${label}`,
      showCloseButton: true,
    });
    return true;
  } catch (e) {
    console.error("[CalendarWidget] Bulk status update failed:", e);
    return false;
  }
}

async function bulkArchive(ids: string[]): Promise<boolean> {
  const xrm = getXrm();
  if (!xrm?.WebApi) return false;
  const clean = ids.map((id) => id.replace(/[{}]/g, ""));
  try {
    await Promise.all(
      clean.map(async (id) => {
        await xrm.WebApi.updateRecord(EVENT_ENTITY_NAME, id, {
          sprk_eventstatus: EventStatus.ARCHIVED,
        });
        await xrm.WebApi.updateRecord(EVENT_ENTITY_NAME, id, {
          statecode: StateCode.INACTIVE,
          statuscode: 2,
        });
      }),
    );
    xrm.App?.addGlobalNotification?.({
      type: 2,
      level: 1,
      message: `${ids.length} event(s) archived`,
      showCloseButton: true,
    });
    return true;
  } catch (e) {
    console.error("[CalendarWidget] Bulk archive failed:", e);
    return false;
  }
}

async function confirmDialog(title: string, text: string, confirmLabel: string): Promise<boolean> {
  const xrm = getXrm();
  if (xrm?.Navigation?.openConfirmDialog) {
    const result = await xrm.Navigation.openConfirmDialog({
      title,
      text,
      confirmButtonLabel: confirmLabel,
      cancelButtonLabel: "Cancel",
    });
    return !!result?.confirmed;
  }
  return window.confirm(text);
}

// ─────────────────────────────────────────────────────────────────────────────
// Inner layout — consumes EventsPageContext
// ─────────────────────────────────────────────────────────────────────────────

interface ICalendarWorkspaceLayoutProps {
  initialDateField: string;
}

const CalendarWorkspaceLayout: React.FC<ICalendarWorkspaceLayoutProps> = ({
  initialDateField,
}) => {
  const styles = useStyles();
  const {
    filters,
    eventDates,
    refreshTrigger,
    setCalendarFilter,
    setStatusFilter,
    setRecordTypeFilter,
    setEventDates,
    openEvent,
    refreshGrid,
  } = useEventsPageContext();

  // ── Calendar strip state (task 116) ──────────────────────────────────────
  const [viewDate, setViewDate] = React.useState<Date>(() =>
    startOfMonth(new Date()),
  );

  const stripRef = React.useRef<HTMLDivElement | null>(null);
  const [monthsToShow, setMonthsToShow] = React.useState<number>(2);

  React.useEffect(() => {
    if (typeof ResizeObserver === "undefined") return;
    const node = stripRef.current;
    if (!node) return;
    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const w = entry.contentRect.width;
        if (w > 0) {
          const next = computeMonthsForWidth(w);
          setMonthsToShow((prev) => (prev === next ? prev : next));
        }
      }
    });
    ro.observe(node);
    return () => ro.disconnect();
  }, []);

  // Collapse state (task 116) — persisted in localStorage.
  const [calendarCollapsed, setCalendarCollapsed] = React.useState<boolean>(
    () => readCollapsedPref(),
  );
  const toggleCollapsed = React.useCallback(() => {
    setCalendarCollapsed((prev) => {
      const next = !prev;
      writeCollapsedPref(next);
      return next;
    });
  }, []);

  // ── Day-cell selection (task 118) ────────────────────────────────────────
  // Day-click bypasses the pending/applied state machine (operator
  // directive in task 130: "day click applies immediately").
  const [selectedDate, setSelectedDate] = React.useState<Date | null>(null);

  // ── Event-day derivation (task 120) ──────────────────────────────────────
  // See task 120/121/124/127 history in the file header. Unchanged in task 130.
  const lastAutoAnchorSignatureRef = React.useRef<string | null>(null);

  const handleRecordsLoaded = React.useCallback(
    (records: IEventRecord[]) => {
      const counts = new Map<string, number>();
      const eventDateObjects: Date[] = [];
      for (const r of records) {
        const dateStr =
          r.sprk_duedate ||
          (r as unknown as { createdon?: string }).createdon;
        if (!dateStr) continue;
        const d = new Date(dateStr);
        if (Number.isNaN(d.getTime())) continue;
        const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
        counts.set(key, (counts.get(key) ?? 0) + 1);
        eventDateObjects.push(d);
      }
      const next: IEventDateInfo[] = Array.from(counts.entries()).map(
        ([date, count]) => ({ date, count }),
      );
      setEventDates(next);

      // Task 124: auto-anchor to the earliest event's month if none are visible.
      if (eventDateObjects.length === 0) return;
      const sortedKeys = Array.from(counts.keys()).sort();
      const signature = sortedKeys.join("|");
      if (lastAutoAnchorSignatureRef.current === signature) return;

      const visibleStart = new Date(viewDate.getFullYear(), viewDate.getMonth(), 1);
      const visibleEnd = new Date(
        viewDate.getFullYear(),
        viewDate.getMonth() + monthsToShow,
        0,
      );
      const anyInVisible = eventDateObjects.some(
        (d) => d >= visibleStart && d <= visibleEnd,
      );
      if (anyInVisible) {
        lastAutoAnchorSignatureRef.current = signature;
        return;
      }

      const earliest = eventDateObjects.reduce(
        (acc, d) => (d < acc ? d : acc),
        eventDateObjects[0],
      );
      setViewDate(new Date(earliest.getFullYear(), earliest.getMonth(), 1));
      lastAutoAnchorSignatureRef.current = signature;
    },
    [setEventDates, viewDate, monthsToShow],
  );

  // ── Event-type options (Task 130) ────────────────────────────────────────
  // Fetched once at mount from `sprk_eventtype` records. Pattern mirrors
  // RecordTypeFilter.tsx (lines 149-192). console.warn + empty fallback on
  // failure — widget still renders with only the "All" option.
  const [eventTypeOptions, setEventTypeOptions] = React.useState<IEventTypeOption[]>([]);

  React.useEffect(() => {
    let cancelled = false;

    async function fetchEventTypeOptions() {
      const xrm = getXrm();
      if (!xrm?.WebApi) {
        console.warn(
          "[CalendarWorkspaceWidget] Xrm.WebApi unavailable; Event Type dropdown will be empty.",
        );
        return;
      }
      try {
        // Task 131 (R13 follow-up #10, 2026-05-23): operator confirmed the
        // correct schema names. The Event Type LOOKUP TABLE is
        // `sprk_eventtype_ref` (NOT `sprk_eventtype`); its GUID column is
        // `sprk_eventtype_refid`; the lookup FIELD on sprk_event is also
        // `sprk_eventtype_ref` (resolved via the OData annotated value
        // `_sprk_eventtype_ref_value` in GridSection's filter). Task 130
        // used `sprk_eventtype` which produced 0 records → blank dropdown.
        const result = await xrm.WebApi.retrieveMultipleRecords(
          "sprk_eventtype_ref",
          "?$select=sprk_eventtype_refid,sprk_name" +
            "&$filter=statecode eq 0" +
            "&$orderby=sprk_name asc" +
            "&$top=200",
        );
        if (cancelled) return;
        /* eslint-disable @typescript-eslint/no-explicit-any */
        const types: IEventTypeOption[] = (result.entities || [])
          .map((t: any) => ({
            id: t.sprk_eventtype_refid,
            name: t.sprk_name || "Unnamed Type",
          }))
          .filter((t: IEventTypeOption) => !!t.id);
        /* eslint-enable @typescript-eslint/no-explicit-any */
        setEventTypeOptions(types);
      } catch (err) {
        console.warn(
          "[CalendarWorkspaceWidget] Failed to fetch sprk_eventtype options:",
          err,
        );
      }
    }

    fetchEventTypeOptions();
    return () => {
      cancelled = true;
    };
  }, []);

  // ── Pending vs applied filter state (Task 130) ───────────────────────────
  // `pending` holds in-flight edits from the filter row controls.
  // `applied` is what the grid + calendar consume. Apply copies pending →
  // applied AND dispatches the resulting filters through EventsPageContext.
  // Clear resets BOTH to EMPTY_FILTER_SET and clears selectedDate, and
  // dispatches the clear through context. Initial dateField honors the
  // `initialDateField` prop (default `""` = no date filter per task 130).
  const initialFilterSet: ICalendarFilterSet = React.useMemo(
    () => ({
      ...EMPTY_FILTER_SET,
      dateField: initialDateField,
    }),
    [initialDateField],
  );

  const [pending, setPending] = React.useState<ICalendarFilterSet>(initialFilterSet);
  const [applied, setApplied] = React.useState<ICalendarFilterSet>(initialFilterSet);

  const hasUnapplied = !filterSetsEqual(pending, applied);
  const hasAnyApplied = !filterSetIsEmpty(applied) || selectedDate !== null;

  // Dispatch the applied filter set through EventsPageContext on every
  // change. GridSection consumes filters.calendarFilter, filters.recordType,
  // filters.statusCodes — these are the three knobs we need.
  React.useEffect(() => {
    // Date filter — only dispatch if dateField is set (not "(none)") AND
    // at least one bound is provided. Otherwise CLEAR the calendar filter
    // so the grid returns all events. Day-click via onDaySelect bypasses
    // this effect by NOT updating `applied` (it dispatches directly).
    if (applied.dateField && applied.dateField !== DATE_FIELD_NONE) {
      if (applied.fromDate && applied.toDate) {
        setCalendarFilter({
          type: "range",
          start: applied.fromDate,
          end: applied.toDate,
          dateFields: [applied.dateField],
        });
      } else if (applied.fromDate) {
        setCalendarFilter({
          type: "single",
          date: applied.fromDate,
          dateFields: [applied.dateField],
        });
      } else if (applied.toDate) {
        // Only `to` provided — treat as single date for symmetry with
        // the original single-input behavior.
        setCalendarFilter({
          type: "single",
          date: applied.toDate,
          dateFields: [applied.dateField],
        });
      } else {
        // dateField set but no bounds → no date filter.
        // Don't clobber a day-click selection: only clear when there's
        // no selectedDate either.
        if (!selectedDate) setCalendarFilter({ type: "clear" });
      }
    } else {
      // dateField is "(none)" — clear the calendar filter, but don't
      // clobber an active day-click selection.
      if (!selectedDate) setCalendarFilter({ type: "clear" });
    }

    // Type filter — dispatch through setRecordTypeFilter (single value).
    setRecordTypeFilter(applied.eventTypeId);

    // Status filter — dispatch through setStatusFilter (array of numbers
    // for shape compatibility; single-select per task-130 spec → length 0
    // or 1).
    setStatusFilter(applied.eventStatusValue !== null ? [applied.eventStatusValue] : []);
  }, [
    applied,
    selectedDate,
    setCalendarFilter,
    setRecordTypeFilter,
    setStatusFilter,
  ]);

  // ── Apply + Clear handlers ───────────────────────────────────────────────
  const onApply = React.useCallback(() => {
    setApplied(pending);
  }, [pending]);

  const onClear = React.useCallback(() => {
    setPending(EMPTY_FILTER_SET);
    setApplied(EMPTY_FILTER_SET);
    setSelectedDate(null);
    setCalendarFilter({ type: "clear" });
  }, [setCalendarFilter]);

  // ── Selection state (driven by GridSection) ──────────────────────────────
  const [selectedIds, setSelectedIds] = React.useState<string[]>([]);

  // ── Toolbar handlers ─────────────────────────────────────────────────────
  const hasSelection = selectedIds.length > 0;

  const onNew = React.useCallback(() => {
    const xrm = getXrm();
    if (!xrm?.Navigation?.openForm) return;
    xrm.Navigation.openForm({ entityName: EVENT_ENTITY_NAME });
  }, []);

  const onDelete = React.useCallback(async () => {
    if (!hasSelection) return;
    const ok = await confirmDialog(
      "Delete Events",
      `Delete ${selectedIds.length} event(s)?`,
      "Delete",
    );
    if (!ok) return;
    const xrm = getXrm();
    if (!xrm?.WebApi) return;
    try {
      await Promise.all(
        selectedIds
          .map((id) => id.replace(/[{}]/g, ""))
          .map((id) => xrm.WebApi.deleteRecord(EVENT_ENTITY_NAME, id)),
      );
      refreshGrid();
    } catch (e) {
      console.error("[CalendarWidget] Delete failed:", e);
    }
  }, [hasSelection, selectedIds, refreshGrid]);

  const onComplete = React.useCallback(async () => {
    if (!hasSelection) return;
    const ok = await confirmDialog(
      "Complete Events",
      `Mark ${selectedIds.length} event(s) as complete?`,
      "Complete",
    );
    if (!ok) return;
    await bulkStatusUpdate(selectedIds, EventStatus.COMPLETED, "Completed", {
      sprk_completeddate: new Date().toISOString(),
    });
    refreshGrid();
  }, [hasSelection, selectedIds, refreshGrid]);

  const onClose = React.useCallback(async () => {
    if (!hasSelection) return;
    const ok = await confirmDialog(
      "Close Events",
      `Close ${selectedIds.length} event(s) without action?`,
      "Close",
    );
    if (!ok) return;
    await bulkStatusUpdate(selectedIds, EventStatus.CLOSED, "Closed");
    refreshGrid();
  }, [hasSelection, selectedIds, refreshGrid]);

  const onCancel = React.useCallback(async () => {
    if (!hasSelection) return;
    const ok = await confirmDialog(
      "Cancel Events",
      `Cancel ${selectedIds.length} event(s)?`,
      "Cancel Events",
    );
    if (!ok) return;
    await bulkStatusUpdate(selectedIds, EventStatus.CANCELLED, "Cancelled");
    refreshGrid();
  }, [hasSelection, selectedIds, refreshGrid]);

  const onOnHold = React.useCallback(async () => {
    if (!hasSelection) return;
    const ok = await confirmDialog(
      "Put Events On Hold",
      `Put ${selectedIds.length} event(s) on hold?`,
      "Put On Hold",
    );
    if (!ok) return;
    await bulkStatusUpdate(selectedIds, EventStatus.ON_HOLD, "On Hold");
    refreshGrid();
  }, [hasSelection, selectedIds, refreshGrid]);

  const onArchive = React.useCallback(async () => {
    if (!hasSelection) return;
    const ok = await confirmDialog(
      "Archive Events",
      `Archive ${selectedIds.length} event(s)? This will hide them from active views.`,
      "Archive",
    );
    if (!ok) return;
    await bulkArchive(selectedIds);
    refreshGrid();
  }, [hasSelection, selectedIds, refreshGrid]);

  const onRefresh = React.useCallback(() => {
    refreshGrid();
  }, [refreshGrid]);

  // The toolbar "Calendar" button is now a soft-clear: it deselects the
  // calendar-day filter only. Filter-row state (Type/Status/dateField) is
  // unaffected — to clear those, use the filter row's Clear button.
  const onCalendarToolbarClick = React.useCallback(() => {
    setSelectedDate(null);
    setCalendarFilter({ type: "clear" });
  }, [setCalendarFilter]);

  // ── Day-cell click handler (task 118 — preserved in task 130) ────────────
  // Bypasses pending/applied: dispatches the calendar filter IMMEDIATELY.
  // Uses the applied dateField if set, otherwise sprk_duedate as a sensible
  // default for the "no filter row date field selected" case.
  const onDaySelect = React.useCallback(
    (date: Date | null) => {
      setSelectedDate(date);
      if (date === null) {
        setCalendarFilter({ type: "clear" });
        return;
      }
      const iso = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
      const effectiveDateField =
        applied.dateField && applied.dateField !== DATE_FIELD_NONE
          ? applied.dateField
          : "sprk_duedate";
      setCalendarFilter({
        type: "range",
        start: iso,
        end: iso,
        dateFields: [effectiveDateField],
      });
    },
    [setCalendarFilter, applied.dateField],
  );

  // ── Filter-divergence effect (task 118 — preserved) ──────────────────────
  // If the calendar filter state in context diverges from the local
  // selectedDate (e.g. because the applied dispatch overwrote the day-
  // selection), clear selectedDate so the highlight doesn't lie.
  React.useEffect(() => {
    if (!selectedDate) return;
    const cf = filters.calendarFilter;
    if (!cf || cf.type === "clear") {
      setSelectedDate(null);
      return;
    }
    if (cf.type === "single") {
      const iso = `${selectedDate.getFullYear()}-${String(selectedDate.getMonth() + 1).padStart(2, "0")}-${String(selectedDate.getDate()).padStart(2, "0")}`;
      if (cf.date !== iso) setSelectedDate(null);
      return;
    }
    if (cf.type === "range") {
      const iso = `${selectedDate.getFullYear()}-${String(selectedDate.getMonth() + 1).padStart(2, "0")}-${String(selectedDate.getDate()).padStart(2, "0")}`;
      if (cf.start !== iso || cf.end !== iso) setSelectedDate(null);
    }
  }, [filters.calendarFilter, selectedDate]);

  // ── Grid "open in full" (task 118) ───────────────────────────────────────
  const onOpenEventsList = React.useCallback(() => {
    const xrm = getXrm();
    if (!xrm?.Navigation?.navigateTo) {
      console.warn(
        "[CalendarWidget] Xrm.Navigation.navigateTo unavailable; cannot open entitylist modal.",
      );
      return;
    }
    try {
      xrm.Navigation.navigateTo(
        { pageType: "entitylist", entityName: EVENT_ENTITY_NAME },
        {
          target: 2,
          width: { value: 80, unit: "%" },
          height: { value: 80, unit: "%" },
          position: 1,
        },
      );
    } catch (e) {
      console.error("[CalendarWidget] Failed to open entitylist modal:", e);
    }
  }, []);

  // ── Month navigation handlers (task 116) ─────────────────────────────────
  const onPrevMonth = React.useCallback(
    (e: React.MouseEvent<HTMLButtonElement>) => {
      const step = e.shiftKey ? Math.max(1, monthsToShow) : 1;
      setViewDate((prev) => addMonths(prev, -step));
    },
    [monthsToShow],
  );
  const onNextMonth = React.useCallback(
    (e: React.MouseEvent<HTMLButtonElement>) => {
      const step = e.shiftKey ? Math.max(1, monthsToShow) : 1;
      setViewDate((prev) => addMonths(prev, step));
    },
    [monthsToShow],
  );

  // ── Dropdown display helpers ─────────────────────────────────────────────
  const dateFieldDisplay = React.useMemo(() => {
    const f = DATE_FIELDS.find((x) => x.value === pending.dateField);
    return f ? f.label : "(none)";
  }, [pending.dateField]);

  const eventTypeDisplay = React.useMemo(() => {
    if (!pending.eventTypeId) return "All";
    return eventTypeOptions.find((t) => t.id === pending.eventTypeId)?.name ?? "All";
  }, [pending.eventTypeId, eventTypeOptions]);

  const eventStatusDisplay = React.useMemo(() => {
    if (pending.eventStatusValue === null) return "All";
    return (
      STATUS_OPTIONS.find((s) => s.value === pending.eventStatusValue)?.label ?? "All"
    );
  }, [pending.eventStatusValue]);

  return (
    <div className={styles.root}>
      {/* (1) Filter row — Task 136 (R13 follow-up #15, 2026-05-23):
              flattened to a single flex row. All 7 items (5 fields +
              1 action group) wrap together as siblings. Each field
              uses `flex: 1 0 140px` so when the row narrows below
              ~770px (5 × 140px + 4 × 24px gap + ~50px actions), the
              entire group wraps cleanly to multiple lines without
              the actions floating separately. Order preserved from
              task 131: Event Type → Event Status → Filter by Date
              Field → From → To → Apply → Clear → Chevron. */}
      <div className={styles.dateRangeRow}>
        {/* Task 131: Event Type. Fetched from sprk_eventtype_ref records. */}
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>Event Type</Label>
          <Dropdown
            value={eventTypeDisplay}
            selectedOptions={pending.eventTypeId ? [pending.eventTypeId] : [""]}
            onOptionSelect={(_e, data) => {
              const v = data.optionValue ?? "";
              setPending((prev) => ({
                ...prev,
                eventTypeId: v === "" ? null : v,
              }));
            }}
          >
            <Option value="" text="All">All</Option>
            {eventTypeOptions.map((t) => (
              <Option key={t.id} value={t.id} text={t.name}>
                {t.name}
              </Option>
            ))}
          </Dropdown>
        </div>
        {/* Task 131: Event Status — second. Task 134: CSS Grid sizing. */}
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>Event Status</Label>
          <Dropdown
            value={eventStatusDisplay}
            selectedOptions={
              pending.eventStatusValue !== null
                ? [String(pending.eventStatusValue)]
                : [""]
            }
            onOptionSelect={(_e, data) => {
              const v = data.optionValue ?? "";
              setPending((prev) => ({
                ...prev,
                eventStatusValue: v === "" ? null : Number(v),
              }));
            }}
          >
            <Option value="" text="All">All</Option>
            {STATUS_OPTIONS.map((s) => (
              <Option key={s.value} value={String(s.value)} text={s.label}>
                {s.label}
              </Option>
            ))}
          </Dropdown>
        </div>
        {/* Task 131: Filter by Date Field — third. Task 134: CSS Grid sizing. */}
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>Filter by Date Field</Label>
          <Dropdown
            value={dateFieldDisplay}
            selectedOptions={[pending.dateField]}
            onOptionSelect={(_e, data) => {
              const next = data.optionValue ?? DATE_FIELD_NONE;
              setPending((prev) => ({ ...prev, dateField: next }));
            }}
          >
            {DATE_FIELDS.map((f) => (
              <Option key={f.value || "none"} value={f.value} text={f.label}>
                {f.label}
              </Option>
            ))}
          </Dropdown>
        </div>
        {/* Task 131: From — fourth. */}
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>From</Label>
          <Input
            type="date"
            value={pending.fromDate}
            onChange={(_e, data) =>
              setPending((prev) => ({ ...prev, fromDate: data.value }))
            }
          />
        </div>
        {/* Task 131: To — fifth. */}
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>To</Label>
          <Input
            type="date"
            value={pending.toDate}
            onChange={(_e, data) =>
              setPending((prev) => ({ ...prev, toDate: data.value }))
            }
          />
        </div>
        {/* Task 136: action group — Apply + Clear + collapse chevron.
            Now a sibling flex item of the 5 fields. When the parent
            row's flex-wrap kicks in, this whole group moves to a new
            line as one block (Apply + Clear + chevron stay together). */}
        <div className={styles.filterActions}>
          {hasUnapplied && (
            <Button
              appearance="primary"
              size="small"
              onClick={onApply}
              aria-label="Apply filters"
            >
              Apply
            </Button>
          )}
          {hasAnyApplied && (
            <Button
              appearance="subtle"
              size="small"
              onClick={onClear}
              aria-label="Clear filters"
            >
              Clear
            </Button>
          )}
          <Tooltip
            content={calendarCollapsed ? "Expand calendar" : "Collapse calendar"}
            relationship="label"
          >
            <Button
              appearance="subtle"
              icon={calendarCollapsed ? <CaretDown24Regular /> : <CaretUp24Regular />}
              onClick={toggleCollapsed}
              aria-expanded={!calendarCollapsed}
              aria-label={calendarCollapsed ? "Expand calendar" : "Collapse calendar"}
            />
          </Tooltip>
        </div>
      </div>

      {/* (2) Calendar strip (unchanged in task 130) */}
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
              onFilterChange={(filter) => setCalendarFilter(filter)}
              viewDate={viewDate}
              monthsToShow={monthsToShow}
              layout="horizontal"
              selectedDate={selectedDate}
              onSelectDate={onDaySelect}
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

      {/* (3) EventsPage toolbar (unchanged in task 130) */}
      <div className={styles.toolbarRow}>
        <Toolbar size="small">
          <ToolbarButton icon={<Add24Regular />} appearance="subtle" onClick={onNew}>
            New
          </ToolbarButton>
          <ToolbarDivider />
          <ToolbarButton
            icon={<Delete24Regular />}
            appearance="subtle"
            onClick={onDelete}
            disabled={!hasSelection}
          >
            Delete
          </ToolbarButton>
          <ToolbarDivider />
          <ToolbarButton
            icon={<CheckmarkCircle24Regular />}
            appearance="subtle"
            onClick={onComplete}
            disabled={!hasSelection}
          >
            Complete
          </ToolbarButton>
          <ToolbarButton
            icon={<Dismiss24Regular />}
            appearance="subtle"
            onClick={onClose}
            disabled={!hasSelection}
          >
            Close
          </ToolbarButton>
          <ToolbarButton
            icon={<DismissCircle24Regular />}
            appearance="subtle"
            onClick={onCancel}
            disabled={!hasSelection}
          >
            Cancel
          </ToolbarButton>
          <ToolbarButton
            icon={<Pause24Regular />}
            appearance="subtle"
            onClick={onOnHold}
            disabled={!hasSelection}
          >
            On Hold
          </ToolbarButton>
          <ToolbarButton
            icon={<Archive24Regular />}
            appearance="subtle"
            onClick={onArchive}
            disabled={!hasSelection}
          >
            Archive
          </ToolbarButton>
          <ToolbarDivider />
          <ToolbarButton
            icon={<ArrowClockwise24Regular />}
            appearance="subtle"
            onClick={onRefresh}
          >
            Refresh
          </ToolbarButton>
          <ToolbarDivider />
          <ToolbarButton
            icon={<CalendarLtr24Regular />}
            appearance="subtle"
            onClick={onCalendarToolbarClick}
          >
            Calendar
          </ToolbarButton>
        </Toolbar>
        <div className={styles.toolbarRowSpacer} />
        <Tooltip content="Open Events list" relationship="label">
          <Button
            appearance="subtle"
            icon={<Open24Regular />}
            onClick={onOpenEventsList}
            aria-label="Open Events list view"
          />
        </Tooltip>
      </div>

      {/* (4) Grid — task 130: no viewId prop; GridSection uses OData fallback
              path. Filters flow via filters.calendarFilter +
              filters.recordType + filters.statusCodes (dispatched by the
              applied-state effect above). */}
      <div className={styles.gridContainer}>
        <GridSection
          calendarFilter={filters.calendarFilter}
          assignedToFilter={filters.assignedToUserIds}
          eventTypeFilter={
            filters.recordType ? [filters.recordType] : undefined
          }
          statusFilter={filters.statusCodes}
          onRowClick={openEvent}
          onSelectionChange={setSelectedIds}
          onRecordsLoaded={handleRecordsLoaded}
          key={refreshTrigger}
        />
      </div>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Top-level widget component — provides the EventsPageProvider
// ─────────────────────────────────────────────────────────────────────────────

export const CalendarWorkspaceWidget: React.FC<CalendarWorkspaceWidgetProps> = ({
  // Task 130: default to "" ("(none)") per operator: "the Filter by Date
  // Field needs to have a blank value". Callers can still opt into a
  // specific field by passing initialDateField explicitly.
  initialDateField = "",
}) => {
  const handleOpenEvent = React.useCallback(
    (eventId: string, _eventTypeId?: string) => {
      const xrm = getXrm();
      if (!xrm?.Navigation?.navigateTo) {
        console.warn(
          "[CalendarWidget] Xrm.Navigation.navigateTo unavailable; cannot open event modal.",
        );
        return;
      }
      const cleanId = eventId.replace(/[{}]/g, "");
      try {
        xrm.Navigation.navigateTo(
          {
            pageType: "entityrecord",
            entityName: EVENT_ENTITY_NAME,
            entityId: cleanId,
          },
          {
            target: 2,
            width: { value: 80, unit: "%" },
            height: { value: 80, unit: "%" },
            position: 1,
          },
        );
      } catch (e) {
        console.error("[CalendarWidget] Failed to open event modal:", e);
      }
    },
    [],
  );

  return (
    <EventsPageProvider onOpenEvent={handleOpenEvent}>
      <CalendarWorkspaceLayout initialDateField={initialDateField} />
    </EventsPageProvider>
  );
};

export default CalendarWorkspaceWidget;
