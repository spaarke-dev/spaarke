/**
 * MetricCard — interactive square card displaying a numeric metric with icon
 * and optional notification badge.
 *
 * Design requirements:
 *   - Square aspect ratio via CSS `aspect-ratio: 1`
 *   - Loading state renders a Fluent Spinner
 *   - Badge variants: "new" (success/green), "overdue" (danger/red)
 *   - Count-filter mode (D-24, spaarke-ontology-platform-r1 task 052), all opt-in so existing consumers render unchanged:
 *     `selected` (toggle, `aria-pressed`), `note` (one line), `progress` ("n of m done today" + bar),
 *     `disableWhenEmpty` (disabled at count 0) and `layout="wide"` (not square)
 *   - Fluent v9 semantic tokens only — no hard-coded colors
 *   - Dark mode: inherits token values automatically
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 *
 * ⚠️ **This is the clickable count-filter card** — the one FR-27 requires the worklist row
 * to extend, used with `MetricCardRow`. It is unrelated to `VisualMetricCard` in
 * `Spaarke.Visuals/src/components/MetricCard.tsx` (serves the `VisualHost` PCF's charts/report
 * cards). The two were both named `MetricCard` until the Visuals one was renamed 2026-10-03
 * (item C-3) to remove the collision — this file's name/export did not change.
 */

import * as React from 'react';
import {
  Text,
  Badge,
  Spinner,
  ProgressBar,
  makeStyles,
  shorthands,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import type { FluentIcon } from '@fluentui/react-icons';
import type { MetricBadgeVariant, MetricCardLayout, MetricProgress, MetricTrend } from './types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface MetricCardProps {
  /** Display label shown below the count. */
  label: string;
  /** Fluent v9 icon component for the card. Optional: a count-filter card may have none. */
  icon?: FluentIcon;
  /** Accessible label for the card button. */
  ariaLabel: string;
  /** The numeric value to display. undefined renders an em-dash. */
  value?: number;
  /** When true, shows a Fluent Spinner instead of the value. */
  isLoading?: boolean;
  /** Optional trend direction (currently reserved for future visual indicator). */
  trend?: MetricTrend;
  /** Optional badge variant: "new" → green, "overdue" → red. */
  badgeVariant?: MetricBadgeVariant;
  /** Badge count. Shown only when > 0 and not loading. */
  badgeCount?: number;
  /** Called when the card is clicked or activated via keyboard. */
  onClick?: () => void;
  /** Additional className applied to the root element. */
  className?: string;
  /**
   * Count-filter state (D-24). When defined the card is a toggle button and exposes `aria-pressed={selected}`. When
   * undefined (the default) the card is a plain button with no `aria-pressed`, exactly as before.
   */
  selected?: boolean;
  /** One line under the label, for example "oldest 4 days" or "3 past due". Truncated with an ellipsis, never wrapped. */
  note?: string;
  /** "n of m done today" line plus a thin progress bar. Hidden when `total` is not a positive number. */
  progress?: MetricProgress;
  /**
   * When true the card is disabled while its count is 0: `aria-disabled`, out of the tab order, no click. Default
   * false, so an existing card showing 0 stays clickable.
   */
  disableWhenEmpty?: boolean;
  /** `square` (default) or `wide` (not square, left aligned). */
  layout?: MetricCardLayout;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    /**
     * Square aspect ratio: height equals width.
     * MetricCardRow uses minmax(120px, 160px) columns, so cards stay compact
     * and square across all viewport widths.
     */
    aspectRatio: '1',
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    cursor: 'pointer',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth(tokens.strokeWidthThin),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: 'box-shadow, background-color, border-color',
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: '2px',
    },
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      ...shorthands.borderColor(tokens.colorNeutralStroke1Hover),
      boxShadow: tokens.shadow4,
    },
    ':active': {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
      ...shorthands.borderColor(tokens.colorNeutralStroke1Pressed),
      boxShadow: tokens.shadow2,
    },
  },
  iconWrapper: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '32px',
    height: '32px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    marginBottom: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
  value: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase600,
    marginBottom: tokens.spacingVerticalXXS,
  },
  label: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    lineHeight: tokens.lineHeightBase200,
  },
  spinnerWrapper: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '24px',
    marginBottom: tokens.spacingVerticalXXS,
  },
  badgeWrapper: {
    position: 'absolute',
    top: '8px',
    right: '8px',
  },
  /** Not square: drop the aspect ratio, left-align the content (count-filter cards). */
  cardWide: {
    aspectRatio: 'auto',
    alignItems: 'flex-start',
    justifyContent: 'flex-start',
    rowGap: tokens.spacingVerticalXXS,
  },
  labelWide: {
    textAlign: 'start',
  },
  /** Selected count filter: brand border and tint (semantic tokens; dark mode follows the theme). */
  cardSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.borderColor(tokens.colorBrandStroke1),
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
      ...shorthands.borderColor(tokens.colorBrandStroke1),
    },
    ':active': {
      backgroundColor: tokens.colorBrandBackground2Pressed,
      ...shorthands.borderColor(tokens.colorBrandStroke1),
    },
  },
  cardDisabled: {
    cursor: 'not-allowed',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1,
      ...shorthands.borderColor(tokens.colorNeutralStroke2),
      boxShadow: 'none',
    },
    ':active': {
      backgroundColor: tokens.colorNeutralBackground1,
      ...shorthands.borderColor(tokens.colorNeutralStroke2),
      boxShadow: 'none',
    },
  },
  textDisabled: {
    color: tokens.colorNeutralForegroundDisabled,
  },
  note: {
    color: tokens.colorNeutralForeground3,
    maxWidth: '100%',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    lineHeight: tokens.lineHeightBase100,
  },
  progress: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'stretch',
    width: '100%',
    rowGap: tokens.spacingVerticalXXS,
    marginTop: tokens.spacingVerticalXS,
  },
  progressText: {
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase100,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * MetricCard — interactive square card displaying a numeric metric.
 *
 * Shows an icon, a large count value (or spinner while loading), and a
 * label below. An optional notification badge (new/overdue) appears in the
 * top-right corner when `badgeCount > 0`.
 *
 * @example
 * ```tsx
 * <MetricCard
 *   label="My Matters"
 *   icon={GavelRegular}
 *   ariaLabel="View my matters"
 *   value={counts.matters}
 *   isLoading={isLoading}
 *   badgeVariant="new"
 *   badgeCount={3}
 *   onClick={() => navigateToMatters()}
 * />
 * ```
 */
export const MetricCard: React.FC<MetricCardProps> = ({
  label,
  icon: Icon,
  ariaLabel,
  value,
  isLoading = false,
  badgeVariant,
  badgeCount,
  onClick,
  className,
  selected,
  note,
  progress,
  disableWhenEmpty = false,
  layout = 'square',
}) => {
  const styles = useStyles();

  // Disabled only on request (`disableWhenEmpty`), and only for a loaded count of exactly 0.
  const isDisabled = disableWhenEmpty && !isLoading && value === 0;
  const isWide = layout === 'wide';

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (isDisabled) return;
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        onClick?.();
      }
    },
    [onClick, isDisabled]
  );

  const progressTotal = progress ? progress.total : 0;
  const showProgress = progressTotal > 0;
  const progressDone = progress ? Math.min(Math.max(progress.done, 0), progressTotal) : 0;

  const showBadge = !isLoading && badgeVariant !== undefined && badgeCount !== undefined && badgeCount > 0;

  return (
    <div
      role="button"
      tabIndex={isDisabled ? -1 : 0}
      aria-label={ariaLabel}
      aria-pressed={selected}
      aria-disabled={isDisabled ? true : undefined}
      onClick={isDisabled ? undefined : onClick}
      onKeyDown={handleKeyDown}
      className={mergeClasses(
        styles.card,
        isWide && styles.cardWide,
        selected && styles.cardSelected,
        isDisabled && styles.cardDisabled,
        className
      )}
    >
      {/* Notification badge */}
      {showBadge && (
        <div className={styles.badgeWrapper}>
          <Badge appearance="filled" color={badgeVariant === 'overdue' ? 'danger' : 'success'} size="small">
            {badgeCount} {badgeVariant === 'overdue' ? 'Overdue' : 'New'}
          </Badge>
        </div>
      )}

      {/* Icon */}
      {Icon && (
        <div className={styles.iconWrapper} aria-hidden="true">
          <Icon fontSize={16} />
        </div>
      )}

      {/* Value / spinner */}
      {isLoading ? (
        <div className={styles.spinnerWrapper}>
          <Spinner size="small" />
        </div>
      ) : (
        <Text size={600} weight="semibold" className={mergeClasses(styles.value, isDisabled && styles.textDisabled)}>
          {value !== undefined ? value : '\u2014'}
        </Text>
      )}

      {/* Label */}
      <Text
        size={200}
        className={mergeClasses(styles.label, isWide && styles.labelWide, isDisabled && styles.textDisabled)}
      >
        {label}
      </Text>

      {/* Count-filter note: one line, e.g. "oldest 4 days" */}
      {note && (
        <Text size={100} className={mergeClasses(styles.note, isDisabled && styles.textDisabled)} title={note}>
          {note}
        </Text>
      )}

      {/* Count-filter progress: "n of m done today". The bar is decorative (the text carries the meaning). */}
      {showProgress && (
        <div className={styles.progress}>
          <Text size={100} className={styles.progressText}>
            {progressDone} of {progressTotal} done today
          </Text>
          <ProgressBar value={progressDone} max={progressTotal} thickness="medium" aria-hidden="true" />
        </div>
      )}
    </div>
  );
};

MetricCard.displayName = 'MetricCard';
