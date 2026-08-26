/**
 * DateField — record-header renderer for `DateOnly` and `DateAndTime` attributes (FR-06).
 *
 * ONE component covers both Dataverse date shapes; the caller passes `format`
 * (`'date'` for `DateOnly`, `'datetime'` for `DateAndTime`) resolved from the
 * attribute's metadata `Format` — this renderer never inspects metadata or
 * touches `Xrm`/`ComponentFramework` itself (ADR-012 context-agnostic
 * boundary). Read mode shows a locale-formatted date (and time, in
 * `'datetime'` mode) via `Intl.DateTimeFormat` — no hard-coded locale, no
 * manual pattern strings.
 *
 * ── Editor: `Input type="date"` / `type="datetime-local"` (NFR-02) ──────────
 * Edit mode uses the Fluent v9 `Input` in native date mode — the pattern
 * already shipping in this library at
 * `components/CreateWorkAssignmentWizard/EnterInfoStep.tsx` — which renders
 * the BROWSER's own calendar/clock inside Fluent's `Input` chrome:
 *   - `format === 'date'`     → `type="date"`           (`yyyy-MM-dd`)
 *   - `format === 'datetime'` → `type="datetime-local"` (`yyyy-MM-ddTHH:mm`)
 *
 * This deliberately replaces `@fluentui/react-datepicker-compat`, which cost
 * ~285 KB of bundle (breaching NFR-02's 250 KB ceiling by +51%) — almost all
 * of it a SECOND private copy of Fluent internals the Power Apps host already
 * serves, because `pcf-scripts` externalizes only the `@fluentui/react-components`
 * umbrella and the picker imports its deps by their granular package names.
 * `Input` and `Field` live INSIDE that umbrella, so this editor costs zero
 * bundle bytes. Do NOT reintroduce the picker, and do NOT "fix" this with a
 * custom webpack `externals` block — that was tried and crashes at runtime
 * with "Minified React error #31" (see
 * `projects/record-header-and-notepad-r2/notes/decisions/033-nfr02-externals-runtime-failure.md`
 * and the ⛔ comment in the RecordHeader PCF `webpack.config.js`).
 *
 * ── Timezone contract (the classic day-shift failure) ───────────────────────
 * Native date inputs speak WALL-CLOCK time with no zone; Dataverse speaks
 * ISO-8601, often UTC. Every conversion here therefore goes through the
 * LOCAL calendar fields (`getFullYear`/`getMonth`/`getDate`/`getHours`/
 * `getMinutes` out, `new Date(y, m, d, h, min)` in) so the string in the input
 * always names the same day the read-mode cell displays via
 * `Intl.DateTimeFormat(undefined, …)`.
 *
 * Two traps this avoids, both of which shift the date by a day:
 *   1. `new Date('2026-08-21')` — a BARE date string is defined by ES2015+ to
 *      parse as UTC midnight, so it renders as Aug 20 anywhere west of UTC.
 *      `parseDateValue` special-cases that shape and builds LOCAL midnight
 *      instead. (Dataverse Web API returns exactly this shape for
 *      `DateOnly`-behavior attributes; `Xrm.Page.getAttribute().getValue()`
 *      returns a `Date` at local midnight — the two now agree.)
 *   2. `date.toISOString().slice(0, 10)` for the input value — that converts
 *      to UTC first, same shift in the other direction. `toInputValue` uses
 *      the local getters.
 * A date-time string carrying an explicit offset (`…Z`, `+05:00`) is a real
 * instant and is honored as-is; a local-form string (`2026-08-21T00:00:00`) is
 * already local per spec.
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
 *    commits; tiny `Spinner` + disabled input while saving; on save
 *    rejection the draft reverts to the prior value and edit mode is NOT
 *    exited (mirrors TextField.tsx:150-153)
 *  - `filled-lighter` appearance, `size="medium"` — matching every sibling
 *  - Per D-10 / FR-11: `required` is accepted for prop-shape parity with
 *    TextField but renders NOTHING — the `*` marker is deliberately
 *    TextField-only. `required` is intentionally never read below.
 *
 * Typing/picking a value STAGES a draft; Enter or blur COMMITS it. That makes
 * DateField a plain staged-draft renderer exactly like TextField — the old
 * "calendar selection commits immediately" special case belonged to the popup
 * picker and is gone with it. Committing straight from the native input's
 * per-keystroke `change` events would fire a save for every half-typed date.
 *
 * `onSave` receives a `Date | null` (the renderer domain) — the caller's
 * form-buffer payload type for a Dataverse DateAndTime/DateOnly attribute is
 * already a `Date`, so no serialization happens here, and staging goes through
 * the form buffer (`Xrm.Page.getAttribute(n).setValue(v)`), never
 * `Xrm.WebApi.updateRecord`. The value-TYPE differs from TextField's `string`;
 * the commit/cancel/revert SEMANTICS are identical (task 015's contract-parity
 * suite asserts behavior, not payload uniformity).
 *
 * Per ADR-021: Fluent v9 semantic tokens only. Per ADR-022 (React 16/17
 * boundary): plain functional component, no React-18-exclusive APIs.
 *
 * @see FR-06, FR-10, FR-11, NFR-02 record-header-and-notepad-r2 spec
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

/** A bare `yyyy-MM-dd` string — the Dataverse Web API shape for `DateOnly` behavior. */
const BARE_DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** What a native `date` / `datetime-local` input hands back on change. */
const INPUT_VALUE_RE = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/;

const pad2 = (n: number): string => String(n).padStart(2, '0');

/**
 * Parses `value` into a `Date`, distinguishing "empty" (no warning) from
 * "unparseable" (warn once per changed input — NFR-10 graceful degradation).
 *
 * A BARE `yyyy-MM-dd` is built as LOCAL midnight rather than handed to
 * `new Date(string)`, which the spec defines as UTC midnight for that shape —
 * see the timezone section of the file-level JSDoc.
 */
function parseDateValue(value: string | Date | null | undefined): { date: Date | null; invalid: boolean } {
  if (value === null || value === undefined || value === '') {
    return { date: null, invalid: false };
  }
  let date: Date;
  if (value instanceof Date) {
    date = value;
  } else {
    const bare = BARE_DATE_RE.exec(value);
    date = bare
      ? new Date(Number(bare[1]), Number(bare[2]) - 1, Number(bare[3]), 0, 0, 0, 0)
      : new Date(value);
  }
  return isNaN(date.getTime()) ? { date: null, invalid: true } : { date, invalid: false };
}

/** Locale-formatted display string — no hard-coded locale, no manual pattern strings. */
function formatDisplayValue(date: Date, format: 'date' | 'datetime'): string {
  const options: Intl.DateTimeFormatOptions =
    format === 'datetime' ? { dateStyle: 'short', timeStyle: 'short' } : { dateStyle: 'short' };
  return new Intl.DateTimeFormat(undefined, options).format(date);
}

/**
 * `Date` → the wall-clock string a native `date` / `datetime-local` input
 * expects, read off the LOCAL calendar fields so it names the same day the
 * read cell shows. Never `toISOString()` — that converts to UTC first.
 */
export function toInputValue(date: Date | null, format: 'date' | 'datetime'): string {
  if (!date) return '';
  const datePart = `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
  return format === 'datetime'
    ? `${datePart}T${pad2(date.getHours())}:${pad2(date.getMinutes())}`
    : datePart;
}

/**
 * The native input's wall-clock string → `Date`, constructed from LOCAL parts
 * (`new Date(y, m, d, …)`) so no zone conversion happens on the way in.
 *
 * In `'date'` mode the input carries no time-of-day, so the previous draft's
 * time-of-day is preserved (midnight when there was none) — the same merge the
 * retired picker's `mergeDatePart` performed, which keeps a `DateOnly`
 * attribute round-tripping to the instant it was read at.
 *
 * Returns `null` for an empty or incomplete value (native date inputs report
 * `''` until every segment is filled, and the input's own clear affordance
 * produces `''` too — both mean "no value").
 */
export function fromInputValue(raw: string, format: 'date' | 'datetime', base: Date | null): Date | null {
  if (!raw) return null;
  const m = INPUT_VALUE_RE.exec(raw);
  if (!m) return null;
  const [, y, mo, d, h, mi] = m;
  const hours = format === 'datetime' ? Number(h ?? 0) : base ? base.getHours() : 0;
  const minutes = format === 'datetime' ? Number(mi ?? 0) : base ? base.getMinutes() : 0;
  const parsed = new Date(Number(y), Number(mo) - 1, Number(d), hours, minutes, 0, 0);
  return isNaN(parsed.getTime()) ? null : parsed;
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
    minWidth: 0,
  },
  editInput: {
    // The native date/datetime segments need room; without this the Input
    // shrinks to its flex-basis inside a narrow FieldGrid cell and clips.
    flexGrow: 1,
    minWidth: 0,
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

  // Focus-on-entering-edit-mode via ref + effect rather than the native
  // `autoFocus` attribute: `autoFocus` fires `.focus()` synchronously during
  // React's commitMount, which collides with Fluent's `useEventCallback`
  // render-phase guard. A passive effect runs after commit finishes, avoiding
  // the collision while producing the same UX.
  const inputRef = React.useRef<HTMLInputElement>(null);
  React.useEffect(() => {
    if (editing) {
      inputRef.current?.focus();
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
    setEditing(false);
  }, [parsedValue]);

  const handleChange = React.useCallback(
    (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setDraft(prev => fromInputValue(data.value, format, prev));
    },
    [format]
  );

  const handleKeyDown = React.useCallback(
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

  const handleBlur = React.useCallback(() => {
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
          <Input
            ref={inputRef}
            className={styles.editInput}
            type={format === 'datetime' ? 'datetime-local' : 'date'}
            appearance="filled-lighter"
            // v1.1.6: `medium`, NOT `small`. Fluent sizes the small variant at
            // `fontSizeBase200` (12px) while the read-mode cell above uses
            // `fontSizeBase300` (14px), so text visibly shrank the moment a
            // field was clicked. Medium matches both the read state and the OOB
            // Dataverse inputs beside the header.
            size="medium"
            aria-label={label}
            value={toInputValue(draft, format)}
            onChange={handleChange}
            onKeyDown={handleKeyDown}
            onBlur={handleBlur}
            disabled={saving}
            data-testid="record-header-date-field-input"
          />
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
