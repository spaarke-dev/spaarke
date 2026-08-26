/**
 * OptionSetField — record-header option-set field renderer (FR-04, FR-09/FR-10).
 *
 * A display renderer for an option-set (choice) value, with an optional
 * click-to-edit mode. The consumer is responsible for resolving the OData
 * FormattedValue annotation (`@OData.Community.Display.V1.FormattedValue`)
 * into a human-readable option label string BEFORE passing it to this
 * component as `value` — this keeps OptionSetField renderer-agnostic to any
 * specific Xrm.WebApi shape.
 *
 * v1.0.4 typography (2026-08-25, FR-09): the label now uses the same stack
 * TextField adopted at v1.0.4 — `fontSizeBase300` / `colorNeutralForeground1`
 * — instead of the original `caption1` / `colorNeutralForeground2` pairing,
 * so the label no longer reads smaller/grayer than sibling renderers in a
 * mixed `FieldGrid`.
 *
 * v1.0.x edit mode (FR-09/FR-10): when `options` + `onSave` are both
 * supplied, clicking the value enters edit mode (Fluent v9 `Dropdown`)
 * listing every option; selecting an option commits it via `onSave`, Escape
 * cancels, blur commits an unresolved draft. When `onSave` is omitted (or
 * `options` is empty/missing, or `disabled` is true), the renderer stays
 * read-only — the original R1 behavior, fully backward compatible.
 *
 * Consumer boundary for `options` (mirrors the existing FormattedValue
 * boundary): the caller resolves the attribute's choices via
 * `getXrmPage().getAttribute(name).getOptions()` and passes the plain
 * `{ value, label }` pairs — this component never touches `Xrm` or
 * `ComponentFramework` types (ADR-012). `onSave` is invoked with the
 * selected option's numeric `value` — the same payload the caller stages
 * into the form buffer via
 * `getXrmPage().getAttribute(name).setValue(newValue)` for optionset
 * attributes (never `Xrm.WebApi.updateRecord` — see root CLAUDE.md
 * form-buffer rule).
 *
 * Layout matches sibling {@link TextField} exactly for visual parity in a
 * `FieldGrid`:
 *   - Label above (colorNeutralForeground1, v1.0.4 typography)
 *   - Value below (colorNeutralForeground1, body1 typography)
 *   - Applies `gridColumn: span {span}` via inline style (per FR-03 contract:
 *     the field renderer owns its own span; FieldGrid does not set it).
 *
 * Null / undefined / empty-string `value` renders as a hyphen "—" (per FR-04
 * OptionSetField acceptance criterion; unaffected by edit mode).
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens only. No hex / rgb / hsl
 * literals — all colors / typography come from `tokens.*`.
 *
 * Per ADR-022 (React 16/17 boundary): plain functional component, no
 * React-18 exclusive APIs — safe to consume from PCFs (`react@16.14`).
 *
 * @see FR-04, FR-09, FR-10 record-header-and-notepad-r2 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 * @see ADR-012 shared component library (context-agnostic boundary)
 *
 * @example
 * ```tsx
 * // Read-only (consumer has already resolved the FormattedValue):
 * const statusLabel =
 *   record["statuscode@OData.Community.Display.V1.FormattedValue"];
 * <OptionSetField span={1} label="Status" value={statusLabel} />
 *
 * // Editable (consumer resolves options + wires the form buffer):
 * const attr = getXrmPage().getAttribute("sprk_invoicestatus");
 * <OptionSetField
 *   span={1}
 *   label="Status"
 *   value={statusLabel}
 *   options={attr.getOptions().map(o => ({ value: o.value, label: o.text }))}
 *   onSave={async newValue => { attr.setValue(newValue); }}
 * />
 * ```
 */
import * as React from 'react';
import {
  Dropdown,
  Option,
  Spinner,
  makeStyles,
  mergeClasses,
  tokens,
  typographyStyles,
} from '@fluentui/react-components';
import type { OptionOnSelectData, SelectionEvents } from '@fluentui/react-components';

/**
 * A single resolved option-set choice, as returned by
 * `getXrmPage().getAttribute(name).getOptions()`.
 */
export interface IOptionSetFieldOption {
  /** The option's numeric value (the raw optionset int). */
  value: number;
  /** The option's display label. */
  label: string;
}

/**
 * Props for {@link OptionSetField}.
 */
export interface IOptionSetFieldProps {
  /** Display label rendered above the value. Required. */
  label: string;

  /**
   * Resolved option label string. The caller resolves this from the OData
   * `@OData.Community.Display.V1.FormattedValue` annotation before passing.
   *
   * `null` / `undefined` / `''` render as a hyphen "—" per FR-04.
   */
  value: string | null | undefined;

  /**
   * Number of grid columns this cell occupies inside a {@link FieldGrid}
   * (FR-03 contract — the field renderer owns its own span).
   */
  span: 1 | 2 | 3;

  /** Optional extra className applied to the cell root. */
  className?: string;

  /**
   * Accepted for FR-10 contract-shape parity with sibling renderers but
   * intentionally renders NOTHING — the `*` marker is deliberately
   * TextField-only (record-header-and-notepad-r2 D-10). Added by r2 task 015
   * so the shared renderer-contract suite can assert the D-10 negative
   * uniformly; behaviorally inert, so no existing consumer is affected.
   */
  required?: boolean;

  /**
   * The full resolved option list for edit mode — the consumer resolves
   * this via `getXrmPage().getAttribute(name).getOptions()`. Required
   * (non-empty) together with `onSave` for the field to become editable;
   * omit to keep the field permanently read-only (default, backward
   * compatible).
   */
  options?: IOptionSetFieldOption[];

  /**
   * When provided together with a non-empty `options` array, the value
   * becomes click-to-edit via a Fluent v9 `Dropdown`. Invoked with the
   * selected option's numeric `value` (or `null` if the selection could not
   * be resolved) — stage this into the form buffer via
   * `getXrmPage().getAttribute(name).setValue(newValue)`, never
   * `Xrm.WebApi.updateRecord`. Return a rejected Promise to signal a save
   * error — the field reverts to the previous selection and stays in edit
   * mode. Omit to render read-only (default).
   *
   * If `onSave` is provided without a non-empty `options` array, the field
   * stays read-only and a `console.warn` is emitted once (misconfiguration
   * guard — nothing to select).
   */
  onSave?: (newValue: number | null) => Promise<void>;

  /**
   * When `true`, disables editing (value shown but not clickable). Only
   * meaningful when `onSave` and `options` are also provided.
   */
  disabled?: boolean;
}

/** Hyphen glyph used for null / undefined values (FR-04). */
const EMPTY_VALUE_GLYPH = '—';

const useOptionSetFieldStyles = makeStyles({
  /**
   * Cell root — stacks label above value. `gridColumn` is applied inline
   * per-instance (span 1..3) so Griffel keeps a single stable class.
   */
  cell: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0, // enable ellipsis inside a CSS-grid cell
    rowGap: tokens.spacingVerticalXXS,
  },
  /**
   * Label — v1.0.4 typography (matches TextField.tsx:56-69 verbatim): 14px
   * Segoe UI, primary neutral foreground, 4px bottom padding. Replaces the
   * pre-v1.0.4 `caption1` / `colorNeutralForeground2` stack that read
   * smaller and grayer than sibling renderers in a mixed FieldGrid.
   */
  label: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
    lineHeight: tokens.lineHeightBase300,
    paddingBottom: tokens.spacingVerticalXS,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  /**
   * Value — body1 typography, primary foreground. Single-line with
   * ellipsis to stay visually consistent with TextField in mixed grids.
   * Unchanged by the FR-09 typography fix (label-only scope) and by edit
   * mode beyond the `valueEditable` hover affordance below.
   */
  value: {
    ...typographyStyles.body1,
    color: tokens.colorNeutralForeground1,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  /**
   * Editable read-mode value — hint clickability. Applied only when
   * `editable` is true. Mirrors TextField's `valueEditable` intent (cursor
   * + hover background token) without adopting TextField's always-on boxed
   * chrome — this task's scope excludes restyling the read-mode value cell.
   */
  valueEditable: {
    cursor: 'pointer',
    borderRadius: tokens.borderRadiusMedium,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  editRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
});

/**
 * Record-header option-set field renderer. See file-level JSDoc for the
 * full contract, FormattedValue/getOptions() resolution boundary, edit-mode
 * contract, and examples.
 */
export const OptionSetField: React.FC<IOptionSetFieldProps> = ({
  label,
  value,
  span,
  className,
  options,
  onSave,
  disabled,
}) => {
  const styles = useOptionSetFieldStyles();
  const displayValue = value === null || value === undefined || value === '' ? EMPTY_VALUE_GLYPH : value;

  const hasOptions = Array.isArray(options) && options.length > 0;
  const editable = typeof onSave === 'function' && disabled !== true && hasOptions;

  // FR-10: onSave without a non-empty options array is a misconfiguration —
  // warn once and stay read-only rather than rendering a Dropdown with
  // nothing to select.
  const warnedRef = React.useRef(false);
  React.useEffect(() => {
    if (typeof onSave === 'function' && !hasOptions && !warnedRef.current) {
      console.warn(
        '[OptionSetField] `onSave` was provided without a non-empty `options` array — field stays read-only.'
      );
      warnedRef.current = true;
    }
  }, [onSave, hasOptions]);

  // The option whose label matches the resolved display value — the
  // "committed" selection this field reverts to on cancel/reject.
  const originalOptionValue = React.useMemo<number | null>(() => {
    if (!hasOptions || displayValue === EMPTY_VALUE_GLYPH) return null;
    return options!.find(o => o.label === displayValue)?.value ?? null;
  }, [options, hasOptions, displayValue]);

  // Edit-mode state
  const [editing, setEditing] = React.useState(false);
  const [draft, setDraft] = React.useState<number | null>(originalOptionValue);
  const [saving, setSaving] = React.useState(false);

  // Reset draft when the external value changes (so an external refresh
  // doesn't drop an in-progress selection — only sync on entry/settle).
  React.useEffect(() => {
    if (!editing) {
      setDraft(originalOptionValue);
    }
  }, [originalOptionValue, editing]);

  const enterEdit = React.useCallback(() => {
    if (!editable) return;
    setDraft(originalOptionValue);
    setEditing(true);
  }, [editable, originalOptionValue]);

  const commit = React.useCallback(
    async (overrideValue?: number | null) => {
      if (!onSave) return;
      const candidate = overrideValue !== undefined ? overrideValue : draft;
      if (candidate === originalOptionValue) {
        // No change (Escape-equivalent for a re-selected current value) —
        // exit edit with zero onSave calls.
        setDraft(candidate);
        setEditing(false);
        return;
      }
      setDraft(candidate);
      setSaving(true);
      try {
        await onSave(candidate);
        setEditing(false);
      } catch {
        // Revert draft to last-known selection on error; stay in edit mode
        // so the user can retry or cancel (mirrors TextField).
        setDraft(originalOptionValue);
      } finally {
        setSaving(false);
      }
    },
    [onSave, draft, originalOptionValue]
  );

  const cancel = React.useCallback(() => {
    setDraft(originalOptionValue);
    setEditing(false);
  }, [originalOptionValue]);

  const handleOptionSelect = React.useCallback(
    (_ev: SelectionEvents, data: OptionOnSelectData) => {
      // Selecting an option is the Dropdown-domain equivalent of Enter —
      // commit immediately with the selection's numeric value (passed
      // explicitly to avoid reading a stale `draft` closure).
      const newValue = data.optionValue !== undefined ? Number(data.optionValue) : null;
      void commit(newValue);
    },
    [commit]
  );

  const onKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLButtonElement>) => {
      if (ev.key === 'Escape') {
        ev.preventDefault();
        cancel();
      }
    },
    [cancel]
  );

  const selectedLabel = hasOptions ? (options!.find(o => o.value === draft)?.label ?? '') : '';

  return (
    <div
      className={mergeClasses(styles.cell, className)}
      style={{ gridColumn: `span ${span}` }}
      data-field-type="optionset"
      data-span={span}
      data-testid="record-header-optionset-field"
      data-editable={editable ? 'true' : 'false'}
      data-editing={editing ? 'true' : 'false'}
    >
      <span className={styles.label}>{label}</span>
      {editing ? (
        <div className={styles.editRow}>
          <Dropdown
            appearance="filled-lighter"
            size="small"
            value={selectedLabel}
            selectedOptions={draft !== null ? [String(draft)] : []}
            onOptionSelect={handleOptionSelect}
            onBlur={() => void commit()}
            onKeyDown={onKeyDown}
            disabled={saving}
            defaultOpen
            autoFocus
            data-testid="record-header-optionset-field-dropdown"
          >
            {(options ?? []).map(o => (
              <Option key={String(o.value)} value={String(o.value)} text={o.label}>
                {o.label}
              </Option>
            ))}
          </Dropdown>
          {saving ? (
            <Spinner size="tiny" aria-label="Saving" data-testid="record-header-optionset-field-spinner" />
          ) : null}
        </div>
      ) : (
        <span
          className={mergeClasses(styles.value, editable ? styles.valueEditable : undefined)}
          title={typeof displayValue === 'string' ? displayValue : undefined}
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
          data-testid="record-header-optionset-field-value"
        >
          {displayValue}
        </span>
      )}
    </div>
  );
};

OptionSetField.displayName = 'OptionSetField';
