/**
 * StatusSummaryWidget
 *
 * Renders a health / status summary dashboard for the AI output pane. Each
 * category is displayed as a row with a status icon, a bold label, and a
 * brief summary text. Supports four status levels:
 *   - success  → CheckmarkCircle icon, colorStatusSuccessForeground1
 *   - warning  → Warning icon, colorStatusWarningForeground1
 *   - error    → ErrorCircle icon, colorStatusDangerForeground1
 *   - info     → Info icon, colorNeutralForeground2
 *
 * All colors are Fluent v9 design tokens so dark mode works automatically
 * via FluentProvider theme switching (ADR-021).
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Text, Spinner } from '@fluentui/react-components';
import { CheckmarkCircle20Filled, Warning20Filled, ErrorCircle20Filled, Info20Filled } from '@fluentui/react-icons';
import type { OutputWidgetProps } from '../types';

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

/** Status level for a single category row. */
export type StatusLevel = 'success' | 'warning' | 'error' | 'info';

/** A single category entry in the status summary. */
export interface StatusCategory {
  /** Unique identifier for this category. */
  id: string;
  /** Display label shown to the left of the status icon. */
  label: string;
  /** Status level — determines icon and color. */
  status: StatusLevel;
  /** Brief summary text describing the current state. */
  summary: string;
}

export interface StatusSummaryData {
  /** Optional widget title / heading. */
  title?: string;
  /** Ordered list of status categories to render. */
  categories: StatusCategory[];
}

export type StatusSummaryWidgetProps = OutputWidgetProps<StatusSummaryData>;

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
  categoryList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  categoryRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  iconWrapper: {
    flexShrink: '0',
    display: 'flex',
    alignItems: 'center',
    paddingTop: '2px', // visual alignment with first text line
  },
  categoryContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    flex: '1',
  },
  categoryLabel: {
    fontWeight: tokens.fontWeightSemibold,
  },
  categorySummary: {
    color: tokens.colorNeutralForeground2,
  },
  // Status-specific icon colors
  iconSuccess: {
    color: tokens.colorStatusSuccessForeground1,
  },
  iconWarning: {
    color: tokens.colorStatusWarningForeground1,
  },
  iconError: {
    color: tokens.colorStatusDangerForeground1,
  },
  iconInfo: {
    color: tokens.colorNeutralForeground2,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface StatusIconProps {
  status: StatusLevel;
  iconClassName: string;
  successClassName: string;
  warningClassName: string;
  errorClassName: string;
  infoClassName: string;
}

function StatusIcon({
  status,
  iconClassName,
  successClassName,
  warningClassName,
  errorClassName,
  infoClassName,
}: StatusIconProps): React.ReactElement {
  switch (status) {
    case 'success':
      return <CheckmarkCircle20Filled className={mergeClasses(iconClassName, successClassName)} />;
    case 'warning':
      return <Warning20Filled className={mergeClasses(iconClassName, warningClassName)} />;
    case 'error':
      return <ErrorCircle20Filled className={mergeClasses(iconClassName, errorClassName)} />;
    case 'info':
    default:
      return <Info20Filled className={mergeClasses(iconClassName, infoClassName)} />;
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * StatusSummaryWidget renders a health dashboard with one row per category.
 * Each row shows a status icon (colored via Fluent v9 status tokens), a
 * bold label, and a brief summary description.
 */
export default function StatusSummaryWidget({
  data,
  isLoading,
  error,
  className,
}: StatusSummaryWidgetProps): React.ReactElement {
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading status..." />
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
      {data.title && (
        <Text size={500} className={styles.title}>
          {data.title}
        </Text>
      )}

      <div className={styles.categoryList}>
        {data.categories.map(category => (
          <div key={category.id} className={styles.categoryRow}>
            <span className={styles.iconWrapper}>
              <StatusIcon
                status={category.status}
                iconClassName=""
                successClassName={styles.iconSuccess}
                warningClassName={styles.iconWarning}
                errorClassName={styles.iconError}
                infoClassName={styles.iconInfo}
              />
            </span>
            <div className={styles.categoryContent}>
              <Text size={300} className={styles.categoryLabel}>
                {category.label}
              </Text>
              <Text size={200} className={styles.categorySummary}>
                {category.summary}
              </Text>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
