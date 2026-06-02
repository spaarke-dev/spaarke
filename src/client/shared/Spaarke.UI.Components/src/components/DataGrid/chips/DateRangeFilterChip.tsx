/**
 * DateRangeFilterChip — Fluent v9 date-range filter chip with LOCAL → UTC
 * bounds conversion.
 *
 * Renders a compact trigger button labelled with either `label` (no value) or
 * a short summary like `"Jun 1 – Jun 30"` once a range is picked. Clicking
 * opens a Fluent v9 `<Popover>` containing two `<Input type="date">` controls
 * (start + end) and a Clear button.
 *
 * On Apply (or live as the user changes either field) the chip emits a
 * `{ startUtc: Date, endUtc: Date }` object whose UTC bounds correspond to
 * the FULL LOCAL day range chosen by the user — see {@link localDateToUtcBounds}
 * for the conversion semantics and rationale.
 *
 * **Why `<Input type="date">` instead of Fluent `<Calendar>`?**
 * The Fluent v9 `Calendar` lives in `@fluentui/react-calendar-compat` which is
 * NOT a peerDependency of this library. To avoid expanding the dependency
 * surface for a single-task primitive we use the native HTML5 date input
 * styled by Fluent v9 `<Input>` (which honours the active FluentProvider
 * theme + tokens). The conversion logic is the same regardless of picker
 * widget — see the unit test for `localDateToUtcBounds` in
 * `__tests__/dateUtils.test.ts`.
 *
 * **Spec**: projects/spaarke-datagrid-framework-r1/spec.md FR-DG-07 + FR-DG-13
 * **Design**: design.md §6.4 (filter chip primitives), §11.5.2 (tokens)
 * **ADRs**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe)
 *
 * **NFR-03 compliance**: the Popover content is re-wrapped in
 * `<FluentProvider applyStylesToPortals theme={...}>` so dark-mode + portal
 * theme inheritance work across PCF iframe contexts.
 *
 * **NFR-02 compliance**: zero raw hex; all colors via `tokens.*` and
 * `dataGridTokens` (themselves token-derived).
 *
 * **React-16-safe**: no `useId`, no `useSyncExternalStore`, no `createRoot`.
 */

import * as React from 'react';
import {
  Button,
  Input,
  Field,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  FluentProvider,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { ChevronDownRegular, CalendarLtrRegular } from '@fluentui/react-icons';

import { dataGridTokens } from '../tokens';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The UTC bounds the parent FetchXML caller uses when constructing
 * `<condition operator="on-or-after" />` / `<condition operator="on-or-before" />`
 * (or equivalent OData `ge` / `le`) clauses against a UTC-stored Dataverse
 * date/datetime attribute.
 */
export interface UtcDateBounds {
  /** UTC instant corresponding to LOCAL midnight of the start day. */
  startUtc: Date;
  /** UTC instant corresponding to LOCAL 23:59:59.999 of the end day. */
  endUtc: Date;
}

/**
 * Props for {@link DateRangeFilterChip}.
 *
 * Fully controlled: pass `value === null` for "no range selected"; the chip
 * holds NO internal date state outside its open/close + draft-pick state.
 */
export interface DateRangeFilterChipProps {
  /** Currently selected UTC bounds, or `null` when no range is picked. */
  value: UtcDateBounds | null;

  /**
   * Fires when the user clicks Apply on a complete range, OR when they click
   * Clear (emits `null`). Does NOT fire on partial picks (start without end).
   */
  onChange: (next: UtcDateBounds | null) => void;

  /** Chip trigger label when `value === null` (e.g. `"Created on"`, `"Due date"`). */
  label: string;

  /** OPTIONAL — additional class merged AFTER component classes. */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Pure helper — LOCAL → UTC bounds conversion (the load-bearing piece)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Convert a LOCAL date range (two `Date` objects whose Y/M/D parts identify
 * LOCAL calendar days) into the UTC instants that exactly bracket those
 * LOCAL days.
 *
 * **Why this matters** (verbatim port from
 * `src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx#L343-L348`,
 * task 133 / R13 follow-up #12, 2026-05-23):
 *
 * > The prior implementation appended a literal `T00:00:00Z` / `T23:59:59Z`
 * > to the date string, producing UTC bounds. But the calendar HIGHLIGHT
 * > derives day-keys using LOCAL date components (...). The mismatch caused
 * > records whose stored UTC timestamp falls in one UTC day but a different
 * > LOCAL day to match the wrong filter window.
 * >
 * > Fix: convert local-date strings to UTC ISO bounds that align with
 * > LOCAL day boundaries. localDateToUtcBounds("2026-03-12") in
 * > Eastern returns start = "2026-03-12T05:00:00.000Z" and
 * > end = "2026-03-13T04:59:59.999Z" — exactly the UTC range that
 * > corresponds to local March 12 00:00 - 23:59.
 *
 * The original GridSection helper took a `YYYY-MM-DD` string for a single
 * day; this chip operates on two `Date` objects so the input shape adapts
 * but the timezone math is identical: `new Date(y, m-1, d, 0, 0, 0, 0)`
 * resolves to LOCAL midnight of that day, and `.toISOString()` returns the
 * UTC equivalent.
 *
 * @example
 * // Operator in EDT (UTC-4) picks LOCAL 2026-06-01 .. 2026-06-30.
 * localDateToUtcBounds(new Date(2026, 5, 1), new Date(2026, 5, 30))
 * // → { startUtc: 2026-06-01T04:00:00.000Z,
 * //     endUtc:   2026-07-01T03:59:59.999Z }
 *
 * @public
 */
export function localDateToUtcBounds(start: Date, end: Date): UtcDateBounds {
  // Extract LOCAL Y/M/D from each Date and rebuild as LOCAL midnight / EOD.
  // Direct `new Date(y, m-1, d, ...)` constructor uses the host timezone,
  // and `.toISOString()` returns the corresponding UTC instant — exactly
  // what the GridSection helper does for a single day, repeated for two
  // bounds.
  const startLocal = new Date(
    start.getFullYear(),
    start.getMonth(),
    start.getDate(),
    0,
    0,
    0,
    0,
  );
  const endLocal = new Date(
    end.getFullYear(),
    end.getMonth(),
    end.getDate(),
    23,
    59,
    59,
    999,
  );
  return { startUtc: startLocal, endUtc: endLocal };
}

/**
 * Parse a `YYYY-MM-DD` string (the HTML5 `<input type="date">` value format)
 * into a `Date` whose LOCAL Y/M/D match the string. Returns `null` for an
 * empty string or malformed input — callers treat `null` as "not picked".
 *
 * Cannot use `new Date(yyyy_mm_dd)` directly because the ECMAScript spec
 * parses date-only strings as UTC midnight, which would shift the LOCAL
 * day by one in negative-offset timezones. We split the parts and use the
 * Y/M/D constructor instead.
 *
 * @internal
 */
function parseLocalDate(value: string): Date | null {
  if (!value) return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const y = Number.parseInt(match[1], 10);
  const m = Number.parseInt(match[2], 10);
  const d = Number.parseInt(match[3], 10);
  if (Number.isNaN(y) || Number.isNaN(m) || Number.isNaN(d)) return null;
  return new Date(y, m - 1, d, 0, 0, 0, 0);
}

/**
 * Inverse of {@link parseLocalDate} — format a `Date`'s LOCAL Y/M/D as the
 * `YYYY-MM-DD` string the HTML5 date input expects.
 *
 * @internal
 */
function formatLocalDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Render the chip trigger label when a range is picked — short month-day
 * summary, e.g. `"Jun 1 – Jun 30"`. We render against the operator's locale.
 *
 * @internal
 */
function formatRangeSummary(start: Date, end: Date): string {
  const fmt = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' });
  return `${fmt.format(start)} – ${fmt.format(end)}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (module scope per ADR-021)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** Trigger wrapper — sits alongside other chips with parent-owned gap. */
  root: {
    display: 'inline-flex',
    alignItems: 'center',
  },
  /** Trigger button uses Fluent's `appearance="subtle"` default chrome. */
  trigger: {
    fontSize: dataGridTokens.filterChip.fontSize,
  },
  /** Popover surface — comfortable width for two stacked date fields. */
  surface: {
    minWidth: '260px',
    maxWidth: '320px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalS),
  },
  /** Footer row hosting Clear + Apply. */
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
  },
  /** Inline validation caption (e.g. "End is before start"). */
  validation: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const DateRangeFilterChip: React.FC<DateRangeFilterChipProps> = ({
  value,
  onChange,
  label,
  className,
}) => {
  const styles = useStyles();

  // ── Open/close + draft pick state ──────────────────────────────────────────
  // While the popover is open we hold the user's in-progress picks in
  // `draftStart` / `draftEnd`. On Apply we emit; on Clear we emit null;
  // on Close-without-Apply we discard the draft.
  const [open, setOpen] = React.useState<boolean>(false);
  const [draftStart, setDraftStart] = React.useState<string>('');
  const [draftEnd, setDraftEnd] = React.useState<string>('');

  // When the popover OPENS, hydrate the drafts from `value` (if any) so the
  // user sees their last-applied range instead of empty fields.
  const handleOpenChange = React.useCallback(
    (_event: unknown, data: { open: boolean }) => {
      if (data.open) {
        if (value) {
          setDraftStart(formatLocalDate(value.startUtc));
          setDraftEnd(formatLocalDate(value.endUtc));
        } else {
          setDraftStart('');
          setDraftEnd('');
        }
      }
      setOpen(data.open);
    },
    [value],
  );

  const handleStartChange: React.ChangeEventHandler<HTMLInputElement> = React.useCallback(
    (ev) => setDraftStart(ev.target.value),
    [],
  );

  const handleEndChange: React.ChangeEventHandler<HTMLInputElement> = React.useCallback(
    (ev) => setDraftEnd(ev.target.value),
    [],
  );

  const parsedStart = React.useMemo(() => parseLocalDate(draftStart), [draftStart]);
  const parsedEnd = React.useMemo(() => parseLocalDate(draftEnd), [draftEnd]);

  /** Both ends required; end ≥ start. */
  const isValidRange: boolean =
    parsedStart !== null &&
    parsedEnd !== null &&
    parsedEnd.getTime() >= parsedStart.getTime();

  const isReversedRange: boolean =
    parsedStart !== null &&
    parsedEnd !== null &&
    parsedEnd.getTime() < parsedStart.getTime();

  const handleApply = React.useCallback(() => {
    if (!parsedStart || !parsedEnd || !isValidRange) return;
    onChange(localDateToUtcBounds(parsedStart, parsedEnd));
    setOpen(false);
  }, [parsedStart, parsedEnd, isValidRange, onChange]);

  const handleClear = React.useCallback(() => {
    setDraftStart('');
    setDraftEnd('');
    onChange(null);
    setOpen(false);
  }, [onChange]);

  // ── Trigger label ──────────────────────────────────────────────────────────
  const triggerLabel: string = React.useMemo(() => {
    if (!value) return label;
    return formatRangeSummary(value.startUtc, value.endUtc);
  }, [value, label]);

  // ── Render ─────────────────────────────────────────────────────────────────
  return (
    <div className={mergeClasses(styles.root, className)} data-testid="date-range-filter-chip">
      <Popover
        open={open}
        onOpenChange={handleOpenChange}
        trapFocus
        positioning="below-start"
      >
        <PopoverTrigger disableButtonEnhancement>
          <Button
            className={styles.trigger}
            appearance="subtle"
            iconPosition="before"
            icon={<CalendarLtrRegular aria-hidden="true" />}
            aria-label={value ? `${label}: ${triggerLabel}` : label}
            data-testid="date-range-filter-chip-trigger"
          >
            {triggerLabel}
            <ChevronDownRegular aria-hidden="true" />
          </Button>
        </PopoverTrigger>

        <PopoverSurface>
          {/*
            NFR-03 portal-theme re-wrap. The Popover surface renders into a
            React Portal which escapes the root FluentProvider's DOM subtree;
            in PCF iframe contexts the root provider's applyStylesToPortals
            default does not always propagate. The nested FluentProvider here
            inherits its theme via React context (which DOES flow through the
            React tree even though the DOM is portaled), and re-asserts
            applyStylesToPortals so any nested portal stays themed. This is
            the canonical Option A from
            .claude/patterns/ui/fluent-v9-portal-gotcha.md, deliberately NOT
            passing an explicit `theme` prop so the customer-tenant theme is
            never accidentally shadowed.
          */}
          <FluentProvider applyStylesToPortals>
            <div className={styles.surface}>
              <Field label="Start date" data-testid="date-range-filter-chip-start-field">
                <Input
                  type="date"
                  value={draftStart}
                  onChange={handleStartChange}
                  aria-label={`${label} start date`}
                  data-testid="date-range-filter-chip-start"
                />
              </Field>
              <Field label="End date" data-testid="date-range-filter-chip-end-field">
                <Input
                  type="date"
                  value={draftEnd}
                  onChange={handleEndChange}
                  aria-label={`${label} end date`}
                  data-testid="date-range-filter-chip-end"
                />
              </Field>
              {isReversedRange ? (
                <Text size={200} className={styles.validation} role="alert">
                  End date must be on or after the start date.
                </Text>
              ) : null}
              <div className={styles.footer}>
                <Button
                  appearance="subtle"
                  size="small"
                  onClick={handleClear}
                  aria-label={`Clear ${label} filter`}
                  data-testid="date-range-filter-chip-clear"
                >
                  Clear
                </Button>
                <Button
                  appearance="primary"
                  size="small"
                  disabled={!isValidRange}
                  onClick={handleApply}
                  aria-label={`Apply ${label} filter`}
                  data-testid="date-range-filter-chip-apply"
                >
                  Apply
                </Button>
              </div>
            </div>
          </FluentProvider>
        </PopoverSurface>
      </Popover>
    </div>
  );
};

export default DateRangeFilterChip;
