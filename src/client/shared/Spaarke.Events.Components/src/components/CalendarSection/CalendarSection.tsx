/**
 * CalendarSection Component
 *
 * Wrapper component that embeds the EventCalendarFilter component
 * within the Events Custom Page. Provides date selection and filtering
 * capabilities for the Events grid.
 *
 * This component adapts the EventCalendarFilter (originally designed for PCF)
 * to work in a Custom Page context by:
 * - Providing mock context for non-PCF environment
 * - Handling filter output to parent component
 * - Supporting event date indicators from fetched data
 *
 * Task 116 (Round 10): added controlled `viewDate` + `monthsToShow` + `layout`
 * props for the Calendar workspace widget.
 *
 * Task 118 (Round 11): added controlled `selectedDate` + `onSelectDate` props.
 * Day cells with associated events now render with a brand-tint background;
 * clicking an event-day in the controlled-mode widget filters the grid to
 * that single day (handled by the widget — this component just surfaces the
 * click + emits via `onSelectDate`). In `horizontal` layout the internal
 * Calendar header (icon + title + separator) and footer (clear + version)
 * are suppressed because the Calendar widget owns those affordances on its
 * own toolbar row.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/061-events-page-integrate-calendar.poml
 * @see projects/spaarke-ai-platform-unification-r3/tasks/116-calendar-widget-r10-polish.poml
 * @see projects/spaarke-ai-platform-unification-r3/tasks/118-calendar-widget-r11-polish.poml
 */

import * as React from 'react';
import { makeStyles, tokens, shorthands, Text, Button } from '@fluentui/react-components';
import { DismissRegular, Calendar24Regular } from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Calendar filter output format (matches EventCalendarFilter output)
 */
export type CalendarFilterType = 'single' | 'range' | 'clear';

export interface CalendarFilterSingle {
  type: 'single';
  date: string;
  /** Date fields to filter by (e.g., ['sprk_DueDate', 'CreatedOn']) */
  dateFields?: string[];
}

export interface CalendarFilterRange {
  type: 'range';
  start: string;
  end: string;
  /** Date fields to filter by (e.g., ['sprk_DueDate', 'CreatedOn']) */
  dateFields?: string[];
}

export interface CalendarFilterClear {
  type: 'clear';
}

export type CalendarFilterOutput = CalendarFilterSingle | CalendarFilterRange | CalendarFilterClear;

/**
 * Event date info for calendar indicators
 */
export interface IEventDateInfo {
  date: string;
  count: number;
}

/**
 * Props for CalendarSection component
 */
export interface CalendarSectionProps {
  /** Event dates with counts for showing indicators */
  eventDates?: IEventDateInfo[];
  /** Callback when filter changes */
  onFilterChange: (filter: CalendarFilterOutput | null) => void;
  /** Initial selected date (ISO string) */
  initialDate?: string;
  /** Height of the calendar section */
  height?: number;

  /**
   * Controlled-mode anchor month for the rendered range.
   *
   * When provided, CalendarSection is CONTROLLED by the parent — the parent
   * owns the month-anchor state and internal `viewDate` is ignored. The
   * rendered range starts at this month and spans `monthsToShow` months
   * forward.
   *
   * When OMITTED, CalendarSection retains the existing internal-state
   * behavior (defaults to today's month, "+ 2 ahead" rendering — i.e. 3
   * months total). The standalone EventsPage's `CalendarDrawer` consumes
   * CalendarSection without this prop and is therefore unaffected.
   *
   * Task 116 — required for the Calendar widget's external ◀ ▶ navigation.
   */
  viewDate?: Date;

  /**
   * Number of months to render. Defaults to 3 (the prior internal behavior).
   *
   * Calendar widget (task 116) passes a responsive value derived from
   * container width via ResizeObserver. EventsPage standalone omits this
   * prop and gets the original 3-month behavior.
   */
  monthsToShow?: number;

  /**
   * Layout direction for the rendered months.
   *
   * - `'vertical'` (DEFAULT) — months stack top-to-bottom inside a single
   *   vertically-scrolling container. Preserves the prior behavior used by
   *   EventsPage's CalendarDrawer.
   * - `'horizontal'` — months sit side-by-side via flex row with a fixed
   *   gap. Used by the Calendar workspace widget (task 116) where strip
   *   width is bounded by the parent and arrows handle navigation rather
   *   than scrolling.
   *
   * Defaults to `'vertical'` so existing CalendarSection consumers render
   * identically without changes.
   */
  layout?: 'vertical' | 'horizontal';

  /**
   * Controlled selected date (task 118).
   *
   * When provided, the parent owns the single-day selection state. If the
   * parent passes `null`, no day is highlighted as selected. When OMITTED,
   * CalendarSection retains its prior internal `selectedDate` state +
   * `onFilterChange` emission behavior (used by EventsPage's CalendarDrawer
   * unchanged).
   */
  selectedDate?: Date | null;

  /**
   * Controlled selected-date change callback (task 118).
   *
   * Fired when the user clicks a day cell. The parent should:
   *   - Update its `selectedDate` state to the new Date (or null if
   *     toggling off).
   *   - Drive the grid's date filter accordingly.
   *
   * When OMITTED, day clicks fall through to the legacy `onFilterChange`
   * behavior (single-date filter emission). When PROVIDED, day clicks emit
   * via `onSelectDate` ONLY; `onFilterChange` is still called for
   * Shift+click range selections and footer-clear actions.
   *
   * Pass `null` from the handler to clear the selection (mirrors single-day
   * toggle-off semantics).
   */
  onSelectDate?: (date: Date | null) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.padding('12px', '16px'),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke1),
  },
  headerTitle: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('8px'),
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  calendarContent: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.padding('8px'),
    overflowY: 'auto',
  },
  // Task 116: horizontal layout variant. Row of months, no internal scroll
  // (the consuming widget bounds the width and uses external arrows to
  // navigate; we hide overflow so partial months don't appear cut off).
  //
  // Task 118: month-gap bumped from spacingHorizontalL (16px) → XL (20px)
  // per operator feedback ("Add left and right spacing between each
  // month"). Visual breathing room without sacrificing visible month
  // count at typical workspace widths.
  calendarContentHorizontal: {
    flex: 1,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    ...shorthands.padding('8px'),
    columnGap: tokens.spacingHorizontalXL,
    overflow: 'hidden',
  },
  monthContainer: {
    marginBottom: '16px',
  },
  // Task 116: in horizontal layout each month is a fixed-min-width column;
  // the bottom margin from `monthContainer` is irrelevant because flex
  // gap supplies the spacing. We share `monthContainer` for the grid + day
  // structure and override marginBottom for horizontal here.
  //
  // Task 122 (Round 13 follow-up #3): operator feedback "visually the
  // calendars run into each other" — add a subtle light vertical divider
  // on the LEFT edge of every horizontal month EXCEPT the first one. The
  // existing flex columnGap supplies the surrounding whitespace; this
  // border sits centered within that gap and clearly demarcates one
  // month from the next without competing with the day-cell content.
  monthContainerHorizontal: {
    marginBottom: 0,
    flex: '1 1 0',
    minWidth: '240px',
    paddingLeft: tokens.spacingHorizontalL,
    borderLeft: `1px solid ${tokens.colorNeutralStroke2}`,
    ':first-of-type': {
      paddingLeft: 0,
      borderLeft: 'none',
    },
  },
  monthHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding('8px'),
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  weekRow: {
    display: 'grid',
    gridTemplateColumns: 'repeat(7, 1fr)',
    ...shorthands.gap('2px'),
  },
  dayHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    ...shorthands.padding('4px', '0'),
  },
  dayCell: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    position: 'relative',
    minHeight: '36px',
    ...shorthands.padding('4px'),
    ...shorthands.borderRadius('4px'),
    cursor: 'pointer',
    // Task 127 (R13 follow-up #6): removed the base :hover from dayCell.
    // The neutral-background hover now lives on `dayCellNeutralHover` and
    // is applied conditionally only to cells that don't have a stronger
    // background-changing class (event-day, selected, in-range). This
    // eliminates the cross-class cascade fight that previously made the
    // dayWithEvents :hover lose to dayCell :hover (the bug operator kept
    // reporting in tasks 125 + 126). No !important needed anymore.
  },
  // Task 127: neutral hover applied conditionally only when no other
  // background-changing class is present. Keeps hover feedback on
  // "regular" cells without competing with event-day / selected / in-range
  // visualizations.
  dayCellNeutralHover: {
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  // Task 127 (R13 follow-up #6): empty placeholder for days outside the
  // displayed month. Operator: "keep dates only on its specific calendar."
  // Renders a fixed-height invisible cell so the grid alignment is
  // preserved but no day number, click handler, or hover effect exists.
  // The eventIndicator dot is also suppressed here (see render loop).
  dayCellEmpty: {
    minHeight: '36px',
    ...shorthands.padding('4px'),
  },
  dayCellOtherMonth: {
    color: tokens.colorNeutralForeground4,
  },
  dayCellToday: {
    fontWeight: tokens.fontWeightBold,
    color: tokens.colorBrandForeground1,
  },
  // Task 129 (R13 follow-up #8, 2026-05-23): operator: "the blue color
  // variants are very close — perhaps use grey?" Switched the selected-
  // day background from `colorBrandBackgroundSelected` (still blue, just
  // darker) to `colorNeutralBackgroundInverted` (a true grey/near-black)
  // with `colorNeutralForegroundInverted` (light text). Now visually
  // unambiguous: blue = event-day; grey = user-clicked. Inset ring
  // (colorNeutralStroke1) preserved as secondary "active selection" cue.
  dayCellSelected: {
    backgroundColor: tokens.colorNeutralBackgroundInverted,
    color: tokens.colorNeutralForegroundInverted,
    boxShadow: `inset 0 0 0 2px ${tokens.colorNeutralStroke1}`,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackgroundInverted,
    },
  },
  dayCellInRange: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  // Task 122 (Round 13 follow-up #2): solid brand-background + white text
  // per operator's exact wording — "blue background, white font" — replacing
  // task 118's softer brand-tint that proved invisible inside an active
  // From/To range. Event-day indicator now wins visually over the range
  // visualization (also see the showEventsTint logic — task 122 removed
  // the in-range exclusion so this style applies regardless of range state).
  //
  // Task 122 → 125 → 126 → 127 history:
  //   118: introduced soft-tint brand bg (lost in active From/To range)
  //   122: switched to solid colorBrandBackground + white text (operator
  //        wording: "blue background, white font")
  //   125: tried equal-specificity :hover lock — cascade still lost
  //   126: tried !important — still lost (operator: hover still bleeds)
  //   127: ROOT-CAUSE FIX — removed the competing :hover from dayCell
  //        and moved it to a separate `dayCellNeutralHover` class that's
  //        only applied to cells without a stronger background-changing
  //        class. Now there's no cascade fight; dayWithEvents simply has
  //        no :hover at all (the rest-state styling persists). The
  //        !important hack from 126 is removed.
  dayWithEvents: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    fontWeight: tokens.fontWeightSemibold,
  },
  dayNumber: {
    fontSize: tokens.fontSizeBase200,
  },
  eventIndicator: {
    position: 'absolute',
    bottom: '2px',
    width: '4px',
    height: '4px',
    ...shorthands.borderRadius('50%'),
    backgroundColor: tokens.colorBrandBackground,
  },
  eventIndicatorSelected: {
    backgroundColor: tokens.colorNeutralForegroundOnBrand,
  },
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.padding('8px', '16px'),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  clearButton: {
    fontSize: tokens.fontSizeBase200,
    minWidth: 'auto',
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
  selectionInfo: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    ...shorthands.padding('8px', '16px'),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke1),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

const DAYS_OF_WEEK = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

/**
 * Convert Date to ISO date string (YYYY-MM-DD).
 *
 * Task 120: use LOCAL date components (getFullYear/Month/Date) rather than
 * `date.toISOString()` which converts to UTC. The latter causes off-by-one
 * day bugs for users in any non-UTC timezone — a midnight local Date on
 * Feb 3 in US Eastern (UTC-5) is correctly `2026-02-03` via local
 * components but would be `2026-02-03T05:00:00Z` via toISOString (also
 * 2026-02-03 — coincidentally fine for negative offsets) whereas in
 * UTC+5 the same local Feb 3 midnight serializes to `2026-02-02T19:00:00Z`
 * (= Feb 2). Using local components is correct regardless of offset and
 * keys all day-cell membership checks (eventDateMap) symmetrically with
 * widget-side derivation in CalendarWorkspaceWidget.
 */
function toIsoDateString(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Parse ISO date string to Date
 */
function parseIsoDate(dateStr: string): Date {
  const [year, month, day] = dateStr.split('-').map(Number);
  return new Date(year, month - 1, day);
}

/**
 * Get all days to display for a month, padding only enough to complete the
 * weeks that contain current-month days.
 *
 * **Task 035 UAT iteration 3 fix**: was `endPadding = 42 - days.length` which
 * always rendered 6 rows; for short months (e.g. June 2026) the 6th row was
 * entirely next-month dates. Now pads to the next multiple of 7 so calendar
 * height varies between 5 and 6 rows by month (standard Power Apps / Outlook
 * behavior).
 */
function getMonthDays(year: number, month: number): Date[] {
  const firstDay = new Date(year, month, 1);
  const lastDay = new Date(year, month + 1, 0);
  const days: Date[] = [];

  const startPadding = firstDay.getDay();
  for (let i = startPadding - 1; i >= 0; i--) {
    const d = new Date(year, month, -i);
    days.push(d);
  }

  for (let d = 1; d <= lastDay.getDate(); d++) {
    days.push(new Date(year, month, d));
  }

  const weeksNeeded = Math.ceil(days.length / 7);
  const endPadding = weeksNeeded * 7 - days.length;
  for (let d = 1; d <= endPadding; d++) {
    days.push(new Date(year, month + 1, d));
  }

  return days;
}

/**
 * Check if a date is between two dates (inclusive)
 */
function isDateInRange(date: Date, start: Date, end: Date): boolean {
  const d = date.getTime();
  const s = start.getTime();
  const e = end.getTime();
  return d >= Math.min(s, e) && d <= Math.max(s, e);
}

/**
 * Check if two dates are the same day
 */
function isSameDay(date1: Date, date2: Date): boolean {
  return (
    date1.getFullYear() === date2.getFullYear() &&
    date1.getMonth() === date2.getMonth() &&
    date1.getDate() === date2.getDate()
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

const VERSION = '1.1.0';

export const CalendarSection: React.FC<CalendarSectionProps> = ({
  eventDates = [],
  onFilterChange,
  initialDate,
  height,
  viewDate: viewDateProp,
  monthsToShow: monthsToShowProp,
  layout = 'vertical',
  selectedDate: selectedDateProp,
  onSelectDate,
}) => {
  const styles = useStyles();
  const today = new Date();

  // State for current view (which months to display) — used only when the
  // parent does NOT pass `viewDate`. When `viewDateProp` is provided the
  // component is CONTROLLED and this internal state is bypassed entirely
  // (per task 116, to support the Calendar widget's external ◀ ▶ arrows).
  const [internalViewDate, setInternalViewDate] = React.useState<Date>(initialDate ? parseIsoDate(initialDate) : today);
  // setInternalViewDate is exposed for backwards compatibility with the
  // original component (no external API change). It's referenced only when
  // the component is in uncontrolled mode — currently no internal callers,
  // but kept so future month-nav buttons inside CalendarSection itself
  // remain possible without an API break.
  void setInternalViewDate;
  const viewDate = viewDateProp ?? internalViewDate;
  const monthsToShowCount = monthsToShowProp ?? 3;

  // Selection state — supports task 118 controlled mode.
  //
  // When `onSelectDate` is provided, the parent owns the selection: we
  // derive the displayed `selectedDate` from `selectedDateProp` and emit
  // changes via `onSelectDate` instead of internal `setInternalSelectedDate`.
  // When omitted (EventsPage CalendarDrawer), internal state drives the UI
  // and `onFilterChange` carries the day click.
  const isSelectedControlled = onSelectDate !== undefined;
  const [internalSelectedDate, setInternalSelectedDate] = React.useState<string | null>(initialDate ?? null);
  const selectedDate: string | null = isSelectedControlled
    ? selectedDateProp
      ? toIsoDateString(selectedDateProp)
      : null
    : internalSelectedDate;
  const [rangeStart, setRangeStart] = React.useState<string | null>(null);
  const [rangeEnd, setRangeEnd] = React.useState<string | null>(null);

  // Build event date lookup for O(1) access
  const eventDateMap = React.useMemo(() => {
    const map = new Map<string, number>();
    eventDates.forEach(({ date, count }) => {
      map.set(date, count);
    });
    return map;
  }, [eventDates]);

  /**
   * Handle date click.
   *
   * Branches:
   *  - Shift+click with an existing selection → range selection (legacy
   *    behavior, emits via `onFilterChange`).
   *  - Plain click in CONTROLLED selection mode (task 118) → emit through
   *    `onSelectDate`, toggle off if the same day is re-clicked. The
   *    parent (Calendar widget) is responsible for converting the date
   *    into a grid filter (single-day range from/to = clicked date).
   *  - Plain click in UNCONTROLLED mode (legacy EventsPage CalendarDrawer)
   *    → use internal selection state + emit single-day filter via
   *    `onFilterChange`.
   */
  const handleDateClick = React.useCallback(
    (date: Date, isShiftKey: boolean) => {
      const dateStr = toIsoDateString(date);

      if (isShiftKey && selectedDate) {
        // Range selection (legacy path — same for controlled + uncontrolled).
        const start = selectedDate;
        const end = dateStr;
        setRangeStart(start);
        setRangeEnd(end);
        const filter: CalendarFilterRange = {
          type: 'range',
          start: start < end ? start : end,
          end: start < end ? end : start,
        };
        onFilterChange(filter);
        // In controlled mode, clear the single-day selection because we
        // moved to a range — parent expects null when range takes over.
        if (isSelectedControlled) {
          onSelectDate?.(null);
        }
      } else if (isSelectedControlled) {
        // Task 118 controlled path.
        const isToggleOff = selectedDate === dateStr && !rangeStart;
        setRangeStart(null);
        setRangeEnd(null);
        if (isToggleOff) {
          onSelectDate?.(null);
        } else {
          // Re-emit a fresh Date (parent may need it for filter math).
          onSelectDate?.(new Date(date.getFullYear(), date.getMonth(), date.getDate()));
        }
      } else {
        // Legacy uncontrolled path.
        if (selectedDate === dateStr && !rangeStart) {
          // Toggle off
          setInternalSelectedDate(null);
          setRangeStart(null);
          setRangeEnd(null);
          onFilterChange({ type: 'clear' });
        } else {
          // Select new date
          setInternalSelectedDate(dateStr);
          setRangeStart(null);
          setRangeEnd(null);
          onFilterChange({ type: 'single', date: dateStr });
        }
      }
    },
    [selectedDate, rangeStart, onFilterChange, isSelectedControlled, onSelectDate]
  );

  /**
   * Clear selection (footer-clear button + horizontal layout omits the
   * footer entirely; this is uncontrolled-mode only).
   */
  const handleClearSelection = React.useCallback(() => {
    if (isSelectedControlled) {
      onSelectDate?.(null);
    } else {
      setInternalSelectedDate(null);
    }
    setRangeStart(null);
    setRangeEnd(null);
    onFilterChange({ type: 'clear' });
  }, [onFilterChange, isSelectedControlled, onSelectDate]);

  /**
   * Check if a date is selected or in range
   */
  const getDateState = React.useCallback(
    (date: Date): 'selected' | 'in-range' | null => {
      const dateStr = toIsoDateString(date);

      if (rangeStart && rangeEnd) {
        const start = parseIsoDate(rangeStart);
        const end = parseIsoDate(rangeEnd);
        if (isSameDay(date, start) || isSameDay(date, end)) {
          return 'selected';
        }
        if (isDateInRange(date, start, end)) {
          return 'in-range';
        }
      } else if (selectedDate === dateStr) {
        return 'selected';
      }

      return null;
    },
    [selectedDate, rangeStart, rangeEnd]
  );

  /**
   * Render a single month calendar
   */
  const renderMonth = (year: number, month: number) => {
    const days = getMonthDays(year, month);
    const weeks: Date[][] = [];

    for (let i = 0; i < days.length; i += 7) {
      weeks.push(days.slice(i, i + 7));
    }

    return (
      <div
        key={`${year}-${month}`}
        className={`${styles.monthContainer} ${layout === 'horizontal' ? styles.monthContainerHorizontal : ''}`}
      >
        <div className={styles.monthHeader}>
          {MONTHS[month]} {year}
        </div>

        {/* Day headers */}
        <div className={styles.weekRow}>
          {DAYS_OF_WEEK.map(day => (
            <div key={day} className={styles.dayHeader}>
              {day}
            </div>
          ))}
        </div>

        {/* Weeks */}
        {weeks.map((week, weekIdx) => (
          <div key={weekIdx} className={styles.weekRow}>
            {week.map((day, dayIdx) => {
              const isOtherMonth = day.getMonth() !== month;
              const isToday = isSameDay(day, today);
              const dateState = getDateState(day);
              const dateStr = toIsoDateString(day);
              const hasEvents = eventDateMap.has(dateStr);

              // Task 122 (Round 13 follow-up #2): event-day highlight must
              // dominate the in-range visualization. Operator showed a screenshot
              // where May 25 (a day with events, within an active From/To range)
              // had NO blue highlight because the in-range state suppressed
              // showEventsTint. Now the event-day tint shows REGARDLESS of
              // in-range — the solid colorBrandBackground (dayWithEvents style,
              // task 122) is visually stronger than the in-range stroke, so
              // the user sees the event indicator even within an active
              // date-range filter. The `selected` state still wins (explicit
              // user click on a day should highlight more strongly than a
              // passive event-day indicator). Other-month days remain excluded
              // — they're already muted and competing tints would be noisy.
              const showEventsTint = hasEvents && !isOtherMonth && dateState !== 'selected';

              // Task 127 (R13 follow-up #6, 2026-05-22): operator wants
              // other-month days completely hidden. Render an empty
              // placeholder div that preserves grid alignment but has no
              // day number, click handler, hover, or event indicator.
              if (isOtherMonth) {
                return <div key={dayIdx} className={styles.dayCellEmpty} aria-hidden="true" />;
              }

              // Task 127: dayCellNeutralHover applies ONLY when no other
              // background-changing class is present. This prevents the
              // cross-class :hover cascade fight that caused tasks 125/126
              // to fail. dayWithEvents now has NO :hover at all (the
              // rest-state blue persists since no rule overrides it).
              const showNeutralHover = !showEventsTint && dateState !== 'selected' && dateState !== 'in-range';

              return (
                <div
                  key={dayIdx}
                  className={`${styles.dayCell} ${
                    showNeutralHover ? styles.dayCellNeutralHover : ''
                  } ${isToday ? styles.dayCellToday : ''} ${showEventsTint ? styles.dayWithEvents : ''} ${
                    dateState === 'selected' ? styles.dayCellSelected : ''
                  } ${dateState === 'in-range' ? styles.dayCellInRange : ''}`}
                  onClick={e => handleDateClick(day, e.shiftKey)}
                  role="button"
                  tabIndex={0}
                  aria-label={`${day.toDateString()}${hasEvents ? ' - has events' : ''}`}
                  onKeyDown={e => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      handleDateClick(day, e.shiftKey);
                    }
                  }}
                >
                  <span className={styles.dayNumber}>{day.getDate()}</span>
                  {/* Task 126: legacy event-indicator dot removed (redundant
                      with the solid brand-blue background from dayWithEvents). */}
                </div>
              );
            })}
          </div>
        ))}
      </div>
    );
  };

  // Generate months to display. Default count is 3 (the prior internal
  // behavior — current + 2 ahead); parents may override via `monthsToShow`
  // prop (task 116 Calendar widget computes a responsive count from
  // container width).
  const monthsToShow = React.useMemo(() => {
    const months: { year: number; month: number }[] = [];
    const count = Math.max(1, monthsToShowCount);
    for (let i = 0; i < count; i++) {
      const d = new Date(viewDate.getFullYear(), viewDate.getMonth() + i, 1);
      months.push({ year: d.getFullYear(), month: d.getMonth() });
    }
    return months;
  }, [viewDate, monthsToShowCount]);

  const hasSelection = selectedDate !== null || rangeStart !== null;

  // Format selection info
  const selectionInfo = React.useMemo(() => {
    if (rangeStart && rangeEnd) {
      return `Selected: ${rangeStart} to ${rangeEnd}`;
    }
    if (selectedDate) {
      return `Selected: ${selectedDate}`;
    }
    return null;
  }, [selectedDate, rangeStart, rangeEnd]);

  // Task 118: in horizontal layout (Calendar workspace widget), the parent
  // owns the chrome (toolbar row with collapse chevron, plus its own date-
  // range filter row). The internal Calendar header (icon + "Calendar" +
  // separator) and footer (clear + version) are visual duplicates here, so
  // we suppress both. The EventsPage CalendarDrawer (vertical layout) is
  // unaffected.
  const isHorizontal = layout === 'horizontal';

  return (
    <div className={styles.container} style={{ height }}>
      {/* Header — suppressed in horizontal layout (task 118) */}
      {!isHorizontal && (
        <div className={styles.header}>
          <div className={styles.headerTitle}>
            <Calendar24Regular />
            <span>Calendar</span>
          </div>
        </div>
      )}

      {/* Selection info banner — also suppressed in horizontal layout
          (widget shows date selection via its own filter row + grid). */}
      {!isHorizontal && selectionInfo && <div className={styles.selectionInfo}>{selectionInfo}</div>}

      {/* Calendar content — vertical stack (default, EventsPage drawer) or
          horizontal row (Calendar widget, task 116). */}
      <div className={isHorizontal ? styles.calendarContentHorizontal : styles.calendarContent}>
        {monthsToShow.map(({ year, month }) => renderMonth(year, month))}
      </div>

      {/* Footer — suppressed in horizontal layout (task 118) */}
      {!isHorizontal && (
        <div className={styles.footer}>
          {hasSelection ? (
            <Button
              className={styles.clearButton}
              appearance="subtle"
              size="small"
              icon={<DismissRegular />}
              onClick={handleClearSelection}
              aria-label="Clear selection"
            >
              Clear
            </Button>
          ) : (
            <span />
          )}
          <Text className={styles.versionText}>v{VERSION}</Text>
        </div>
      )}
    </div>
  );
};

export default CalendarSection;
