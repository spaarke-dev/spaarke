/**
 * NumberField — numeric/currency record-header renderer for `FieldGrid` (FR-07).
 *
 * Covers Dataverse `Integer`, `Decimal`, `Double`, and `Money` attribute
 * types. The consumer resolves `kind` (from `AttributeType`), `precision`
 * (from attribute metadata), and `currencySymbol` (from the record's
 * formatted-value annotation / currency metadata) and passes them as props —
 * this component never touches `Xrm` or metadata APIs (ADR-012
 * context-agnostic contract).
 *
 * Contract mirrors {@link TextField} verbatim (FR-10, copied structure):
 *  - Label above (regular weight, neutral foreground — v1.0.4 typography)
 *  - Value below on a light-grey OOB-parity surface, RIGHT-aligned — the
 *    numeric/money alignment convention DataGrid encodes in
 *    `defaultAlignFor` (configResolution.ts:393)
 *  - `null` / `undefined` / `''` render the em-dash "—"; the number `0` is a
 *    REAL value and renders as formatted `0` — FR-11's empty check is
 *    strict, never falsy
 *  - A non-numeric string value renders the em-dash and emits a
 *    `console.warn` — never a throw, never literal "NaN" text (NFR-10)
 *  - `required===true` renders NOTHING (D-10 / FR-11 — the `*` marker is
 *    deliberately TextField-only; this prop exists for contract-shape parity)
 *  - CSS `grid-column: span N` on root for FieldGrid integration
 *  - When `onSave` supplied: click-to-edit; the edit input holds the RAW
 *    unformatted number as the draft (no symbol, no thousands separators
 *    while typing), also right-aligned; Enter commits / Escape cancels /
 *    blur commits; empty draft commits `null`; a non-numeric draft NEVER
 *    reaches `onSave` (stays in edit mode); on save rejection the draft
 *    reverts to the prior value AND the component STAYS in edit mode;
 *    spinner + disabled input while saving
 *
 * Money rendering deliberately prefixes the formatted number with
 * `currencySymbol` TEXT rather than using `Intl.NumberFormat`'s `currency`
 * style — the symbol is metadata text supplied by the consumer, not a
 * guessable ISO currency code, and guessing one would be wrong for
 * multi-currency orgs.
 *
 * Per ADR-021: Fluent v9 semantic tokens only. Per ADR-022 (React 16/17
 * boundary): plain functional component, no React-18-exclusive APIs.
 *
 * @see FR-07, FR-10, FR-11 (+ D-10) record-header-and-notepad-r2 spec
 * @see ADR-012 Shared component library (context-agnostic contract)
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 *
 * @example
 * ```tsx
 * // Consumer has already resolved kind/precision/currencySymbol from metadata:
 * <NumberField
 *   span={1}
 *   label="Total Amount"
 *   value={12500}
 *   kind="money"
 *   precision={2}
 *   currencySymbol="$"
 * />
 * // → "$12,500.00", right-aligned
 * ```
 */
import * as React from 'react';
import { Input, Spinner, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/** Dataverse numeric attribute kinds this renderer covers (FR-07). */
export type NumberFieldKind = 'integer' | 'decimal' | 'double' | 'money';

/**
 * Props for {@link NumberField}.
 */
export interface INumberFieldProps {
  /** Display label rendered above the value. */
  label: string;
  /**
   * Raw numeric value. Accepts a `number` (the common case) or a numeric
   * `string` (e.g. a value read off a form buffer as text). `null` /
   * `undefined` / `''` render the em-dash empty state; a non-numeric string
   * also renders the em-dash (plus a `console.warn`) rather than throwing.
   */
  value: number | string | null | undefined;
  /** Number of grid columns this cell occupies inside a `FieldGrid`. */
  span: 1 | 2 | 3;
  /**
   * Dataverse `AttributeType` mapped to a formatting kind by the consumer:
   * `Integer` → `'integer'`, `Decimal` → `'decimal'`, `Double` → `'double'`,
   * `Money` → `'money'`.
   */
  kind: NumberFieldKind;
  /**
   * Fraction-digit count from Dataverse attribute metadata (`Precision`).
   * Ignored for `kind='integer'` (always 0 fraction digits). Defaults to 2
   * when omitted for decimal / double / money.
   */
  precision?: number;
  /**
   * Currency symbol TEXT (e.g. `'$'`, `'€'`) resolved by the consumer from
   * the record's formatted-value annotation or currency metadata — never an
   * ISO currency code. Only meaningful when `kind='money'`; ignored
   * otherwise.
   */
  currencySymbol?: string;
  /**
   * Present for FR-10 contract-shape parity with sibling renderers only.
   * NumberField renders NO `*` marker regardless of this value (D-10 /
   * FR-11 — the required marker is deliberately TextField-only).
   */
  required?: boolean;
  /**
   * When provided, the value becomes click-to-edit. Callback receives the
   * committed value as a `number`, or `null` when the draft was cleared.
   * Return a rejected Promise to signal a save error — the field reverts to
   * the previous value and STAYS in edit mode. Omit to render read-only
   * (default).
   */
  onSave?: (newValue: number | null) => Promise<void>;
  /**
   * When `true`, disables editing (value shown but not clickable). Only
   * meaningful when `onSave` is also provided.
   */
  disabled?: boolean;
}

export const EMPTY_VALUE_PLACEHOLDER = '—';

/** Default fraction-digit count when `precision` is omitted (decimal / double / money). */
const DEFAULT_PRECISION = 2;

const useNumberFieldStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    rowGap: tokens.spacingVerticalXXS,
  },
  label: {
    // Matches TextField's v1.0.4 OOB-parity label typography.
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
    // v1.0.3 OOB-input-parity surface (matches TextField's sibling cell).
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    minHeight: '2em',
    display: 'flex',
    alignItems: 'center',
    // FR-07: numeric/money values read right-aligned — mirrors DataGrid's
    // `defaultAlignFor` (configResolution.ts:393). The cell is a flex
    // container so `justifyContent` (not `textAlign`) positions the value.
    justifyContent: 'flex-end',
    textAlign: 'right',
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
  },
  /**
   * Applied via the Fluent v9 `input` slot override (NOT the top-level
   * `className`, which targets `Input`'s root `<span>` wrapper) so the
   * raw-number draft reads right-aligned to match the read-mode value cell.
   */
  editInputRaw: {
    textAlign: 'right',
  },
});

/**
 * Formats a resolved numeric value for read mode per `kind` / `precision`.
 * `kind='money'` prefixes the formatted number with `currencySymbol` text —
 * see {@link INumberFieldProps.currencySymbol} for why `Intl` currency style
 * is deliberately not used.
 */
function formatNumberValue(
  kind: NumberFieldKind,
  precision: number | undefined,
  currencySymbol: string | undefined,
  numericValue: number
): string {
  if (kind === 'integer') {
    return new Intl.NumberFormat(undefined, {
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(numericValue);
  }

  const digits = typeof precision === 'number' ? precision : DEFAULT_PRECISION;
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(numericValue);

  return kind === 'money' ? `${currencySymbol ?? ''}${formatted}` : formatted;
}

/** Result of resolving a raw prop value to a usable numeric value. */
interface IResolvedNumber {
  /** Parsed numeric value, or `null` for the empty state. */
  numeric: number | null;
  /** True when a non-empty value could not be parsed as a finite number. */
  invalid: boolean;
}

/**
 * Resolves `value` (`number | string | null | undefined`) to a numeric
 * value for display. Empty (`null` / `undefined` / `''`) is distinct from
 * invalid (non-numeric string / non-finite number) — per FR-11 the empty
 * check is strict, never falsy, so `0` is a real value.
 */
function resolveNumericValue(value: number | string | null | undefined): IResolvedNumber {
  if (value === null || value === undefined || value === '') {
    return { numeric: null, invalid: false };
  }
  if (typeof value === 'number') {
    return Number.isFinite(value) ? { numeric: value, invalid: false } : { numeric: null, invalid: true };
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? { numeric: parsed, invalid: false } : { numeric: null, invalid: true };
}

/**
 * Record-header numeric/currency field renderer. See file-level JSDoc for
 * the full contract, empty/invalid-value handling, and edit-mode behavior.
 */
export const NumberField: React.FC<INumberFieldProps> = ({
  label,
  value,
  span,
  kind,
  precision,
  currencySymbol,
  onSave,
  disabled,
}) => {
  const styles = useNumberFieldStyles();

  const { numeric, invalid } = resolveNumericValue(value);

  React.useEffect(() => {
    if (invalid) {
      console.warn(`[NumberField] Non-numeric value for "${label}": ${String(value)} — rendering empty state.`);
    }
  }, [invalid, label, value]);

  const displayValue =
    numeric === null ? EMPTY_VALUE_PLACEHOLDER : formatNumberValue(kind, precision, currencySymbol, numeric);

  // Raw (unformatted) string for the edit-mode draft — no symbol, no
  // thousands separators while typing.
  const rawDraftValue = numeric === null ? '' : String(numeric);

  const editable = typeof onSave === 'function' && disabled !== true;

  // Edit-mode state
  const [editing, setEditing] = React.useState(false);
  const [draft, setDraft] = React.useState<string>(rawDraftValue);
  const [saving, setSaving] = React.useState(false);

  // Reset draft when the external value changes (so an external refresh
  // doesn't drop the user's typed value mid-edit — only sync on entry).
  React.useEffect(() => {
    if (!editing) {
      setDraft(rawDraftValue);
    }
  }, [rawDraftValue, editing]);

  const enterEdit = React.useCallback(() => {
    if (!editable) return;
    setDraft(rawDraftValue);
    setEditing(true);
  }, [editable, rawDraftValue]);

  const commit = React.useCallback(async () => {
    if (!onSave) return;

    const trimmed = draft.trim();

    if (trimmed === '') {
      if (numeric === null) {
        // Already empty — nothing changed.
        setEditing(false);
        return;
      }
      setSaving(true);
      try {
        await onSave(null);
        setEditing(false);
      } catch {
        setDraft(rawDraftValue);
      } finally {
        setSaving(false);
      }
      return;
    }

    const parsedDraft = Number(trimmed);
    if (!Number.isFinite(parsedDraft)) {
      // Non-numeric draft: never reaches onSave; keep editing (FR-11).
      return;
    }

    if (parsedDraft === numeric) {
      setEditing(false);
      return;
    }

    setSaving(true);
    try {
      await onSave(parsedDraft);
      setEditing(false);
    } catch {
      // Revert draft to last-known value on error; stay in edit mode so the
      // user can retry or cancel.
      setDraft(rawDraftValue);
    } finally {
      setSaving(false);
    }
  }, [onSave, draft, numeric, rawDraftValue]);

  const cancel = React.useCallback(() => {
    setDraft(rawDraftValue);
    setEditing(false);
  }, [rawDraftValue]);

  const onKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === 'Enter') {
        ev.preventDefault();
        void commit();
      } else if (ev.key === 'Escape') {
        ev.preventDefault();
        cancel();
      }
    },
    [commit, cancel]
  );

  return (
    <div
      className={mergeClasses(styles.root)}
      style={{ gridColumn: `span ${span}` }}
      data-testid="record-header-number-field"
      data-field-type="number"
      data-span={span}
      data-editable={editable ? 'true' : 'false'}
      data-editing={editing ? 'true' : 'false'}
    >
      <div className={styles.label} data-testid="record-header-number-field-label">
        {label}
      </div>
      {editing ? (
        <div className={styles.editRow}>
          <Input
            appearance="filled-lighter"
            // v1.1.6: `medium`, NOT `small`. Fluent sizes the small variant at
            // `fontSizeBase200` (12px) while the read-mode cell above uses
            // `fontSizeBase300` (14px), so text visibly shrank the moment a
            // field was clicked. Medium matches both the read state and the OOB
            // Dataverse inputs beside the header.
            size="medium"
            value={draft}
            onChange={(_, data) => setDraft(data.value)}
            onBlur={() => void commit()}
            onKeyDown={onKeyDown}
            disabled={saving}
            autoFocus
            input={{ className: styles.editInputRaw }}
            data-testid="record-header-number-field-input"
          />
          {saving ? <Spinner size="tiny" aria-label="Saving" data-testid="record-header-number-field-spinner" /> : null}
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
          data-testid="record-header-number-field-value"
        >
          {displayValue}
        </div>
      )}
    </div>
  );
};

NumberField.displayName = 'NumberField';
