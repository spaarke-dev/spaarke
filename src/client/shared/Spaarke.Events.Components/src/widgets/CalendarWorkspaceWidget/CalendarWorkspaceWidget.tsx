/**
 * CalendarWorkspaceWidget — the 5th SpaarkeAi system workspace widget.
 *
 * Task 115 (Round 9, 2026-05-22). Operator vision: a "Calendar" workspace
 * surfaces all events + tasks the user has access to (same data scope as
 * the standalone `sprk_eventspage` code page) and re-uses the SAME Events
 * + Tasks shared components hoisted in task 114.
 *
 * Layout (operator mockup, top → bottom):
 *  1. Date-range filter row    — "Filter by Date Field" dropdown + From/To pickers
 *  2. 3-month calendar strip   — composed via `<CalendarSection>` (which already
 *                                renders current + 2 ahead — see CalendarSection
 *                                v1.0.0 monthsToShow). The original brief asked
 *                                for ◀ ▶ navigation; CalendarSection's internal
 *                                viewDate state advances on user interaction but
 *                                does NOT expose external prev/next handles.
 *                                Adding nav arrows would require modifying the
 *                                shared component — out of scope (composition
 *                                over modification per the brief). Flagged for
 *                                a future round.
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
} from "@fluentui/react-icons";

import {
  EventsPageProvider,
  useEventsPageContext,
} from "../../context/EventsPageContext";
import { CalendarSection } from "../../components/CalendarSection/CalendarSection";
import type { IEventDateInfo } from "../../components/CalendarSection/CalendarSection";
import { GridSection } from "../../components/GridSection/GridSection";
import {
  ViewSelectorDropdown,
  useViewSelection,
} from "../../components/ViewSelectorDropdown";

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
  calendarStripContainer: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
    // CalendarSection already renders 3 months stacked vertically; constrain
    // height so the strip does not dominate the widget on tall hosts.
    maxHeight: "560px",
    minHeight: "320px",
    overflow: "hidden",
  },
  toolbarRow: {
    ...shorthands.padding("0", "4px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  viewSelectorRow: {
    display: "flex",
    alignItems: "center",
    ...shorthands.padding("8px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
  },
  gridContainer: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    minHeight: "320px",
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
    openEvent,
    refreshGrid,
  } = useEventsPageContext();

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
  // `useViewSelection()` reads from / writes to sessionStorage; if a caller
  // wants a non-default initial view, we one-shot prime the hook on first
  // mount (avoids forking the hook signature for a narrow widget need).
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
  // calendar strip is always visible above the grid. We map this button to a
  // calendar-filter clear (deselects any active date filter) so the button is
  // not a dead affordance.
  const onCalendarToolbarClick = React.useCallback(() => {
    setCalendarFilter({ type: "clear" });
    setFromDate("");
    setToDate("");
  }, [setCalendarFilter]);

  return (
    <div className={styles.root}>
      {/* (1) Date-range filter row */}
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
      </div>

      {/* (2) 3-month calendar strip — CalendarSection already renders
              current month + next 2 (see CalendarSection v1.0.0 monthsToShow). */}
      <div className={styles.calendarStripContainer}>
        <CalendarSection
          eventDates={eventDates as IEventDateInfo[]}
          onFilterChange={(filter) => setCalendarFilter(filter)}
        />
      </div>

      {/* (3) Full EventsPage toolbar */}
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
      </div>

      {/* (4) View selector */}
      <div className={styles.viewSelectorRow}>
        <ViewSelectorDropdown
          selectedViewId={selectedViewId}
          onViewChange={(viewId) => setSelectedViewId(viewId)}
        />
      </div>

      {/* (5) Grid — auto-binds to filters via EventsPageContext */}
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
