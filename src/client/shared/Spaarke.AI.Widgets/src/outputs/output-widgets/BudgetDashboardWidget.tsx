/**
 * BudgetDashboardWidget
 *
 * Renders a financial budget summary as a list of progress bars showing
 * spent vs. budget amounts per line item. Designed for use in the AI output
 * pane when the AI returns a BudgetDashboard SSE payload.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data shape injected via the AI streaming response (already parsed by the
 * calling code page). No direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Text, ProgressBar, Spinner } from '@fluentui/react-components';
import type { OutputWidgetProps } from '../types';

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export interface BudgetLineItem {
  /** Display label for this budget category (e.g. "Legal Fees"). */
  label: string;
  /** Amount spent so far in this category. */
  spent: number;
  /** Total budget allocated for this category. */
  budget: number;
  /** ISO 4217 currency code (e.g. "USD", "GBP"). */
  currency: string;
}

export interface BudgetDashboardData {
  /** Widget title / heading (e.g. "Q3 Matter Budget"). */
  title: string;
  /** One or more budget line items to render as progress bars. */
  items: BudgetLineItem[];
}

export type BudgetDashboardWidgetProps = OutputWidgetProps<BudgetDashboardData>;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
  },
  itemList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  item: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  itemHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'baseline',
  },
  overBudget: {
    color: tokens.colorStatusDangerForeground1,
  },
  underBudget: {
    color: tokens.colorStatusSuccessForeground1,
  },
  progressTrack: {
    width: '100%',
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatCurrency(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency,
      maximumFractionDigits: 0,
    }).format(amount);
  } catch {
    return `${currency} ${amount.toFixed(0)}`;
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * BudgetDashboardWidget renders a titled list of budget line items as Fluent
 * v9 ProgressBar rows showing spent vs. total budget. The progress value is
 * clamped to [0, 1] so bars never overflow their track; over-budget items
 * display their label in danger-foreground color.
 */
export default function BudgetDashboardWidget({
  data,
  isLoading,
  error,
  className,
}: BudgetDashboardWidgetProps): React.ReactElement {
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading budget..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Text size={500} className={styles.title}>
        {data.title}
      </Text>

      <div className={styles.itemList}>
        {data.items.map((item, index) => {
          const ratio = item.budget > 0 ? item.spent / item.budget : 0;
          const clamped = Math.min(ratio, 1);
          const isOver = ratio > 1;

          return (
            <div key={index} className={styles.item}>
              <div className={styles.itemHeader}>
                <Text size={300}>{item.label}</Text>
                <Text size={200} className={isOver ? styles.overBudget : styles.underBudget}>
                  {formatCurrency(item.spent, item.currency)} / {formatCurrency(item.budget, item.currency)}
                </Text>
              </div>
              <ProgressBar className={styles.progressTrack} value={clamped} color={isOver ? 'error' : 'success'} />
            </div>
          );
        })}
      </div>
    </div>
  );
}
