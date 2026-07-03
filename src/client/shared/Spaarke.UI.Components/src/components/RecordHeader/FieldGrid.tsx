/**
 * FieldGrid — CSS-grid wrapper for record-header field renderers (FR-03).
 *
 * A configurable 2- or 3-column CSS grid that lays out field renderer children
 * (`TextField`, `LookupField`, `OptionSetField`, `TextareaField`, etc.) row-by-row.
 * Each field cell reads its own `span` prop (1..3) via a shared field-styles hook
 * and applies `gridColumn: span N` to itself — FieldGrid does NOT set
 * `gridColumn` on its children. CSS grid handles wrapping naturally, so a
 * `span=3` cell automatically starts a new row.
 *
 * Per FR-03 / spec §3.3, this is the composition primitive per-entity thin PCFs
 * use to declare their 5-field header layout. It intentionally accepts any
 * `React.ReactNode` so it does not couple to specific field renderer components.
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens only. No hex / rgb / hsl
 * literals; grid gap uses `tokens.spacingHorizontalM` (column gap) and
 * `tokens.spacingVerticalS` (row gap).
 *
 * Per ADR-022 (React 16/17 boundary): plain functional component, no React-18
 * exclusive APIs — safe to consume from PCFs (`react@16.14`).
 *
 * @see FR-03 record-header-and-notepad-r1 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 *
 * @example
 * ```tsx
 * <FieldGrid columns={3}>
 *   <TextField     span={1} label="Matter Number"      value={values.sprk_matternumber} required />
 *   <TextField     span={2} label="Matter Name"        value={values.sprk_mattername} />
 *   <LookupField   span={1} label="Matter Type"        value={values.sprk_mattertype} />
 *   <LookupField   span={1} label="Practice Area"      value={values.sprk_practicearea} />
 *   <TextareaField span={3} label="Matter Description" value={values.sprk_matterdescription} />
 * </FieldGrid>
 * ```
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/**
 * Props for {@link FieldGrid}.
 */
export interface IFieldGridProps {
  /**
   * Number of grid columns. Only 2 or 3 are supported (FR-03).
   *
   * @defaultValue 3
   */
  columns?: 2 | 3;

  /**
   * Field renderer children. Each child should apply its own
   * `gridColumn: span N` style via a shared field-styles hook so the grid can
   * naturally flow row-by-row and let `span=3` fields start a new row.
   *
   * FieldGrid does NOT set `gridColumn` on children — that responsibility
   * belongs to the field renderer to keep FieldGrid renderer-agnostic.
   */
  children: React.ReactNode;

  /** Optional extra className applied to the grid root. */
  className?: string;
}

const useFieldGridStyles = makeStyles({
  /**
   * Base grid — display + gap. Column count is applied inline via the
   * per-column style below (Griffel needs static keys, so we key off a
   * discrete class per supported column count).
   */
  grid: {
    display: 'grid',
    // Gap: row/column tokens. Column gap uses spacingHorizontalM; row gap
    // uses spacingVerticalS. Fluent v9 semantic tokens per ADR-021.
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalS,
  },
  /** 2-column layout. */
  twoColumns: {
    gridTemplateColumns: 'repeat(2, 1fr)',
  },
  /** 3-column layout (default). */
  threeColumns: {
    gridTemplateColumns: 'repeat(3, 1fr)',
  },
});

/**
 * CSS-grid wrapper for record-header field renderers.
 *
 * See file-level JSDoc for full contract, span behavior, and example.
 */
export const FieldGrid: React.FC<IFieldGridProps> = ({ columns = 3, children, className }) => {
  const styles = useFieldGridStyles();
  const columnClass = columns === 2 ? styles.twoColumns : styles.threeColumns;

  return (
    <div role="group" className={mergeClasses(styles.grid, columnClass, className)} data-columns={columns}>
      {children}
    </div>
  );
};

FieldGrid.displayName = 'FieldGrid';
