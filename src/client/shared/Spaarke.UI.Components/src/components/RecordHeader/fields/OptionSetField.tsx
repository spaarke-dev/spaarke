/**
 * OptionSetField — record-header option-set field renderer (FR-04).
 *
 * A pure display renderer for an option-set (choice) value. The consumer
 * is responsible for resolving the OData FormattedValue annotation
 * (`@OData.Community.Display.V1.FormattedValue`) into a human-readable
 * option label string BEFORE passing it to this component — this keeps
 * OptionSetField renderer-agnostic to any specific Xrm.WebApi shape.
 *
 * Layout matches sibling {@link TextField} exactly for visual parity in a
 * `FieldGrid`:
 *   - Label above (colorNeutralForeground2, caption1 typography)
 *   - Value below (colorNeutralForeground1, body1 typography)
 *   - Applies `gridColumn: span {span}` via inline style (per FR-03 contract:
 *     the field renderer owns its own span; FieldGrid does not set it).
 *
 * Null / undefined `value` renders as a hyphen "—" (per FR-04 OptionSetField
 * acceptance criterion).
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens only. No hex / rgb / hsl
 * literals — all colors / typography come from `tokens.*`.
 *
 * Per ADR-022 (React 16/17 boundary): plain functional component, no
 * React-18 exclusive APIs — safe to consume from PCFs (`react@16.14`).
 *
 * @see FR-04 record-header-and-notepad-r1 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 *
 * @example
 * ```tsx
 * // Consumer has already resolved the FormattedValue into a label string:
 * const statusLabel =
 *   record["statuscode@OData.Community.Display.V1.FormattedValue"];
 *
 * <OptionSetField span={1} label="Status" value={statusLabel} />
 * ```
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens, typographyStyles } from '@fluentui/react-components';

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
   * `null` / `undefined` render as a hyphen "—" per FR-04.
   */
  value: string | null | undefined;

  /**
   * Number of grid columns this cell occupies inside a {@link FieldGrid}
   * (FR-03 contract — the field renderer owns its own span).
   */
  span: 1 | 2 | 3;

  /** Optional extra className applied to the cell root. */
  className?: string;
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
   * Label — caption1 typography, muted foreground per ADR-021 semantic
   * tokens. Matches TextField sibling for visual parity across a
   * FieldGrid.
   */
  label: {
    ...typographyStyles.caption1,
    color: tokens.colorNeutralForeground2,
  },
  /**
   * Value — body1 typography, primary foreground. Single-line with
   * ellipsis to stay visually consistent with TextField in mixed grids.
   */
  value: {
    ...typographyStyles.body1,
    color: tokens.colorNeutralForeground1,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
});

/**
 * Record-header option-set field renderer. See file-level JSDoc for the
 * full contract, FormattedValue resolution boundary, and example.
 */
export const OptionSetField: React.FC<IOptionSetFieldProps> = ({
  label,
  value,
  span,
  className,
}) => {
  const styles = useOptionSetFieldStyles();
  const displayValue =
    value === null || value === undefined || value === '' ? EMPTY_VALUE_GLYPH : value;

  return (
    <div
      className={mergeClasses(styles.cell, className)}
      style={{ gridColumn: `span ${span}` }}
      data-field-type="optionset"
      data-span={span}
    >
      <span className={styles.label}>{label}</span>
      <span className={styles.value} title={typeof displayValue === 'string' ? displayValue : undefined}>
        {displayValue}
      </span>
    </div>
  );
};

OptionSetField.displayName = 'OptionSetField';
