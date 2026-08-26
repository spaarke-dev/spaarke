/**
 * RecordHeaderShell — outer card chrome for record-header surfaces (FR-02).
 *
 * Composes a Fluent v9 card container with a `HeaderToolbar` at the top
 * and a body slot for `children`. On `loading === true`, the body renders
 * a small Fluent v9 `Skeleton` grid — 3 columns × 2 rows by default (matches
 * `FieldGrid` default), or 2 columns × 2 rows when the optional `columns={2}`
 * prop is passed (FR-18) — instead of `children`, so the card chrome stays
 * visually stable while data resolves AND the skeleton's footprint matches
 * the loaded layout (no reflow on load).
 *
 * Per-entity thin PCFs (`MatterHeaderPcf`, future `ProjectHeaderPcf`,
 * `InvoiceHeaderPcf`, …) wrap this around a `FieldGrid` — the shell is
 * responsible for card chrome + loading affordance ONLY; layout of body
 * content is the caller's responsibility (typically a `FieldGrid`).
 *
 * Standards:
 *  - ADR-012  Shared component library (context-agnostic; no PCF imports)
 *  - ADR-021  Fluent v9 semantic tokens exclusively — zero hex/rgb/hsl.
 *             Card bg = colorNeutralBackground1, border = colorNeutralStroke1,
 *             radius = borderRadiusMedium, padding = spacingHorizontalM /
 *             spacingVerticalS per FR-02 acceptance.
 *  - ADR-022  React 16/17 safe — plain `React.FC`, no React 18-exclusive
 *             APIs (no `use()`, no `useSyncExternalStore`, no `createRoot`).
 *
 * @see ../../../../projects/record-header-and-notepad-r1/spec.md#fr-02
 */

import * as React from 'react';
import { Skeleton, SkeletonItem, makeStyles, shorthands, tokens } from '@fluentui/react-components';

import { HeaderToolbar } from '../HeaderToolbar';
import type { IRecordHeaderShellProps } from './types';

// ---------------------------------------------------------------------------
// Styles — module scope per fluent-v9-component-authoring.md
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Outer card container. Fluent v9 semantic tokens throughout:
   *  - background: colorNeutralBackground1 — adapts to light/dark/HC themes
   *  - border:     colorNeutralStroke1 — 1px hairline in the neutral family
   *  - radius:     borderRadiusMedium — matches Fluent v9 card idiom
   *  - padding:    spacingHorizontalM (inline) + spacingVerticalS (block)
   *                per FR-02 note ("Card padding: spacingHorizontalM +
   *                spacingVerticalS per Fluent v9 defaults").
   */
  card: {
    display: 'flex',
    flexDirection: 'column',
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.gap(tokens.spacingVerticalS),
  },

  /**
   * Borderless variant (v1.0.2): bare vertical stack — no border, no
   * background fill, no border-radius, no padding. Consumers embedding
   * the header inside a form section that provides its own chrome
   * use `borderless={true}`. Toolbar row + body still stack vertically
   * with the same gap so the shell still "looks like a group" without
   * looking like a card.
   */
  cardBorderless: {
    display: 'flex',
    flexDirection: 'column',
    boxSizing: 'border-box',
    color: tokens.colorNeutralForeground1,
    ...shorthands.gap(tokens.spacingVerticalS),
  },

  /**
   * Body region wraps `children` OR the loading skeleton. A dedicated
   * region gives the shell a stable box for the loading↔loaded swap
   * (the toolbar stays put; only the body swaps).
   */
  body: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
  },

  /**
   * Skeleton container mirrors FieldGrid's default layout (3 columns,
   * 2 rows) so the placeholder occupies the same visual footprint as
   * the loaded body. Column/row gaps use the same semantic tokens
   * FieldGrid uses (spacingHorizontalM / spacingVerticalS) so the
   * loading→loaded swap looks continuous. This is the DEFAULT skeleton
   * grid (columns omitted or 3) — kept byte-identical to the original
   * pre-FR-18 implementation for backward compatibility.
   */
  skeleton: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, 1fr)',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalS,
  },

  /**
   * Skeleton container variant for `columns={2}` (FR-18). Griffel's
   * `makeStyles` is build-time static, so the runtime `columns` value
   * cannot be interpolated into a single class — this is a second
   * precompiled class selected at render time instead. Same gap tokens
   * as the 3-column variant; only `gridTemplateColumns` differs.
   */
  skeletonTwoColumn: {
    display: 'grid',
    gridTemplateColumns: 'repeat(2, 1fr)',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalS,
  },

  /**
   * Individual skeleton cell. Height is a token-derived line height so
   * the placeholder height doesn't hardcode pixel values.
   */
  skeletonCell: {
    height: tokens.lineHeightBase400,
    minWidth: 0,
  },
});

// ---------------------------------------------------------------------------
// Loading skeleton — columns × 2 rows (matches FieldGrid; FR-18)
// ---------------------------------------------------------------------------

/**
 * FR-02 / FR-18 loading affordance. Renders a `columns` × 2 grid of
 * `SkeletonItem` placeholders inside a Fluent v9 `Skeleton` wrapper.
 *
 * Default (`columns` 3, or omitted upstream) renders six cells — matching
 * the ~5-field header footprint of `MatterHeaderPcf` (5 fields with one
 * spanning 3 columns still visually parks in ~6 grid cells). `columns={2}`
 * renders four cells so a 2-column configured header doesn't flash a
 * mismatched 3-column skeleton during load.
 *
 * Cell testids follow the pre-existing stable scheme
 * (`record-header-shell-skeleton-cell-0..N`) regardless of `columns`, so the
 * default 3-column case is byte-identical to the original implementation.
 */
const LoadingSkeleton: React.FC<{ className: string; cellClassName: string; columns: 2 | 3 }> = ({
  className,
  cellClassName,
  columns,
}) => {
  const cellCount = columns * 2; // columns × 2 rows
  const cellIndexes = Array.from({ length: cellCount }, (_, index) => index);

  return (
    <Skeleton className={className} aria-label="Loading record header" data-testid="record-header-shell-skeleton">
      {cellIndexes.map((index) => (
        <SkeletonItem key={index} className={cellClassName} data-testid={`record-header-shell-skeleton-cell-${index}`} />
      ))}
    </Skeleton>
  );
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Outer card chrome for record-header surfaces (FR-02). See file-level
 * JSDoc for the binding rules and props contract in `./types.ts`.
 *
 * Plain functional component per ADR-022 (React 16/17 compatible).
 *
 * @example
 * ```tsx
 * const toolbar = useRecordHeaderToolbarActions({ entity: 'sprk_matter', recordId });
 * const { values, loading } = useRecordFieldValues('sprk_matter', recordId, [...]);
 *
 * return (
 *   <RecordHeaderShell toolbar={toolbar} loading={loading}>
 *     <FieldGrid columns={3}>
 *       <TextField span={1} label="Matter Number" value={values.sprk_matternumber} />
 *       // ...
 *     </FieldGrid>
 *   </RecordHeaderShell>
 * );
 * ```
 */
export const RecordHeaderShell: React.FC<IRecordHeaderShellProps> = ({
  toolbar,
  loading,
  children,
  borderless,
  columns,
}) => {
  const styles = useStyles();
  const isLoading = loading === true;
  const containerClass = borderless === true ? styles.cardBorderless : styles.card;
  // Only 2 opts in to the smaller skeleton; every other value (including
  // undefined) resolves to the original 3-column default — backward compatible.
  const resolvedColumns: 2 | 3 = columns === 2 ? 2 : 3;
  const skeletonClassName = resolvedColumns === 2 ? styles.skeletonTwoColumn : styles.skeleton;

  return (
    <div
      className={containerClass}
      data-testid="record-header-shell"
      data-borderless={borderless === true ? 'true' : 'false'}
    >
      <HeaderToolbar {...toolbar} />
      <div className={styles.body} data-testid="record-header-shell-body">
        {isLoading ? (
          <LoadingSkeleton className={skeletonClassName} cellClassName={styles.skeletonCell} columns={resolvedColumns} />
        ) : (
          children
        )}
      </div>
    </div>
  );
};

RecordHeaderShell.displayName = 'RecordHeaderShell';
