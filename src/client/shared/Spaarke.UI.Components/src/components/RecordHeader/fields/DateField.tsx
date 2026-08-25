/**
 * DateField — record-header renderer for `DateOnly` and `DateAndTime` attributes (FR-06).
 *
 * ONE component covers both Dataverse date shapes; the caller passes `format`
 * (`'date'` for `DateOnly`, `'datetime'` for `DateAndTime`) resolved from the
 * attribute's metadata `Format` — this renderer never inspects metadata or
 * touches `Xrm`/`ComponentFramework` itself (ADR-012 context-agnostic
 * boundary). Read mode shows a locale-formatted date (and time, in
 * `'datetime'` mode) via `Intl.DateTimeFormat` — no hard-coded locale, no
 * manual pattern strings. Edit mode uses the Fluent v9 `DatePicker`
 * (`@fluentui/react-datepicker-compat`) plus, in `'datetime'` mode, a native
 * `Input type="time"` alongside it (the lightest token-pure, React-16-safe
 * option for a time-of-day control — no second picker package).
 *
 * Contract (FR-10, copied verbatim from {@link TextField}'s shape/semantics):
 *  - Label above (regular weight, neutral foreground — v1.0.4 typography)
 *  - Value below, OOB-input-parity read cell (colorNeutralBackground3, ~2em)
 *  - `null` / `undefined` / `''` value renders as an em-dash "—"
 *  - An unparseable value ALSO renders "—" plus a `console.warn` — never a
 *    throw, never literal "Invalid Date" text (NFR-10 graceful degradation)
 *  - CSS `grid-column: span N` self-applied on root for `FieldGrid` (FR-03)
 *  - `editable = typeof onSave === 'function' && disabled !== true`
 *  - Enter commits, Escape cancels (draft discarded, edit exited), blur
 *    commits; tiny `Spinner` + disabled inputs while saving; on save
 *    rejection the draft reverts to the prior value and edit mode is NOT
 *    exited (mirrors TextField.tsx:150-153)
 *  - Per D-10 / FR-11: `required` is accepted for prop-shape parity with
 *    TextField but renders NOTHING — the `*` marker is deliberately
 *    TextField-only. `required` is intentionally never read below.
 *
 * Picking a calendar day (or confirming a typed time) commits immediately —
 * this matches the form-buffer dirty-state UX (`Xrm.Page.getAttribute(n)
 * .setValue(v)`, staged on selection, per project CLAUDE.md) rather than
 * requiring a separate Enter after a picker selection. Enter/Escape/blur on
 * the date input only drive the DateField-level commit/cancel contract while
 * the calendar popup itself is CLOSED — while the popup is open, those keys
 * are left to the popup's own (Fluent-authored) keyboard navigation so
 * arrow-key day browsing and Escape-to-close-popup keep working.
 *
 * `onSave` receives a `Date | null` (the renderer domain) — the caller's
 * form-buffer payload type for a Dataverse DateAndTime/DateOnly attribute is
 * already a `Date`, so no serialization happens here. The value-TYPE differs
 * from TextField's `string`; the commit/cancel/revert SEMANTICS are
 * identical (task 015's contract-parity suite asserts behavior, not payload
 * uniformity).
 *
 * Per ADR-021: Fluent v9 semantic tokens only. Per ADR-022 (React 16/17
 * boundary): plain functional component, no React-18-exclusive APIs —
 * `@fluentui/react-datepicker-compat` peer-supports React >=16.14.0 <20.0.0
 * and is Griffel/token-based (verified against the published package before
 * adding it as a dependency — see task 010 notes).
 *
 * @see FR-06, FR-10, FR-11 record-header-and-notepad-r2 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 * @see ADR-012 shared component library (context-agnostic renderers)
 *
 * @example
 * ```tsx
 * // DateOnly attribute (e.g. sprk_invoicedate):
 * <DateField label="Invoice Date" span={1} format="date" value={record.sprk_invoicedate} />
 *
 * // DateAndTime attribute (e.g. sprk_plannedstart), editable:
 * <DateField
 *   label="Planned Start"
 *   span={1}
 *   format="datetime"
 *   value={record.sprk_plannedstart}
 *   onSave={async (next) => attribute.setValue(next)}
 * />
 * ```
 */
import * as React from 'react';
import { Input, Spinner, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { DatePicker } from '@fluentui/react-datepicker-compat';

/**
 * Props for {@link DateField}.
 */
export interface IDateFieldProps {
  label: string;
  /** ISO string, `Date`, or empty (`null`/`undefined`/`''`) — see file-level JSDoc for parsing rules. */
  value: string | Date | null | undefined;
  span: 1 | 2 | 3;
  /**
   * Display/edit mode, resolved by the consumer from the attribute's
   * Dataverse metadata `Format`: `DateOnly` → `'date'`, `DateAndTime` →
   * `'datetime'`. This renderer never resolves metadata itself.
   */
  format: 'date' | 'datetime';
  /**
   * Accepted for FR-10 contract-shape parity with {@link TextField}.
   * Renders NOTHING (D-10 / FR-11) — the `*` marker stays TextField-only.
   */
  required?: boolean;
  /**
   * When provided, the value becomes click-to-edit. Invoked with the new
   * `Date` (or `null` if cleared) on commit — see file-level JSDoc for when
   * a commit fires. Return a rejected Promise to signal a save error — the
   * field reverts to the previous value and stays in edit mode. Omit to
   * render read-only (default).
   */
  onSave?: (newValue: Date | null) => Promise<void>;
  /**
   * When `true`, disables editing (value shown but not clickable). Only
   * meaningful when `onSave` is also provided.
   */
  disabled?: boolean;
}

export const EMPTY_VALUE_PLACEHOLDER = '—';

/**
 * Parses `value` into a `Date`, distinguishing "empty" (no warning) from
 * "unparseable" (warn once per changed input — NFR-10 graceful degradation).
 */
function parseDateValue(value: string | Date | null | undefined): { date: Date | null; invalid: boolean } {
  if (value === null || value === undefined || value === '') {
    return { date: null, invalid: false };
  }
  const date = value instanceof Date ? value : new Date(value);
  return isNaN(date.getTime()) ? { date: null, invalid: true } : { date, invalid: false };
}

/** Locale-formatted display string — no hard-coded locale, no manual pattern strings. */
function formatDisplayValue(date: Date, format: 'date' | 'datetime'): string {
  const options: Intl.DateTimeFormatOptions =
    format === 'datetime' ? { dateStyle: 'short', timeStyle: 'short' } : { dateStyle: 'short' };
  return new Intl.DateTimeFormat(undefined, options).format(date);
}

/** Merge a newly-picked calendar date (Y/M/D) onto the draft's existing time-of-day (H/M). */
function mergeDatePart(base: Date | null, newDatePart: Date | null): Date | null {
  if (!newDatePart) return null;
  const merged = new Date(newDatePart);
  if (base) {
    merged.setHours(base.getHours(), base.getMinutes(), 0, 0);
  } else {
    merged.setHours(0, 0, 0, 0);
  }
  return merged;
}

/** Merge a newly-typed HH:mm time-of-day onto the draft's existing date-part (Y/M/D). */
function mergeTimePart(base: Date | null, hours: number, minutes: number): Date {
  const merged = base ? new Date(base) : new Date();
  merged.setHours(hours, minutes, 0, 0);
  return merged;
}

/** Format a `Date` as the `HH:mm` string a native `<input type="time">` expects. */
function toTimeInputValue(date: Date | null): string {
  if (!date) return '';
  const hh = String(date.getHours()).padStart(2, '0');
  const mm = String(date.getMinutes()).padStart(2, '0');
  return `${hh}:${mm}`;
}

const useDateFieldStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    rowGap: tokens.spacingVerticalXXS,
  },
  label: {
    // Matches TextField's v1.0.4 OOB form-field label typography exactly —
    // visual parity across a mixed FieldGrid (Matter parity baseline).
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
    lineHeight: tokens.lineHeightBase300,
    paddingBottom: tokens.spacingVerticalXS,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  value: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    // v1.0.3 OOB Dataverse-style read-cell — matches TextField/Matter baseline.
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    minHeight: '2em',
    display: 'flex',
    alignItems: 'center',
  },
  valueEditable: {
    cursor: 'text',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  editRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
});

export const DateField: React.FC<IDateFieldProps> = ({ label, value, span, format, onSave, disabled }) => {
  const styles = useDateFieldStyles();

  const { date: parsedValue, invalid } = React.useMemo(() => parseDateValue(value), [value]);

  React.useEffect(() => {
    if (invalid) {
      // NFR-10 graceful degradation: warn, never throw, never render "Invalid Date".
      console.warn(`DateField: could not parse date value for field "${label}":`, value);
    }
  }, [invalid, value, label]);

  const displayValue = parsedValue ? formatDisplayValue(parsedValue, format) : EMPTY_VALUE_PLACEHOLDER;
  const editable = typeof onSave === 'function' && disabled !== true;

  // Edit-mode state.
  const [editing, setEditing] = React.useState(false);
  const [draft, setDraft] = React.useState<Date | null>(parsedValue);
  const [saving, setSaving] = React.useState(false);
  // Tracks the DatePicker's OWN calendar-popup visibility (controlled) so
  // DateField-level Enter/Escape/blur only fire while the popup is closed —
  // otherwise the popup's own keyboard navigation and dismiss handling own
  // those keys.
  const [pickerOpen, setPickerOpen] = React.useState(false);

  // Focus-on-entering-edit-mode via ref + effect rather than the native
  // `autoFocus` attribute: `autoFocus` fires `.focus()` synchronously during
  // React's commitMount, which collides with Fluent's `useEventCallback`
  // render-phase guard on DatePicker's internal onFocus handler ("Cannot
  // call an event handler while rendering"). A passive effect runs after
  // commit finishes, avoiding the collision while producing the same UX.
  const dateInputRef = React.useRef<HTMLInputElement>(null);
  React.useEffect(() => {
    if (editing) {
      dateInputRef.current?.focus();
    }
  }, [editing]);

  // Reset draft when the external value changes (so an external refresh
  // doesn't drop an in-progress edit) — only sync when not editing.
  React.useEffect(() => {
    if (!editing) {
      setDraft(parsedValue);
    }
  }, [parsedValue, editing]);

  const enterEdit = React.useCallback(() => {
    if (!editable) return;
    setDraft(parsedValue);
    setPickerOpen(false);
    setEditing(true);
  }, [editable, parsedValue]);

  const commit = React.useCallback(
    async (candidate: Date | null) => {
      if (!onSave) return;
      const originalTime = parsedValue ? parsedValue.getTime() : null;
      const candidateTime = candidate ? candidate.getTime() : null;
      if (candidateTime === originalTime) {
        setEditing(false);
        return;
      }
      setSaving(true);
      try {
        await onSave(candidate);
        setEditing(false);
      } catch {
        // Revert draft to last-known value on error; stay in edit mode so
        // the user can retry or cancel (mirrors TextField.tsx:150-153).
        setDraft(parsedValue);
      } finally {
        setSaving(false);
      }
    },
    [onSave, parsedValue]
  );

  const cancel = React.useCallback(() => {
    setDraft(parsedValue);
    setPickerOpen(false);
    setEditing(false);
  }, [parsedValue]);

  const handleSelectDate = React.useCallback(
    (selected: Date | null | undefined) => {
      const merged = mergeDatePart(draft, selected ?? null);
      setDraft(merged);
      void commit(merged);
    },
    [draft, commit]
  );

  const handleDateKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (pickerOpen) return; // let the open calendar own its own keyboard nav
      if (ev.key === 'Enter') {
        ev.preventDefault();
        void commit(draft);
      } else if (ev.key === 'Escape') {
        ev.preventDefault();
        cancel();
      }
    },
    [pickerOpen, commit, draft, cancel]
  );

  const handleDateBlur = React.useCallback(() => {
    if (pickerOpen) return; // focus is moving into the open calendar popup
    void commit(draft);
  }, [pickerOpen, commit, draft]);

  const handleTimeChange = React.useCallback(
    (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      const match = /^(\d{2}):(\d{2})$/.exec(data.value);
      if (!match) return;
      setDraft(prev => mergeTimePart(prev, Number(match[1]), Number(match[2])));
    },
    []
  );

  const handleTimeKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === 'Enter') {
        ev.preventDefault();
        void commit(draft);
      } else if (ev.key === 'Escape') {
        ev.preventDefault();
        cancel();
      }
    },
    [commit, draft, cancel]
  );

  const handleTimeBlur = React.useCallback(() => {
    void commit(draft);
  }, [commit, draft]);

  return (
    <div
      className={mergeClasses(styles.root)}
      style={{ gridColumn: `span ${span}` }}
      data-testid="record-header-date-field"
      data-span={span}
      data-format={format}
      data-editable={editable ? 'true' : 'false'}
      data-editing={editing ? 'true' : 'false'}
    >
      <div className={styles.label} data-testid="record-header-date-field-label">
        {label}
      </div>
      {editing ? (
        <div className={styles.editRow}>
          <DatePicker
            ref={dateInputRef}
            appearance="filled-lighter"
            size="small"
            placeholder="Select a date"
            value={draft}
            onSelectDate={handleSelectDate}
            formatDate={d => (d ? formatDisplayValue(d, 'date') : '')}
            open={pickerOpen}
            onOpenChange={setPickerOpen}
            disabled={saving}
            onKeyDown={handleDateKeyDown}
            onBlur={handleDateBlur}
            data-testid="record-header-date-field-date-input"
          />
          {format === 'datetime' ? (
            <Input
              type="time"
              appearance="filled-lighter"
              size="small"
              value={toTimeInputValue(draft)}
              onChange={handleTimeChange}
              onKeyDown={handleTimeKeyDown}
              onBlur={handleTimeBlur}
              disabled={saving}
              data-testid="record-header-date-field-time-input"
            />
          ) : null}
          {saving ? <Spinner size="tiny" aria-label="Saving" data-testid="record-header-date-field-spinner" /> : null}
        </div>
      ) : (
        <div
          className={mergeClasses(styles.value, editable ? styles.valueEditable : undefined)}
          title={displayValue}
          onClick={editable ? enterEdit : undefined}
          role={editable ? 'button' : undefined}
          tabIndex={editable ? 0 : undefined}
          onKeyDown={
            editable
              ? ev => {
                  if (ev.key === 'Enter' || ev.key === ' ') {
                    ev.preventDefault();
                    enterEdit();
                  }
                }
              : undefined
          }
          data-testid="record-header-date-field-value"
        >
          {displayValue}
        </div>
      )}
    </div>
  );
};

DateField.displayName = 'DateField';
