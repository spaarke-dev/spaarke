/**
 * CalendarWorkspaceWidget — the 5th SpaarkeAi system workspace widget.
 *
 * Task 115 (Round 9, 2026-05-22) shipped the initial vertical-stacked
 * 3-month strip. Task 116 (Round 10, 2026-05-22) polished it per operator
 * smoke feedback:
 *
 *  1. **Horizontal calendar strip** (was vertical stacking). The shared
 *     `CalendarSection` now accepts `layout?: 'vertical' | 'horizontal'`
 *     (default 'vertical' so EventsPage's `CalendarDrawer` is unchanged).
 *  2. **Responsive month count.** A `ResizeObserver` measures the strip
 *     container and maps the width to a month count via
 *     `computeMonthsForWidth`. CalendarSection accepts `monthsToShow?:
 *     number` (default 3 = prior behavior).
 *  3. **External ◀ ▶ arrow navigation.** CalendarSection accepts
 *     `viewDate?: Date` — controlled-component mode. The widget owns the
 *     anchor month state; arrows shift it by ±1 month. No upper bound on
 *     month/year navigation.
 *  4. **No internal scroll on the strip.** `overflow: hidden` on the
 *     strip container; arrows ARE the navigation, no scrollbar.
 *  5. **Collapsible strip.** Chevron toggle right of the strip. When
 *     collapsed, the strip unmounts; date-filter / toolbar / view selector
 *     / grid all remain visible. The grid container has `flex: 1 1 auto`
 *     so it absorbs the freed vertical space. Collapsed preference
 *     persists in `localStorage["spaarke:calendar:collapsed"]` (try/catch
 *     wrapped per task 092 `pinnedWorkspaces.ts` pattern).
 *  6. **Reduced default height.** Strip container is `min-height: 280px`,
 *     `flex-shrink: 0`. Single-month grid baseline is ~240–280px, so this
 *     fits one full row of months without dominating the workspace.
 *
 * Layout (operator mockup, top → bottom):
 *  1. Date-range filter row    — "Filter by Date Field" dropdown + From/To pickers
 *  2. Calendar strip row       — ◀ button · responsive horizontal CalendarSection · ▶ button · collapse chevron
 *  3. EventsPage toolbar       — full parity with standalone EventsPage:
 *                                New / Delete / Complete / Close / Cancel /
 *                                On Hold / Archive / Refresh / Calendar
 *  4. View selector            — `<ViewSelectorDropdown>` (defaulted to Active Events)
 *  5. Grid                     — `<GridSection>` (filters auto-bind to context)
 *
 * Integration seam (operator decision, Round 9 Q&A 2026-05-22):
 *   Event detail opens as a MODAL via `Xrm.Navigation.navigateTo(
 *     { pageType: "entityrecord", entityName, entityId },
 *     { target: 2, width/height: 80% }
 *   )` — NOT via `Xrm.App.sidePanes`. Mirrors the Documents Expand
 *   affordance from task 111 (consistent SpaarkeAi modal-detail UX).
 *
 * Hidden scrollbar pattern: matches task 107 Workspace pane treatment —
 * `scrollbar-width: none` + WebKit `::-webkit-scrollbar { display: none }`
 * applied at the root container, so the widget grows naturally inside its
 * LegalWorkspaceApp(embedded) host without a visible vertical scrollbar.
 *
 * CLAUDE.md §10 (BFF Hygiene): ZERO new BFF endpoints. All Dataverse
 * access flows through the shared `FetchXmlService` / `ViewService` /
 * `Xrm.WebApi` (ADR-028).
 *
 * @see src/client/shared/Spaarke.Events.Components/src/context/EventsPageContext.tsx
 * @see src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarSection.tsx
 * @see src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx
 * @see src/solutions/EventsPage/src/App.tsx (the standalone composition this widget mirrors)
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
  // Task 118: replaces ChevronUp20Regular / ChevronDown20Regular for the
  // calendar collapse toggle. Caret family is visually distinct from the
  // month-navigation chevrons (◀ ▶) and clearly conveys vertical
  // expand/collapse direction. Both exist in the installed
  // @fluentui/react-icons version.
  CaretUp24Regular,
  CaretDown24Regular,
  // Task 118: "open in full" affordance on the grid section. Same icon
  // family used by task 111 Documents per-row Expand and FilePreview's
  // "Open Record" button — consistent SpaarkeAi modal-launch UX.
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
import {
  ViewSelectorDropdown,
  useViewSelection,
} from "../../components/ViewSelectorDropdown";
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
// Date-range filter options
// ─────────────────────────────────────────────────────────────────────────────

const DATE_FIELDS = [
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
    // Private browsing / quota / corrupt storage — degrade silently.
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

/**
 * Map calendar-strip container width (px) to a month count.
 *
 * Single-month grid is roughly 240–280px wide once padding + day cells are
 * counted. Breakpoints are calibrated for SpaarkeAi's typical workspace
 * pane widths:
 *   - ~450–600px at the default 25/50/25 split (task 117 baseline) → 2 mo.
 *   - ~900–1100px when Context pane is collapsed → 3-4 mo.
 *   - >1400px on ultra-wide / Assistant + Context both collapsed → 5 mo.
 *
 * Cap at 5 to avoid over-rendering on ultra-wide monitors (each month
 * paints 42 day cells, so 5 months = 210 cells + headers).
 */
function computeMonthsForWidth(widthPx: number): number {
  if (widthPx < 380) return 1;
  if (widthPx < 720) return 2;
  if (widthPx < 1060) return 3;
  if (widthPx < 1400) return 4;
  return 5;
}

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for the Calendar workspace widget.
 *
 * Currently the widget is self-contained — the LegalWorkspace section
 * factory does not need to pass any external configuration. We keep this
 * surface explicit (rather than `Record<string, never>`) so future
 * additions (initialDateField, initialViewId, externally-driven refresh)
 * can land without an API break.
 */
export interface CalendarWorkspaceWidgetProps {
  /** Optional override for the default date field (default: "sprk_duedate"). */
  initialDateField?: string;
  /** Optional override for the default ViewSelectorDropdown view ID. */
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
    // Hide vertical scrollbar — matches task 107 Workspace pane treatment.
    scrollbarWidth: "none",
    "::-webkit-scrollbar": {
      display: "none",
    },
  },
  // Task 118: filter row now also hosts the calendar collapse chevron at
  // its right edge (flex spacer between the 3 filter fields and the
  // chevron). Operator: "move the calendar collapse icon to the same row
  // as the filters; allows the filters to still show when the calendars
  // are collapsed and still filter the Event list." The chevron is
  // always-visible regardless of strip collapse state.
  dateRangeRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-end",
    flexWrap: "wrap",
    ...shorthands.gap("12px"),
    ...shorthands.padding("4px", "8px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  dateRangeField: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("2px"),
    minWidth: "140px",
  },
  dateRangeLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  // Task 118: flex spacer pushes the collapse chevron to the right edge
  // responsively at any viewport width. No media queries needed.
  filterRowSpacer: {
    flex: "1 1 auto",
  },
  // Task 118: container for the collapse chevron at the right edge of the
  // filter row. Bottom-aligned so it sits flush with the From/To inputs
  // (the dateRangeRow uses `alignItems: flex-end`).
  collapseToggleSlot: {
    display: "flex",
    alignItems: "flex-end",
    flexShrink: 0,
  },
  // Task 122: container for the Clear-date-filter button. Bottom-aligned
  // like collapseToggleSlot so it lines up flush with the From/To inputs.
  // Conditionally rendered only when fromDate || toDate (filter is active).
  dateRangeClearSlot: {
    display: "flex",
    alignItems: "flex-end",
    flexShrink: 0,
  },
  // Task 116: calendar row = ◀ + strip (flex: 1) + ▶. Task 118 REMOVED
  // the in-row collapse chevron; it now lives on the filter row above.
  // The row itself is `flex-shrink: 0` so the grid below can claim
  // remaining vertical space via `flex: 1 1 auto` on `gridContainer`.
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
  // The middle strip: hosts CalendarSection in horizontal layout. No
  // scrollbar — arrows are the navigation.
  calendarStrip: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    minWidth: 0, // allow flex shrink past content's intrinsic width
  },
  navButton: {
    alignSelf: "center",
    flexShrink: 0,
  },
  // Task 120: toolbarRow wraps the <Toolbar> in a flex container so the
  // "open in full" icon can sit right-justified on the same row as the
  // CRUD toolbar buttons (operator: "can the open modal go on the same
  // row (right justified) as the other grid controls"). Layout:
  //   <Toolbar>{CRUD buttons}</Toolbar> | <flex spacer> | <Open button>
  toolbarRow: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    ...shorthands.padding("0", "4px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  toolbarRowSpacer: {
    flex: "1 1 auto",
  },
  // Task 120: view selector row no longer hosts the Open icon (moved up
  // to toolbarRow). The row now contains only the ViewSelectorDropdown.
  viewSelectorRow: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    ...shorthands.padding("8px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  // Task 120: add breathing room between the calendar/toolbar/view-selector
  // section and the grid header. Operator: "Add a little padding/margin
  // space between the grid and the calendars." spacingVerticalL ≈ 16px
  // matches Fluent v9's standard vertical rhythm without overpowering.
  gridContainer: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    minHeight: "320px",
    marginTop: tokens.spacingVerticalL,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Bulk action helpers (mirror EventsPage App.tsx — Xrm.WebApi only)
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
  initialViewId?: string;
}

const CalendarWorkspaceLayout: React.FC<ICalendarWorkspaceLayoutProps> = ({
  initialDateField,
  initialViewId,
}) => {
  const styles = useStyles();
  const {
    filters,
    eventDates,
    refreshTrigger,
    setCalendarFilter,
    setEventDates,
    openEvent,
    refreshGrid,
  } = useEventsPageContext();

  // ── Calendar strip state (task 116) ──────────────────────────────────────
  // Anchor month for the rendered range. CalendarSection is in CONTROLLED
  // mode via this state — arrows shift ±1 month with no upper bound.
  const [viewDate, setViewDate] = React.useState<Date>(() =>
    startOfMonth(new Date()),
  );

  // Responsive month count derived from strip width via ResizeObserver.
  // Initial guess of 2 mirrors the default 25/50/25 SpaarkeAi pane split
  // (~600px Workspace pane → 2 months); the observer overrides on first
  // measurement so cold-load reflow is invisible.
  const stripRef = React.useRef<HTMLDivElement | null>(null);
  const [monthsToShow, setMonthsToShow] = React.useState<number>(2);

  React.useEffect(() => {
    if (typeof ResizeObserver === "undefined") return; // SSR / very old browsers
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
  // Click a day with associated events → grid filters to that single day.
  // Re-click the same day → clears the filter (toggle). Click a different
  // day → moves the filter. Owned here (not inside CalendarSection)
  // because the filter dispatches through EventsPageContext.
  const [selectedDate, setSelectedDate] = React.useState<Date | null>(null);

  // ── Event-day derivation (task 120) ──────────────────────────────────────
  // Bug fix: prior to task 120, `eventDates` in EventsPageContext was never
  // populated by the Calendar workspace widget — it defaulted to `[]` and
  // CalendarSection's day-cell highlight (task 118) had nothing to match
  // against. Effect: event-bearing days never showed the blue tint despite
  // matching records in the grid.
  //
  // Fix: GridSection now exposes an `onRecordsLoaded` callback (task 120)
  // emitting the post-fetch records. We derive per-date counts here using
  // LOCAL date components (NOT toISOString — see CalendarSection's
  // toIsoDateString rationale) to avoid the off-by-one UTC bug for users
  // in non-zero UTC offsets. The result is dispatched through
  // setEventDates so any consumer relying on context.eventDates (current
  // widget + future consumers) sees the same data.
  //
  // Date-field priority per operator (Round 13 follow-up, 2026-05-22):
  // "the date highlight should be based on due date or if no due date then
  // created date." sprk_duedate first, createdon as the only fallback.
  // sprk_startdate is intentionally skipped — events without a due date
  // anchor to when they were created, not when they were scheduled to
  // begin (which can mislead the user about deadline visibility).
  const handleRecordsLoaded = React.useCallback(
    (records: IEventRecord[]) => {
      const counts = new Map<string, number>();
      for (const r of records) {
        const dateStr =
          r.sprk_duedate ||
          (r as unknown as { createdon?: string }).createdon;
        if (!dateStr) continue;
        const d = new Date(dateStr);
        if (Number.isNaN(d.getTime())) continue;
        // Local-date key — symmetric with CalendarSection's day-cell
        // membership check (task 120 toIsoDateString fix).
        const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
        counts.set(key, (counts.get(key) ?? 0) + 1);
      }
      const next: IEventDateInfo[] = Array.from(counts.entries()).map(
        ([date, count]) => ({ date, count }),
      );
      setEventDates(next);
    },
    [setEventDates],
  );

  // ── Date-range filter (top row) ──────────────────────────────────────────
  const [dateField, setDateField] = React.useState<string>(initialDateField);
  const [fromDate, setFromDate] = React.useState<string>("");
  const [toDate, setToDate] = React.useState<string>("");

  // Push date-range changes through the context's calendar filter.
  React.useEffect(() => {
    if (!fromDate && !toDate) {
      // Don't clear if the calendar already set a single date; only clear
      // when both date-range pickers are empty AND no single-date selection
      // is active in the calendar strip below.
      return;
    }
    if (fromDate && toDate) {
      setCalendarFilter({
        type: "range",
        start: fromDate,
        end: toDate,
        dateFields: [dateField],
      });
    } else if (fromDate) {
      setCalendarFilter({ type: "single", date: fromDate, dateFields: [dateField] });
    }
  }, [fromDate, toDate, dateField, setCalendarFilter]);

  // ── View selector ────────────────────────────────────────────────────────
  const [selectedViewId, setSelectedViewId] = useViewSelection();
  const primedRef = React.useRef(false);
  React.useEffect(() => {
    if (!primedRef.current && initialViewId && initialViewId !== selectedViewId) {
      setSelectedViewId(initialViewId);
      primedRef.current = true;
    }
  }, [initialViewId, selectedViewId, setSelectedViewId]);

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

  // The "Calendar" toolbar button in standalone EventsPage opens the side-pane
  // Date Filter. In the embedded widget, there is no separate side pane — the
  // calendar strip is (when not collapsed) always visible above the grid. We
  // map this button to a calendar-filter clear (deselects any active date
  // filter) so the button is not a dead affordance.
  const onCalendarToolbarClick = React.useCallback(() => {
    setCalendarFilter({ type: "clear" });
    setFromDate("");
    setToDate("");
    setSelectedDate(null);
  }, [setCalendarFilter]);

  // ── Day-cell click handler (task 118) ────────────────────────────────────
  // CalendarSection emits the clicked date via `onSelectDate`. We:
  //  1. Update local `selectedDate` state for visual highlight.
  //  2. Dispatch a single-day range filter through EventsPageContext so
  //     GridSection narrows to that day. We emit a range from/to = clicked
  //     date (the existing EventsPageContext + GridSection plumbing
  //     handles single-day ranges identically to multi-day ranges).
  //  3. On toggle-off (CalendarSection emits `null`), clear the calendar
  //     filter — but DON'T clobber the From/To inputs since the user may
  //     still want those active.
  //
  // Day click takes precedence over From/To (operator priority). If the
  // user later changes From/To, the divergence effect below clears
  // `selectedDate`.
  const onDaySelect = React.useCallback(
    (date: Date | null) => {
      setSelectedDate(date);
      if (date === null) {
        setCalendarFilter({ type: "clear" });
        return;
      }
      const iso = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
      setCalendarFilter({
        type: "range",
        start: iso,
        end: iso,
        dateFields: [dateField],
      });
    },
    [setCalendarFilter, dateField],
  );

  // ── Filter-divergence effect (task 118) ──────────────────────────────────
  // If the From/To pickers (or any other component) push a calendar filter
  // that doesn't match the current single-day selection, clear
  // `selectedDate` so the day highlight doesn't lie about what's filtering
  // the grid.
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
  // Operator: "The grid needs to have an open icon to open a modal showing
  // the sprk_events entity view." Mirrors the FilePreview "Open Record"
  // pattern + task 111 per-row Documents Expand affordance.
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
  // Step = 1 month per click by default; Shift+click advances by the visible
  // window so power users can jump faster on wide screens.
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

  return (
    <div className={styles.root}>
      {/* (1) Date-range filter row — task 118: chevron moved here, right-
              justified, so the filter row remains visible (and continues
              to drive the grid filter) when the calendar strip collapses. */}
      <div className={styles.dateRangeRow}>
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>Filter by Date Field</Label>
          <Dropdown
            value={DATE_FIELDS.find((f) => f.value === dateField)?.label ?? ""}
            selectedOptions={[dateField]}
            onOptionSelect={(_e, data) => {
              if (data.optionValue) setDateField(data.optionValue);
            }}
          >
            {DATE_FIELDS.map((f) => (
              <Option key={f.value} value={f.value} text={f.label}>
                {f.label}
              </Option>
            ))}
          </Dropdown>
        </div>
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>From</Label>
          <Input
            type="date"
            value={fromDate}
            onChange={(_e, data) => setFromDate(data.value)}
          />
        </div>
        <div className={styles.dateRangeField}>
          <Label className={styles.dateRangeLabel}>To</Label>
          <Input
            type="date"
            value={toDate}
            onChange={(_e, data) => setToDate(data.value)}
          />
        </div>
        {/* Task 122: explicit Clear button on the filter row. Only renders
            when a From or To date is set (otherwise it's a dead affordance).
            Click clears both date inputs + the calendarFilter in context so
            the grid + day-highlight return to the unfiltered state. Reuses
            the same handler the Calendar toolbar button uses. */}
        {(fromDate || toDate) && (
          <div className={styles.dateRangeClearSlot}>
            <Tooltip content="Clear date filter" relationship="label">
              <Button
                appearance="subtle"
                icon={<Dismiss24Regular />}
                onClick={onCalendarToolbarClick}
                aria-label="Clear date filter"
              />
            </Tooltip>
          </div>
        )}
        {/* Flex spacer pushes the chevron to the right edge responsively */}
        <div className={styles.filterRowSpacer} />
        <div className={styles.collapseToggleSlot}>
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

      {/* (2) Calendar strip — task 118: only the strip itself hides when
              collapsed. The filter row above and everything below remain
              visible. The internal CalendarSection header (icon +
              "Calendar" + separator) is suppressed in horizontal layout
              per task 118. */}
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

      {/* (3) Full EventsPage toolbar — task 120: wraps <Toolbar> in a flex
              row with the "Open events list" icon right-justified via a
              flex spacer. Operator: "can the open modal go on the same row
              (right justified) as the other grid controls." */}
      <div className={styles.toolbarRow}>
        <Toolbar size="small">
          <ToolbarButton
            icon={<Add24Regular />}
            appearance="subtle"
            onClick={onNew}
          >
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

      {/* (4) View selector — task 120 removed the Open icon from this row
              (moved up to the toolbar row per operator). Row now contains
              only the dropdown. */}
      <div className={styles.viewSelectorRow}>
        <ViewSelectorDropdown
          selectedViewId={selectedViewId}
          onViewChange={(viewId) => setSelectedViewId(viewId)}
        />
      </div>

      {/* (5) Grid — auto-binds to filters via EventsPageContext.
              Task 120: `onRecordsLoaded` derives per-date counts for the
              event-day highlight (drives `eventDates` in context). */}
      <div className={styles.gridContainer}>
        <GridSection
          calendarFilter={filters.calendarFilter}
          assignedToFilter={filters.assignedToUserIds}
          eventTypeFilter={
            filters.recordType ? [filters.recordType] : undefined
          }
          statusFilter={filters.statusCodes}
          viewId={selectedViewId}
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

/**
 * The "Calendar" workspace widget. Mounted by the LegalWorkspace section
 * factory and rendered inside the SpaarkeAi workspace pane via
 * LegalWorkspaceApp(embedded).
 */
export const CalendarWorkspaceWidget: React.FC<CalendarWorkspaceWidgetProps> = ({
  initialDateField = "sprk_duedate",
  initialViewId,
}) => {
  // onOpenEvent → modal navigation (Round 9 decision: NOT Xrm.App.sidePanes).
  // Mirrors the Documents Expand affordance pattern from task 111 — opens
  // the Event record as a centered modal at 80% × 80% of the viewport.
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
      <CalendarWorkspaceLayout
        initialDateField={initialDateField}
        initialViewId={initialViewId}
      />
    </EventsPageProvider>
  );
};

export default CalendarWorkspaceWidget;
