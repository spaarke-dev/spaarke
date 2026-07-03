/**
 * TextField — single-line label + value renderer for `FieldGrid` (FR-04).
 *
 * One of four sibling field renderers colocated in `RecordHeader/fields/` for
 * extension. TextField specifically renders a **text value with ellipsis on
 * overflow** and an optional required marker beside the label.
 *
 * Contract (FR-04, spec §3.3):
 *  - Label above (small caption; `tokens.colorNeutralForeground2`,
 *    `tokens.fontSizeBase200`, weight `semibold`)
 *  - Value below (`tokens.colorNeutralForeground1`, `tokens.fontSizeBase300`)
 *  - Value is single-line with ellipsis on overflow.
 *  - `null` / `undefined` value renders as an em-dash "—".
 *  - `required===true` renders a "*" marker beside the label using
 *    `tokens.colorPaletteRedForeground1`.
 *  - CSS `grid-column: span N` (1..3) applied on the root so this cell
 *    integrates into `FieldGrid`'s CSS grid (FieldGrid does NOT set span on
 *    its children — the renderer owns that per FR-03 contract).
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens only. Zero hex / rgb / hsl
 * literals; typography via `tokens.fontSizeBase*` / `tokens.fontWeightSemibold`.
 * Per ADR-022 (React 16/17 boundary): plain functional component, no
 * React-18-exclusive APIs — safe to consume from PCFs (`react@16.14`).
 *
 * Value type is restricted to primitives (`string | number | null | undefined`)
 * so ellipsis behavior stays deterministic; do NOT widen to `React.ReactNode`.
 *
 * @see FR-04 record-header-and-notepad-r1 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 * @see ./index.ts — barrel export
 * @see ../FieldGrid.tsx — parent CSS-grid container
 *
 * @example
 * ```tsx
 * <FieldGrid columns={3}>
 *   <TextField span={1} label="Matter Number" value={values.sprk_matternumber} required />
 *   <TextField span={2} label="Matter Name"   value={values.sprk_mattername} />
 * </FieldGrid>
 * ```
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/**
 * Props for {@link TextField}.
 */
export interface ITextFieldProps {
  /** Label rendered above the value (required, non-empty). */
  label: string;

  /**
   * Value rendered below the label. Primitives only (per spec note: keeps
   * ellipsis behavior deterministic — do NOT accept arbitrary `React.ReactNode`).
   * `null` / `undefined` renders as an em-dash "—".
   */
  value: string | number | null | undefined;

  /**
   * CSS grid column span (1..3). Applied as `grid-column: span N` on the root
   * so this cell participates in `FieldGrid`'s CSS grid.
   */
  span: 1 | 2 | 3;

  /**
   * When `true`, a "*" marker is rendered beside the label using
   * `tokens.colorPaletteRedForeground1`. Defaults to `false`.
   */
  required?: boolean;
}

/**
 * Em-dash rendered when `value` is `null` or `undefined`.
 * Kept as a named constant for test / discovery clarity.
 */
export const EMPTY_VALUE_PLACEHOLDER = '—';

const useTextFieldStyles = makeStyles({
  /**
   * Root cell — vertical stack (label above, value below).
   * Grid-column span is applied INLINE (`style` prop) because Griffel needs
   * static class keys; a discrete class per span value would explode the CSS.
   */
  root: {
    display: 'flex',
    flexDirection: 'column',
    // Minimum width 0 lets flex/grid child shrink and enable ellipsis.
    minWidth: 0,
    rowGap: tokens.spacingVerticalXXS,
  },
  /** Label row — caption typography, semibold, secondary neutral color. */
  label: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase200,
    // Label may also overflow (e.g. long labels in narrow cells).
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  /**
   * Required marker "*" — inline next to the label.
   * Semantic palette token for danger foreground (ADR-021).
   */
  requiredMarker: {
    color: tokens.colorPaletteRedForeground1,
    marginLeft: tokens.spacingHorizontalXXS,
  },
  /** Value row — body typography, primary neutral color, ellipsis on overflow. */
  value: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
});

/**
 * Single-line label + value renderer with optional required marker and
 * ellipsis on overflow. See file-level JSDoc for the full contract.
 */
export const TextField: React.FC<ITextFieldProps> = ({ label, value, span, required }) => {
  const styles = useTextFieldStyles();

  // Render em-dash for null/undefined so the value row remains structurally
  // present (avoids layout jump when value later populates).
  const displayValue = value === null || value === undefined ? EMPTY_VALUE_PLACEHOLDER : String(value);

  return (
    <div
      className={mergeClasses(styles.root)}
      // Inline grid-column: span N — see makeStyles comment above.
      style={{ gridColumn: `span ${span}` }}
      data-testid="record-header-text-field"
      data-span={span}
    >
      <div className={styles.label} data-testid="record-header-text-field-label">
        {label}
        {required === true ? (
          <span
            className={styles.requiredMarker}
            aria-hidden="true"
            data-testid="record-header-text-field-required-marker"
          >
            {'*'}
          </span>
        ) : null}
      </div>
      <div
        className={styles.value}
        title={displayValue}
        data-testid="record-header-text-field-value"
      >
        {displayValue}
      </div>
    </div>
  );
};

TextField.displayName = 'TextField';
